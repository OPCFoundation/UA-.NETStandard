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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

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
        /// Gets whether only continuation or abort chunks of the current channel MSG may arrive.
        /// </summary>
        private protected bool HasIncompleteDataChannelMessage => m_dataChannelIncomingKind.HasValue;

        /// <summary>
        /// Gets the explicitly attached data context, or null on ordinary endpoints.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
            UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
        public DataChannelContext? DataChannels => m_dataChannels;

        /// <summary>
        /// Reports a carrier-scoped flow protocol failure without exposing peer payloads.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
            UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
        public event Action<DataChannelResult>? DataChannelProtocolFault;

        /// <summary>
        /// Attaches after profile selection and authenticated OPN. The caller holds its handshake
        /// serialization; this method does not reacquire Gate and never starts network traffic.
        /// Call the returned context's StartAsync outside Gate after ingress has been attached.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
            UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
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
        [System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
            UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
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
            if (!FlowModel.IsUasc(kind))
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
            var carrier = new UascDataChannelCarrier(this, kind, identity);
            DataChannelContext context;
            if (kind == DataChannelCarrierKind.WebTransport)
            {
                if (Transport is not IDataChannelUascManagementProvider provider ||
                    SecurityMode != MessageSecurityMode.SignAndEncrypt)
                {
                    throw new DataChannelException(DataChannelResult.Unsupported);
                }
                context = provider.CreateContext(carrier, factory);
            }
            else
            {
                context = factory.Create(carrier);
            }
            if (context == null || !context.UsesCarrierIdentity(kind, identity) ||
                (kind != DataChannelCarrierKind.WebTransport && !context.UsesCarrier(carrier)))
            {
                throw new DataChannelException(DataChannelResult.InvalidState);
            }
            m_dataChannelKind = kind;
            m_dataChannelIsUascInitiator = isUascInitiator;
            m_dataChannels = context;
            if (Transport is IUaSCByteTransportSendProgress progress)
            {
                progress.SetSendProgressCallback(OnTransportSendProgress);
            }
            return m_dataChannels;
        }

        /// <summary>
        /// Gets complete MSG plaintext capacity, including the application TypeId but excluding security overhead.
        /// Both negotiated message size and chunk count apply; legacy management remains single-chunk.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
            UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
        public int GetMaxDataChannelRecordSize(DataChannelCarrierKind kind)
        {
            if (!FlowModel.IsUasc(kind))
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
                paddingCountSize;
            if (kind == DataChannelCarrierKind.WebTransport)
            {
                return Math.Max(0, available);
            }
            int messageLimit = m_dataChannelIsUascInitiator ? MaxRequestMessageSize : MaxResponseMessageSize;
            int chunkLimit = m_dataChannelIsUascInitiator ? MaxRequestChunkCount : MaxResponseChunkCount;
            long maximum = Math.Max(0, available) * (long)(chunkLimit > 0 ? chunkLimit : int.MaxValue);
            if (messageLimit > 0)
            {
                maximum = Math.Min(maximum, messageLimit);
            }
            return (int)Math.Min(maximum, int.MaxValue - FlowRecordCodec.HeaderSize);
        }

        /// <summary>
        /// Classifies MSG only after the caller has verified its security and sequence header.
        /// Reports whether the reassembler took ownership of the receive-loop buffer.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected bool TryProcessVerifiedDataChannelMessage(
            uint messageType,
            uint requestId,
            ArraySegment<byte> messageBody,
            bool isRequest,
            out bool ownsBuffer)
        {
            ownsBuffer = false;
            if (!TcpMessageType.IsType(messageType, TcpMessageType.Message))
            {
                if (HasIncompleteDataChannelMessage)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError,
                        "Another message cannot interrupt incomplete Data Channel MSG chunks.");
                }
                return false;
            }
            DataChannelContext? context = m_dataChannels;
            if (context == null)
            {
                if (requestId == 0 && messageBody.Count >= UascFlowMessageCodec.TypeIdSize &&
                    messageBody.AsSpan()[0] == 0 && messageBody.AsSpan()[1] is 254 or 255)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError,
                        "A channel MSG requires an explicitly selected Data Channel capability.");
                }
                return false;
            }
            context.RecordVerifiedMessage();
            if (m_dataChannelKind == DataChannelCarrierKind.WebTransport)
            {
                context.QueueVerifiedUasc(
                    DataChannelUascMessageKind.Msg, (byte)(messageType >> 24),
                    requestId, new ByteString(messageBody.AsMemory()));
                return true;
            }
            if (requestId != 0)
            {
                if (m_dataChannelIncomingKind.HasValue ||
                    (!HasPartialMessage && messageBody.Count < UascFlowMessageCodec.TypeIdSize) ||
                    (!HasPartialMessage && messageBody.Count >= 2 &&
                        messageBody.AsSpan()[0] == 0 && messageBody.AsSpan()[1] is 254 or 255))
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError,
                        "A channel message requires contiguous RequestId-zero chunks.");
                }
                if (!context.IsReady)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError,
                        "Services are not accepted before protected HELLO confirmation.");
                }
                return false;
            }
            if (SecurityMode == MessageSecurityMode.None)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError,
                    "RequestId zero requires an explicitly selected Data Channel capability.");
            }
            try
            {
                if (TcpMessageType.IsAbort(messageType))
                {
                    if (!m_dataChannelIncomingKind.HasValue)
                    {
                        throw new FlowCodecException(FlowCodecError.WrongType);
                    }
                    ValidateDataChannelAbort(messageBody);
                    m_dataChannelIncomingKind = null;
                    m_dataChannelIncomingBytes = 0;
                    TakeSavedChunks().Release(BufferManager, nameof(TryProcessVerifiedDataChannelMessage));
                    return true;
                }
                if (!m_dataChannelIncomingKind.HasValue)
                {
                    if (HasPartialMessage)
                    {
                        throw new FlowCodecException(FlowCodecError.WrongType);
                    }
                    m_dataChannelIncomingKind = UascFlowMessageCodec.Classify(messageBody.AsSpan());
                    m_dataChannelIncomingBytes = 0;
                }
                if (m_dataChannelIncomingKind == FlowRecordKind.Data && !context.IsReady)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError,
                        "DATA is not accepted before protected HELLO confirmation.");
                }
                m_dataChannelIncomingBytes += messageBody.Count;
                int bodyLimit = m_dataChannelIncomingKind == FlowRecordKind.Control
                    ? context.MaxUascControlBodyBytes : context.MaxUascDataBodyBytes;
                if (m_dataChannelIncomingBytes > bodyLimit)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "The Data Channel message exceeds its configured body limit.");
                }
                if (!TcpMessageType.IsFinal(messageType))
                {
                    ownsBuffer = true;
                    SaveChunk(0, messageBody, isRequest, isRequest, isFinal: false, isDataChannel: true);
                    return true;
                }
                if (!HasPartialMessage)
                {
                    m_dataChannelIncomingKind = null;
                    m_dataChannelIncomingBytes = 0;
                    context.QueueVerifiedUasc(
                        DataChannelUascMessageKind.Msg, (byte)'F', 0, new ByteString(messageBody.AsMemory()));
                    return true;
                }
                ownsBuffer = true;
                SaveChunk(0, messageBody, isRequest, isRequest, isFinal: true, isDataChannel: true);
                m_dataChannelIncomingKind = null;
                m_dataChannelIncomingBytes = 0;
                BufferCollection chunks = DetachSavedChunks(out List<IDisposable>? leases);
                try
                {
                    if (chunks.Count == 0)
                    {
                        return true;
                    }
                    byte[] assembled = new byte[chunks.TotalSize];
                    int offset = 0;
                    foreach (ArraySegment<byte> chunk in chunks)
                    {
                        chunk.AsSpan().CopyTo(assembled.AsSpan(offset));
                        offset += chunk.Count;
                    }
                    context.QueueVerifiedUasc(
                        DataChannelUascMessageKind.Msg, (byte)'F', 0, new ByteString(assembled));
                }
                finally
                {
                    try
                    {
                        chunks.Release(BufferManager, nameof(TryProcessVerifiedDataChannelMessage));
                    }
                    finally
                    {
                        DisposeIsolationLeases(leases);
                    }
                }
                return true;
            }
            catch (FlowCodecException exception)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, exception.Message, exception);
            }
        }

        /// <summary>
        /// Validates the bounded Part 6 abort body independently of the abandoned application body.
        /// </summary>
        private static void ValidateDataChannelAbort(ArraySegment<byte> messageBody)
        {
            ReadOnlySpan<byte> body = messageBody.AsSpan();
            if (body.Length < sizeof(uint) * 2 ||
                !StatusCode.IsBad(BinaryPrimitives.ReadUInt32LittleEndian(body)))
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            int length = BinaryPrimitives.ReadInt32LittleEndian(body[sizeof(uint)..]);
            if (length is < -1 or > 4096 || body.Length != sizeof(uint) * 2 + Math.Max(0, length))
            {
                throw new FlowCodecException(FlowCodecError.InvalidLength);
            }
            try
            {
                s_dataChannelAbortEncoding.GetCharCount(
                    messageBody.GetArray(), messageBody.Offset + sizeof(uint) * 2, Math.Max(0, length));
            }
            catch (DecoderFallbackException)
            {
                throw new FlowCodecException(FlowCodecError.InvalidLength);
            }
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
            bool management = m_dataChannelKind == DataChannelCarrierKind.WebTransport;
            if (m_dataChannels == null || write.Path == DataChannelPath.Datagram ||
                (management && (write.Path != DataChannelPath.Management || write.IsData)))
            {
                return DataChannelResult.Unsupported;
            }
            int maximum = GetMaxDataChannelRecordSize(m_dataChannelKind);
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
                            TcpMessageType.Message,
                            write.RequestId,
                            encoded,
                            m_dataChannelIsUascInitiator,
                            out bool limitsExceeded);
                        chunks = message.Chunks;
                        if (limitsExceeded)
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
            if (m_transport is IUaSCByteTransportSendProgress progress)
            {
                progress.SetSendProgressCallback(null);
            }
            if (m_dataChannelIncomingKind.HasValue)
            {
                m_dataChannelIncomingKind = null;
                m_dataChannelIncomingBytes = 0;
                TakeSavedChunks().Release(BufferManager, nameof(NotifyChannelClosed));
            }
            DataChannelContext? context = Interlocked.Exchange(ref m_dataChannels, null);
            if (context != null)
            {
                context.CarrierClosed(DataChannelResult.Closed);
                _ = DisposeDataChannelContextAsync(context);
            }
            m_dataChannelIncomingKind = null;
            m_dataChannelTokenChanged.Complete();
        }

        /// <summary>
        /// Forwards positive byte acceptance to the active data context's send-stall watchdog.
        /// </summary>
        private void OnTransportSendProgress(int bytesWritten)
        {
            m_dataChannels?.NotifyUascSendProgress(bytesWritten);
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
            DataChannelPeerIdentity identity) : IDataChannelCarrier, IDataChannelDirectBindingProvider,
                IDataChannelDirectStreamProvider, IDataChannelDirectDatagramProvider
        {
            public DataChannelCarrierKind Kind => kind;
            public DataChannelPeerIdentity Identity => identity;
            public int MaxRecordSize => channel.GetMaxDataChannelRecordSize(kind);
            public int MaxDatagramSize => 0;
            public ulong ManagementStreamId => 0;
            public bool IsAvailable => DirectBindingProvider?.IsAvailable == true;
            public string TransportProfileUri => DirectBindingProvider?.TransportProfileUri ?? string.Empty;
            public string Capability => DirectBindingProvider?.Capability ?? string.Empty;
            public string Authority => DirectBindingProvider?.Authority ?? string.Empty;
            public string Path => DirectBindingProvider?.Path ?? string.Empty;
            public bool AreDirectStreamsAvailable =>
                DirectBindingProvider is IDataChannelDirectStreamProvider { AreDirectStreamsAvailable: true };

            public int MaxDirectDatagramSize => DirectBindingProvider is IDataChannelDirectDatagramProvider provider
                ? provider.MaxDirectDatagramSize : 0;

            public ValueTask<DataChannelResult> SendDirectDatagramAsync(
                DataChannelWrite write,
                CancellationToken cancellationToken = default)
            {
                return DirectBindingProvider is IDataChannelDirectDatagramProvider provider
                    ? provider.SendDirectDatagramAsync(write, cancellationToken)
                    : throw new DataChannelException(DataChannelResult.Unsupported);
            }

            public ValueTask<ByteString> ReceiveDirectDatagramAsync(CancellationToken cancellationToken = default)
            {
                return DirectBindingProvider is IDataChannelDirectDatagramProvider provider
                    ? provider.ReceiveDirectDatagramAsync(cancellationToken)
                    : throw new DataChannelException(DataChannelResult.Unsupported);
            }

            public ValueTask<Stream> OpenDirectStreamAsync(
                Guid associationToken,
                CancellationToken cancellationToken = default)
            {
                return DirectBindingProvider is IDataChannelDirectStreamProvider provider
                    ? provider.OpenDirectStreamAsync(associationToken, cancellationToken)
                    : throw new DataChannelException(DataChannelResult.Unsupported);
            }

            public ValueTask<Stream> AcceptDirectStreamAsync(
                Guid associationToken,
                CancellationToken cancellationToken = default)
            {
                return DirectBindingProvider is IDataChannelDirectStreamProvider provider
                    ? provider.AcceptDirectStreamAsync(associationToken, cancellationToken)
                    : throw new DataChannelException(DataChannelResult.Unsupported);
            }

            public ValueTask<ByteString> ExportKeyingMaterialAsync(
                ByteString transcriptDigest,
                CancellationToken cancellationToken = default)
            {
                return DirectBindingProvider?.ExportKeyingMaterialAsync(transcriptDigest, cancellationToken) ??
                    throw new DataChannelException(DataChannelResult.Unsupported);
            }

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

            private IDataChannelDirectBindingProvider? DirectBindingProvider =>
                channel.Transport as IDataChannelDirectBindingProvider;
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

        /// <summary>
        /// The remembered body classification while contiguous RequestId-zero chunks are incomplete.
        /// </summary>
        private FlowRecordKind? m_dataChannelIncomingKind;

        /// <summary>
        /// Counts plaintext bytes before accepting any additional chunk into the reassembly budget.
        /// </summary>
        private long m_dataChannelIncomingBytes;

        /// <summary>
        /// Strict UTF-8 validation for the bounded Part 6 abort reason.
        /// </summary>
        private static readonly UTF8Encoding s_dataChannelAbortEncoding = new(false, true);
    }
}
