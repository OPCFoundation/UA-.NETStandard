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
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.WebApi
{
    /// <summary>
    /// <see cref="ITransportChannel"/> adapter for the WSS
    /// <c>opcua+openapi</c> sub-protocol (OPC UA Part 6 §7.5.2). Wraps a
    /// <see cref="ClientWebSocket"/> that negotiates either
    /// <see cref="Profiles.OpcUaWsSubProtocolOpenApi"/> or the
    /// bearer-token variant
    /// <c>opcua+openapi+&lt;accesstoken&gt;</c>. Each request/response
    /// round-trip writes / reads a single WebSocket text frame whose body
    /// is the standard <c>{TypeId, Body}</c> OPC UA JSON envelope
    /// (matches the server-side
    /// <c>HttpsTransportListener.AcceptWebSocketOpenApiAsync</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requests are multiplexed by RequestHandle. A single receive loop
    /// dispatches responses, so a pending Publish does not block other services.
    /// </para>
    /// <para>
    /// Bearer authentication rides in the sub-protocol name because
    /// browser WebSocket APIs forbid custom HTTP headers; supply the
    /// token via <see cref="WebApiClientOptions.BearerToken"/> and the
    /// channel appends it to the negotiated sub-protocol.
    /// </para>
    /// </remarks>
    public sealed class WebApiWssTransportChannel : ITransportChannel, ISecureChannel
    {
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly WebApiClientOptions m_userOptions;
        private readonly TimeProvider m_timeProvider;
        private readonly Lock m_connectionLock = new();
        private Connection? m_connection;
        private TransportChannelSettings? m_settings;
        private ChannelQuotas? m_quotas;
        private Uri? m_url;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="telemetry">Telemetry context propagated into the
        /// channel's <see cref="MessageContext"/>.</param>
        /// <param name="options">Default Web API client options. The
        /// channel reads
        /// <see cref="WebApiClientOptions.BearerToken"/> to negotiate
        /// the <c>opcua+openapi+&lt;accesstoken&gt;</c> variant; other
        /// fields (Basic / HttpMessageHandler) are ignored on this
        /// transport.</param>
        /// <param name="timeProvider">Optional time provider reserved
        /// for future use (timeout scheduling).</param>
        public WebApiWssTransportChannel(
            ITelemetryContext telemetry,
            WebApiClientOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            m_telemetry = telemetry
                ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = m_telemetry.CreateLogger<WebApiWssTransportChannel>();
            m_userOptions = options ?? new WebApiClientOptions();
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Gets a value indicating whether this compiled assembly performs
        /// custom OPC UA server certificate validation for outbound WSS
        /// connections.
        /// </summary>
        /// <remarks>
        /// The custom validation callback relies on
        /// <c>ClientWebSocketOptions.RemoteCertificateValidationCallback</c>,
        /// which is only available on .NET 7 or later. When the assembly is
        /// compiled for an older target framework (for example
        /// <c>netstandard2.1</c>) the callback cannot be wired up and this
        /// probe returns <see langword="false"/>, allowing callers and tests to
        /// react at runtime instead of assuming compile-time availability.
        /// </remarks>
        public static bool IsServerCertificateValidationSupported =>
#if NET7_0_OR_GREATER
            true;
#else
            false;
#endif

        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcWssOpenApi;

        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures => TransportChannelFeatures.Reconnect;

        /// <inheritdoc/>
        public EndpointDescription EndpointDescription
            => m_settings?.Description ?? throw BadNotConnected();

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration
            => m_settings?.Configuration ?? throw BadNotConnected();

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext
            => m_quotas?.MessageContext ?? throw BadNotConnected();

        /// <inheritdoc/>
        public ChannelToken CurrentToken => new();

        /// <inheritdoc/>
        public byte[] ChannelThumbprint => [];

        /// <inheritdoc/>
        public byte[] ClientChannelCertificate { get; } = [];

        /// <inheritdoc/>
        public byte[] ServerChannelCertificate { get; } = [];

        /// <inheritdoc/>
        public event ChannelTokenActivatedEventHandler OnTokenActivated
        {
            add { }
            remove { }
        }

        /// <inheritdoc/>
        public int OperationTimeout { get; set; }

        /// <inheritdoc/>
        public async ValueTask OpenAsync(
            Uri url,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            ThrowIfDisposed();

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            Uri endpointUrl = NormalizeUrl(url);

            var quotas = new ChannelQuotas(new ServiceMessageContext(m_telemetry, settings.Factory!)
            {
                MaxArrayLength = settings.Configuration!.MaxArrayLength,
                MaxByteStringLength = settings.Configuration.MaxByteStringLength,
                MaxMessageSize = settings.Configuration.MaxMessageSize,
                MaxStringLength = settings.Configuration.MaxStringLength,
                MaxEncodingNestingLevels = settings.Configuration.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = settings.Configuration.MaxDecoderRecoveries,
                NamespaceUris = settings.NamespaceUris!,
                ServerUris = new StringTable()
            })
            {
                MaxBufferSize = settings.Configuration.MaxBufferSize,
                MaxMessageSize = settings.Configuration.MaxMessageSize,
                ChannelLifetime = settings.Configuration.ChannelLifetime,
                SecurityTokenLifetime = settings.Configuration.SecurityTokenLifetime,
                CertificateValidator = settings.CertificateValidator,
                SecurityPolicyRegistry = settings.SecurityPolicyRegistry
            };

            string subProtocol = string.IsNullOrEmpty(m_userOptions.BearerToken)
                ? Profiles.OpcUaWsSubProtocolOpenApi
                : Profiles.OpcUaWsSubProtocolOpenApiBearerPrefix + m_userOptions.BearerToken;

            if (!string.IsNullOrEmpty(m_userOptions.BearerToken))
            {
                // Bearer-in-sub-protocol exposes the token to every
                // TCP intermediary in the 101 handshake (the spec requires
                // the server to echo the selected sub-protocol). When the
                // negotiated URL is not wss://, refuse to send the token
                // in cleartext.
                if (!IsSecureScheme(endpointUrl))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Bearer access token must not be sent over plain HTTP/WS. " +
                        "Use a wss:// (TLS) endpoint or omit BearerToken.");
                }
                m_logger.WSSOpcuaOpenapiAccesstokenBearerToken();
            }

            var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol(subProtocol);

            if (settings.ClientCertificate != null)
            {
                Certificate cert = settings.ClientCertificate.AddRef();
                try
                {
                    X509Certificate2 x509 = cert.AsX509Certificate2();
                    ws.Options.ClientCertificates ??= [];
                    ws.Options.ClientCertificates.Add(x509);
                }
                finally
                {
                    cert.Dispose();
                }
            }

            // TLS server-certificate validation: delegate to the OPC UA
            // CertificateValidator when one is configured (mirrors the
            // sibling HttpsTransportChannel.ServerCertificateCustom...
            // callback). When no validator is configured, fall back to
            // the TLS chain/hostname result so an attacker MITM can no
            // longer present an arbitrary certificate and be silently
            // trusted.
            // RemoteCertificateValidationCallback was added in .NET 7;
            // on legacy TFMs (net472 / net48 / netstandard2.x) the
            // property does not exist, so the channel falls back to the
            // OS-level TLS chain check (see also the
            // HttpsTransportListener doc note about legacy-TFM WSS).
#if NET7_0_OR_GREATER
            ws.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                ValidateServerCertificate(sender, certificate, chain, errors, quotas.CertificateValidator);
#endif

            var connection = new Connection(ws, quotas, m_logger);
            lock (m_connectionLock)
            {
                if (m_disposed || m_connection != null)
                {
                    connection.Dispose();
                    ThrowIfDisposed();
                    throw new ServiceResultException(StatusCodes.BadInvalidState, "The channel is already open.");
                }
                m_url = endpointUrl;
                m_settings = settings;
                m_quotas = quotas;
                OperationTimeout = settings.Configuration.OperationTimeout;
                connection.Start();
                m_connection = connection;
            }
            try
            {
                using var opening = CancellationTokenSource.CreateLinkedTokenSource(ct, connection.ShutdownToken);
                await ws.ConnectAsync(endpointUrl, opening.Token).ConfigureAwait(false);
                opening.Token.ThrowIfCancellationRequested();
                connection.Opened.TrySetResult(true);
            }
            catch
            {
                lock (m_connectionLock)
                {
                    if (ReferenceEquals(m_connection, connection))
                    {
                        m_connection = null;
                    }
                }
                connection.Stop(BadNotConnected());
                await connection.Receiver.ConfigureAwait(false);
                throw;
            }

            // Hydrate the EndpointDescription (server cert, app info,
            // user identity policies) from a sessionless GetEndpoints
            // over the freshly-opened WebSocket so Session.OpenAsync can
            // encrypt non-anonymous user tokens. Matches the
            // hydrate-on-open behaviour of WebApiTransportChannel; without
            // it, UserName under Basic256Sha256 NREs in RsaUtils.Encrypt.
            await HydrateEndpointFromServerAsync(settings, ct).ConfigureAwait(false);
        }

        private async Task HydrateEndpointFromServerAsync(
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            if (settings.Description == null || m_connection == null || m_quotas == null)
            {
                return;
            }
            if (settings.Description.ServerCertificate.Length > 0)
            {
                return;
            }

            var request = new GetEndpointsRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = m_timeProvider.GetUtcNow().UtcDateTime,
                    RequestHandle = 1,
                    TimeoutHint = (uint)OperationTimeout
                },
                EndpointUrl = settings.Description.EndpointUrl
            };

            IServiceResponse response;
            try
            {
                response = await SendRequestAsync(request, ct).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (response is not GetEndpointsResponse endpointsResponse ||
                endpointsResponse.Endpoints.Count == 0)
            {
                return;
            }

            EndpointDescription? match = null;
            EndpointDescription? smNoneMatch = null;
            for (int i = 0; i < endpointsResponse.Endpoints.Count; i++)
            {
                EndpointDescription candidate = endpointsResponse.Endpoints[i];
                if (candidate.SecurityMode != MessageSecurityMode.None)
                {
                    continue;
                }
                if (Profiles.IsWssOpenApi(candidate.TransportProfileUri))
                {
                    match = candidate;
                    break;
                }
                smNoneMatch ??= candidate;
            }
            match ??= smNoneMatch;
            if (match == null)
            {
                return;
            }

            settings.Description.ServerCertificate = match.ServerCertificate;
            if (match.Server != null)
            {
                settings.Description.Server = match.Server;
            }
            if (match.UserIdentityTokens.Count > 0)
            {
                settings.Description.UserIdentityTokens = match.UserIdentityTokens;
            }
        }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            if (connection == null)
            {
                throw new ArgumentNullException(nameof(connection));
            }
            return OpenAsync(connection.EndpointUrl, settings, ct);
        }

        /// <inheritdoc/>
        public async ValueTask CloseAsync(CancellationToken ct)
        {
            if (m_disposed)
            {
                return;
            }

            Connection? connection;
            lock (m_connectionLock)
            {
                connection = m_connection;
                m_connection = null;
            }
            if (connection == null)
            {
                return;
            }

            try
            {
                await connection.CloseOutputAsync(ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
            {
                m_logger.WssConnectionClosed(exception);
            }
            finally
            {
                connection.Stop(new ServiceResultException(StatusCodes.BadConnectionClosed));
                await connection.Receiver.ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection = null,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            Uri url = connection?.EndpointUrl ?? m_url ?? throw BadNotConnected();
            TransportChannelSettings settings = m_settings ?? throw BadNotConnected();
            await CloseAsync(ct).ConfigureAwait(false);
            await OpenAsync(url, settings, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<IServiceResponse> SendRequestAsync(
            IServiceRequest request,
            CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ThrowIfDisposed();
            Connection connection;
            lock (m_connectionLock)
            {
                connection = m_connection ?? throw BadNotConnected();
            }
            ChannelQuotas quotas = connection.Quotas;

            // Encode the request using the standard {TypeId, Body}
            // envelope expected by the server's
            // AcceptWebSocketOpenApiAsync (same envelope as opcua+uajson;
            // the OpenAPI sub-protocol is distinguished by the
            // negotiated sub-protocol name and the discovery profile URI).
            byte[] requestBytes;
            using (var memory = new MemoryStream())
            {
                using (var encoder = new JsonEncoder(memory, quotas.MessageContext, JsonEncoderOptions.Compact))
                {
                    encoder.EncodeMessage(request, request.TypeId);
                }
                requestBytes = memory.ToArray();
            }

            uint handle = request.RequestHeader?.RequestHandle ?? 0;
            TaskCompletionSource<IServiceResponse> response = connection.Register(handle);
            using CancellationTokenSource timeout = m_timeProvider.CreateCancellationTokenSource(
                OperationTimeout > 0 ? TimeSpan.FromMilliseconds(OperationTimeout) : Timeout.InfiniteTimeSpan);
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            CancellationToken requestToken = requestCancellation.Token;
            using CancellationTokenRegistration registration = requestToken.Register(
                () => response.TrySetCanceled(requestToken));
            using var sendCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                requestToken, connection.ShutdownToken);
            bool sent = false;
            try
            {
                await connection.SendAsync(requestBytes, sendCancellation.Token).ConfigureAwait(false);
                sent = true;
            }
            catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
            {
                response.TrySetCanceled(requestToken);
                if (connection.Socket.State == WebSocketState.Aborted)
                {
                    connection.Stop(new ServiceResultException(StatusCodes.BadConnectionClosed));
                }
            }
            catch (Exception exception) when (
                exception is WebSocketException or IOException or ObjectDisposedException or OperationCanceledException)
            {
                connection.Stop(new ServiceResultException(
                    StatusCodes.BadConnectionClosed, "The WebSocket send failed.", exception));
            }
            finally
            {
                if (!sent)
                {
                    connection.Remove(handle);
                }
            }
            try
            {
                return await response.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new ServiceResultException(StatusCodes.BadRequestTimeout);
            }
        }

        /// <summary>
        /// Decoder options applied to every inbound response. Clients
        /// typically don't know all server namespace URIs up front, so
        /// UpdateNamespaceTable=true lets the codec append unknown URIs
        /// to the message context's NamespaceTable on the fly. Without
        /// this, NodeIds whose namespace URI isn't already registered
        /// would decode as NodeId.Null (e.g. CreateSession's SessionId
        /// and AuthenticationToken would be lost).
        /// </summary>
        private static readonly JsonDecoderOptions s_decoderOptions = new()
        {
            UpdateNamespaceTable = true
        };

        private static IServiceResponse DecodeServiceResponse(
            byte[] payload,
            IServiceMessageContext context)
        {
            using var decoder = new JsonDecoder(
                new System.Buffers.ReadOnlySequence<byte>(payload),
                context,
                s_decoderOptions);
            return decoder.DecodeMessage<IServiceResponse>();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_connectionLock)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_connection?.Dispose();
                m_connection = null;
            }
            m_settings?.ServerCertificate?.Dispose();
            m_settings?.ClientCertificate?.Dispose();
            m_settings?.ClientCertificateChain?.Dispose();
        }

        private sealed class Connection : IDisposable
        {
            public Connection(ClientWebSocket socket, ChannelQuotas quotas, ILogger logger)
            {
                Socket = socket;
                Quotas = quotas;
                ShutdownToken = m_shutdown.Token;
                m_logger = logger;
            }

            public ClientWebSocket Socket { get; }
            public ChannelQuotas Quotas { get; }
            public CancellationToken ShutdownToken { get; }
            public TaskCompletionSource<bool> Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task Receiver { get; private set; } = Task.CompletedTask;

            public void Start()
            {
                m_started = true;
                Receiver = ReceiveResponsesAsync();
            }

            public TaskCompletionSource<IServiceResponse> Register(uint handle)
            {
                lock (m_lock)
                {
                    if (m_stopped || Opened.Task.Status != TaskStatus.RanToCompletion)
                    {
                        throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                    }
                    var completion = new TaskCompletionSource<IServiceResponse>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    if (!m_pending.TryAdd(handle, completion))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadInvalidArgument, "RequestHandle is already in use on this channel.");
                    }
                    return completion;
                }
            }

            public void Complete(IServiceResponse response)
            {
                TaskCompletionSource<IServiceResponse>? completion;
                lock (m_lock)
                {
                    if (!m_pending.TryGetValue(response.ResponseHeader.RequestHandle, out completion))
                    {
                        throw new ServiceResultException(StatusCodes.BadUnknownResponse);
                    }
                    m_pending.Remove(response.ResponseHeader.RequestHandle);
                }
                completion.TrySetResult(response);
            }

            public void Remove(uint handle)
            {
                lock (m_lock)
                {
                    m_pending.Remove(handle);
                }
            }

            public async Task SendAsync(byte[] bytes, CancellationToken ct)
            {
                await m_sendLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                        endOfMessage: true, ct).ConfigureAwait(false);
                }
                finally
                {
                    m_sendLock.Release();
                }
            }

            public async Task CloseOutputAsync(CancellationToken ct)
            {
                await m_sendLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, ct)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    m_sendLock.Release();
                }
            }

            public void Stop(Exception failure)
            {
                TaskCompletionSource<IServiceResponse>[] pending;
                lock (m_lock)
                {
                    if (m_stopped)
                    {
                        return;
                    }
                    m_stopped = true;
                    pending = [.. m_pending.Values];
                    m_pending.Clear();
                }
                foreach (TaskCompletionSource<IServiceResponse> completion in pending)
                {
                    completion.TrySetException(failure);
                }
                m_shutdown.Cancel();
                Socket.Abort();
            }

            public void Dispose()
            {
                Stop(new ServiceResultException(StatusCodes.BadConnectionClosed));
                if (!m_started)
                {
                    DisposeResources();
                }
            }

            private async Task ReceiveResponsesAsync()
            {
                Exception failure = new ServiceResultException(StatusCodes.BadConnectionClosed);
                try
                {
                    await Opened.Task.WaitAsync(ShutdownToken).ConfigureAwait(false);
                    int maxSize = Quotas.MaxMessageSize > 0 ? Quotas.MaxMessageSize : int.MaxValue;
                    while (!ShutdownToken.IsCancellationRequested)
                    {
                        byte[] bytes = await ReceiveMessageAsync(Socket, maxSize, ShutdownToken).ConfigureAwait(false);
                        Complete(DecodeServiceResponse(bytes, Quotas.MessageContext));
                    }
                }
                catch (OperationCanceledException) when (ShutdownToken.IsCancellationRequested)
                {
                }
                catch (Exception exception) when (
                    exception is ServiceResultException or WebSocketException or IOException or FormatException or
                        ObjectDisposedException or InvalidOperationException or System.Text.Json.JsonException)
                {
                    failure = exception is ServiceResultException
                        ? exception
                        : new ServiceResultException(
                            StatusCodes.BadConnectionClosed, "The WebSocket receive failed.", exception);
                    m_logger.WssConnectionClosed(exception);
                }
                finally
                {
                    Stop(failure);
                    await m_sendLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                    DisposeResources();
                }
            }

            private void DisposeResources()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }
                if (Socket.Options.ClientCertificates != null)
                {
                    foreach (X509Certificate certificate in Socket.Options.ClientCertificates)
                    {
                        certificate.Dispose();
                    }
                }
                Socket.Dispose();
                m_sendLock.Dispose();
                m_shutdown.Dispose();
            }

            private readonly Lock m_lock = new();
            private readonly SemaphoreSlim m_sendLock = new(1, 1);
            private readonly Dictionary<uint, TaskCompletionSource<IServiceResponse>> m_pending = [];
            private readonly CancellationTokenSource m_shutdown = new();
            private readonly ILogger m_logger;
            private bool m_stopped;
            private bool m_started;
            private int m_disposed;
        }

        private static async Task<byte[]> ReceiveMessageAsync(
            WebSocket ws,
            int maxMessageSize,
            CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            byte[] receiveBuffer = new byte[8192];
            while (true)
            {
                WebSocketReceiveResult result = await ws
                    .ReceiveAsync(new ArraySegment<byte>(receiveBuffer), ct)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConnectionClosed,
                        "WebSocket closed by server while awaiting response.");
                }
                buffer.Write(receiveBuffer, 0, result.Count);
                if (buffer.Length > maxMessageSize)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "Response exceeded MaxMessageSize {0}.",
                        maxMessageSize);
                }
                if (result.EndOfMessage)
                {
                    return buffer.ToArray();
                }
                // Zero-progress continuation-frame guard. A peer that
                // streams empty continuation frames without ever
                // terminating the message would spin this loop (CPU DoS).
                // Mirrors the WebSocketByteTransport guard for opcua+uacp.
                if (result.Count == 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "WebSocket continuation frame made no progress.");
                }
            }
        }

#if NET7_0_OR_GREATER
        private bool ValidateServerCertificate(
            object sender,
            X509Certificate? certificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors,
            ICertificateValidatorEx? validator)
        {
            try
            {
                if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateHostNameInvalid,
                        "The TLS certificate host name does not match the endpoint.");
                }
                using CertificateCollection validationCollection = CertificateValidationHelpers
                    .BuildValidationCertificateCollection(certificate, chain);
                if (validator != null)
                {
                    // CA2025: task awaited via GetAwaiter().GetResult(); the disposable's
                    // using scope extends past the await. Mirrors HttpsTransportChannel.
#pragma warning disable CA2025
                    CertificateValidationResult validationResult = validator
                        .ValidateAsync(validationCollection, ct: default)
                        .GetAwaiter()
                        .GetResult();
#pragma warning restore CA2025
                    if (!validationResult.IsValid)
                    {
                        // Log a stable, non-tainted message and return false
                        // directly. Do NOT propagate validationResult.StatusCode
                        // into a thrown exception that the outer catch will
                        // log — CodeQL (CS347 "Clear text storage of sensitive
                        // information") tracks the StatusCode field as
                        // certificate-derived data and would flag the log
                        // entry. Mirrors WebApiTransportChannel.
                        m_logger.ChannelTypeOPCUACertificateValidatorRejected(nameof(WebApiWssTransportChannel));
                        return false;
                    }
                    return true;
                }

                if (sslPolicyErrors != SslPolicyErrors.None)
                {
                    // No OPC UA certificate validator configured: do not
                    // blindly accept the server certificate. Fall back to
                    // the default TLS chain/hostname result (MITM guard).
                    m_logger.ChannelTypeNoCertificateValidatorConfiguredTLS(
                        nameof(WebApiWssTransportChannel),
                        sslPolicyErrors);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                m_logger.ChannelTypeFailedValidateServerCertificate(
                    ex,
                    nameof(WebApiWssTransportChannel));
                return false;
            }
        }

#endif

        /// <summary>
        /// WSS-bearer requires TLS so the token cannot be observed by
        /// network intermediaries. The sub-protocol still appears in
        /// the server's access log on the wss path; that's why short
        /// token TTLs (&lt;= 60s) and log redaction are still recommended.
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private static bool IsSecureScheme(Uri? url)
        {
            if (url == null)
            {
                return false;
            }
            return string.Equals(url.Scheme, Utils.UriSchemeWss, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(url.Scheme, Utils.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private static Uri NormalizeUrl(Uri url)
        {
            // The synthetic registry-key scheme "opc.wss+openapi" must
            // become an addressable "wss://..." URL before being passed
            // to ClientWebSocket.
            if (string.Equals(url.Scheme, Utils.UriSchemeOpcWssOpenApi, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(url) { Scheme = Utils.UriSchemeWss };
                return builder.Uri;
            }
            if (string.Equals(url.Scheme, Utils.UriSchemeOpcWss, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(url) { Scheme = Utils.UriSchemeWss };
                return builder.Uri;
            }
            return url;
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(WebApiWssTransportChannel));
            }
        }

        private static ServiceResultException BadNotConnected()
        {
            return ServiceResultException.Create(
                StatusCodes.BadNotConnected,
                "The WSS Web API channel is not open.");
        }
    }

    /// <summary>
    /// Source-generated log messages for <see cref="WebApiWssTransportChannel"/>.
    /// </summary>
    internal static partial class WebApiWssTransportChannelLog
    {
        [LoggerMessage(EventId = ClientEventIds.WebApiWssTransportChannel + 0, Level = LogLevel.Warning,
            Message = "WSS opcua+openapi+<accesstoken>: bearer token rides in the WebSocket sub-protocol name" +
                " (browser-compatible). Prefer short-lived tokens (<= 60s) and redact the" +
                " Sec-WebSocket-Protocol header from proxy / WAF logs.")]
        public static partial void WSSOpcuaOpenapiAccesstokenBearerToken(this ILogger logger);

        [LoggerMessage(EventId = ClientEventIds.WebApiWssTransportChannel + 1, Level = LogLevel.Debug,
            Message = "The WSS OpenAPI connection closed.")]
        public static partial void WssConnectionClosed(this ILogger logger, Exception exception);
    }
}
