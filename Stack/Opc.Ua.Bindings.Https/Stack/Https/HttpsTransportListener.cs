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
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua.Security.Certificates;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

#if NETSTANDARD2_1 || NET472_OR_GREATER || NET5_0_OR_GREATER
using System.Security.Cryptography;
#endif

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Creates a new <see cref="HttpsTransportListener"/> with
    /// <see cref="ITransportListener"/> interface.
    /// </summary>
    public class HttpsTransportListenerFactory : HttpsServiceHost
    {
        /// <summary>
        /// The protocol supported by the listener.
        /// </summary>
        public override string UriScheme => Utils.UriSchemeHttps;

        /// <inheritdoc/>
        protected override string? JsonTransportProfileUri => Profiles.HttpsJsonTransport;

        /// <summary>
        /// The method creates a new instance of a <see cref="HttpsTransportListener"/>.
        /// </summary>
        /// <returns>The transport listener.</returns>
        public override ITransportListener Create(ITelemetryContext telemetry)
        {
            return new HttpsTransportListener(Utils.UriSchemeHttps, telemetry);
        }
    }

    /// <summary>
    /// Creates a new <see cref="HttpsTransportListener"/> with
    /// <see cref="ITransportListener"/> interface.
    /// </summary>
    public class OpcHttpsTransportListenerFactory : HttpsServiceHost
    {
        /// <summary>
        /// The protocol supported by the listener.
        /// </summary>
        public override string UriScheme => Utils.UriSchemeOpcHttps;

        /// <inheritdoc/>
        protected override string? JsonTransportProfileUri => Profiles.HttpsJsonTransport;

        /// <summary>
        /// The method creates a new instance of a <see cref="HttpsTransportListener"/>.
        /// </summary>
        /// <returns>The transport listener.</returns>
        public override ITransportListener Create(ITelemetryContext telemetry)
        {
            return new HttpsTransportListener(Utils.UriSchemeOpcHttps, telemetry);
        }
    }

    /// <summary>
    /// Creates a new <see cref="HttpsTransportListener"/> bound to the
    /// standard <c>wss://</c> URL scheme for the
    /// <see cref="Profiles.UaWssTransport"/> profile.
    /// </summary>
    public class WssTransportListenerFactory : HttpsServiceHost
    {
        /// <inheritdoc/>
        public override string UriScheme => Utils.UriSchemeWss;

        /// <inheritdoc/>
        protected override string TransportProfileUri => Profiles.UaWssTransport;

        /// <inheritdoc/>
        protected override string? JsonTransportProfileUri => Profiles.UaWssJsonTransport;

        /// <inheritdoc/>
        public override ITransportListener Create(ITelemetryContext telemetry)
        {
            return new HttpsTransportListener(Utils.UriSchemeWss, telemetry);
        }
    }

    /// <summary>
    /// Creates a new <see cref="HttpsTransportListener"/> bound to the
    /// OPC UA <c>opc.wss://</c> URL scheme alias for the
    /// <see cref="Profiles.UaWssTransport"/> profile.
    /// </summary>
    public class OpcWssTransportListenerFactory : HttpsServiceHost
    {
        /// <inheritdoc/>
        public override string UriScheme => Utils.UriSchemeOpcWss;

        /// <inheritdoc/>
        protected override string TransportProfileUri => Profiles.UaWssTransport;

        /// <inheritdoc/>
        protected override string? JsonTransportProfileUri => Profiles.UaWssJsonTransport;

        /// <inheritdoc/>
        public override ITransportListener Create(ITelemetryContext telemetry)
        {
            return new HttpsTransportListener(Utils.UriSchemeOpcWss, telemetry);
        }
    }

    /// <summary>
    /// Implements the kestrel startup of the Https listener.
    /// </summary>
    public class Startup
    {
        private const string kHttpsContentType = "text/plain";

        /// <summary>
        /// Configure the request pipeline for the listener.
        /// The <paramref name="listener"/> is resolved from the web host's DI container,
        /// which is populated via <c>IWebHostBuilder.ConfigureServices</c> in
        /// <see cref="HttpsTransportListener.ConfigureWebHost"/>.
        /// Using method injection here (rather than constructor injection) ensures the
        /// instance is resolved from the web-host app container on all target frameworks,
        /// including .NET 8+ where <c>HostBuilder</c> activates <c>Startup</c> from a
        /// separate host-level container that does not include web-host app services.
        /// </summary>
        /// <param name="appBuilder">The application builder.</param>
        /// <param name="listener">The transport listener that handles OPC UA requests.</param>
        public void Configure(IApplicationBuilder appBuilder, HttpsTransportListener listener)
        {
            // Enable WebSocket upgrades so the WSS handler added in p2-wss-listener-handler
            // can accept opcua+uacp / opcua+uajson sub-protocols.
            appBuilder.UseWebSockets();

            appBuilder.Run(context =>
            {
                // 1) WSS opcua+uacp / opcua+uajson upgrade (Part 6 §7.5.2)
                if (context.WebSockets.IsWebSocketRequest)
                {
                    return listener.AcceptWebSocketAsync(context);
                }

                // 2) HTTP POST is the only verb defined for HTTPS binary / JSON
                //    (Part 6 §7.4.2 + §7.4.5).
                if (context.Request.Method != "POST")
                {
                    context.Response.ContentLength = 0;
                    context.Response.ContentType = kHttpsContentType;
                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    return context.Response.WriteAsync(string.Empty);
                }

                // 3) Dispatch on Content-Type: 'application/opcua+uajson' is the
                //    HTTPS-JSON sub-protocol (Part 6 §7.4.5); anything else is
                //    treated as binary by SendBinaryAsync (which itself enforces
                //    the 'application/octet-stream' contract per Part 6 §7.4.4).
                string? contentType = context.Request.ContentType;
                if (!string.IsNullOrEmpty(contentType) &&
                    contentType!.StartsWith(
                        Profiles.OpcUaJsonContentType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return listener.SendJsonAsync(context);
                }

                return listener.SendBinaryAsync(context);
            });
        }
    }

    /// <summary>
    /// Manages the connections for a UA HTTPS server.
    /// </summary>
    public class HttpsTransportListener : ITransportListener, ITransportListenerCertificateRotation
    {
        private const string kHttpsContentType = "text/plain";
        private const string kApplicationContentType = "application/octet-stream";
        private const string kAuthorizationKey = "Authorization";
        private const string kBearerKey = "Bearer";

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpsTransportListener"/> class.
        /// </summary>
        public HttpsTransportListener(string uriScheme, ITelemetryContext telemetry)
        {
            UriScheme = uriScheme;
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<HttpsTransportListener>();
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
                ConnectionStatusChanged = null;
                ConnectionWaiting = null;
                m_host?.Dispose();
                m_host = null;
                m_pinnedServerCertX509?.Dispose();
                m_pinnedServerCertX509 = null;
                m_pinnedServerCert?.Dispose();
                m_pinnedServerCert = null;
            }
        }

        /// <inheritdoc/>
        public string UriScheme { get; }

        /// <inheritdoc/>
        public string ListenerId { get; private set; } = default!;

        internal byte[] ServerChannelCertificate { get; set; } = [];

        /// <summary>
        /// Opens the listener and starts accepting connection.
        /// </summary>
        /// <param name="baseAddress">The base address.</param>
        /// <param name="settings">The settings to use when creating the listener.</param>
        /// <param name="callback">The callback to use when requests arrive via the channel.</param>
        /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Open(
            Uri baseAddress,
            TransportListenerSettings settings,
            ITransportListenerCallback callback)
        {
            // assign a unique guid to the listener.
            ListenerId = Guid.NewGuid().ToString();

            EndpointUrl = baseAddress;
            m_descriptions = settings.Descriptions!;
            EndpointConfiguration configuration = settings.Configuration!;

            // initialize the quotas.
            m_quotas = new ChannelQuotas(new ServiceMessageContext(m_telemetry, settings.Factory!)
            {
                MaxArrayLength = configuration.MaxArrayLength,
                MaxByteStringLength = configuration.MaxByteStringLength,
                MaxMessageSize = configuration.MaxMessageSize,
                MaxStringLength = configuration.MaxStringLength,
                MaxEncodingNestingLevels = configuration.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = configuration.MaxDecoderRecoveries,
                NamespaceUris = settings.NamespaceUris!,
                ServerUris = new StringTable()
            })
            {
                MaxBufferSize = configuration.MaxBufferSize,
                MaxMessageSize = configuration.MaxMessageSize,
                ChannelLifetime = configuration.ChannelLifetime,
                SecurityTokenLifetime = configuration.SecurityTokenLifetime,
                CertificateValidator = settings.CertificateValidator
            };

            // save the callback to the server.
            m_callback = callback!;

            m_serverCertProvider = settings.ServerCertificates!;

            m_mutualTlsEnabled = settings.HttpsMutualTls;

            // buffer manager used by the WSS path to rent send / receive chunks.
            m_bufferManager = new BufferManager(
                "HttpsListener",
                m_quotas.MaxBufferSize,
                m_telemetry);

            // start the listener
            Start();
        }

        /// <summary>
        /// Closes the listener and stops accepting connection.
        /// </summary>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Close()
        {
            Stop();
        }

#pragma warning disable CS0414
        /// <summary>
        /// Raised when a new connection is waiting for a client.
        /// </summary>
        public event ConnectionWaitingHandlerAsync? ConnectionWaiting;

        /// <summary>
        /// Raised when a monitored connection's status changed.
        /// </summary>
        public event EventHandler<ConnectionStatusEventArgs>? ConnectionStatusChanged;
#pragma warning restore CS0414

        /// <inheritdoc/>
        /// <remarks>
        /// Reverse connect for the https transport listener is not implemented.
        /// </remarks>
        public void CreateReverseConnection(Uri url, int timeout)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public void UpdateChannelLastActiveTime(string globalChannelId)
        {
            // intentionally not implemented
        }

        /// <summary>
        /// Gets the URL for the listener's endpoint.
        /// </summary>
        /// <value>The URL for the listener's endpoint.</value>
        public Uri EndpointUrl { get; private set; } = null!;

        /// <summary>
        /// Starts listening at the specified port.
        /// </summary>
        public void Start()
        {
#if NET8_0_OR_GREATER
            m_host = new HostBuilder()
                .ConfigureWebHostDefaults(ConfigureWebHost)
                .Build();
            m_host.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
#else
            var hostBuilder = new WebHostBuilder();
            ConfigureWebHost(hostBuilder);
            m_host = hostBuilder.Start(Utils.ReplaceLocalhost(EndpointUrl.ToString()));
#endif
        }

        /// <summary>
        /// CA1859: The IWebHostBuilder interface cannot be narrowed to WebHostBuilder here because
        /// on NET8_0_OR_GREATER this method is called from HostBuilder.ConfigureWebHostDefaults()
        /// which passes an IWebHostBuilder, not WebHostBuilder.
        /// </summary>
        /// <param name="webHostBuilder"></param>
#pragma warning disable CA1859
        private void ConfigureWebHost(IWebHostBuilder webHostBuilder)
#pragma warning restore CA1859
        {
            // prepare the server TLS certificate. The provider returns a
            // borrowed reference owned by the registry; AddRef so this
            // listener owns the cert independent of the registry's lifetime
            // (the registry may dispose its snapshot during cert hot-update,
            // which would otherwise free the OS handle Kestrel still holds).
            Certificate? serverCertificate = m_serverCertProvider.GetInstanceCertificate(
                SecurityPolicies.Https)?.Certificate?.AddRef();
#if NETSTANDARD2_1 || NET472_OR_GREATER || NET5_0_OR_GREATER
            try
            {
                // Create a copy of the certificate with the private key on platforms
                // which default to the ephemeral KeySet. Also a new certificate must be reloaded.
                // If the key fails to copy, its probably a non exportable key from the X509Store.
                // Then we can use the original certificate, the private key is already in the key store.
                using Certificate copy = X509Utils.CreateCopyWithPrivateKey(serverCertificate!, false);
                if (!ReferenceEquals(copy, serverCertificate))
                {
                    serverCertificate!.Dispose();
                    serverCertificate = copy;
                    serverCertificate.AddRef();
                }
            }
            catch (CryptographicException ce)
            {
                m_logger.LogTrace("Copy of the private key for https was denied: {Message}", ce.Message);
            }
#endif
            // pin the cert for the lifetime of the listener so that the
            // OS-level private key handle backing the Kestrel-held
            // X509Certificate2 cannot be invalidated by a concurrent cert
            // reload elsewhere in the process.
            m_pinnedServerCert?.Dispose();
            m_pinnedServerCert = serverCertificate;
            m_pinnedServerCertX509?.Dispose();
            m_pinnedServerCertX509 = serverCertificate!.AsX509Certificate2();

            // save the server certificate so it can be used in the secure channel context.
            ServerChannelCertificate = serverCertificate!.RawData;

            var httpsOptions = new HttpsConnectionAdapterOptions
            {
                CheckCertificateRevocation = false,
                ClientCertificateMode = m_mutualTlsEnabled
                    ? ClientCertificateMode.AllowCertificate
                    : ClientCertificateMode.NoCertificate,
                // note: this is the TLS certificate!
                ServerCertificate = m_pinnedServerCertX509,
                ClientCertificateValidation = ValidateClientCertificate,
                SslProtocols = SslProtocols.None
            };

            UriHostNameType hostType = Uri.CheckHostName(EndpointUrl.Host);
            if (hostType is UriHostNameType.Dns or UriHostNameType.Unknown or UriHostNameType.Basic)
            {
                // bind to any address
                webHostBuilder.UseKestrel(options =>
                    options.ListenAnyIP(
                        EndpointUrl.Port,
                        listenOptions => listenOptions.UseHttps(httpsOptions)));
            }
            else
            {
                // bind to specific address
                var ipAddress = IPAddress.Parse(EndpointUrl.Host);
                webHostBuilder.UseKestrel(options =>
                    options.Listen(
                        ipAddress,
                        EndpointUrl.Port,
                        listenOptions => listenOptions.UseHttps(httpsOptions)));
            }

            webHostBuilder.UseContentRoot(Directory.GetCurrentDirectory());
            // Register this listener instance in the web host DI container so it can be
            // injected into Startup.Configure as a method parameter — no static state needed.
            webHostBuilder.ConfigureServices(services => services.AddSingleton(this));
            webHostBuilder.UseStartup<Startup>();
        }

        /// <summary>
        /// Stops listening.
        /// </summary>
        public void Stop()
        {
            Dispose();
        }

        /// <summary>
        /// Handles HTTPS POST requests carrying an OPC UA binary message
        /// (Part 6 §7.4.4 — <c>application/octet-stream</c>). Public for
        /// backwards compatibility with the previous <c>SendAsync</c> name;
        /// <see cref="SendAsync"/> remains as a thin synonym.
        /// </summary>
        public async Task SendBinaryAsync(HttpContext context)
        {
            string message = string.Empty;
            CancellationToken ct = context.RequestAborted;
            try
            {
                if (m_callback == null)
                {
                    await WriteResponseAsync(
                        context.Response,
                        message,
                        HttpStatusCode.NotImplemented)
                        .ConfigureAwait(false);
                    return;
                }

                if (context.Request.ContentType != kApplicationContentType)
                {
                    message = "HTTPSLISTENER - Unsupported content type.";
                    await WriteResponseAsync(context.Response, message, HttpStatusCode.BadRequest)
                        .ConfigureAwait(false);
                    return;
                }

                int length = (int)(context.Request.ContentLength ?? 0);
                byte[] buffer = await ReadBodyAsync(context.Request).ConfigureAwait(false);

                if (buffer.Length != length)
                {
                    message = "HTTPSLISTENER - Invalid buffer.";
                    await WriteResponseAsync(context.Response, message, HttpStatusCode.BadRequest)
                        .ConfigureAwait(false);
                    return;
                }

                IServiceRequest input = BinaryDecoder.DecodeMessage<IServiceRequest>(
                    buffer,
                    m_quotas.MessageContext);

                if (m_mutualTlsEnabled && input.TypeId == DataTypeIds.CreateSessionRequest)
                {
                    // Match tls client certificate against client application certificate provided in CreateSessionRequest
                    var tlsClientCertificate = ByteString.From(context.Connection.ClientCertificate?.RawData);
                    ByteString opcUaClientCertificate = ((CreateSessionRequest)input).ClientCertificate;

                    if (context.Connection.ClientCertificate?.RawData == null ||
                        tlsClientCertificate != opcUaClientCertificate)
                    {
                        message =
                            "Client TLS certificate does not match with ClientCertificate provided in CreateSessionRequest";
                        m_logger.LogError("{Message}", message);
                        await WriteResponseAsync(
                            context.Response,
                            message,
                            HttpStatusCode.Unauthorized)
                            .ConfigureAwait(false);
                        return;
                    }
                }

                // extract the JWT token from the HTTP headers.
                input.RequestHeader ??= new RequestHeader();

                if (input.RequestHeader.AuthenticationToken.IsNull &&
                    input.TypeId != DataTypeIds.CreateSessionRequest &&
                    context.Request.Headers.TryGetValue(
                        kAuthorizationKey,
                        out Microsoft.Extensions.Primitives.StringValues keys))
                {
                    foreach (string? value in keys)
                    {
                        if (value != null && value.StartsWith(kBearerKey, StringComparison.OrdinalIgnoreCase))
                        {
                            // note: use NodeId(string, uint) to avoid the NodeId.Parse call.
                            input.RequestHeader.AuthenticationToken = new NodeId(
                                value[(kBearerKey.Length + 1)..].Trim(),
                                0);
                        }
                    }
                }

                if (!context.Request.Headers.TryGetValue(
                        Profiles.HttpsSecurityPolicyHeader,
                        out Microsoft.Extensions.Primitives.StringValues header))
                {
                    header = SecurityPolicies.None;
                }

                EndpointDescription? endpoint = null;
                foreach (EndpointDescription ep in m_descriptions)
                {
                    if (Utils.IsUriHttpsScheme(ep.EndpointUrl!))
                    {
                        if (!string.IsNullOrEmpty(header) &&
                            !string.Equals(ep.SecurityPolicyUri, header, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        endpoint = ep;
                        break;
                    }
                }

                if (endpoint == null)
                {
                    ServiceResultException? serviceResultException = null;
                    if (input.TypeId != DataTypeIds.GetEndpointsRequest &&
                        input.TypeId != DataTypeIds.FindServersRequest &&
                        input.TypeId != DataTypeIds.FindServersOnNetworkRequest)
                    {
                        serviceResultException = new ServiceResultException(
                            StatusCodes.BadSecurityPolicyRejected,
                            "Channel can only be used for discovery.");
                    }
                    else if (length > TcpMessageLimits.DefaultDiscoveryMaxMessageSize)
                    {
                        serviceResultException = new ServiceResultException(
                            StatusCodes.BadSecurityPolicyRejected,
                            "Discovery Channel message size exceeded.");
                    }

                    if (serviceResultException != null)
                    {
                        IServiceResponse serviceResponse = EndpointBase.CreateFault(
                            m_logger,
                            null,
                            serviceResultException);
                        await WriteServiceResponseAsync(context, serviceResponse, ct)
                            .ConfigureAwait(false);
                        return;
                    }
                }

                var secureChannelContext = new SecureChannelContext(
                    ListenerId,
                    endpoint,
                    RequestEncoding.Binary,
                    context.Connection.ClientCertificate?.RawData,
                    ServerChannelCertificate);

                IServiceResponse output =
                    await m_callback.ProcessRequestAsync(
                        secureChannelContext,
                        input,
                        ct).ConfigureAwait(false);

                if (!ct.IsCancellationRequested)
                {
                    await WriteServiceResponseAsync(context, output, ct).ConfigureAwait(false);
                }

                return;
            }
            catch (Exception e)
            {
                message = "HTTPSLISTENER - Unexpected error processing request.";
                m_logger.LogError(e, "{Message}", message);
            }

            await WriteResponseAsync(context.Response, message, HttpStatusCode.InternalServerError)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Backward-compatible alias for <see cref="SendBinaryAsync"/>.
        /// </summary>
        public Task SendAsync(HttpContext context) => SendBinaryAsync(context);

        /// <summary>
        /// Handles HTTPS POST requests carrying an OPC UA JSON message
        /// (Part 6 §7.4.5 — <c>application/opcua+uajson</c>). JSON over
        /// HTTPS does not use the UA SecureChannel layer, so only the
        /// <see cref="MessageSecurityMode.None"/> policy is accepted;
        /// transport security is provided by TLS at the HTTPS layer.
        /// </summary>
        public async Task SendJsonAsync(HttpContext context)
        {
            string message = string.Empty;
            CancellationToken ct = context.RequestAborted;
            try
            {
                if (m_callback == null)
                {
                    await WriteResponseAsync(
                        context.Response,
                        message,
                        HttpStatusCode.NotImplemented).ConfigureAwait(false);
                    return;
                }

                // Optional JWT extraction (same as the binary path).
                NodeId authenticationToken = NodeId.Null;
                if (context.Request.Headers.TryGetValue(
                        kAuthorizationKey,
                        out Microsoft.Extensions.Primitives.StringValues keys))
                {
                    foreach (string? value in keys)
                    {
                        if (value != null &&
                            value.StartsWith(kBearerKey, StringComparison.OrdinalIgnoreCase))
                        {
                            authenticationToken = new NodeId(
                                value[(kBearerKey.Length + 1)..].Trim(),
                                0);
                        }
                    }
                }

                IServiceRequest input;
                try
                {
                    input = await JsonRequestMapper
                        .DecodeRequestAsync(context.Request.Body, m_quotas.MessageContext, ct)
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    IServiceResponse fault = EndpointBase.CreateFault(
                        m_logger,
                        null,
                        sre);
                    await WriteJsonResponseAsync(context, fault, ct).ConfigureAwait(false);
                    return;
                }

                input.RequestHeader ??= new RequestHeader();
                if (input.RequestHeader.AuthenticationToken.IsNull && !authenticationToken.IsNull &&
                    input.TypeId != DataTypeIds.CreateSessionRequest)
                {
                    input.RequestHeader.AuthenticationToken = authenticationToken;
                }

                // The JSON sub-protocol is restricted to SecurityPolicy = None
                // (Part 6 §7.4.5); pick the matching endpoint up front.
                EndpointDescription? endpoint = null;
                foreach (EndpointDescription ep in m_descriptions)
                {
                    if (ep.TransportProfileUri == Profiles.HttpsJsonTransport &&
                        ep.SecurityMode == MessageSecurityMode.None)
                    {
                        endpoint = ep;
                        break;
                    }
                }

                var secureChannelContext = new SecureChannelContext(
                    ListenerId,
                    endpoint,
                    RequestEncoding.Json,
                    context.Connection.ClientCertificate?.RawData,
                    ServerChannelCertificate);

                IServiceResponse output = await m_callback
                    .ProcessRequestAsync(secureChannelContext, input, ct)
                    .ConfigureAwait(false);

                if (!ct.IsCancellationRequested)
                {
                    await WriteJsonResponseAsync(context, output, ct).ConfigureAwait(false);
                }
                return;
            }
            catch (Exception e)
            {
                message = "HTTPSLISTENER - Unexpected error processing JSON request.";
                m_logger.LogError(e, "{Message}", message);
            }

            await WriteResponseAsync(context.Response, message, HttpStatusCode.InternalServerError)
                .ConfigureAwait(false);
        }

        private async Task WriteJsonResponseAsync(
            HttpContext context,
            IServiceResponse response,
            CancellationToken ct)
        {
            byte[] payload = JsonRequestMapper.EncodeResponse(response, m_quotas.MessageContext);
            context.Response.ContentLength = payload.Length;
            context.Response.ContentType = Profiles.OpcUaJsonContentType;
            context.Response.StatusCode = (int)HttpStatusCode.OK;
#if NETSTANDARD2_1 || NET5_0_OR_GREATER
            await context.Response.Body
                .WriteAsync(payload.AsMemory(0, payload.Length), ct)
                .ConfigureAwait(false);
#else
            await context.Response.Body
                .WriteAsync(payload, 0, payload.Length, ct)
                .ConfigureAwait(false);
#endif
        }

        /// <summary>
        /// Handles WebSocket upgrade requests for the WSS transport
        /// (Part 6 §7.5). Negotiates the <c>opcua+uacp</c> sub-protocol
        /// (binary + UASC SecureChannel); the <c>opcua+uajson</c>
        /// sub-protocol returns 501 until <c>p3-wss-json-handler</c>.
        /// </summary>
        public async Task AcceptWebSocketAsync(HttpContext context)
        {
            if (m_callback == null)
            {
                await WriteResponseAsync(
                    context.Response,
                    "HTTPSLISTENER - listener is not open.",
                    HttpStatusCode.NotImplemented).ConfigureAwait(false);
                return;
            }

            string? selected = SelectWebSocketSubProtocol(context);
            if (selected == null)
            {
                await WriteResponseAsync(
                    context.Response,
                    "HTTPSLISTENER - no supported WebSocket sub-protocol requested. " +
                    "Use 'opcua+uacp' or 'opcua+uajson'.",
                    HttpStatusCode.BadRequest).ConfigureAwait(false);
                return;
            }
            if (string.Equals(selected, Profiles.OpcUaWsSubProtocolUaJson, StringComparison.Ordinal))
            {
                await AcceptWebSocketJsonAsync(context).ConfigureAwait(false);
                return;
            }

            // selected == opcua+uacp
            WebSocket ws = await context.WebSockets
                .AcceptWebSocketAsync(selected)
                .ConfigureAwait(false);

            EndPoint? localEndpoint = MakeEndpoint(
                context.Connection.LocalIpAddress,
                context.Connection.LocalPort);
            EndPoint? remoteEndpoint = MakeEndpoint(
                context.Connection.RemoteIpAddress,
                context.Connection.RemotePort);

#pragma warning disable CA2000 // transport disposed in the finally block below
            var transport = new WebSocketServerByteTransport(
                ws,
                localEndpoint,
                remoteEndpoint,
                m_bufferManager,
                m_quotas.MaxBufferSize,
                m_telemetry);
#pragma warning restore CA2000

            var perWsListener = new WssChannelListener(this);
            uint channelId = (uint)Interlocked.Increment(ref m_nextChannelId);

            var channel = new TcpServerChannel(
                ListenerId,
                perWsListener,
                m_bufferManager,
                m_quotas,
                m_serverCertProvider,
                m_descriptions,
                m_telemetry);

            channel.SetRequestReceivedCallback(
                new TcpChannelRequestEventHandler(OnRequestReceivedAsync));
            channel.SetReportOpenSecureChannelAuditCallback(
                new ReportAuditOpenSecureChannelEventHandler(OnReportAuditOpenSecureChannelEvent));
            channel.SetReportCloseSecureChannelAuditCallback(
                new ReportAuditCloseSecureChannelEventHandler(OnReportAuditCloseSecureChannelEvent));
            channel.SetReportCertificateAuditCallback(
                new ReportAuditCertificateEventHandler(OnReportAuditCertificateEvent));

            perWsListener.AttachChannel(channelId, channel);

            try
            {
                channel.Attach(channelId, transport);

                // Hold the request open until the channel is torn down (the
                // listener closes the channel when the WebSocket sees Close or
                // a fatal UASC error). Honor request abort to allow shutdown.
                await perWsListener
                    .WaitForChannelClosedAsync(context.RequestAborted)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Connection torn down by the request abort token; normal shutdown.
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "WSSLISTENER - unexpected error on WebSocket session.");
            }
            finally
            {
                try
                {
                    channel.Dispose();
                }
                catch
                {
                    // Dispose is best-effort.
                }
                try
                {
                    transport.Close();
                }
                catch
                {
                    // Close is best-effort.
                }
            }
        }

        private async Task<IServiceResponse> OnRequestReceivedAsyncShim(
            SecureChannelContext channelContext,
            IServiceRequest request)
        {
            return await m_callback!.ProcessRequestAsync(channelContext, request).ConfigureAwait(false);
        }

        private async void OnRequestReceivedAsync(
            TcpListenerChannel channel,
            uint requestId,
            IServiceRequest request)
        {
            try
            {
                if (m_callback == null)
                {
                    return;
                }
                var serverChannel = channel as TcpServerChannel;
                if (serverChannel == null)
                {
                    return;
                }

                var context = new SecureChannelContext(
                    channel.GlobalChannelId,
                    channel.EndpointDescription,
                    RequestEncoding.Binary,
                    channel.ClientCertificate?.RawData,
                    channel.ServerCertificate?.RawData,
                    channel.ChannelThumbprint);

                IServiceResponse response = await m_callback
                    .ProcessRequestAsync(context, request)
                    .ConfigureAwait(false);

                serverChannel.SendResponse(requestId, response);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "WSSLISTENER - error processing request {RequestId}.", requestId);
            }
        }

        private void OnReportAuditOpenSecureChannelEvent(
            TcpServerChannel channel,
            OpenSecureChannelRequest request,
            Certificate? clientCertificate,
            Exception? exception)
        {
            if (m_callback == null)
            {
                return;
            }
            m_callback.ReportAuditOpenSecureChannelEvent(
                channel.GlobalChannelId,
                channel.EndpointDescription!,
                request,
                clientCertificate!,
                exception!);
        }

        private void OnReportAuditCloseSecureChannelEvent(
            TcpServerChannel channel,
            Exception? exception)
        {
            m_callback?.ReportAuditCloseSecureChannelEvent(
                channel.GlobalChannelId,
                exception!);
        }

        private void OnReportAuditCertificateEvent(
            Certificate? clientCertificate,
            Exception? exception)
        {
            m_callback?.ReportAuditCertificateEvent(clientCertificate!, exception!);
        }

        private static IPEndPoint? MakeEndpoint(IPAddress? address, int port)
        {
            return address == null ? null : new IPEndPoint(address, port);
        }

        private static string? SelectWebSocketSubProtocol(HttpContext context)
        {
            foreach (string requested in context.WebSockets.WebSocketRequestedProtocols)
            {
                if (string.Equals(requested, Profiles.OpcUaWsSubProtocolUacp, StringComparison.Ordinal))
                {
                    return Profiles.OpcUaWsSubProtocolUacp;
                }
                if (string.Equals(requested, Profiles.OpcUaWsSubProtocolUaJson, StringComparison.Ordinal))
                {
                    return Profiles.OpcUaWsSubProtocolUaJson;
                }
            }
            return null;
        }

        /// <summary>
        /// Handles a WebSocket upgrade for the <c>opcua+uajson</c>
        /// sub-protocol (Part 6 §7.5.2). Each WS message carries one
        /// JSON-encoded OPC UA request; the JSON sub-protocol does not
        /// use the UA Secure Conversation layer so only Security Mode
        /// None is supported (transport security is TLS).
        /// </summary>
        private async Task AcceptWebSocketJsonAsync(HttpContext context)
        {
            WebSocket ws = await context.WebSockets
                .AcceptWebSocketAsync(Profiles.OpcUaWsSubProtocolUaJson)
                .ConfigureAwait(false);

            CancellationToken ct = context.RequestAborted;

            // Pick the JSON endpoint description for the active scheme.
            EndpointDescription? endpoint = null;
            foreach (EndpointDescription ep in m_descriptions)
            {
                if (ep.TransportProfileUri == Profiles.UaWssJsonTransport &&
                    ep.SecurityMode == MessageSecurityMode.None)
                {
                    endpoint = ep;
                    break;
                }
            }

            var channelContext = new SecureChannelContext(
                ListenerId,
                endpoint,
                RequestEncoding.Json,
                context.Connection.ClientCertificate?.RawData,
                ServerChannelCertificate);

            byte[]? receiveBuffer = null;
            try
            {
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    receiveBuffer ??= m_bufferManager.TakeBuffer(
                        m_quotas.MaxBufferSize,
                        nameof(AcceptWebSocketJsonAsync));

                    int totalRead = 0;
                    bool completed;
                    do
                    {
                        WebSocketReceiveResult result = await ws
                            .ReceiveAsync(
                                new ArraySegment<byte>(
                                    receiveBuffer,
                                    totalRead,
                                    receiveBuffer.Length - totalRead),
                                ct)
                            .ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await ws.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Client requested close.",
                                ct).ConfigureAwait(false);
                            return;
                        }
                        totalRead += result.Count;
                        if (totalRead > m_quotas.MaxBufferSize)
                        {
                            await ws.CloseAsync(
                                WebSocketCloseStatus.MessageTooBig,
                                "Message exceeded configured limit.",
                                ct).ConfigureAwait(false);
                            return;
                        }
                        completed = result.EndOfMessage;
                    }
                    while (!completed);

                    IServiceResponse responseToSend;
                    try
                    {
                        byte[] messageBytes = new byte[totalRead];
                        Buffer.BlockCopy(receiveBuffer, 0, messageBytes, 0, totalRead);
                        IServiceRequest request = JsonDecoder.DecodeMessage<IServiceRequest>(
                            messageBytes,
                            m_quotas.MessageContext);
                        request.RequestHeader ??= new RequestHeader();

                        responseToSend = await m_callback!
                            .ProcessRequestAsync(channelContext, request, ct)
                            .ConfigureAwait(false);
                    }
                    catch (ServiceResultException sre)
                    {
                        responseToSend = EndpointBase.CreateFault(m_logger, null, sre);
                    }
                    catch (Exception ex)
                    {
                        m_logger.LogError(ex, "WSSLISTENER - error processing JSON request.");
                        responseToSend = EndpointBase.CreateFault(m_logger, null, ex);
                    }

                    byte[] responseBytes = JsonRequestMapper.EncodeResponse(
                        responseToSend,
                        m_quotas.MessageContext);
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                    await ws.SendAsync(
                        new ReadOnlyMemory<byte>(responseBytes, 0, responseBytes.Length),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
#else
                    await ws.SendAsync(
                        new ArraySegment<byte>(responseBytes, 0, responseBytes.Length),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        ct).ConfigureAwait(false);
#endif
                }
            }
            catch (OperationCanceledException)
            {
                // Request aborted; normal shutdown.
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "WSSLISTENER - unexpected JSON WebSocket error.");
            }
            finally
            {
                if (receiveBuffer != null)
                {
                    m_bufferManager.ReturnBuffer(receiveBuffer, nameof(AcceptWebSocketJsonAsync));
                }
                try
                {
                    if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                    {
                        await ws.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            string.Empty,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Best-effort.
                }
                ws.Dispose();
            }
        }

        /// <summary>
        /// Per-WebSocket adapter that lets a single <see cref="TcpServerChannel"/>
        /// run inside the HTTPS listener without needing a full
        /// <see cref="ITcpChannelListener"/>-shaped lifecycle. There is one
        /// <see cref="WssChannelListener"/> per accepted WebSocket; it tracks
        /// exactly one channel and signals the request handler when the
        /// channel closes so the request pipeline can clean up.
        /// </summary>
        private sealed class WssChannelListener : ITcpChannelListener
        {
            internal WssChannelListener(HttpsTransportListener owner)
            {
                m_owner = owner;
                m_closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Uri EndpointUrl => m_owner.EndpointUrl;

            internal void AttachChannel(uint channelId, TcpServerChannel channel)
            {
                m_channelId = channelId;
                m_channel = channel;
            }

            public bool ReconnectToExistingChannel(
                IUaSCByteTransport transport,
                uint requestId,
                uint sequenceNumber,
                uint channelId,
                Certificate clientCertificate,
                ChannelToken token,
                OpenSecureChannelRequest request)
            {
                // Each WSS upgrade owns exactly one channel; reconnect over a
                // different WebSocket would require a different listener.
                throw new ServiceResultException(
                    StatusCodes.BadTcpSecureChannelUnknown,
                    "Reconnect is not supported on the WSS transport.");
            }

            [Obsolete("Use TransferListenerChannelAsync instead.")]
            public Task<bool> TransferListenerChannel(uint channelId, string serverUri, Uri endpointUrl)
            {
                return Task.FromResult(false);
            }

            public Task<bool> TransferListenerChannelAsync(uint channelId, string serverUri, Uri endpointUrl)
            {
                // Reverse-connect handoff is not applicable to a single WSS upgrade.
                return Task.FromResult(false);
            }

            public void ChannelClosed(uint channelId)
            {
                if (channelId == m_channelId)
                {
                    m_closed.TrySetResult(true);
                }
            }

            internal async Task WaitForChannelClosedAsync(CancellationToken ct)
            {
                using (ct.Register(static s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), m_closed))
                {
                    await m_closed.Task.ConfigureAwait(false);
                }
            }

            private readonly HttpsTransportListener m_owner;
            private readonly TaskCompletionSource<bool> m_closed;
            private uint m_channelId;
            private TcpServerChannel? m_channel;
        }

        /// <summary>
        /// Called when an UpdateCertificate event occurred. Performs the
        /// soft fan-out (refresh validator, registry, and per-endpoint
        /// blobs); see <see cref="CloseChannelsForCertificate"/> for the
        /// post-ApplyChanges connection teardown that actually rebinds
        /// the Kestrel TLS certificate.
        /// </summary>
        public void CertificateUpdate(
            ICertificateValidatorEx validator,
            ICertificateRegistry serverCertificates)
        {
            m_quotas.CertificateValidator = validator;
            m_serverCertProvider = serverCertificates;

            foreach (EndpointDescription description in m_descriptions)
            {
                ServerBase.SetServerCertificateInEndpointDescription(
                    description,
                    serverCertificates,
                    false);
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<string> CloseChannelsForCertificate(Certificate oldCertificate)
        {
            if (oldCertificate == null)
            {
                throw new ArgumentNullException(nameof(oldCertificate));
            }

            // Nothing to do if the listener was never opened. This keeps
            // the call safe to invoke unconditionally from the
            // ConfigurationNodeManager fan-out.
            if (m_descriptions == null)
            {
                return [];
            }

            // HTTPS owns the server certificate at the Kestrel / HTTP.sys
            // listener level, so we can't surgically close individual TLS
            // connections without restarting the host. Per OPC UA Part 12
            // §7.10.9 a Stop()/Start() cycle satisfies the "force
            // renegotiate" requirement; existing Sessions remain valid
            // and the client's reconnect logic re-binds them over the
            // freshly-issued TLS endpoint.
            Stop();
            Start();

            // The HTTPS listener does not track per-channel ids that map
            // to OPC UA SecureChannels (the binding is request-scoped via
            // HTTP), so there is nothing meaningful to return for
            // diagnostics here.
            return [];
        }

        /// <summary>
        /// Encodes a service response and writes it back.
        /// </summary>
        private async Task WriteServiceResponseAsync(
            HttpContext context,
            IServiceResponse response,
            CancellationToken ct)
        {
            byte[] encodedResponse = BinaryEncoder.EncodeMessage(response, m_quotas.MessageContext);
            context.Response.ContentLength = encodedResponse.Length;
            context.Response.ContentType = context.Request.ContentType;
            context.Response.StatusCode = (int)HttpStatusCode.OK;
#if NETSTANDARD2_1 || NET5_0_OR_GREATER
            await context
                .Response.Body.WriteAsync(encodedResponse.AsMemory(0, encodedResponse.Length), ct)
                .ConfigureAwait(false);
#else
            await context
                .Response.Body.WriteAsync(encodedResponse, 0, encodedResponse.Length, ct)
                .ConfigureAwait(false);
#endif
        }

        private static Task WriteResponseAsync(
            HttpResponse response,
            string message,
            HttpStatusCode status)
        {
            response.ContentLength = message.Length;
            response.ContentType = kHttpsContentType;
            response.StatusCode = (int)status;
            return response.WriteAsync(message);
        }

        private static async Task<byte[]> ReadBodyAsync(HttpRequest req)
        {
            using var memory = new MemoryStream();
            using var reader = new StreamReader(req.Body);
            await reader.BaseStream.CopyToAsync(memory).ConfigureAwait(false);
            return memory.ToArray();
        }

        /// <summary>
        /// Validate TLS client certificate at TLS handshake.
        /// </summary>
        /// <param name="clientCertificate">Client certificate</param>
        /// <param name="chain">Certificate chain</param>
        /// <param name="sslPolicyErrors">SSl policy errors</param>
        private bool ValidateClientCertificate(
            X509Certificate2? clientCertificate,
            X509Chain? chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                // certificate is valid
                return true;
            }

            try
            {
                using var cert = Certificate.FromRawData(clientCertificate!.RawData);
#pragma warning disable CA2025
                CertificateValidationResult result = m_quotas.CertificateValidator!
                    .ValidateAsync(cert, ct: default)
                    .GetAwaiter()
                    .GetResult();
#pragma warning restore CA2025
                if (!result.IsValid)
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        private List<EndpointDescription> m_descriptions = null!;
        private ChannelQuotas m_quotas = null!;
        private BufferManager m_bufferManager = null!;
        private ITransportListenerCallback? m_callback;
#if NET8_0_OR_GREATER
        private IHost? m_host;
#else
        private IWebHost? m_host;
#endif
        private ICertificateRegistry m_serverCertProvider = null!;
        private Certificate? m_pinnedServerCert;
        private X509Certificate2? m_pinnedServerCertX509;
        private bool m_mutualTlsEnabled;
        private int m_nextChannelId;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
    }
}
