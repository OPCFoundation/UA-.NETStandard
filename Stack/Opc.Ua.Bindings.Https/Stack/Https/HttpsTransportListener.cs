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
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Hosting;
using Opc.Ua.Security.Certificates;
using Microsoft.Extensions.Logging;


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
    /// Implements the kestrel startup of the Https listener.
    /// </summary>
    public class Startup
    {
        private const string kHttpsContentType = "text/plain";

        /// <summary>
        /// Get the Https listener.
        /// </summary>
        public static HttpsTransportListener Listener { get; set; }

        /// <summary>
        /// Configure the request pipeline for the listener.
        /// </summary>
        /// <param name="appBuilder">The application builder.</param>
        public void Configure(IApplicationBuilder appBuilder)
        {
            appBuilder.Run(context =>
            {
                if (context.Request.Method != "POST")
                {
                    context.Response.ContentLength = 0;
                    context.Response.ContentType = kHttpsContentType;
                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    return context.Response.WriteAsync(string.Empty);
                }

                return Listener.SendAsync(context);
            });
        }
    }

    /// <summary>
    /// Manages the connections for a UA HTTPS server.
    /// </summary>
    public class HttpsTransportListener : ITransportListener
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
            }
        }

        /// <inheritdoc/>
        public string UriScheme { get; }

        /// <inheritdoc/>
        public string ListenerId { get; private set; }

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
            m_descriptions = settings.Descriptions;
            EndpointConfiguration configuration = settings.Configuration;

            // initialize the quotas.
            m_quotas = new ChannelQuotas(new ServiceMessageContext(m_telemetry)
            {
                MaxArrayLength = configuration.MaxArrayLength,
                MaxByteStringLength = configuration.MaxByteStringLength,
                MaxMessageSize = configuration.MaxMessageSize,
                MaxStringLength = configuration.MaxStringLength,
                MaxEncodingNestingLevels = configuration.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = configuration.MaxDecoderRecoveries,
                NamespaceUris = settings.NamespaceUris,
                ServerUris = new StringTable(),
                Factory = settings.Factory
            })
            {
                MaxBufferSize = configuration.MaxBufferSize,
                MaxMessageSize = configuration.MaxMessageSize,
                ChannelLifetime = configuration.ChannelLifetime,
                SecurityTokenLifetime = configuration.SecurityTokenLifetime,
                CertificateValidator = settings.CertificateValidator
            };

            // save the callback to the server.
            m_callback = callback;

            m_serverCertProvider = settings.ServerCertificateTypesProvider;

            m_mutualTlsEnabled = settings.HttpsMutualTls;
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
        public event ConnectionWaitingHandlerAsync ConnectionWaiting;

        /// <summary>
        /// Raised when a monitored connection's status changed.
        /// </summary>
        public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;
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
        public Uri EndpointUrl { get; private set; }

        /// <summary>
        /// Starts listening at the specified port.
        /// </summary>
        public void Start()
        {
            Startup.Listener = this;
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

        private void ConfigureWebHost(IWebHostBuilder webHostBuilder)
        {
            // prepare the server TLS certificate
            X509Certificate2 serverCertificate = m_serverCertProvider.GetInstanceCertificate(
                SecurityPolicies.Https);
#if NETSTANDARD2_1 || NET472_OR_GREATER || NET5_0_OR_GREATER
            try
            {
                // Create a copy of the certificate with the private key on platforms
                // which default to the ephemeral KeySet. Also a new certificate must be reloaded.
                // If the key fails to copy, its probably a non exportable key from the X509Store.
                // Then we can use the original certificate, the private key is already in the key store.
                serverCertificate = X509Utils.CreateCopyWithPrivateKey(serverCertificate, false);
            }
            catch (CryptographicException ce)
            {
                m_logger.LogTrace("Copy of the private key for https was denied: {Message}", ce.Message);
            }
#endif

            var httpsOptions = new HttpsConnectionAdapterOptions
            {
                CheckCertificateRevocation = false,
                ClientCertificateMode = m_mutualTlsEnabled
                    ? ClientCertificateMode.AllowCertificate
                    : ClientCertificateMode.NoCertificate,
                // note: this is the TLS certificate!
                ServerCertificate = serverCertificate,
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
        /// Handles requests arriving from a channel.
        /// </summary>
        public async Task SendAsync(HttpContext context)
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

                int length = (int)context.Request.ContentLength;
                byte[] buffer = await ReadBodyAsync(context.Request).ConfigureAwait(false);

                if (buffer.Length != length)
                {
                    message = "HTTPSLISTENER - Invalid buffer.";
                    await WriteResponseAsync(context.Response, message, HttpStatusCode.BadRequest)
                        .ConfigureAwait(false);
                    return;
                }

                var input = (IServiceRequest)BinaryDecoder.DecodeMessage(
                    buffer,
                    null,
                    m_quotas.MessageContext);

                if (m_mutualTlsEnabled && input.TypeId == DataTypeIds.CreateSessionRequest)
                {
                    // Match tls client certificate against client application certificate provided in CreateSessionRequest
                    byte[] tlsClientCertificate = context.Connection.ClientCertificate?.RawData;
                    byte[] opcUaClientCertificate = ((CreateSessionRequest)input).ClientCertificate;

                    if (tlsClientCertificate == null ||
                        !Utils.IsEqual(tlsClientCertificate, opcUaClientCertificate))
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

                if (NodeId.IsNull(input.RequestHeader.AuthenticationToken) &&
                    input.TypeId != DataTypeIds.CreateSessionRequest &&
                    context.Request.Headers.TryGetValue(
                        kAuthorizationKey,
                        out Microsoft.Extensions.Primitives.StringValues keys))
                {
                    foreach (string value in keys)
                    {
                        if (value.StartsWith(kBearerKey, StringComparison.OrdinalIgnoreCase))
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

                EndpointDescription endpoint = null;
                foreach (EndpointDescription ep in m_descriptions)
                {
                    if (Utils.IsUriHttpsScheme(ep.EndpointUrl))
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
                    ServiceResultException serviceResultException = null;
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
                        RequestEncoding.Binary);

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
        /// Called when a UpdateCertificate event occured.
        /// </summary>
        public void CertificateUpdate(
            ICertificateValidator validator,
            CertificateTypesProvider serverCertificateTypes)
        {
            Stop();

            m_quotas.CertificateValidator = validator;
            m_serverCertProvider = serverCertificateTypes;

            foreach (EndpointDescription description in m_descriptions)
            {
                ServerBase.SetServerCertificateInEndpointDescription(
                    description,
                    serverCertificateTypes,
                    false);
            }

            Start();
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
            X509Certificate2 clientCertificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                // certificate is valid
                return true;
            }

            try
            {
                m_quotas.CertificateValidator.ValidateAsync(clientCertificate, default).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        private EndpointDescriptionCollection m_descriptions;
        private ChannelQuotas m_quotas;
        private ITransportListenerCallback m_callback;
#if NET8_0_OR_GREATER
        private IHost m_host;
#else
        private IWebHost m_host;
#endif
        private CertificateTypesProvider m_serverCertProvider;
        private bool m_mutualTlsEnabled;
        private readonly ILogger m_logger;
        private readonly ITelemetryContext m_telemetry;
    }
}
