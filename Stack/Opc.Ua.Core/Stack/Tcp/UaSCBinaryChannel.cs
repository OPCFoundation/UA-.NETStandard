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
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Manages the server side of a UA TCP channel.
    /// </summary>
    public partial class UaSCUaBinaryChannel : IDisposable
    {
        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public UaSCUaBinaryChannel(
            string contextId,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            Certificate? serverCertificate,
            List<EndpointDescription>? endpoints,
            MessageSecurityMode securityMode,
            string? securityPolicyUri,
            ITelemetryContext telemetry)
            : this(
                contextId,
                bufferManager,
                quotas,
                null,
                serverCertificate,
                endpoints,
                securityMode,
                securityPolicyUri,
                telemetry,
                null)
        {
        }

        /// <summary>
        /// Attaches the object to an existing socket using the supplied
        /// <see cref="TimeProvider"/> for token-lifetime tracking.
        /// </summary>
        public UaSCUaBinaryChannel(
            string contextId,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            Certificate? serverCertificate,
            List<EndpointDescription>? endpoints,
            MessageSecurityMode securityMode,
            string? securityPolicyUri,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider)
            : this(
                contextId,
                bufferManager,
                quotas,
                null,
                serverCertificate,
                endpoints,
                securityMode,
                securityPolicyUri,
                telemetry,
                timeProvider)
        {
        }

        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        public UaSCUaBinaryChannel(
            string contextId,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            ICertificateRegistry? serverCertificates,
            List<EndpointDescription>? endpoints,
            MessageSecurityMode securityMode,
            string? securityPolicyUri,
            ITelemetryContext telemetry)
            : this(
                contextId,
                bufferManager,
                quotas,
                serverCertificates,
                null,
                endpoints,
                securityMode,
                securityPolicyUri,
                telemetry,
                null)
        {
        }

        /// <summary>
        /// Attaches the object to an existing socket using the supplied
        /// <see cref="TimeProvider"/> for token-lifetime tracking.
        /// </summary>
        public UaSCUaBinaryChannel(
            string contextId,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            ICertificateRegistry? serverCertificates,
            List<EndpointDescription>? endpoints,
            MessageSecurityMode securityMode,
            string? securityPolicyUri,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider)
            : this(
                contextId,
                bufferManager,
                quotas,
                serverCertificates,
                null,
                endpoints,
                securityMode,
                securityPolicyUri,
                telemetry,
                timeProvider)
        {
        }

        /// <summary>
        /// Attaches the object to an existing socket.
        /// </summary>
        private UaSCUaBinaryChannel(
            string contextId,
            BufferManager bufferManager,
            ChannelQuotas quotas,
            ICertificateRegistry? serverCertificates,
            Certificate? serverCertificate,
            List<EndpointDescription>? endpoints,
            MessageSecurityMode securityMode,
            string? securityPolicyUri,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider)
        {
            // create a unique contex if none provided.
            m_contextId = contextId;
            Telemetry = telemetry;
            m_logger = telemetry.CreateLogger<UaSCUaBinaryChannel>();
            TimeProvider = timeProvider ?? TimeProvider.System;
            m_lastActiveTimestamp = TimeProvider.GetTimestamp();

            if (string.IsNullOrEmpty(m_contextId))
            {
                m_contextId = Guid.NewGuid().ToString();
            }

            // secuirty turned off if message security mode is set to none.
            if (securityMode == MessageSecurityMode.None || securityPolicyUri == null)
            {
                securityPolicyUri = SecurityPolicies.None;
            }

            CertificateCollection? serverCertificateChain = null;
            if (serverCertificates != null && securityMode != MessageSecurityMode.None)
            {
                // Acquire a caller-owned entry (certificate + issuer chain),
                // validate it, then keep independent ref-counted handles so the
                // channel stays valid even if the registry later hot-swaps its
                // certificates.
                using CertificateEntry instanceEntry =
                    serverCertificates.AcquireApplicationCertificateBySecurityPolicy(securityPolicyUri)
                    ?? throw new ArgumentException(
                        Utils.Format(
                            "The certificate registry has no instance certificate for security policy {0}.",
                            securityPolicyUri),
                        nameof(securityPolicyUri));

                Certificate borrowed = instanceEntry.Certificate;
                if (borrowed.RawData.Length > TcpMessageLimits.MaxCertificateSize)
                {
                    throw new ArgumentException(
                        Utils.Format(
                            "The DER encoded certificate may not be more than {0} bytes.",
                            TcpMessageLimits.MaxCertificateSize
                        ),
                        nameof(serverCertificate));
                }

                serverCertificate = borrowed.AddRef();
                // The entry already carries the issuer chain; build the
                // [leaf, ...issuers] collection without a second registry lookup.
                serverCertificateChain = BuildServerCertificateChain(instanceEntry);
            }

            if (Encoding.UTF8.GetByteCount(securityPolicyUri) > TcpMessageLimits
                .MaxSecurityPolicyUriSize)
            {
                throw new ArgumentException(
                    Utils.Format(
                        "UTF-8 form of the security policy URI may not be more than {0} bytes.",
                        TcpMessageLimits.MaxSecurityPolicyUriSize
                    ),
                    nameof(securityPolicyUri));
            }

            BufferManager = bufferManager ?? throw new ArgumentNullException(nameof(bufferManager));
            Quotas = quotas ?? throw new ArgumentNullException(nameof(quotas));
            m_serverCertificates = serverCertificates;
            ServerCertificate = serverCertificate;
            ServerCertificateChain = serverCertificateChain;
            m_endpoints = endpoints ?? [];
            SecurityMode = securityMode;
            SecurityPolicyUri = securityPolicyUri;
            DiscoveryOnly = false;
            m_uninitialized = true;

            m_state = (int)TcpChannelState.Closed;
            ReceiveBufferSize = quotas.MaxBufferSize;
            SendBufferSize = quotas.MaxBufferSize;
            m_activeWriteRequests = 0;

            if (ReceiveBufferSize < TcpMessageLimits.MinBufferSize)
            {
                ReceiveBufferSize = TcpMessageLimits.MinBufferSize;
            }

            if (ReceiveBufferSize > TcpMessageLimits.MaxBufferSize)
            {
                ReceiveBufferSize = TcpMessageLimits.MaxBufferSize;
            }

            if (SendBufferSize < TcpMessageLimits.MinBufferSize)
            {
                SendBufferSize = TcpMessageLimits.MinBufferSize;
            }

            if (SendBufferSize > TcpMessageLimits.MaxBufferSize)
            {
                SendBufferSize = TcpMessageLimits.MaxBufferSize;
            }

            ReceiveBufferSize = Math.Max(
                TcpMessageLimits.MinBufferSize,
                bufferManager.GetSuggestedBufferSize(ReceiveBufferSize));
            SendBufferSize = Math.Max(
                TcpMessageLimits.MinBufferSize,
                bufferManager.GetSuggestedBufferSize(SendBufferSize));

            MaxRequestMessageSize = quotas.MaxMessageSize;
            MaxResponseMessageSize = quotas.MaxMessageSize;

            MaxRequestChunkCount = CalculateChunkCount(
                MaxRequestMessageSize,
                TcpMessageLimits.MinBufferSize);
            MaxResponseChunkCount = CalculateChunkCount(
                MaxResponseMessageSize,
                TcpMessageLimits.MinBufferSize);

            CalculateSymmetricKeySizes();
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_receiveLoopCts?.Cancel();
                IUaSCByteTransport? transport = Interlocked.Exchange(ref m_transport, null);
                transport?.Close();
                DiscardTokens();
                m_receiveLoopCts?.Dispose();
                m_receiveLoopCts = null;

                ServerCertificateChain?.Dispose();
                ServerCertificateChain = null;
                // The channel always owns an independent handle on
                // ServerCertificate (the server side AddRef's it from the
                // registry; the client side receives an owned handle), so
                // always release it.
                ServerCertificate?.Dispose();
                ServerCertificate = null;

                ClientCertificateChain?.Dispose();
                ClientCertificateChain = null;
                ClientCertificate?.Dispose();
                ClientCertificate = null;

                m_localNonce?.Dispose();
                m_localNonce = null;

                m_remoteNonce?.Dispose();
                m_remoteNonce = null;
            }
        }

        /// <summary>
        /// Telemetry context for the channel
        /// </summary>
        protected ITelemetryContext Telemetry { get; }

        /// <summary>
        /// The <see cref="System.TimeProvider"/> used by this channel for
        /// time and duration calculations.
        /// </summary>
        protected TimeProvider TimeProvider { get; }

        /// <summary>
        /// The identifier assigned to the channel by the server.
        /// </summary>
        public uint Id { get; private set; }

        /// <summary>
        /// The globally unique identifier assigned to the channel by the server.
        /// </summary>
        public string GlobalChannelId { get; private set; } = string.Empty;

        /// <inheritdoc/>
        internal byte[]? ChannelThumbprint { get; set; }

        /// <inheritdoc/>
        public byte[]? ClientChannelCertificate { get; protected set; }

        /// <inheritdoc/>
        public byte[]? ServerChannelCertificate { get; protected set; }

        /// <summary>
        /// Raised when the state of the channel changes.
        /// </summary>
        public void SetStateChangedCallback(TcpChannelStateEventHandler callback)
        {
            lock (DataLock)
            {
                m_stateChanged = callback;
            }
        }

        /// <summary>
        /// Returns the monotonic elapsed time since the channel last
        /// received or sent a message, measured against the channel's
        /// <see cref="TimeProvider"/>.
        /// </summary>
        internal TimeSpan GetElapsedSinceLastActive()
        {
            return TimeProvider.GetElapsedTime(m_lastActiveTimestamp);
        }

        /// <summary>
        /// Reports that the channel state has changed (in another thread).
        /// </summary>
        protected void ChannelStateChanged(TcpChannelState state, ServiceResult reason)
        {
            TcpChannelStateEventHandler? stateChanged = m_stateChanged;
            if (stateChanged != null)
            {
                _ = Task.Run(() => stateChanged?.Invoke(this, state, reason));
            }
        }

        /// <summary>
        /// Returns a new sequence number.
        /// </summary>
        protected uint GetNewSequenceNumber()
        {
            bool isLegacy = SecurityPolicy!.LegacySequenceNumbers;

            long newSeqNumber = Interlocked.Increment(ref m_sequenceNumber);
            bool maxValueOverflow = isLegacy
                ? newSeqNumber > kMaxValueLegacyTrue
                : newSeqNumber > kMaxValueLegacyFalse;

            // LegacySequenceNumbers are TRUE for non ECC profiles
            // https://reference.opcfoundation.org/Core/Part6/v105/docs/6.7.2.4
            if (isLegacy)
            {
                if (maxValueOverflow)
                {
                    // First number after wrap around shall be less than 1024
                    // 1 for legaccy reasons
                    Interlocked.Exchange(ref m_sequenceNumber, 1);
                    return 1;
                }
                return (uint)newSeqNumber;
            }
            uint retVal = (uint)newSeqNumber - 1;
            if (maxValueOverflow)
            {
                // First number after wrap around and as initial value shall be 0
                Interlocked.Exchange(ref m_sequenceNumber, 0);
                Interlocked.Exchange(ref m_localSequenceNumber, 0);
                return retVal;
            }
            Interlocked.Exchange(ref m_localSequenceNumber, retVal);

            return retVal;
        }

        /// <summary>
        /// Resets the sequence number after a connect.
        /// </summary>
        protected void ResetSequenceNumber(uint sequenceNumber)
        {
            m_remoteSequenceNumber = sequenceNumber;
        }

        /// <summary>
        /// Checks if the sequence number is valid.
        /// </summary>
        protected bool VerifySequenceNumber(uint sequenceNumber, string context)
        {
            // Accept the first sequence number depending on security policy
            if (m_firstReceivedSequenceNumber &&
                (
                    !CryptoUtils.IsEccPolicy(SecurityPolicyUri) ||
                    (CryptoUtils.IsEccPolicy(SecurityPolicyUri) && (sequenceNumber == 0))))
            {
                m_remoteSequenceNumber = sequenceNumber;
                m_firstReceivedSequenceNumber = false;
                return true;
            }

            // everything ok if new number is greater.
            if (sequenceNumber > m_remoteSequenceNumber)
            {
                m_remoteSequenceNumber = sequenceNumber;
                return true;
            }

            // check for a valid rollover.
            if (m_remoteSequenceNumber > TcpMessageLimits.MinSequenceNumber &&
                sequenceNumber < TcpMessageLimits.MaxRolloverSequenceNumber)
            {
                // only one rollover per token is allowed and with valid values depending on security policy
                if (!m_sequenceRollover &&
                    (
                        !CryptoUtils.IsEccPolicy(SecurityPolicyUri) ||
                        (CryptoUtils.IsEccPolicy(SecurityPolicyUri) && (sequenceNumber == 0))))
                {
                    m_sequenceRollover = true;
                    m_remoteSequenceNumber = sequenceNumber;
                    return true;
                }
            }

            m_logger.LogError(
                "ChannelId {ChannelId}: {Context} - Duplicate sequence number: {SequenceNumber} <= {RemoteSequenceNumber}",
                ChannelId,
                context,
                sequenceNumber,
                m_remoteSequenceNumber);
            return false;
        }

        /// <summary>
        /// Saves an intermediate chunk for an incoming message.
        /// </summary>
        protected bool SaveIntermediateChunk(
            uint requestId,
            ArraySegment<byte> chunk,
            bool isServerContext)
        {
            bool firstChunk = false;
            if (m_partialMessageChunks == null)
            {
                firstChunk = true;
                m_partialMessageChunks = [];
            }

            bool chunkOrSizeLimitsExceeded = MessageLimitsExceeded(
                isServerContext,
                m_partialMessageChunks.TotalSize,
                m_partialMessageChunks.Count);

            if ((m_partialRequestId != requestId) || chunkOrSizeLimitsExceeded)
            {
                if (m_partialMessageChunks.Count > 0)
                {
                    m_logger.LogWarning(
                        "WARNING - Discarding unprocessed message chunks for Request #{PartialRequestId}",
                        m_partialRequestId);
                }

                m_partialMessageChunks.Release(BufferManager, "SaveIntermediateChunk");
            }

            if (chunkOrSizeLimitsExceeded)
            {
                DoMessageLimitsExceeded();
                return firstChunk;
            }

            if (requestId != 0)
            {
                m_partialRequestId = requestId;
                m_partialMessageChunks.Add(chunk);
            }

            return firstChunk;
        }

        /// <summary>
        /// Returns the chunks saved for message.
        /// </summary>
        protected BufferCollection GetSavedChunks(
            uint requestId,
            ArraySegment<byte> chunk,
            bool isServerContext)
        {
            SaveIntermediateChunk(requestId, chunk, isServerContext);
            BufferCollection savedChunks = m_partialMessageChunks!;
            m_partialMessageChunks = null;
            return savedChunks;
        }

        /// <summary>
        /// Returns total length of the chunks saved for message.
        /// </summary>
        protected int GetSavedChunksTotalSize()
        {
            return m_partialMessageChunks?.TotalSize ?? 0;
        }

        /// <summary>
        /// Code executed when the message limits are exceeded.
        /// </summary>
        protected virtual void DoMessageLimitsExceeded()
        {
            m_logger.LogError(
                "ChannelId {ChannelId}: - Message limits exceeded while building up message. Channel will be closed.",
                ChannelId);
        }

        /// <inheritdoc/>
        public virtual bool ChannelFull => m_activeWriteRequests > 100;

        /// <summary>
        /// Dispatches a complete UASC <c>MessageChunk</c> pulled from the
        /// transport's receive loop into the channel pipeline.
        /// </summary>
        protected virtual void OnChunkReceived(ArraySegment<byte> message)
        {
            try
            {
                uint messageType = BitConverter.ToUInt32(message.GetArray(), message.Offset);

                if (!HandleIncomingMessage(messageType, message))
                {
                    BufferManager.ReturnBuffer(message.GetArray(), "OnChunkReceived");
                }
            }
            catch (Exception e)
            {
                HandleMessageProcessingError(
                    e,
                    StatusCodes.BadTcpInternalError,
                    "An error occurred receiving a message.");
                BufferManager.ReturnBuffer(message.Array, "OnChunkReceived");
            }
        }

        /// <summary>
        /// Processes an incoming message.
        /// </summary>
        /// <returns>True if the implementor takes ownership of the buffer.</returns>
        protected virtual bool HandleIncomingMessage(
            uint messageType,
            ArraySegment<byte> messageChunk)
        {
            return false;
        }

        /// <summary>
        /// Handles an error parsing or verifying a message.
        /// </summary>
        protected void HandleMessageProcessingError(
            Exception e,
            StatusCode defaultCode,
            string format,
            params object[] args)
        {
            HandleMessageProcessingError(ServiceResult.Create(e, defaultCode, format, args));
        }

        /// <summary>
        /// Handles an error parsing or verifying a message.
        /// </summary>
        protected void HandleMessageProcessingError(
            StatusCode statusCode,
            string format,
            params object[] args)
        {
            HandleMessageProcessingError(ServiceResult.Create(statusCode, format, args));
        }

        /// <summary>
        /// Handles an error parsing or verifying a message.
        /// </summary>
        protected virtual void HandleMessageProcessingError(ServiceResult result)
        {
        }

        /// <summary>
        /// Reports a fatal transport-level error (connection closed, framing
        /// error, etc.) from the receive loop into the channel pipeline.
        /// </summary>
        protected virtual void OnTransportError(ServiceResult result)
        {
            lock (DataLock)
            {
                HandleSocketError(result);
            }
        }

        /// <summary>
        /// Handles a socket error.
        /// </summary>
        protected virtual void HandleSocketError(ServiceResult result)
        {
        }

        /// <summary>
        /// Starts the long-running receive loop that pulls UASC chunks from
        /// the current <see cref="Transport"/> and dispatches them into the
        /// channel via <see cref="OnChunkReceived"/>. Idempotent: subsequent
        /// calls are no-ops while a loop is already running on the current
        /// transport.
        /// </summary>
        protected internal virtual void StartReceiveLoop()
        {
            StartReceiveLoopWithBody(RunReceiveLoopAsync);
        }

        /// <summary>
        /// Sets up the receive-loop state (CTS, task, running flag) and
        /// runs the supplied <paramref name="loopBody"/> on a background
        /// task. Used by <see cref="StartReceiveLoop"/> for the default
        /// long-running loop and by derived classes (e.g.
        /// <c>TcpReverseConnectChannel</c>) that need a one-shot variant
        /// (read a single ReverseHello chunk then exit so the transport
        /// can be handed off without aborting the underlying connection
        /// on cancellation - critical for WebSocket transports where
        /// <c>CancellationToken</c> on <c>WebSocket.ReceiveAsync</c>
        /// aborts the whole connection).
        /// </summary>
        protected void StartReceiveLoopWithBody(
            Func<IUaSCByteTransport, CancellationToken, Task> loopBody)
        {
            IUaSCByteTransport? transport = m_transport;
            if (transport == null)
            {
                return;
            }
            if (Interlocked.CompareExchange(ref m_receiveLoopRunning, 1, 0) != 0)
            {
                return;
            }
            m_receiveLoopCts?.Dispose();
            m_receiveLoopCts = new CancellationTokenSource();
            CancellationToken ct = m_receiveLoopCts.Token;
            m_receiveLoopTask = Task.Run(
                async () =>
                {
                    try
                    {
                        await loopBody(transport, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref m_receiveLoopRunning, 0);
                    }
                },
                ct);
        }

        /// <summary>
        /// Stops the channel's receive loop (if running), detaches the current
        /// <see cref="Transport"/> from the channel, and returns it. The
        /// returned transport is the caller's responsibility — the channel's
        /// own <see cref="Dispose(bool)"/> will no longer close it.
        /// </summary>
        /// <remarks>
        /// Used by the reverse-connect handoff in
        /// <c>TcpTransportListener.TransferListenerChannelAsync</c> so that
        /// the listener-side channel releases the socket cleanly before the
        /// client side starts its own receive loop on the same transport.
        /// </remarks>
        internal async ValueTask<IUaSCByteTransport?> DetachTransportAsync()
        {
            IUaSCByteTransport? transport = Interlocked.Exchange(ref m_transport, null);

            CancellationTokenSource? cts = m_receiveLoopCts;
            cts?.Cancel();

            Task? loop = m_receiveLoopTask;
            if (loop != null)
            {
                try
                {
                    await loop.ConfigureAwait(false);
                }
                catch
                {
                    // The loop's exit path catches its own exceptions; any escapes
                    // here are last-resort and must not block the handoff.
                }
                m_receiveLoopTask = null;
            }

            return transport;
        }

        private async Task RunReceiveLoopAsync(IUaSCByteTransport transport, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                ArraySegment<byte> chunk;
                try
                {
                    chunk = await transport.ReceiveChunkAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (ServiceResultException sre)
                {
                    OnTransportError(sre.Result);
                    return;
                }
                catch (Exception ex)
                {
                    OnTransportError(ServiceResult.Create(
                        ex,
                        StatusCodes.BadTcpInternalError,
                        ex.Message));
                    return;
                }

                OnChunkReceived(chunk);
            }
        }

        /// <summary>
        /// Sends one complete UASC <c>MessageChunk</c> as a contiguous buffer
        /// through the current <see cref="Transport"/>. Returns to the caller
        /// immediately; the write completes asynchronously and reports its
        /// result via <see cref="HandleWriteComplete"/>.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// Thrown synchronously if no transport is attached.
        /// </exception>
        protected void BeginWriteMessage(ArraySegment<byte> buffer, object? state)
        {
            IUaSCByteTransport transport = m_transport
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed,
                    "The transport was closed by the remote application.");

            Interlocked.Increment(ref m_activeWriteRequests);
            ReadOnlyMemory<byte> chunk = new(buffer.GetArray(), buffer.Offset, buffer.Count);
            _ = WriteSingleChunkAsync(transport, chunk, buffer.GetArray(), state);
        }

        /// <summary>
        /// Sends one complete UASC <c>MessageChunk</c> gathered from multiple
        /// buffer segments through the current <see cref="Transport"/>.
        /// Returns to the caller immediately; the write completes
        /// asynchronously and reports its result via
        /// <see cref="HandleWriteComplete"/>.
        /// </summary>
        protected void BeginWriteMessage(BufferCollection buffers, object? state)
        {
            IUaSCByteTransport? transport = m_transport;
            if (transport == null)
            {
                // Mirror the legacy contract: report failure via HandleWriteComplete
                // rather than throwing synchronously so callers' state is released.
                HandleWriteComplete(
                    buffers,
                    state,
                    0,
                    ServiceResult.Create(
                        StatusCodes.BadConnectionClosed,
                        "The transport was closed by the remote application."));
                return;
            }

            Interlocked.Increment(ref m_activeWriteRequests);
            _ = WriteBuffersAsync(transport, buffers, state);
        }

        private async Task WriteSingleChunkAsync(
            IUaSCByteTransport transport,
            ReadOnlyMemory<byte> chunk,
            byte[] backingBuffer,
            object? state)
        {
            ServiceResult result = ServiceResult.Good;
            int sent = chunk.Length;
            try
            {
                await transport.SendChunkAsync(chunk, CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                sent = 0;
                result = sre.Result;
            }
            catch (Exception ex)
            {
                sent = 0;
                result = ServiceResult.Create(
                    ex,
                    StatusCodes.BadTcpInternalError,
                    "Unexpected error during write operation.");
            }
            finally
            {
                try
                {
                    if (backingBuffer != null)
                    {
                        BufferManager.ReturnBuffer(backingBuffer, "WriteChunkAsync");
                    }
                }
                catch
                {
                    // Best-effort: a double-return throws but should not mask the write result.
                }
                HandleWriteComplete(null, state, sent, result);
            }
        }

        private async Task WriteBuffersAsync(
            IUaSCByteTransport transport,
            BufferCollection buffers,
            object? state)
        {
            ServiceResult result = ServiceResult.Good;
            int sent = buffers.TotalSize;
            try
            {
                await transport.SendChunkAsync(buffers, CancellationToken.None).ConfigureAwait(false);
            }
            catch (ServiceResultException sre)
            {
                sent = 0;
                result = sre.Result;
            }
            catch (Exception ex)
            {
                sent = 0;
                result = ServiceResult.Create(
                    ex,
                    StatusCodes.BadTcpInternalError,
                    "Unexpected error during write operation.");
            }
            finally
            {
                HandleWriteComplete(buffers, state, sent, result);
            }
        }

        /// <summary>
        /// Called after a write operation completes.
        /// </summary>
        protected virtual void HandleWriteComplete(
            BufferCollection? buffers,
            object? state,
            int bytesWritten,
            ServiceResult result)
        {
            // Communication is active on the channel
            UpdateLastActiveTime();

            buffers?.Release(BufferManager, "WriteOperation");
            Interlocked.Decrement(ref m_activeWriteRequests);
        }

        /// <summary>
        /// Writes an error to a stream.
        /// </summary>
        protected static void WriteErrorMessageBody(BinaryEncoder encoder, ServiceResult error)
        {
            string? reason = error.LocalizedText.Text;

            // check that length is not exceeded.
            if (reason != null &&
                Encoding.UTF8.GetByteCount(reason) > TcpMessageLimits.MaxErrorReasonLength)
            {
                reason = reason[
                    ..(TcpMessageLimits.MaxErrorReasonLength / Encoding.UTF8.GetMaxByteCount(1))];
            }

            encoder.WriteStatusCode(null, error.StatusCode);
            encoder.WriteString(null, reason);
        }

        /// <summary>
        /// Reads an error from a stream.
        /// </summary>
        protected static ServiceResult ReadErrorMessageBody(BinaryDecoder decoder)
        {
            // read the status code.
            uint statusCode = decoder.ReadUInt32(null);

            string? reason = null;

            // ensure the reason does not exceed the limits in the protocol.
            int reasonLength = decoder.ReadInt32(null);

            if (reasonLength is > 0 and < TcpMessageLimits.MaxErrorReasonLength)
            {
                byte[] reasonBytes = new byte[reasonLength];

                for (int ii = 0; ii < reasonLength; ii++)
                {
                    reasonBytes[ii] = decoder.ReadByte(null);
                }

                reason = Encoding.UTF8.GetString(reasonBytes, 0, reasonLength);
            }

            reason ??= new ServiceResult(statusCode).ToString();

            return new ServiceResult(
                null,
                statusCode,
                LocalizedText.From(Utils.Format("Error received from remote host: {0}", reason)),
                reason);
        }

        /// <summary>
        /// Checks if the message limits have been exceeded.
        /// </summary>
        protected bool MessageLimitsExceeded(bool isRequest, int messageSize, int chunkCount)
        {
            if (isRequest)
            {
                if (MaxRequestChunkCount > 0 && MaxRequestChunkCount < chunkCount)
                {
                    return true;
                }

                if (MaxRequestMessageSize > 0 && MaxRequestMessageSize < messageSize)
                {
                    return true;
                }
            }
            else
            {
                if (MaxResponseChunkCount > 0 && MaxResponseChunkCount < chunkCount)
                {
                    return true;
                }

                if (MaxResponseMessageSize > 0 && MaxResponseMessageSize < messageSize)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Updates the message type stored in the message header.
        /// </summary>
        protected static void UpdateMessageType(byte[] buffer, int offset, uint messageType)
        {
            buffer[offset++] = (byte)(messageType & 0x000000FF);
            buffer[offset++] = (byte)((messageType & 0x0000FF00) >> 8);
            buffer[offset++] = (byte)((messageType & 0x00FF0000) >> 16);
            buffer[offset] = (byte)((messageType & 0xFF000000) >> 24);
        }

        /// <summary>
        /// Updates the message size stored in the message header.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected static void UpdateMessageSize(byte[] buffer, int offset, int messageSize)
        {
            if (offset >= int.MaxValue - 4)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            offset += 4;

            buffer[offset++] = (byte)(messageSize & 0x000000FF);
            buffer[offset++] = (byte)((messageSize & 0x0000FF00) >> 8);
            buffer[offset++] = (byte)((messageSize & 0x00FF0000) >> 16);
            buffer[offset] = (byte)((messageSize & 0xFF000000) >> 24);
        }

        /// <summary>
        /// The synchronization object for the channel.
        /// </summary>
        protected object DataLock { get; } = new();

        /// <summary>
        /// The byte-level transport that carries UASC chunks for the channel.
        /// Set by listener channels after a successful accept/connect, by
        /// client channels after <c>ConnectAsync</c>, or by reverse-connect
        /// flows after the inbound TCP handshake completes.
        /// </summary>
        protected internal IUaSCByteTransport? Transport
        {
            get => m_transport;
            set => m_transport = value;
        }

        /// <summary>
        /// Whether the client channel uses a reverse hello socket.
        /// </summary>
        protected internal bool ReverseSocket { get; set; }

        /// <summary>
        /// The buffer manager for the channel.
        /// </summary>
        protected BufferManager BufferManager { get; }

        /// <summary>
        /// The resource quotas for the channel.
        /// </summary>
        protected ChannelQuotas Quotas { get; }

        /// <summary>
        /// The size of the receive buffer.
        /// </summary>
        protected int ReceiveBufferSize { get; set; }

        /// <summary>
        /// The size of the send buffer.
        /// </summary>
        protected int SendBufferSize { get; set; }

        /// <summary>
        /// The maximum size for a request message.
        /// </summary>
        protected int MaxRequestMessageSize { get; set; }

        /// <summary>
        /// The maximum number of chunks per request message.
        /// </summary>
        protected int MaxRequestChunkCount { get; set; }

        /// <summary>
        /// The maximum size for a response message.
        /// </summary>
        protected int MaxResponseMessageSize { get; set; }

        /// <summary>
        /// The maximum number of chunks per response message.
        /// </summary>
        protected int MaxResponseChunkCount { get; set; }

        /// <summary>
        /// The state of the channel.
        /// </summary>
        protected TcpChannelState State
        {
            get => (TcpChannelState)m_state;
            set
            {
                if (Interlocked.Exchange(ref m_state, (int)value) != (int)value)
                {
                    m_logger.LogDebug("ChannelId {ChannelId}: in {State} state.", ChannelId, value);
                }
            }
        }

        /// <summary>
        /// The identifier assigned to the channel by the server.
        /// </summary>
        protected uint ChannelId
        {
            get => Id;
            set
            {
                Id = value;
                GlobalChannelId = Utils.Format("{0}-{1}", m_contextId, Id);
            }
        }

        /// <summary>
        /// A class that stores the state for a write operation.
        /// </summary>
        protected class WriteOperation : ChannelAsyncOperation<int>
        {
            /// <summary>
            /// Initializes the object with a callback
            /// </summary>
            public WriteOperation(int timeout, AsyncCallback? callback, object? asyncState, ILogger logger)
                : this(timeout, callback, asyncState, logger, null)
            {
            }

            /// <summary>
            /// Initializes the object with a callback and supplied
            /// <see cref="TimeProvider"/>.
            /// </summary>
            public WriteOperation(
                int timeout,
                AsyncCallback? callback,
                object? asyncState,
                ILogger logger,
                TimeProvider? timeProvider)
                : base(timeout, callback, asyncState, logger, timeProvider)
            {
            }

            /// <summary>
            /// The request id associated with the operation.
            /// </summary>
            public uint RequestId { get; set; }

            /// <summary>
            /// The body of the request or response associated with the operation.
            /// </summary>
            public IEncodeable? MessageBody { get; set; }
        }

        /// <summary>
        /// Calculate the chunk count which can be used for messages based on buffer size.
        /// </summary>
        /// <param name="messageSize">The message size to be used.</param>
        /// <param name="bufferSize">The buffer available for a message.</param>
        /// <returns>The chunk count.</returns>
        protected static int CalculateChunkCount(int messageSize, int bufferSize)
        {
            if (bufferSize > 0)
            {
                int chunkCount = messageSize / bufferSize;
                if (chunkCount * bufferSize < messageSize)
                {
                    chunkCount++;
                }
                return chunkCount;
            }
            return 1;
        }

        /// <summary>
        /// Check the MessageType and size against the content and size of the stream.
        /// </summary>
        /// <param name="decoder">The decoder of the stream.</param>
        /// <param name="expectedMessageType">The message type to be checked.</param>
        /// <param name="count">The length of the message.</param>
        /// <exception cref="ServiceResultException"></exception>
        protected static void ReadAndVerifyMessageTypeAndSize(
            IDecoder decoder,
            uint expectedMessageType,
            int count)
        {
            uint messageType = decoder.ReadUInt32(null);
            if (messageType != expectedMessageType)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpMessageTypeInvalid,
                    "Expected message type {0:X8} instead of {0:X8}.",
                    expectedMessageType,
                    messageType);
            }
            int messageSize = decoder.ReadInt32(null);
            if (messageSize > count)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTcpMessageTooLarge,
                    "Messages size {0} is larger than buffer size {1}.",
                    messageSize,
                    count);
            }
        }

        /// <summary>
        /// Update the last time that communication has occured on the channel.
        /// </summary>
        public void UpdateLastActiveTime()
        {
            m_lastActiveTimestamp = TimeProvider.GetTimestamp();
        }

        /// <summary>
        /// treat TcpChannelState as int to use Interlocked
        /// </summary>
        private int m_state;
        private int m_activeWriteRequests;
        private long m_lastActiveTimestamp;
        private readonly string m_contextId;
        private readonly ILogger m_logger;
        private long m_sequenceNumber;
        private long m_localSequenceNumber;
        private uint m_remoteSequenceNumber;
        private bool m_sequenceRollover;
        private bool m_firstReceivedSequenceNumber = true;
        private uint m_partialRequestId;
        private BufferCollection? m_partialMessageChunks;

        private IUaSCByteTransport? m_transport;
        private CancellationTokenSource? m_receiveLoopCts;
        private Task? m_receiveLoopTask;
        private int m_receiveLoopRunning;

        private TcpChannelStateEventHandler? m_stateChanged;
        private const uint kMaxValueLegacyTrue = TcpMessageLimits.MinSequenceNumber;
        private const uint kMaxValueLegacyFalse = uint.MaxValue;
    }

    /// <summary>
    /// The possible channel states.
    /// </summary>
    public enum TcpChannelState
    {
        /// <summary>
        /// The channel is closed.
        /// </summary>
        Closed,

        /// <summary>
        /// The channel is closing.
        /// </summary>
        Closing,

        /// <summary>
        /// The channel establishing a network connection.
        /// </summary>
        Connecting,

        /// <summary>
        /// The channel negotiating security parameters.
        /// </summary>
        Opening,

        /// <summary>
        /// The channel is open and accepting messages.
        /// </summary>
        Open,

        /// <summary>
        /// The channel is in a error state.
        /// </summary>
        Faulted
    }

    /// <summary>
    /// Used to report changes to the channel state.
    /// </summary>
    public delegate void TcpChannelStateEventHandler(
        UaSCUaBinaryChannel channel,
        TcpChannelState state,
        ServiceResult error);
}
