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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Attaches the common flow runtime to an explicitly selected, authenticated UASC profile.
    /// Endpoint/handshake selection remains with the transport owner; this never upgrades an ordinary endpoint.
    /// </summary>
    public partial class UaSCUaBinaryChannel
    {
        /// <summary>
        /// Gets remaining per-token sequence space shared by every UASC message type.
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
        /// Gets whether the owner should renew before exhausting sequence space.
        /// </summary>
        public bool IsSequenceRenewalDue => SequenceBudget.ShouldRenew;

        /// <summary>
        /// Gets the explicitly attached data context, or null on ordinary endpoints.
        /// </summary>
        public DataChannelContext? DataChannels => m_dataChannels;

        /// <summary>
        /// Reports a carrier-scoped flow protocol failure without exposing peer payloads.
        /// </summary>
        public event Action<DataChannelResult>? DataChannelProtocolFault;

        /// <summary>
        /// Attaches after profile selection and authenticated OPN. The caller holds its handshake
        /// serialization; this method does not reacquire Gate and never starts network traffic.
        /// Call the returned context's StartAsync outside Gate after ingress has been attached.
        /// </summary>
        public DataChannelContext AttachDataChannels(
            DataChannelCarrierKind kind,
            DataChannelPeerIdentity identity,
            bool isUascInitiator,
            IDataChannelAuthorizer authorizer,
            DataChannelOptions options,
            DataChannelApplicationBudget budget,
            ITelemetryContext telemetry)
        {
            return AttachDataChannels(kind, identity, isUascInitiator,
                new DataChannelContextFactory(authorizer, options, budget, TimeProvider, telemetry));
        }

        /// <summary>
        /// Attaches an application factory's unstarted context after the selected profile's OPN.
        /// Identity/key validation is the handshake owner's responsibility; claimed HELLO values are insufficient.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="identity"/> is <c>null</c>.</exception>
        /// <exception cref="DataChannelException"></exception>
        /// <exception cref="NotSupportedException"></exception>
        public DataChannelContext AttachDataChannels(
            DataChannelCarrierKind kind,
            DataChannelPeerIdentity identity,
            bool isUascInitiator,
            IDataChannelContextFactory factory)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            if (kind is not (DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc))
            {
                throw new DataChannelException(DataChannelResult.Unsupported);
            }
            if (m_dataChannels != null)
            {
                throw new DataChannelException(DataChannelResult.InvalidState);
            }
            if (SecurityMode == MessageSecurityMode.None ||
                CurrentToken == null ||
                (SecurityMode == MessageSecurityMode.SignAndEncrypt
                    ? identity.Protection != DataChannelProtectionMode.Confidential
                    : identity.Protection != DataChannelProtectionMode.Integrity))
            {
                throw new DataChannelException(DataChannelResult.SecurityFailure);
            }
            if (kind == DataChannelCarrierKind.SharedUasc && identity.IsTransportInitiator != isUascInitiator)
            {
                throw new NotSupportedException(
                    "Reversed shared-UASC roles need a specified protected context-selection exchange before first HELLO.");
            }
            var carrier = new UascDataChannelCarrier(this, kind, identity);
            DataChannelContext context = factory.Create(carrier);
            if (context == null || !context.UsesCarrier(carrier))
            {
                throw new DataChannelException(DataChannelResult.InvalidState);
            }
            m_dataChannelKind = kind;
            m_dataChannelIsUascInitiator = isUascInitiator;
            m_dataChannels = context;
            return m_dataChannels;
        }

        /// <summary>
        /// Gets exact complete-record capacity using the same cipher-block arithmetic as UASC encoding.
        /// The common record header is included; FLO ChannelId and UASC security overhead are excluded.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public int GetMaxDataChannelRecordSize(DataChannelCarrierKind kind)
        {
            if (kind is not (DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            int blockSize = EncryptionBlockSize > 0 ? EncryptionBlockSize : 1;
            int maxCipherTextSize = SendBufferSize - TcpMessageLimits.SymmetricHeaderSize;
            int maxPlainTextSize = maxCipherTextSize / blockSize * blockSize;
            int paddingCountSize =
                SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                CurrentToken?.SecurityPolicy?.NoSymmetricEncryptionPadding == true
                    ? 0 : blockSize > byte.MaxValue ? 2 : 1;
            int available = maxPlainTextSize -
                SymmetricSignatureSize -
                TcpMessageLimits.SequenceHeaderSize -
                paddingCountSize -
                4;
            return Math.Max(0, available);
        }

        /// <summary>
        /// Verifies UASC security and sequence state before handing bounded owned plaintext to the flow runtime.
        /// Returns false because the receive-loop buffer is never retained.
        /// </summary>
        protected bool ProcessDataChannelMessage(
            uint messageType,
            ArraySegment<byte> messageChunk,
            bool isRequest)
        {
            DataChannelContext? context = m_dataChannels;
            if (context == null ||
                (messageType & TcpMessageType.MessageTypeMask) != TcpMessageType.Flow ||
                SecurityMode == MessageSecurityMode.None)
            {
                OnDataChannelProtocolFault(DataChannelResult.Unsupported);
                return false;
            }
            try
            {
                ArraySegment<byte> body = ReadSymmetricMessage(
                    messageChunk, isRequest, out ChannelToken _, out uint requestId, out uint sequence);
                if (!VerifySequenceNumber(sequence, nameof(ProcessDataChannelMessage)))
                {
                    OnDataChannelProtocolFault(DataChannelResult.SecurityFailure);
                    return false;
                }
                context.QueueVerifiedUasc(
                    (byte)(messageType >> 24), requestId, new ByteString(body.AsMemory()));
            }
            catch (Exception exception) when (
                exception is ServiceResultException or ArgumentException or CryptographicException)
            {
                OnDataChannelProtocolFault(DataChannelResult.SecurityFailure);
            }
            return false;
        }

        /// <summary>
        /// Queues unsealed flow bytes in the existing writer. Actual TryCommit, numbering and security
        /// run together in SealWriteBatch, after Service precedence and before immutable secured ordering.
        /// Cancellation after enqueue cannot release writer-owned chunks or create sequence gaps.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="write"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<DataChannelResult> SendDataChannelWriteAsync(
            DataChannelWrite write,
            CancellationToken cancellationToken)
        {
            if (write == null)
            {
                throw new ArgumentNullException(nameof(write));
            }
            if (m_dataChannels == null || write.Path == DataChannelPath.Datagram)
            {
                return DataChannelResult.Unsupported;
            }
            int maximum = GetMaxDataChannelRecordSize(m_dataChannelKind) + 4;
            if (write.Payload.Length > maximum)
            {
                return DataChannelResult.TooLarge;
            }
            if (!MemoryMarshal.TryGetArray(write.Payload.Memory, out ArraySegment<byte> encoded))
            {
                return DataChannelResult.InvalidArgument;
            }
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (m_dataChannels == null)
                {
                    return DataChannelResult.Closed;
                }
                Task renewed = m_dataChannelTokenChanged.Task;
                if (SequenceBudget.MustStall)
                {
                    await renewed.WaitAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                BufferCollection? chunks = null;
                var operation = new DataChannelWriteOperation(write);
                try
                {
                    using (await Gate.EnterAsync(cancellationToken).ConfigureAwait(false))
                    {
                        EncodedSymmetricMessage message = EncodeSymmetricMessage(
                            TcpMessageType.Flow,
                            write.RequestId,
                            encoded,
                            m_dataChannelIsUascInitiator,
                            out bool limitsExceeded);
                        chunks = message.Chunks;
                        if (limitsExceeded || chunks.Count != 1)
                        {
                            return DataChannelResult.TooLarge;
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!BeginWriteSymmetricMessage(message, operation))
                        {
                            return DataChannelResult.Closed;
                        }
                        chunks = null;
                    }
                    ServiceResult result = await operation.Completion.ConfigureAwait(false);
                    if (ServiceResult.IsBad(result))
                    {
                        throw new ServiceResultException(result);
                    }
                    if (operation.Result == DataChannelResult.WouldBlock && !write.IsCommitted)
                    {
                        await renewed.WaitAsync(cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    return operation.Result;
                }
                finally
                {
                    chunks?.Release(BufferManager, nameof(SendDataChannelWriteAsync));
                }
            }
        }

        /// <summary>
        /// Joins writer completion without giving caller cancellation ownership of queued buffers.
        /// </summary>
        private sealed class DataChannelWriteOperation(DataChannelWrite write)
        {
            /// <summary>
            /// Gets the exact flow commitment cut-off.
            /// </summary>
            internal DataChannelWrite Write { get; } = write;

            /// <summary>
            /// Gets physical writer completion, including final buffer release.
            /// </summary>
            internal Task<ServiceResult> Completion => m_completion.Task;

            /// <summary>
            /// Gets the protocol result of pre-security admission.
            /// </summary>
            internal DataChannelResult Result { get; private set; }

            /// <summary>
            /// Records pre-security refusal without reporting a transport failure.
            /// </summary>
            internal void Refuse(DataChannelResult result)
            {
                Result = result;
            }

            /// <summary>
            /// Records actual writer completion.
            /// </summary>
            internal void Complete(ServiceResult result)
            {
                m_completion.TrySetResult(result);
            }

            private readonly TaskCompletionSource<ServiceResult> m_completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Reports a carrier-scoped protocol error and invokes normal UASC shutdown.
        /// </summary>
        internal void OnDataChannelProtocolFault(DataChannelResult result)
        {
            m_dataChannels?.CarrierClosed(result);
            try
            {
                DataChannelProtocolFault?.Invoke(result);
            }
            finally
            {
                OnTransportError(ServiceResult.Create(
                    StatusCodes.BadTcpMessageTypeInvalid, "The selected flow carrier rejected a protocol record."));
            }
        }

        /// <summary>
        /// Rebases sequence usage and wakes flow writers stalled before security commitment.
        /// </summary>
        private protected void NotifySecurityTokenActivated()
        {
            Interlocked.Exchange(ref m_sequenceNumberBaseline, Interlocked.Read(ref m_sequenceNumber));
            m_sequenceBudget.OnTokenActivated();
            TaskSignal previous = Interlocked.Exchange(ref m_dataChannelTokenChanged, new TaskSignal());
            previous.Complete();
        }

        /// <summary>
        /// Synchronously retires the context on transport replacement/loss without reacquiring Gate.
        /// The asynchronous join never frees buffers still owned by the existing writer.
        /// </summary>
        protected void NotifyChannelClosed()
        {
            DataChannelContext? context = Interlocked.Exchange(ref m_dataChannels, null);
            if (context != null)
            {
                context.CarrierClosed(DataChannelResult.Closed);
                _ = DisposeDataChannelContextAsync(context);
            }
            m_dataChannelTokenChanged.Complete();
        }

        /// <summary>
        /// Observes asynchronous context cleanup from the synchronous channel close hook.
        /// </summary>
        private async Task DisposeDataChannelContextAsync(DataChannelContext context)
        {
            try
            {
                await context.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                m_logger.FlowCarrierFailure(exception);
            }
        }

        /// <summary>
        /// Gets actual issuance under the current security token, not channel-lifetime traffic.
        /// </summary>
        internal long SequenceNumbersIssuedUnderCurrentToken
        {
            get
            {
                long issued = Interlocked.Read(ref m_sequenceNumber);
                long baseline = Interlocked.Read(ref m_sequenceNumberBaseline);
                if (issued < baseline)
                {
                    Interlocked.Exchange(ref m_sequenceNumberBaseline, 0);
                    return issued;
                }
                return issued - baseline;
            }
        }

        /// <summary>
        /// Adapts UASC only; native methods refuse rather than fabricate successful support.
        /// </summary>
        private sealed class UascDataChannelCarrier(
            UaSCUaBinaryChannel channel,
            DataChannelCarrierKind kind,
            DataChannelPeerIdentity identity) : IDataChannelCarrier
        {
            public DataChannelCarrierKind Kind => kind;
            public DataChannelPeerIdentity Identity => identity;
            public int MaxRecordSize => channel.GetMaxDataChannelRecordSize(kind);
            public int MaxDatagramSize => 0;
            public ulong ManagementStreamId => 0;

            public ValueTask<ulong> OpenStreamAsync(CancellationToken cancellationToken = default)
            {
                throw new DataChannelException(DataChannelResult.Unsupported);
            }

            public ValueTask<bool> BindStreamAsync(
                ulong streamId,
                uint channelId,
                bool locallyOpened,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(false);
            }

            public ValueTask<DataChannelResult> SendAsync(
                DataChannelWrite write,
                CancellationToken cancellationToken = default)
            {
                return channel.SendDataChannelWriteAsync(write, cancellationToken);
            }

            public ValueTask CloseFlowAsync(
                ulong streamId,
                DataChannelResult result,
                CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask CloseAsync(
                DataChannelResult result,
                CancellationToken cancellationToken = default)
            {
                channel.OnTransportError(ServiceResult.Create(
                    StatusCodes.BadSecureChannelClosed, "The authenticated data context was closed."));
                return default;
            }
        }

        private readonly SequenceNumberBudget m_sequenceBudget;
        private TaskSignal m_dataChannelTokenChanged = new();
        // Disposal is transferred atomically to the asynchronous close join.
        // TODO: Remove this suppression when CA2213 can track Interlocked ownership transfer.
#pragma warning disable CA2213
        private DataChannelContext? m_dataChannels;
#pragma warning restore CA2213
        private DataChannelCarrierKind m_dataChannelKind;
        private bool m_dataChannelIsUascInitiator;
        private long m_sequenceNumberBaseline;
    }
}
