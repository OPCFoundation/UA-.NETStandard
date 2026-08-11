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
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Carries MessageTypes that are neither Service calls nor part of
    /// establishing the SecureChannel, on behalf of a registered extension.
    /// </summary>
    public partial class UaSCUaBinaryChannel : ISecureChannelMessageHost
    {
        /// <inheritdoc/>
        int ISecureChannelMessageHost.SendBufferSize => SendBufferSize;

        /// <inheritdoc/>
        int ISecureChannelMessageHost.ReceiveBufferSize => ReceiveBufferSize;

        /// <inheritdoc/>
        int ISecureChannelMessageHost.SymmetricSignatureSize => SymmetricSignatureSize;

        /// <inheritdoc/>
        BufferManager ISecureChannelMessageHost.BufferManager => BufferManager;

        /// <inheritdoc/>
        TimeProvider ISecureChannelMessageHost.TimeProvider => TimeProvider;

        /// <inheritdoc/>
        SequenceNumberBudget ISecureChannelMessageHost.SequenceBudget => SequenceBudget;

        /// <summary>
        /// Tracks how much of the SecureChannel SequenceNumber space remains
        /// under the current SecurityToken. Every MessageType the channel
        /// carries draws on the same space.
        /// </summary>
        public SequenceNumberBudget SequenceBudget
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
        /// Registers the extension that owns a MessageType on this channel.
        /// </summary>
        /// <remarks>
        /// The first registration for a MessageType wins, so a caller that
        /// races another gets the instance already in place and can discard
        /// the one it built.
        /// </remarks>
        /// <param name="extension">The extension.</param>
        /// <returns>The registered extension, which is <paramref name="extension"/>
        /// unless another was registered first.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="extension"/> is <c>null</c>.</exception>
        public ISecureChannelMessageExtension RegisterMessageExtension(
            ISecureChannelMessageExtension extension)
        {
            if (extension == null)
            {
                throw new ArgumentNullException(nameof(extension));
            }

            return m_messageExtensions.GetOrAdd(extension.MessageType, extension);
        }

        /// <summary>
        /// Returns the extension that owns a MessageType, if one is registered.
        /// </summary>
        /// <param name="messageType">The MessageType, without the chunk type.</param>
        /// <param name="extension">The extension.</param>
        public bool TryGetMessageExtension(
            uint messageType,
            out ISecureChannelMessageExtension? extension)
        {
            return m_messageExtensions.TryGetValue(messageType, out extension);
        }

        /// <inheritdoc/>
        void ISecureChannelMessageHost.Fault(ServiceResult reason)
        {
            OnTransportError(reason);
        }

        /// <inheritdoc/>
        async ValueTask ISecureChannelMessageHost.SendMessageAsync(
            uint messageType,
            uint requestId,
            bool isRequest,
            ArraySegment<byte> body,
            Action? onSecuring,
            CancellationToken ct)
        {
            BufferCollection? chunks = null;
            SendGateTicket? sendTicket = null;
            bool sendTurnAcquired = false;

            try
            {
                // The chunk shares the SecureChannel's symmetric keys and its
                // single monotonic SequenceNumber space with Service traffic,
                // so securing one has to be serialized against the Service path
                // exactly as the Service path serializes against itself.
                // Without this an extension thread and a Service response reach
                // the same HMAC concurrently - which throws outright on Windows,
                // where the CNG hash provider refuses concurrent use - and race
                // for SequenceNumbers, which silently emits duplicates and is
                // fatal to the channel. Only the securing is held here; the send
                // is awaited outside it so a slow peer cannot block Service
                // traffic.
                lock (DataLock)
                {
                    ChannelToken token = CurrentToken
                        ?? throw ServiceResultException.Create(
                            StatusCodes.BadSecureChannelClosed,
                            "The SecureChannel has no active token.");

                    onSecuring?.Invoke();

                    chunks = WriteSymmetricMessage(
                        messageType,
                        requestId,
                        token,
                        body,
                        isRequest,
                        out bool limitsExceeded,
                        out sendTicket);

                    if (limitsExceeded)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadRequestTooLarge,
                            "The message exceeds the negotiated buffer size.");
                    }
                }

                IUaSCByteTransport transport = Transport
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "The transport was closed by the remote application.");

                await AwaitSendTurnAsync(sendTicket, ct).ConfigureAwait(false);
                sendTurnAcquired = true;
                await transport.SendChunkAsync(chunks, ct).ConfigureAwait(false);
            }
            finally
            {
                if (sendTurnAcquired)
                {
                    ReleaseSendTicket(sendTicket!);
                }

                chunks?.Release(BufferManager, nameof(ISecureChannelMessageHost.SendMessageAsync));
            }
        }

        /// <summary>
        /// Routes an incoming chunk of an extension-owned MessageType.
        /// </summary>
        /// <remarks>
        /// The chunk is decrypted, verified and sequence-checked here, so an
        /// extension never sees content the channel has not authenticated. A
        /// MessageType with no registered extension is a protocol error, which
        /// is what OPC 10000-6 §6.7.2.2 requires of a receiver that does not
        /// implement it.
        /// </remarks>
        /// <param name="messageType">The message type and chunk type.</param>
        /// <param name="messageChunk">The chunk.</param>
        /// <param name="isRequest">True when the chunk was sent by the client,
        /// which selects the key set used to verify it.</param>
        /// <returns>False, because this method never takes ownership of the
        /// buffer.</returns>
        protected bool ProcessExtensionMessage(
            uint messageType,
            ArraySegment<byte> messageChunk,
            bool isRequest)
        {
            uint ownedType = messageType & TcpMessageType.MessageTypeMask;

            if (!m_messageExtensions.TryGetValue(ownedType, out ISecureChannelMessageExtension? extension) ||
                extension == null)
            {
                OnTransportError(ServiceResult.Create(
                    StatusCodes.BadTcpMessageTypeInvalid,
                    "No extension is registered for the message type: {0:X8}.",
                    messageType));
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

                if (!VerifySequenceNumber(sequenceNumber, nameof(ProcessExtensionMessage)))
                {
                    extension.OnMessageRejected(ServiceResult.Create(
                        StatusCodes.BadSequenceNumberInvalid,
                        "The SequenceNumber of the chunk is out of order."));
                    return false;
                }
            }
            catch (ServiceResultException e)
            {
                extension.OnMessageRejected(new ServiceResult(e));
                return false;
            }

            extension.OnMessageReceived(new SecureChannelMessage(messageType, requestId, body));
            return false;
        }

        /// <summary>
        /// Tells every registered extension that a new SecurityToken is in
        /// force, which restores the SequenceNumber space it draws on.
        /// </summary>
        private protected void NotifySecurityTokenActivated()
        {
            Interlocked.Exchange(
                ref m_sequenceNumberBaseline,
                Interlocked.Read(ref m_sequenceNumber));

            m_sequenceBudget.OnTokenActivated();

            if (m_messageExtensions.IsEmpty)
            {
                return;
            }

            foreach (ISecureChannelMessageExtension extension in m_messageExtensions.Values)
            {
                extension.OnSecurityTokenActivated();
            }
        }

        /// <summary>
        /// Tells every registered extension that the SecureChannel is gone.
        /// </summary>
        private protected void NotifyChannelClosed()
        {
            if (m_messageExtensions.IsEmpty)
            {
                return;
            }

            foreach (ISecureChannelMessageExtension extension in m_messageExtensions.Values)
            {
                extension.OnChannelClosed();
            }

            m_messageExtensions.Clear();
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

        private readonly ConcurrentDictionary<uint, ISecureChannelMessageExtension> m_messageExtensions
            = new();
        private readonly SequenceNumberBudget m_sequenceBudget = new();
        private long m_sequenceNumberBaseline;
    }
}
