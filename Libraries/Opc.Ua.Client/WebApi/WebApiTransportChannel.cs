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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client.WebApi
{
    /// <summary>
    /// Adapter that surfaces the HTTPS Web API binding
    /// (OPC UA Part 6 §G.3 "OpenAPI Mapping") through the standard
    /// <see cref="ITransportChannel"/> contract so the existing
    /// <c>Session</c> / <c>ManagedSession</c> / V2 subscription engine
    /// dispatch through Web API endpoints unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SendRequestAsync(IServiceRequest, CancellationToken)"/>
    /// dispatches on the runtime CLR type of the request:
    /// <see cref="WebApiServiceRoutes.TryGetByRequestType"/> resolves
    /// the HTTP path + response type, the body is encoded with
    /// <see cref="WebApiBodyCodec"/>, POSTed via the inner
    /// <see cref="WebApiClient"/>, and the response is decoded via
    /// the non-generic <c>WebApiBodyCodec.DecodeBodyAsync(Type,
    /// Stream, IServiceMessageContext, JsonDecoderOptions?,
    /// CancellationToken)</c> overload.
    /// </para>
    /// <para>
    /// The channel is stateless on the OPC UA layer: there is no secure
    /// channel handshake (TLS handles transport security), no channel
    /// token, no reverse connect.
    /// <see cref="ReconnectAsync(ITransportWaitingConnection?, CancellationToken)"/>
    /// is a no-op because <see cref="HttpClient"/> reconnects underneath
    /// transparently. Session-layer authentication
    /// (<c>AuthenticationToken</c> on the request header) is set by
    /// <see cref="Session"/> the same way it is for any other channel.
    /// </para>
    /// <para>
    /// Web API transport authentication (Bearer / Basic / mTLS) is
    /// configured on the supplied <see cref="WebApiClientOptions"/>
    /// or auto-derived from
    /// <see cref="TransportChannelSettings.ClientCertificate"/> when
    /// the channel is opened.
    /// </para>
    /// </remarks>
    public sealed class WebApiTransportChannel : ITransportChannel, ISecureChannel
    {
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly WebApiClientOptions m_userOptions;
        private readonly IOpcUaHttpClientFactory? m_httpClientFactory;
        private readonly TimeProvider m_timeProvider;
        private WebApiClient? m_client;
        private TransportChannelSettings? m_settings;
        private ChannelQuotas? m_quotas;
        private Uri? m_url;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        /// <param name="telemetry">
        /// Telemetry context propagated into the channel's
        /// <see cref="MessageContext"/>.
        /// </param>
        /// <param name="options">
        /// Default Web API client options applied to the inner
        /// <see cref="WebApiClient"/>. Encoding, auth tokens, request
        /// timeout, and <see cref="HttpMessageHandler"/> are taken from
        /// here. May be <c>null</c> to accept defaults.
        /// </param>
        /// <param name="httpClientFactory">
        /// Optional OPC UA HTTP client factory used when the supplied
        /// <see cref="WebApiClientOptions"/> does not carry an explicit
        /// <see cref="HttpMessageHandler"/>.
        /// </param>
        /// <param name="timeProvider">
        /// Optional <see cref="TimeProvider"/> reserved for future use
        /// (timeout scheduling, retry pacing). Defaults to
        /// <see cref="TimeProvider.System"/>.
        /// </param>
        public WebApiTransportChannel(
            ITelemetryContext telemetry,
            WebApiClientOptions? options = null,
            IOpcUaHttpClientFactory? httpClientFactory = null,
            TimeProvider? timeProvider = null)
        {
            m_telemetry = telemetry
                ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = m_telemetry.CreateLogger<WebApiTransportChannel>();
            m_userOptions = options ?? new WebApiClientOptions();
            m_httpClientFactory = httpClientFactory;
            m_timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeOpcHttpsWebApi;

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
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            ThrowIfDisposed();

            m_url = NormalizeUrl(url);
            m_settings = settings;
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

            WebApiClientOptions clientOptions = BuildClientOptions(settings);
            m_client = WebApiClient.Create(m_url, clientOptions);

            // The Web API binding is selected by transport profile URI on a
            // caller-supplied EndpointDescription that typically lacks
            // ServerCertificate, ApplicationUri, and UserIdentityTokens
            // (the caller knows the URL but not the server's certificate /
            // identity model). Standard Session.OpenAsync needs the server
            // certificate to encrypt non-anonymous user tokens
            // (e.g. UserName under Basic256Sha256). Issue a sessionless
            // GetEndpoints over the freshly-opened Web API channel so we
            // can hydrate those fields from the matching SM=None
            // server-side description before Session.OpenAsync runs.
            await HydrateEndpointFromServerAsync(settings, ct).ConfigureAwait(false);
        }

        private async Task HydrateEndpointFromServerAsync(
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            if (settings.Description == null || m_client == null || m_url == null)
            {
                return;
            }
            // Skip when the caller already supplied the cert (e.g. when the
            // standard discovery-then-open path was used and updated the
            // description from a prior GetEndpoints).
            if (settings.Description.ServerCertificate.Length > 0)
            {
                return;
            }

            var request = new GetEndpointsRequest
            {
                RequestHeader = new RequestHeader
                {
                    Timestamp = DateTime.UtcNow,
                    RequestHandle = 1,
                    TimeoutHint = (uint)OperationTimeout
                },
                EndpointUrl = settings.Description.EndpointUrl,
                LocaleIds = default,
                ProfileUris = default
            };

            GetEndpointsResponse response;
            try
            {
                response = await m_client
                    .GetEndpointsAsync(request, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Best-effort: if the server doesn't respond to sessionless
                // GetEndpoints over Web API, leave the description as-is.
                // Session.OpenAsync will still fail later if non-anonymous
                // user tokens require encryption, but at least the channel
                // is open and Anonymous + sessionless flows work.
                return;
            }

            EndpointDescription? match = SelectMatchingEndpoint(
                response.Endpoints,
                settings.Description);
            if (match == null)
            {
                return;
            }
            // In-place update on the shared EndpointDescription so the
            // Session sees the populated cert / app description / tokens.
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

        private static EndpointDescription? SelectMatchingEndpoint(
            ArrayOf<EndpointDescription> endpoints,
            EndpointDescription expected)
        {
            // First pass: exact transport profile match (when the server
            // explicitly advertises the OpenAPI endpoint).
            EndpointDescription? webApiMatch = null;
            EndpointDescription? smNoneMatch = null;
            for (int i = 0; i < endpoints.Count; i++)
            {
                EndpointDescription candidate = endpoints[i];
                if (candidate.SecurityMode != MessageSecurityMode.None)
                {
                    continue;
                }
                if (Profiles.IsHttpsOpenApi(candidate.TransportProfileUri))
                {
                    webApiMatch = candidate;
                    break;
                }
                smNoneMatch ??= candidate;
            }
            // If the server doesn't advertise the OpenAPI profile,
            // fall back to any SM=None endpoint — that's the same TLS
            // endpoint we're talking to, just under a different OPC
            // UA transport profile, so its ServerCertificate / Server
            // / UserIdentityTokens are applicable.
            return webApiMatch ?? smNoneMatch;
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
        public ValueTask CloseAsync(CancellationToken ct)
        {
            if (m_disposed)
            {
                return default;
            }

            m_client?.Dispose();
            m_client = null;
            return default;
        }

        /// <inheritdoc/>
        public ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection = null,
            CancellationToken ct = default)
        {
            // HTTPS is connectionless from the OPC UA layer perspective —
            // HttpClient transparently reopens TCP/TLS as needed, so the
            // channel does not have to do anything here. Re-bind the URL
            // when a new waiting connection is supplied.
            if (connection != null)
            {
                m_url = NormalizeUrl(connection.EndpointUrl);
                if (m_client != null && m_url != null)
                {
                    m_client.Dispose();
                    m_client = WebApiClient.Create(m_url, BuildClientOptions(m_settings!));
                }
            }
            return default;
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
            WebApiClient client = m_client ?? throw BadNotConnected();

            // Dispatch on the runtime CLR type via a hard-coded
            // switch over the 28 spec routes. Each branch calls a
            // strongly-typed WebApiClient.<Service>Async method, so
            // every generic instantiation of InvokeAsync<TReq, TRes>
            // is visible to the trimmer at compile time and no
            // RequiresUnreferencedCode / UnconditionalSuppressMessage
            // attribute is needed on this method.
            switch (request)
            {
                case ReadRequest r:
                    return await client.ReadAsync(r, ct).ConfigureAwait(false);
                case WriteRequest r:
                    return await client.WriteAsync(r, ct).ConfigureAwait(false);
                case HistoryReadRequest r:
                    return await client.HistoryReadAsync(r, ct).ConfigureAwait(false);
                case HistoryUpdateRequest r:
                    return await client.HistoryUpdateAsync(r, ct).ConfigureAwait(false);
                case CallRequest r:
                    return await client.CallAsync(r, ct).ConfigureAwait(false);
                case BrowseRequest r:
                    return await client.BrowseAsync(r, ct).ConfigureAwait(false);
                case BrowseNextRequest r:
                    return await client.BrowseNextAsync(r, ct).ConfigureAwait(false);
                case TranslateBrowsePathsToNodeIdsRequest r:
                    return await client.TranslateBrowsePathsToNodeIdsAsync(r, ct).ConfigureAwait(false);
                case RegisterNodesRequest r:
                    return await client.RegisterNodesAsync(r, ct).ConfigureAwait(false);
                case UnregisterNodesRequest r:
                    return await client.UnregisterNodesAsync(r, ct).ConfigureAwait(false);
                case FindServersRequest r:
                    return await client.FindServersAsync(r, ct).ConfigureAwait(false);
                case GetEndpointsRequest r:
                    return await client.GetEndpointsAsync(r, ct).ConfigureAwait(false);
                case CreateSessionRequest r:
                    return await client.CreateSessionAsync(r, ct).ConfigureAwait(false);
                case ActivateSessionRequest r:
                    return await client.ActivateSessionAsync(r, ct).ConfigureAwait(false);
                case CloseSessionRequest r:
                    return await client.CloseSessionAsync(r, ct).ConfigureAwait(false);
                case CancelRequest r:
                    return await client.CancelAsync(r, ct).ConfigureAwait(false);
                case CreateMonitoredItemsRequest r:
                    return await client.CreateMonitoredItemsAsync(r, ct).ConfigureAwait(false);
                case ModifyMonitoredItemsRequest r:
                    return await client.ModifyMonitoredItemsAsync(r, ct).ConfigureAwait(false);
                case SetMonitoringModeRequest r:
                    return await client.SetMonitoringModeAsync(r, ct).ConfigureAwait(false);
                case SetTriggeringRequest r:
                    return await client.SetTriggeringAsync(r, ct).ConfigureAwait(false);
                case DeleteMonitoredItemsRequest r:
                    return await client.DeleteMonitoredItemsAsync(r, ct).ConfigureAwait(false);
                case CreateSubscriptionRequest r:
                    return await client.CreateSubscriptionAsync(r, ct).ConfigureAwait(false);
                case ModifySubscriptionRequest r:
                    return await client.ModifySubscriptionAsync(r, ct).ConfigureAwait(false);
                case SetPublishingModeRequest r:
                    return await client.SetPublishingModeAsync(r, ct).ConfigureAwait(false);
                case PublishRequest r:
                    return await client.PublishAsync(r, ct).ConfigureAwait(false);
                case RepublishRequest r:
                    return await client.RepublishAsync(r, ct).ConfigureAwait(false);
                case TransferSubscriptionsRequest r:
                    return await client.TransferSubscriptionsAsync(r, ct).ConfigureAwait(false);
                case DeleteSubscriptionsRequest r:
                    return await client.DeleteSubscriptionsAsync(r, ct).ConfigureAwait(false);
                default:
                    string typeName = request.GetType().FullName ?? request.GetType().Name;
                    throw ServiceResultException.Create(
                        StatusCodes.BadServiceUnsupported,
                        "No Web API route registered for request type '{0}'.",
                        typeName);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            m_client?.Dispose();
            m_client = null;
            m_settings?.ServerCertificate?.Dispose();
            m_settings?.ClientCertificate?.Dispose();
            m_settings?.ClientCertificateChain?.Dispose();
        }

        private WebApiClientOptions BuildClientOptions(TransportChannelSettings settings)
        {
            // Compose: caller-supplied defaults + auto-derived TLS handler
            // (server-cert validation when CertificateValidator is set, plus
            // mutual TLS when ClientCertificate is set). The caller can
            // opt out entirely by supplying a custom HttpMessageHandler.
            HttpMessageHandler? handler = m_userOptions.HttpMessageHandler;
            bool disposeHandler = m_userOptions.DisposeHandler;
            if (handler == null &&
                (settings.ClientCertificate != null || settings.CertificateValidator != null))
            {
                handler = CreateTlsHandler(settings);
                disposeHandler = true;
            }

            return new WebApiClientOptions
            {
                Encoding = m_userOptions.Encoding,
                MessageContext = m_quotas?.MessageContext ?? m_userOptions.MessageContext,
                HttpMessageHandler = handler,
                DisposeHandler = disposeHandler,
                BearerToken = m_userOptions.BearerToken,
                BasicCredentials = m_userOptions.BasicCredentials,
                RequestTimeout = m_userOptions.RequestTimeout
            };
        }

        private HttpClientHandler CreateTlsHandler(TransportChannelSettings settings)
        {
            var handler = new HttpClientHandler
            {
                ClientCertificateOptions = ClientCertificateOption.Manual,
                AllowAutoRedirect = false
            };

            if (settings.ClientCertificate != null)
            {
                Certificate cert = settings.ClientCertificate.AddRef();
                try
                {
                    X509Certificate2 x509 = cert.AsX509Certificate2();
                    handler.ClientCertificates.Add(x509);
                }
                finally
                {
                    cert.Dispose();
                }
            }

            // sec-10: install a server-cert validation callback so the
            // OPC UA CertificateValidator (TrustedPeers store,
            // application-URI rule, rejected list) is consulted. Mirrors
            // HttpsTransportChannel.ServerCertificateCustomValidationCallback.
            // Without this, the WebApi client only ran the default TLS
            // chain checks — OPC UA-specific trust state was bypassed.
            if (settings.CertificateValidator != null)
            {
                handler.ServerCertificateCustomValidationCallback = ValidateServerCertificate;
            }

            return handler;
        }

        private bool ValidateServerCertificate(
            HttpRequestMessage request,
            X509Certificate2? certificate,
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
                else if (certificate != null)
                {
                    validationChain.Add(certificate);
                }

                using var validationCollection = CertificateCollection.From(validationChain);
                ICertificateValidatorEx? validator = m_quotas?.CertificateValidator;
                if (validator != null)
                {
                    // CA2025: task awaited via GetAwaiter().GetResult(); the
                    // disposable's using scope extends past the await. Mirrors
                    // HttpsTransportChannel.
#pragma warning disable CA2025
                    CertificateValidationResult validationResult = validator
                        .ValidateAsync(validationCollection, ct: default)
                        .GetAwaiter()
                        .GetResult();
#pragma warning restore CA2025
                    if (!validationResult.IsValid)
                    {
                        m_logger.LogError(
                            "{ChannelType} Server certificate rejected by CertificateValidator: {Status}.",
                            nameof(WebApiTransportChannel),
                            validationResult.StatusCode);
                        return false;
                    }
                    return true;
                }

                if (sslPolicyErrors != SslPolicyErrors.None)
                {
                    m_logger.LogError(
                        "{ChannelType} No certificate validator configured and TLS reported {Errors}; rejecting server certificate.",
                        nameof(WebApiTransportChannel),
                        sslPolicyErrors);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "{ChannelType} Failed to validate server certificate.",
                    nameof(WebApiTransportChannel));
                return false;
            }
        }

        private static Uri NormalizeUrl(Uri url)
        {
            // The synthetic registry-key scheme "opc.https+webapi" must
            // become an addressable "https://..." URL before being passed
            // to HttpClient.
            if (string.Equals(url.Scheme, Utils.UriSchemeOpcHttpsWebApi, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(url) { Scheme = Utils.UriSchemeHttps };
                return builder.Uri;
            }
            if (string.Equals(url.Scheme, Utils.UriSchemeOpcHttps, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(url) { Scheme = Utils.UriSchemeHttps };
                return builder.Uri;
            }
            return url;
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(WebApiTransportChannel));
            }
        }

        private static ServiceResultException BadNotConnected()
        {
            return ServiceResultException.Create(
                StatusCodes.BadNotConnected,
                "The Web API channel is not open.");
        }
    }
}
