/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Carries data channels over the SecureChannel using the inline framing of
    /// the Part 6 errata: a frame is one STR MessageChunk, secured and written
    /// exactly as a MSG chunk is.
    /// </summary>
    public partial class UaSCUaBinaryChannel
    {
        /// <summary>
        /// Tracks how much of the SecureChannel SequenceNumber space remains
        /// under the current SecurityToken. Every MessageType the channel
        /// carries draws on the same space.
        /// </summary>
        internal SequenceNumberBudget SequenceBudget
        {
            get
            {
                m_sequenceBudget.ObserveConsumed(SequenceNumbersIssuedUnderCurrentToken);
                return m_sequenceBudget;
            }
        }

        /// <summary>
        /// True when the SequenceNumber space remaining under the current
        /// token has fallen below the renewal threshold, so the owning channel
        /// should initiate OpenSecureChannel with RenewalRequest ahead of the
        /// normal lifetime based renewal.
        /// </summary>
        public bool IsSequenceRenewalDue => SequenceBudget.ShouldRenew;

        /// <summary>
        /// The data channels multiplexed onto this SecureChannel, or
        /// <c>null</c> when the feature has not been enabled on it.
        /// </summary>
        public DataChannelManager? DataChannels => m_dataChannels;

        /// <summary>
        /// Raised when a frame violates the framing rules badly enough to cost
        /// the whole SecureChannel.
        /// </summary>
        /// <remarks>
        /// The channel is faulted either way; this reports which rule was
        /// broken, which the transport error alone does not carry.
        /// </remarks>
        public event EventHandler<DataChannelFrameError>? DataChannelProtocolFault;

        /// <summary>
        /// Enables the data channel feature on this SecureChannel.
        /// </summary>
        /// <remarks>
        /// Experimental. Until this is called the STR dispatch is inert and an
        /// incoming frame closes the SecureChannel, which is what the
        /// interoperability rule of the Part 6 errata §5.16 requires of a peer
        /// that does not implement this specification. Calling it more than
        /// once returns the manager already in place.
        /// </remarks>
        /// <param name="isServer">True on the server side, which allocates
        /// ChannelIds.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="maxDataChannels">The most channels kept open at once
        /// on this SecureChannel.</param>
        /// <param name="maxCreditPerChannel">The largest window granted to one
        /// channel.</param>
        public DataChannelManager EnableDataChannels(
            bool isServer,
            ITelemetryContext telemetry,
            ushort maxDataChannels = 16,
            uint maxCreditPerChannel = 1024 * 1024)
        {
            using (Gate.Enter())
            {
                if (m_dataChannels != null)
                {
                    return m_dataChannels;
                }

                m_dataChannelsAreSource = isServer;
                m_dataChannels = new DataChannelManager(
                    new InlineDataChannelTransport(this),
                    isServer,
                    telemetry,
                    maxDataChannels,
                    maxCreditPerChannel);

                return m_dataChannels;
            }
        }

        /// <summary>
        /// The largest secured body a data channel frame may occupy on this
        /// SecureChannel.
        /// </summary>
        /// <remarks>
        /// Computed with the arithmetic <c>WriteSymmetricMessage</c> uses to
        /// split a message into chunks rather than an approximation of it,
        /// because a frame at this size has to fit exactly one chunk. A second
        /// chunk would carry the Intermediate chunk type, which the frame codec
        /// rejects outright, and the send would be refused after the
        /// SequenceNumber had already been claimed. The part an approximation
        /// misses is the cipher block rounding: a 65535 byte send buffer under
        /// a 16 byte block cipher loses its last 15 bytes, so a body sized
        /// against the raw buffer overflows into a second chunk.
        /// </remarks>
        public int MaxDataChannelBodySize
        {
            get
            {
                int blockSize = EncryptionBlockSize > 0 ? EncryptionBlockSize : 1;
                int maxCipherTextSize = SendBufferSize - TcpMessageLimits.SymmetricHeaderSize;
                int maxPlainTextSize = maxCipherTextSize / blockSize * blockSize;

                // Conservative while no token is in force: assume padding is
                // present, so the advertised size is never larger than what a
                // later token can actually carry.
                int paddingCountSize =
                    SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                    CurrentToken?.SecurityPolicy?.NoSymmetricEncryptionPadding == true
                        ? 0
                        : blockSize > byte.MaxValue ? 2 : 1;

                int available = maxPlainTextSize -
                    SymmetricSignatureSize -
                    TcpMessageLimits.SequenceHeaderSize -
                    paddingCountSize -
                    DataChannelConstants.StreamHeaderSize -
                    DataChannelConstants.DeadlineSize;

                return available > 0 ? available : 0;
            }
        }

        /// <summary>
        /// Routes an incoming STR chunk to the data channels.
        /// </summary>
        /// <remarks>
        /// The chunk is decrypted, verified and sequence-checked here, so the
        /// engine never sees content the channel has not authenticated. A STR
        /// chunk arriving before the feature is enabled is a protocol error,
        /// which is what OPC 10000-6 §6.7.2.2 requires of a receiver that does
        /// not implement the MessageType.
        /// </remarks>
        /// <param name="messageType">The message type and chunk type.</param>
        /// <param name="messageChunk">The chunk.</param>
        /// <param name="isRequest">True when the chunk was sent by the client,
        /// which selects the key set used to verify it.</param>
        /// <returns>False, because this method never takes ownership of the
        /// buffer.</returns>
        protected bool ProcessDataChannelMessage(
            uint messageType,
            ArraySegment<byte> messageChunk,
            bool isRequest)
        {
            DataChannelFrameError? fatalError = null;

            using (Gate.Enter())
            {
                DataChannelManager? channels = m_dataChannels;

                if (channels == null)
                {
                    fatalError = DataChannelFrameError.MalformedHeader;
                }
                else
                {
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

                        if (!VerifySequenceNumber(sequenceNumber, nameof(ProcessDataChannelMessage)))
                        {
                            fatalError = DataChannelFrameError.MalformedHeader;
                        }
                    }
                    catch (ServiceResultException)
                    {
                        fatalError = DataChannelFrameError.MalformedHeader;
                        body = default;
                        requestId = 0;
                    }

                    if (fatalError == null &&
                        !DataChannelFrameCodec.TryValidateChunkHeaders(
                            messageType,
                            requestId,
                            out DataChannelFrameError headerError))
                    {
                        fatalError = headerError;
                    }

                    DataChannelFrame frame = default;

                    if (fatalError == null &&
                        !DataChannelFrameCodec.TryDecode(
                            new ReadOnlyMemory<byte>(body.Array!, body.Offset, body.Count),
                            ReceiveBufferSize,
                            out frame,
                            out DataChannelFrameError error))
                    {
                        if (error.IsFatal())
                        {
                            fatalError = error;
                        }
                        else if (channels.TryGetChannel(frame.ChannelId, out DataChannel? faulted) &&
                            faulted != null)
                        {
                            faulted.Reset(error.ToStatusCode());
                        }
                    }
                    else if (fatalError == null)
                    {
                        channels.HandleFrame(frame);
                    }
                }
            }

            if (fatalError != null)
            {
                OnDataChannelProtocolFault(fatalError.Value);
            }

            return false;
        }

        /// <summary>
        /// Secures a data channel frame under the channel's keys and writes it
        /// as one STR chunk.
        /// </summary>
        /// <remarks>
        /// Assigning the SequenceNumber and applying message security are
        /// serialized against the Service traffic on the same channel, because
        /// both draw on the same keys and the same counter. The SequenceNumber
        /// is claimed inside that serialization and the send is refused with
        /// <c>Bad_SecureChannelTokenUnknown</c> when the space under the
        /// current SecurityToken is exhausted, so a sender stalls rather than
        /// reuse a number (Part 6 errata §5.1.1). The write itself is awaited
        /// outside it, so a slow peer on a data channel cannot stall Service
        /// traffic.
        /// </remarks>
        /// <param name="frame">The frame to write.</param>
        /// <param name="ct">Cancellation token.</param>
        internal async ValueTask SendDataChannelFrameAsync(
            DataChannelFrame frame,
            CancellationToken ct)
        {
            int size = frame.EncodedSize;
            byte[] encoded = BufferManager.TakeBuffer(size, nameof(SendDataChannelFrameAsync), ct);
            BufferCollection? chunks = null;
            SendGateTicket? sendTicket = null;

            try
            {
                int written = DataChannelFrameCodec.Encode(encoded.AsSpan(0, size), frame);

                using (await Gate.EnterAsync(ct).ConfigureAwait(false))
                {
                    ChannelToken token = CurrentToken
                        ?? throw ServiceResultException.Create(
                            StatusCodes.BadSecureChannelClosed,
                            "The SecureChannel has no active token.");

                    if (!SequenceBudget.TryConsume())
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadSecureChannelTokenUnknown,
                            "The SequenceNumber space under the current SecurityToken is exhausted.");
                    }

                    chunks = WriteSymmetricMessage(
                        TcpMessageType.Stream,
                        DataChannelConstants.FrameRequestId,
                        token,
                        new ArraySegment<byte>(encoded, 0, written),
                        !m_dataChannelsAreSource,
                        out bool limitsExceeded,
                        out sendTicket);

                    if (limitsExceeded)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadDataChannelLimitsExceeded,
                            "The data channel frame exceeds the negotiated buffer size.");
                    }
                }

                IUaSCByteTransport transport = Transport
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "The transport was closed by the remote application.");

                await AwaitSendTurnAsync(sendTicket, ct).ConfigureAwait(false);
                await transport.SendChunkAsync(chunks, ct).ConfigureAwait(false);
            }
            finally
            {
                // The ticket is released on every path out, not only the one
                // that reached the transport. WriteSymmetricMessage hands its
                // ticket to the caller even when it reports limitsExceeded, and
                // a ticket that is never completed becomes the tail every later
                // send chains behind — which would stall MSG, OPN and CLO for
                // every Session on this SecureChannel, silently and for good,
                // because the scheduler logs a send fault rather than faulting
                // the channel.
                if (sendTicket != null)
                {
                    ReleaseSendTicket(sendTicket);
                }

                chunks?.Release(BufferManager, nameof(SendDataChannelFrameAsync));
                BufferManager.ReturnBuffer(encoded, nameof(SendDataChannelFrameAsync));
            }
        }

        /// <summary>
        /// Reports a framing violation whose blast radius is the whole
        /// SecureChannel.
        /// </summary>
        /// <param name="error">The rule that was broken.</param>
        internal void OnDataChannelProtocolFault(DataChannelFrameError error)
        {
            DataChannelProtocolFault?.Invoke(this, error);

            OnTransportError(ServiceResult.Create(
                StatusCodes.BadTcpMessageTypeInvalid,
                "A data channel frame violated the framing rules: {0}.",
                error));
        }

        /// <summary>
        /// Rebases the SequenceNumber budget when a new SecurityToken takes
        /// effect, which restores the space the channel draws on.
        /// </summary>
        private protected void NotifySecurityTokenActivated()
        {
            Interlocked.Exchange(
                ref m_sequenceNumberBaseline,
                Interlocked.Read(ref m_sequenceNumber));

            m_sequenceBudget.OnTokenActivated();
        }

        /// <summary>
        /// Aborts every data channel when the SecureChannel is gone.
        /// </summary>
        private protected void NotifyChannelClosed()
        {
            DataChannelManager? channels = Interlocked.Exchange(ref m_dataChannels, null);

            if (channels == null)
            {
                return;
            }

            // Abort first, so every channel reaches Faulted and its source is
            // told synchronously. Disposing the manager stops its scheduler and
            // is awaited off this path, because Dispose cannot block.
            channels.AbortAll(StatusCodes.BadSecureChannelClosed);
            _ = channels.DisposeAsync().AsTask();
        }

        /// <summary>
        /// How many SequenceNumbers this channel has issued under the
        /// SecurityToken currently in force.
        /// </summary>
        /// <remarks>
        /// The counter runs for the lifetime of the channel while the space is
        /// per token, so what has been consumed under the current token is the
        /// distance from the value the counter held when that token was
        /// activated. Observing the raw counter would leave a long lived channel
        /// looking permanently exhausted.
        /// </remarks>
        internal long SequenceNumbersIssuedUnderCurrentToken
        {
            get
            {
                long issued = Interlocked.Read(ref m_sequenceNumber);
                long baseline = Interlocked.Read(ref m_sequenceNumberBaseline);

                if (issued < baseline)
                {
                    // The space wrapped under this token, so the count restarts
                    // from the new origin rather than going negative.
                    Interlocked.Exchange(ref m_sequenceNumberBaseline, 0);
                    return issued;
                }

                return issued - baseline;
            }
        }

        /// <summary>
        /// Presents the SecureChannel to the engine as the transport that
        /// carries its frames.
        /// </summary>
        /// <param name="channel">The SecureChannel.</param>
        private sealed class InlineDataChannelTransport(UaSCUaBinaryChannel channel)
            : IDataChannelTransport
        {
            /// <inheritdoc/>
            public DataChannelFramingMode FramingMode => DataChannelFramingMode.Inline;

            /// <inheritdoc/>
            public int MaxFrameBodySize => channel.MaxDataChannelBodySize;

            /// <inheritdoc/>
            public bool HasTransportFlowControl => false;

            /// <inheritdoc/>
            public BufferManager BufferManager => channel.BufferManager;

            /// <inheritdoc/>
            public TimeProvider TimeProvider => channel.TimeProvider;

            /// <inheritdoc/>
            public ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
            {
                return channel.SendDataChannelFrameAsync(frame, ct);
            }

            /// <inheritdoc/>
            public void OnProtocolFault(DataChannelFrameError error)
            {
                channel.OnDataChannelProtocolFault(error);
            }
        }

        private readonly SequenceNumberBudget m_sequenceBudget = new();

        // CA2213: the manager is disposed by NotifyChannelClosed, which Dispose
        // calls. The analyzer cannot follow the Interlocked.Exchange that hands
        // ownership over, and the disposal is asynchronous because the manager
        // is IAsyncDisposable while Dispose is not.
#pragma warning disable CA2213
        private DataChannelManager? m_dataChannels;
#pragma warning restore CA2213
        private bool m_dataChannelsAreSource;
        private long m_sequenceNumberBaseline;
    }
}
