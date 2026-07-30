/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Inline data channel framing over the UASC binary channel: a frame
    /// is one STR MessageChunk, written to the connection exactly as a
    /// MSG chunk is.
    /// </summary>
    public partial class UaSCUaBinaryChannel
    {
        /// <summary>
        /// The data channels multiplexed onto this SecureChannel, or null
        /// when the feature is not enabled.
        /// </summary>
        /// <remarks>
        /// Experimental. Until <see cref="EnableDataChannels"/> is called
        /// the STR dispatch is inert and an incoming frame closes the
        /// SecureChannel, which is what the interoperability rule of the
        /// Part 6 errata 5.16 requires of a peer that does not implement
        /// this specification.
        /// </remarks>
        public DataChannelManager? DataChannels => m_dataChannels;

        /// <summary>
        /// Enables the data channel feature on this SecureChannel.
        /// </summary>
        /// <param name="isServer">True on the server side, which
        /// allocates ChannelIds.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="maxDataChannels">The most channels kept open at
        /// once on this SecureChannel.</param>
        /// <param name="maxCreditPerChannel">The largest window granted
        /// to one channel.</param>
        public DataChannelManager EnableDataChannels(
            bool isServer,
            ITelemetryContext telemetry,
            ushort maxDataChannels = 16,
            uint maxCreditPerChannel = 1024 * 1024)
        {
            DataChannelManager? existing = m_dataChannels;

            if (existing != null)
            {
                return existing;
            }

            var transport = new UaSCDataChannelTransport(this);
            var manager = new DataChannelManager(
                transport,
                isServer,
                telemetry,
                maxDataChannels,
                maxCreditPerChannel);

            DataChannelManager? raced = Interlocked.CompareExchange(
                ref m_dataChannels,
                manager,
                null);

            if (raced != null)
            {
                _ = manager.DisposeAsync().AsTask();
                return raced;
            }

            m_isDataChannelSource = isServer;
            return manager;
        }

        /// <summary>
        /// Processes an incoming STR chunk.
        /// </summary>
        /// <param name="messageType">The message type and chunk type.</param>
        /// <param name="messageChunk">The chunk.</param>
        /// <param name="isRequest">True when the chunk was sent by the
        /// client, which selects the key set used to verify it.</param>
        /// <returns>False, because this method never takes ownership of
        /// the buffer.</returns>
        protected bool ProcessStreamMessage(
            uint messageType,
            ArraySegment<byte> messageChunk,
            bool isRequest)
        {
            DataChannelManager? manager = m_dataChannels;

            if (manager == null)
            {
                // A peer shall not transmit a STR frame until an
                // OpenDataChannel on this SecureChannel has completed, so
                // a frame arriving here is either an unsolicited probe or
                // a corrupted stream. It is never silently dropped.
                m_logger.DataChannelFeatureDisabled();
                OnDataChannelProtocolFault(DataChannelFrameError.InvalidControlChannelFrame);
                return false;
            }

            ArraySegment<byte> body;
            uint requestId;

            try
            {
                body = ReadSymmetricMessage(
                    messageChunk,
                    isRequest,
                    out ChannelToken _,
                    out requestId,
                    out uint sequenceNumber);

                if (!VerifySequenceNumber(sequenceNumber, nameof(ProcessStreamMessage)))
                {
                    OnDataChannelProtocolFault(DataChannelFrameError.MalformedHeader);
                    return false;
                }
            }
            catch (ServiceResultException)
            {
                OnDataChannelProtocolFault(DataChannelFrameError.MalformedHeader);
                return false;
            }

            if (!DataChannelFrameCodec.TryValidateChunkHeaders(
                messageType,
                requestId,
                out DataChannelFrameError headerError))
            {
                OnDataChannelProtocolFault(headerError);
                return false;
            }

            if (!DataChannelFrameCodec.TryDecode(
                new ReadOnlyMemory<byte>(body.GetArray(), body.Offset, body.Count),
                ReceiveBufferSize,
                out DataChannelFrame frame,
                out DataChannelFrameError error))
            {
                if (error.IsFatal())
                {
                    OnDataChannelProtocolFault(error);
                    return false;
                }

                if (manager.TryGetChannel(frame.ChannelId, out DataChannel? faulted) &&
                    faulted != null)
                {
                    faulted.Reset(error.ToStatusCode());
                }

                return false;
            }

            manager.HandleFrame(frame);
            return false;
        }

        /// <summary>
        /// Tracks how much of the SecureChannel SequenceNumber space
        /// remains under the current SecurityToken. STR, MSG, OPN and CLO
        /// chunks all consume the same sender SequenceNumber space.
        /// </summary>
        public DataChannelSequenceBudget SequenceBudget
        {
            get
            {
                SynchronizeSequenceBudget();
                return m_sequenceBudget;
            }
        }

        /// <summary>
        /// True when the SequenceNumber space remaining under the current
        /// token has fallen below the renewal threshold, so the owning
        /// channel should initiate OpenSecureChannel with
        /// RenewalRequest ahead of the normal lifetime based renewal.
        /// </summary>
        public bool IsSequenceRenewalDue
        {
            get
            {
                SynchronizeSequenceBudget();
                return m_sequenceBudget.ShouldRenew;
            }
        }

        /// <summary>
        /// Writes one data channel frame as a STR chunk.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <param name="ct">Cancellation token.</param>
        internal async ValueTask SendDataChannelFrameAsync(
            DataChannelFrame frame,
            CancellationToken ct)
        {
            int size = frame.EncodedSize;
            byte[] body = BufferManager.TakeBuffer(size, nameof(SendDataChannelFrameAsync), ct);

            BufferCollection? chunks = null;

            try
            {
                // A STR chunk shares the SecureChannel's symmetric keys and
                // its single monotonic SequenceNumber space with Service
                // traffic (§5.1), so securing one has to be serialized
                // against the Service path exactly as the Service path
                // serializes against itself. Without this the scheduler
                // thread and a Service response reach the same HMAC
                // concurrently — which throws outright on Windows, where the
                // CNG hash provider refuses concurrent use — and race for
                // SequenceNumbers, which silently emits duplicates and is
                // fatal to the channel. Only the securing is held under the
                // lock; the send is awaited outside it so a slow peer cannot
                // block Service traffic.
                lock (DataLock)
                {
                    ChannelToken token = CurrentToken
                        ?? throw ServiceResultException.Create(
                            StatusCodes.BadSecureChannelClosed,
                            "The SecureChannel has no active token.");

                    // Initiating renewal is not sufficient on its own, because
                    // a slow renewal can still be overtaken. A sender stalls
                    // its data channels rather than emitting a chunk that
                    // would reuse a SequenceNumber under the current TokenId.
                    SynchronizeSequenceBudget();

                    if (!m_sequenceBudget.TryConsume())
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadSecureChannelTokenUnknown,
                            "The SequenceNumber space under the current SecurityToken is exhausted.");
                    }

                    int written = DataChannelFrameCodec.Encode(body.AsSpan(0, size), frame);

                    chunks = WriteSymmetricMessage(
                        TcpMessageType.Stream,
                        DataChannelConstants.FrameRequestId,
                        token,
                        new ArraySegment<byte>(body, 0, written),
                        m_isDataChannelSource ? false : true,
                        out bool limitsExceeded);

                    if (limitsExceeded)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDataChannelLimitsExceeded,
                            "The data channel frame exceeds the negotiated buffer size.");
                    }
                }

                IUaSCByteTransport transport = GetDataChannelTransport();
                await transport.SendChunkAsync(chunks, ct).ConfigureAwait(false);
            }
            finally
            {
                chunks?.Release(BufferManager, nameof(SendDataChannelFrameAsync));
                BufferManager.ReturnBuffer(body, nameof(SendDataChannelFrameAsync));
            }
        }

        /// <summary>
        /// Reports a fault whose blast radius is the whole SecureChannel.
        /// The default closes the connection; the listener and client
        /// channels override it with their own fault machinery.
        /// </summary>
        /// <param name="error">Why the frame was rejected.</param>
        protected virtual void OnDataChannelProtocolFault(DataChannelFrameError error)
        {
            OnTransportError(ServiceResult.Create(
                StatusCodes.BadTcpMessageTypeInvalid,
                "A data channel frame violated the framing rules: {0}.",
                error));
        }

        /// <summary>
        /// The largest secured body a data channel frame may occupy on
        /// this SecureChannel.
        /// </summary>
        internal int MaxDataChannelBodySize
            => DataChannelFrameCodec.MaxPayload(
                DataChannelFramingMode.Inline,
                SendBufferSize,
                SymmetricSignatureSize + 2,
                withDeadline: true);

        private IUaSCByteTransport GetDataChannelTransport()
        {
            IUaSCByteTransport? transport = m_transport;

            return transport ?? throw ServiceResultException.Create(
                StatusCodes.BadConnectionClosed,
                "The transport was closed by the remote application.");
        }

        private void SynchronizeSequenceBudget()
        {
            // m_sequenceNumber counts for the lifetime of the channel, but
            // the budget is per SecurityToken, so what is consumed under the
            // current token is the distance from the value the counter held
            // when that token was activated. Observing the raw counter would
            // undo every reset the moment it was made, leaving the budget
            // permanently exhausted on a long lived channel even though each
            // new token brings a fresh space.
            long issued = Interlocked.Read(ref m_sequenceNumber);
            long baseline = Interlocked.Read(ref m_sequenceBudgetBaseline);

            if (issued < baseline)
            {
                // The space wrapped under this token, so the count restarts
                // from the new origin rather than going negative.
                Interlocked.Exchange(ref m_sequenceBudgetBaseline, 0);
                baseline = 0;
            }

            m_sequenceBudget.ObserveConsumed(issued - baseline);
        }

        /// <summary>
        /// Rebases the SequenceNumber budget on a newly activated
        /// SecurityToken. The space is per token, so a new token restores
        /// the budget the data channel sender stalls against
        /// (Part 6 errata 5.1.1).
        /// </summary>
        private protected void ResetSequenceBudget()
        {
            Interlocked.Exchange(
                ref m_sequenceBudgetBaseline,
                Interlocked.Read(ref m_sequenceNumber));

            m_sequenceBudget.OnTokenActivated();
        }

        /// <summary>
        /// Adapts the UASC binary channel to the data channel engine.
        /// </summary>
        private sealed class UaSCDataChannelTransport : IDataChannelTransport
        {
            public UaSCDataChannelTransport(UaSCUaBinaryChannel owner)
            {
                m_owner = owner;
            }

            /// <inheritdoc/>
            public DataChannelFramingMode FramingMode => DataChannelFramingMode.Inline;

            /// <inheritdoc/>
            public int MaxFrameBodySize => m_owner.MaxDataChannelBodySize;

            /// <inheritdoc/>
            public bool HasTransportFlowControl => false;

            /// <inheritdoc/>
            public BufferManager BufferManager => m_owner.BufferManager;

            /// <inheritdoc/>
            public TimeProvider TimeProvider => m_owner.TimeProvider;

            /// <inheritdoc/>
            public ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
            {
                return m_owner.SendDataChannelFrameAsync(frame, ct);
            }

            /// <inheritdoc/>
            public void OnProtocolFault(DataChannelFrameError error)
            {
                m_owner.OnDataChannelProtocolFault(error);
            }

            private readonly UaSCUaBinaryChannel m_owner;
        }

        private DataChannelManager? m_dataChannels;
        private readonly DataChannelSequenceBudget m_sequenceBudget = new();
        private long m_sequenceBudgetBaseline;
        private bool m_isDataChannelSource;
    }

    /// <summary>
    /// Maps server-side SecureChannel identifiers to their UASC channels so
    /// the DataChannel Service Set can bind an accepted OpenDataChannel to the
    /// transport that will carry its STR frames.
    /// </summary>
    public static class UaSCDataChannelSecureChannelRegistry
    {
        /// <summary>
        /// Finds the server-side UASC channel that owns a SecureChannel.
        /// </summary>
        public static bool TryGet(
            string secureChannelId,
            out UaSCUaBinaryChannel? channel)
        {
            return s_channels.TryGetValue(secureChannelId, out channel);
        }

        internal static void Bind(string secureChannelId, UaSCUaBinaryChannel channel)
        {
            if (!string.IsNullOrEmpty(secureChannelId))
            {
                s_channels[secureChannelId] = channel;
            }
        }

        internal static void Unbind(string secureChannelId, UaSCUaBinaryChannel channel)
        {
            if (!string.IsNullOrEmpty(secureChannelId) &&
                s_channels.TryGetValue(secureChannelId, out UaSCUaBinaryChannel? current) &&
                ReferenceEquals(current, channel))
            {
                s_channels.TryRemove(secureChannelId, out _);
            }
        }

        private static readonly ConcurrentDictionary<string, UaSCUaBinaryChannel> s_channels = new();
    }
}
