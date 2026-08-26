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
            m_backgroundWork = new BackgroundTaskScope(nameof(UaSCUaBinaryChannel), telemetry);
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
                bufferManager.GetSuggestedBufferSize(
                    Math.Min(ReceiveBufferSize, bufferManager.MaxSuggestedBufferSize)));
            SendBufferSize = Math.Max(
                TcpMessageLimits.MinBufferSize,
                bufferManager.GetSuggestedBufferSize(
                    Math.Min(SendBufferSize, bufferManager.MaxSuggestedBufferSize)));

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
                // Signal only: Dispose is synchronous, so it cannot await the
                // drain without blocking. State-change notifications stop being
                // accepted immediately and any in flight are cancelled.
                m_backgroundWork.Dispose();

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
        /// Owns the work the channel schedules off its own threads, so a faulting
        /// subscriber is reported and nothing is scheduled after disposal.
        /// </summary>
        private protected BackgroundTaskScope BackgroundWork => m_backgroundWork;

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
        /// <remarks>
        /// Deliberately takes no lock, for the same reason as
        /// <c>TcpListenerChannel.SetRequestReceivedCallback</c>: a single
        /// delegate field needs no more than a volatile write, and the gate is
        /// not re-entrant.
        /// </remarks>
        public void SetStateChangedCallback(TcpChannelStateEventHandler callback)
        {
            m_stateChanged = callback;
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
                // Off the caller's thread because a subscriber must not be able to
                // stall the channel, but owned so a throwing subscriber is reported
                // rather than silently dropped, and so notifications stop once the
                // channel is disposed.
                m_backgroundWork.Run(
                    nameof(ChannelStateChanged),
                    _ =>
                    {
                        stateChanged.Invoke(this, state, reason);
                        return default;
                    });
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

            if (m_logger.IsEnabled(LogLevel.Error))
            {
                m_logger.UaSCChannelLog3(
                    ChannelId,
                    context,
                    sequenceNumber,
                    m_remoteSequenceNumber);
            }
            return false;
        }

        /// <summary>
        /// Saves an intermediate chunk for an incoming message.
        /// </summary>
        /// <param name="requestId">The request the chunk belongs to.</param>
        /// <param name="chunk">The chunk to save.</param>
        /// <param name="isServerContext">Whether this is a server channel.</param>
        /// <param name="gateHeld">
        /// Whether the caller already holds the channel gate. Passed on to
        /// <see cref="DoMessageLimitsExceeded(bool)"/>, which tears the channel
        /// down and must know whether it may take the gate.
        /// </param>
        protected bool SaveIntermediateChunk(
            uint requestId,
            ArraySegment<byte> chunk,
            bool isServerContext,
            bool gateHeld)
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
                    m_logger.UaSCChannelLog4(m_partialRequestId);
                }

                m_partialMessageChunks.Release(BufferManager, "SaveIntermediateChunk");
            }

            if (chunkOrSizeLimitsExceeded)
            {
                DoMessageLimitsExceeded(gateHeld);
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
        /// <inheritdoc cref="SaveIntermediateChunk" path="/param"/>
        protected BufferCollection GetSavedChunks(
            uint requestId,
            ArraySegment<byte> chunk,
            bool isServerContext,
            bool gateHeld)
        {
            SaveIntermediateChunk(requestId, chunk, isServerContext, gateHeld);
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
        /// <param name="gateHeld">
        /// Whether the caller already holds the channel gate. The gate is not
        /// re-entrant, so an override that tears the channel down must call the
        /// lock-free core of that teardown when this is <c>true</c>.
        /// </param>
        protected virtual void DoMessageLimitsExceeded(bool gateHeld)
        {
            m_logger.UaSCChannelLog5(ChannelId);
        }

        /// <inheritdoc/>
        public virtual bool ChannelFull => m_activeWriteRequests > 100;

        /// <summary>
        /// Dispatches a complete UASC <c>MessageChunk</c> pulled from the
        /// transport's receive loop into the channel pipeline.
        /// </summary>
        /// <param name="message">The chunk to dispatch.</param>
        /// <param name="ct">Cancels the dispatch.</param>
        /// <remarks>
        /// This is the path the receive loop takes. It exists so that the secure
        /// channel open path can await a private key served over a network; with
        /// a software key nothing suspends and the sequencing is the same as the
        /// synchronous path this replaces.
        /// </remarks>
        protected virtual async ValueTask OnChunkReceivedAsync(
            ArraySegment<byte> message,
            CancellationToken ct)
        {
            try
            {
                if (message.Count > ReceiveBufferSize)
                {
                    var result = ServiceResult.Create(
                        StatusCodes.BadTcpMessageTooLarge,
                        "Message size {0} bytes exceeds the negotiated receive buffer size of {1} bytes.",
                        message.Count,
                        ReceiveBufferSize);
                    BufferManager.ReturnBuffer(message.GetArray(), "OnChunkReceived");
                    OnTransportError(result);
                    return;
                }

                uint messageType = BitConverter.ToUInt32(message.GetArray(), message.Offset);

                if (!await HandleIncomingMessageAsync(messageType, message, ct)
                        .ConfigureAwait(false))
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
        /// <param name="messageType">The UA TCP message type.</param>
        /// <param name="messageChunk">The chunk to process.</param>
        /// <param name="ct">Cancels the processing.</param>
        /// <returns>True if the implementor takes ownership of the buffer.</returns>
        protected virtual ValueTask<bool> HandleIncomingMessageAsync(
            uint messageType,
            ArraySegment<byte> messageChunk,
            CancellationToken ct)
        {
            return new ValueTask<bool>(false);
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
        /// <remarks>
        /// Deliberately does not hold the gate. <see cref="HandleSocketError"/>
        /// tears the channel down, which notifies the listener, which disposes
        /// the channel — and disposal takes the gate. Each override takes the
        /// gate for the state it actually mutates instead.
        /// </remarks>
        protected virtual void OnTransportError(ServiceResult result)
        {
            HandleSocketError(result);
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
        /// channel via <see cref="OnChunkReceivedAsync"/>. Idempotent: subsequent
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

                try
                {
                    await OnChunkReceivedAsync(chunk, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Dispatching a chunk must never end the loop silently. The
                    // task this runs on is fire-and-forget, so an escaping
                    // exception would otherwise stop the channel receiving with no
                    // report at all, and the peer would simply time out.
                    OnTransportError(ServiceResult.Create(
                        ex,
                        StatusCodes.BadTcpInternalError,
                        "An error occurred dispatching a received message."));
                    return;
                }
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

            // Queued rather than started inline. The write completes by calling
            // HandleWriteComplete, which enters the gate, and the caller may be
            // holding it: started inline, the synchronous prologue would run on
            // the caller's stack, disclaim the caller's own entitlement and then
            // block on a gate that this very thread holds.
            byte[] backingBuffer = buffer.GetArray();
            QueueWrite(() => WriteSingleChunkAsync(transport, chunk, backingBuffer, state));
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
            Interlocked.Increment(ref m_activeWriteRequests);

            IUaSCByteTransport? transport = m_transport;
            if (transport == null)
            {
                // The caller can hold the channel gate. Report asynchronously
                // because client write completion enters the same non-reentrant gate.
                ReportWriteComplete(
                    buffers,
                    state,
                    0,
                    ServiceResult.Create(
                        StatusCodes.BadConnectionClosed,
                        "The transport was closed by the remote application."));
                return;
            }

            // Queued rather than started inline, for the reason given in
            // BeginWriteMessage(ArraySegment{byte}, object).
            QueueWrite(() => WriteBuffersAsync(transport, buffers, state));
        }

        /// <summary>
        /// Queues a write so that it runs off the caller's stack but still in
        /// the order the caller issued it.
        /// </summary>
        /// <remarks>
        /// Both matter. Running off the caller's stack is required because the
        /// write reports completion through <see cref="HandleWriteComplete"/>,
        /// which enters the gate the caller may be holding. Preserving order is
        /// required because sequence numbers are assigned under that gate, and a
        /// peer rejects a chunk whose sequence number does not follow its
        /// predecessor. Independently queued work items carry no such guarantee,
        /// so each write is appended to a chain and cannot start before the one
        /// issued before it has finished.
        /// </remarks>
        private void QueueWrite(Func<Task> write)
        {
            lock (m_writeChainLock)
            {
                // No ExecuteSynchronously on the write itself: the continuation
                // must reach the pool rather than run on whichever thread
                // completed its predecessor, which may be the caller's. The
                // trailing continuation observes any fault so that a write which
                // fails outside its own error handling cannot surface later as an
                // unobserved task exception.
                m_writeChain = m_writeChain
                    .ContinueWith(
                        static (_, state) => ((Func<Task>)state!)(),
                        write,
                        CancellationToken.None,
                        TaskContinuationOptions.DenyChildAttach,
                        TaskScheduler.Default)
                    .Unwrap()
                    .ContinueWith(
                        static completed => _ = completed.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously |
                            TaskContinuationOptions.DenyChildAttach,
                        TaskScheduler.Default);
            }
        }

        /// <summary>
        /// Sends a message on the caller's stack instead of queuing it.
        /// </summary>
        /// <param name="buffer">The encoded message. Ownership transfers.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown synchronously if no transport is attached.
        /// </exception>
        /// <remarks>
        /// Reserved for the terminal error message a faulting channel sends
        /// immediately before closing its transport. A queued write would be
        /// discarded by that close, and the peer would see the connection drop
        /// rather than the reason for it. Starting the send here hands the bytes
        /// to the socket before the caller's next statement closes it.
        /// <para>
        /// This deliberately does not report through
        /// <see cref="HandleWriteComplete"/>, and touches the gate only to
        /// disclaim what it inherited. Reporting would enter the gate — which
        /// the caller holds — from a continuation that may resume on another
        /// thread, and that is precisely the deadlock queuing exists to avoid.
        /// Nothing is owed to a channel that is already faulted beyond returning
        /// the buffer.
        /// </para>
        /// </remarks>
        protected void WriteMessageInline(ArraySegment<byte> buffer)
        {
            IUaSCByteTransport transport = m_transport
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadConnectionClosed,
                    "The transport was closed by the remote application.");

            ReadOnlyMemory<byte> chunk = new(buffer.GetArray(), buffer.Offset, buffer.Count);

            _ = SendInlineAsync(transport, chunk, buffer.GetArray());
        }

        private async Task SendInlineAsync(
            IUaSCByteTransport transport,
            ReadOnlyMemory<byte> chunk,
            byte[] backingBuffer)
        {
            try
            {
                await transport.SendChunkAsync(chunk, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The channel is already faulted and the caller is closing it;
                // failing to deliver the reason changes nothing it can act on.
                m_logger.UaSCChannelTerminalWriteFailed(ex, ChannelId);
            }
            finally
            {
                try
                {
                    if (backingBuffer != null)
                    {
                        BufferManager.ReturnBuffer(backingBuffer, "SendInlineAsync");
                    }
                }
                catch
                {
                    // Best-effort: a double-return throws but must not escape a
                    // fire-and-forget send.
                }
            }
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
                ReportWriteComplete(null, state, sent, result);
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
                ReportWriteComplete(buffers, state, sent, result);
            }
        }

        /// <summary>
        /// Reports a completed write without holding up the writes queued behind
        /// it.
        /// </summary>
        /// <remarks>
        /// The write chain exists to keep chunks in the order their sequence
        /// numbers were assigned, which only concerns reaching the transport.
        /// Reporting completion does not, and some channels implement it by
        /// entering the gate — so leaving it in the chain would make every
        /// subsequent write on the channel wait for a contended gate, throttling
        /// a busy publisher badly enough to change its behaviour.
        /// </remarks>
        private void ReportWriteComplete(
            BufferCollection? buffers,
            object? state,
            int sent,
            ServiceResult result)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    HandleWriteComplete(buffers, state, sent, result);
                }
                catch (Exception ex)
                {
                    m_logger.UaSCChannelWriteCompletionFailed(ex, ChannelId);
                }
            });
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
        /// Serialises access to the channel's state.
        /// </summary>
        /// <remarks>
        /// This replaces the monitor the channel used to serialise its state on.
        /// A monitor cannot be held across an <see langword="await"/>, and the
        /// secure channel open path has to await once a private key may be served
        /// over a network.
        /// <para>
        /// Unlike a monitor it is <b>not re-entrant</b>. A method that runs with
        /// the gate held must call the lock-free <c>Core</c> variant of anything
        /// that would otherwise take it again.
        /// </para>
        /// </remarks>
        internal ChannelGate Gate { get; } = new();

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
                    m_logger.UaSCChannelLog6(ChannelId, value);
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
        private readonly Lock m_writeChainLock = new();
        private Task m_writeChain = Task.CompletedTask;
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
        private readonly BackgroundTaskScope m_backgroundWork;
        private CancellationTokenSource? m_receiveLoopCts;
        private Task? m_receiveLoopTask;
        private int m_receiveLoopRunning;

        private volatile TcpChannelStateEventHandler? m_stateChanged;
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

    /// <summary>
    /// Source-generated log messages for UaSCBinaryChannel.
    /// </summary>
    internal static partial class UaSCBinaryChannelLog
    {
        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 0, Level = LogLevel.Warning,
            Message = "Could not verify signature on message.")]
        public static partial void UaSCChannelLog0(this ILogger logger);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 1, Level = LogLevel.Information,
            Message = "Security Policy: {SecurityPolicyUri}")]
        public static partial void UaSCChannelLog1(this ILogger logger, string securityPolicyUri);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 2, Level = LogLevel.Information,
            Message = "Sender Certificate {Certificate}")]
        public static partial void UaSCChannelLog2(
            this ILogger logger,
            global::Opc.Ua.Security.Certificates.Certificate? certificate);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 3, Level = LogLevel.Error,
            Message = "ChannelId {ChannelId}: {Context} - Duplicate sequence number: {SequenceNumber} " +
                "<= {RemoteSequenceNumber}")]
        public static partial void UaSCChannelLog3(
            this ILogger logger,
            uint channelId,
            string context,
            uint sequenceNumber,
            uint remoteSequenceNumber);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 4, Level = LogLevel.Warning,
            Message = "WARNING - Discarding unprocessed message chunks for Request #{PartialRequestId}")]
        public static partial void UaSCChannelLog4(this ILogger logger, uint partialRequestId);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 5, Level = LogLevel.Error,
            Message = "ChannelId {ChannelId}: - Message limits exceeded while building up message. " +
                "Channel will be closed.")]
        public static partial void UaSCChannelLog5(this ILogger logger, uint channelId);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 6, Level = LogLevel.Debug,
            Message = "ChannelId {ChannelId}: in {State} state.")]
        public static partial void UaSCChannelLog6(
            this ILogger logger,
            uint channelId,
            global::Opc.Ua.Bindings.TcpChannelState state);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 7, Level = LogLevel.Warning,
            Message = "Message is not an integral multiple of the block size. Length = {Length}, " +
                "BlockSize = {BlockSize}.")]
        public static partial void UaSCChannelLog7(
            this ILogger logger,
            int length,
            int blockSize);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 8, Level = LogLevel.Warning,
            Message = "Message is not an integral multiple of the block size. Length = {Length}, " +
                "BlockSize = {BlockSize}.")]
        public static partial void UaSCChannelLog8(
            this ILogger logger,
            int length,
            int blockSize);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 9, Level = LogLevel.Information,
            Message = "ChannelId {ChannelId}: New Token created. " +
                "CreatedAt={CreatedAt:HH:mm:ss.fff}-{CreatedAtTimestamp}. Lifetime={Lifetime}.")]
        public static partial void UaSCChannelLog9(
            this ILogger logger,
            uint channelId,
            global::System.DateTime createdAt,
            long createdAtTimestamp,
            int lifetime);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 10, Level = LogLevel.Information,
            Message = "ChannelId {Id}: Token #{TokenId} activated. " +
                "CreatedAt={CreatedAt:HH:mm:ss.fff}-{CreatedAtTimestamp}. Lifetime={Lifetime}.")]
        public static partial void UaSCChannelLog10(
            this ILogger logger,
            uint id,
            uint tokenId,
            global::System.DateTime createdAt,
            long createdAtTimestamp,
            int lifetime);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 11, Level = LogLevel.Information,
            Message = "ChannelId {Id}: Renewed Token #{TokenId} set. " +
                "CreatedAt={CreatedAt:HH:mm:ss.fff}-{CreatedAtTimestamp}. Lifetime={Lifetime}.")]
        public static partial void UaSCChannelLog11(
            this ILogger logger,
            uint id,
            uint tokenId,
            global::System.DateTime createdAt,
            long createdAtTimestamp,
            int lifetime);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 12, Level = LogLevel.Information,
            Message = "ChannelId {Id}: Token #{TokenId} activated forced.")]
        public static partial void UaSCChannelLog12(
            this ILogger logger,
            uint id,
            uint tokenId);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 13, Level = LogLevel.Debug,
            Message = "ChannelId {ChannelId}: Could not deliver the error message describing why " +
                "the channel faulted. The peer will see the connection close instead.")]
        public static partial void UaSCChannelTerminalWriteFailed(
            this ILogger logger,
            Exception exception,
            uint channelId);

        [LoggerMessage(EventId = CoreEventIds.UaSCBinaryChannel + 14, Level = LogLevel.Error,
            Message = "ChannelId {ChannelId}: Reporting a completed write failed.")]
        public static partial void UaSCChannelWriteCompletionFailed(
            this ILogger logger,
            Exception exception,
            uint channelId);
    }

}
