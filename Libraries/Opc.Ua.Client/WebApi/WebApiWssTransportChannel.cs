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
    /// The channel is single-threaded for in-flight requests — WebSockets
    /// are message-oriented but not multiplexed. Callers that need
    /// parallel requests should open multiple channels.
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
        private readonly SemaphoreSlim m_sendLock = new(1, 1);
        private ClientWebSocket? m_ws;
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

        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcWssOpenApi;

        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures => TransportChannelFeatures.None;

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

            m_url = NormalizeUrl(url);
            m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
            OperationTimeout = settings.Configuration?.OperationTimeout ?? 60000;

            m_quotas = new ChannelQuotas(new ServiceMessageContext(m_telemetry, settings.Factory!)
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
                CertificateValidator = settings.CertificateValidator
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
                if (!IsSecureScheme(m_url))
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
            ws.Options.RemoteCertificateValidationCallback = ValidateServerCertificate;
#endif

            try
            {
                await ws.ConnectAsync(m_url, ct).ConfigureAwait(false);
            }
            catch
            {
                ws.Dispose();
                throw;
            }
            m_ws = ws;

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
            if (settings.Description == null || m_ws == null || m_quotas == null)
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

            ClientWebSocket? ws = m_ws;
            m_ws = null;
            if (ws == null)
            {
                return;
            }

            try
            {
                if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await ws.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        string.Empty,
                        ct).ConfigureAwait(false);
                }
            }
            catch
            {
                // Best-effort close.
            }
            finally
            {
                ws.Dispose();
            }
        }

        /// <inheritdoc/>
        public ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection = null,
            CancellationToken ct = default)
        {
            // WSS is connection-oriented; reconnect would require
            // re-opening the channel + re-running CreateSession/Activate.
            // Defer to the managed session reconnect path; the channel
            // itself does not attempt silent reconnect.
            throw ServiceResultException.Create(
                StatusCodes.BadNotSupported,
                "{0} does not support implicit reconnect; use the ManagedSession reconnect policy.",
                nameof(WebApiWssTransportChannel));
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
            ClientWebSocket ws = m_ws ?? throw BadNotConnected();
            ChannelQuotas quotas = m_quotas ?? throw BadNotConnected();

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

            byte[] responseBytes;
            await m_sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await ws.SendAsync(
                    new ArraySegment<byte>(requestBytes, 0, requestBytes.Length),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    ct).ConfigureAwait(false);

                responseBytes = await ReceiveMessageAsync(ws, quotas.MaxBufferSize, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_sendLock.Release();
            }

            return DecodeServiceResponse(
                responseBytes,
                quotas.MessageContext);
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
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_ws?.Dispose();
            m_ws = null;
            m_sendLock.Dispose();
            m_settings?.ServerCertificate?.Dispose();
            m_settings?.ClientCertificate?.Dispose();
            m_settings?.ClientCertificateChain?.Dispose();
        }

        private static async Task<byte[]> ReceiveMessageAsync(
            WebSocket ws,
            int maxBufferSize,
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
                if (buffer.Length > maxBufferSize)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded,
                        "Response exceeded MaxBufferSize {0}.",
                        maxBufferSize);
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
            SslPolicyErrors sslPolicyErrors)
        {
            try
            {
                var validationChain = new X509Certificate2Collection();
                if (chain != null && chain.ChainElements != null)
                {
                    foreach (X509ChainElement element in chain.ChainElements)
                    {
                        validationChain.Add(element.Certificate);
                    }
                }
                else if (certificate is X509Certificate2 x509)
                {
                    validationChain.Add(x509);
                }
                else if (certificate != null)
                {
                    validationChain.Add(new X509Certificate2(certificate));
                }

                using var validationCollection = CertificateCollection.From(validationChain);
                ICertificateValidatorEx? validator = m_quotas?.CertificateValidator;
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
    }

}
