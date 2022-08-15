/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
*/

using System;
using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Hosting;


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
        public override ITransportListener Create()
        {
            return new HttpsTransportListener();
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
            appBuilder.Run(context => {
                if (context.Request.Method != "POST")
                {
                    context.Response.ContentLength = 0;
                    context.Response.ContentType = kHttpsContentType;
                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    return context.Response.WriteAsync(string.Empty);
                }
                else
                {
                    return Listener.SendAsync(context);
                }
            });
        }
    }

    /// <summary>
    /// Manages the connections for a UA HTTPS server.
    /// </summary>
    public class HttpsTransportListener : ITransportListener
    {
        private const string kHttpsContentType = "text/plain";
        private const string kAuthorizationKey = "Authorization";
        private const string kBearerKey = "Bearer";

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="HttpsTransportListener"/> class.
        /// </summary>
        public HttpsTransportListener()
        {
        }
        #endregion

        #region IDisposable Members
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_simulator")]
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ConnectionStatusChanged = null;
                ConnectionWaiting = null;
                Utils.SilentDispose(m_host);
                m_host = null;
            }
        }
        #endregion

        #region ITransportListener Members
        /// <summary>
        /// The URI scheme handled by the listener.
        /// </summary>
        public string UriScheme => Utils.UriSchemeHttps;

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
            m_listenerId = Guid.NewGuid().ToString();

            m_uri = baseAddress;
            m_descriptions = settings.Descriptions;
            var configuration = settings.Configuration;

            // initialize the quotas.
            m_quotas = new ChannelQuotas {
                MaxBufferSize = configuration.MaxBufferSize,
                MaxMessageSize = configuration.MaxMessageSize,
                ChannelLifetime = configuration.ChannelLifetime,
                SecurityTokenLifetime = configuration.SecurityTokenLifetime,

                MessageContext = new ServiceMessageContext {
                    MaxArrayLength = configuration.MaxArrayLength,
                    MaxByteStringLength = configuration.MaxByteStringLength,
                    MaxMessageSize = configuration.MaxMessageSize,
                    MaxStringLength = configuration.MaxStringLength,
                    NamespaceUris = settings.NamespaceUris,
                    ServerUris = new StringTable(),
                    Factory = settings.Factory
                },

                CertificateValidator = settings.CertificateValidator
            };

            // save the callback to the server.
            m_callback = callback;

            m_serverCert = settings.ServerCertificate;

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

        /// <summary>
        /// Raised when a new connection is waiting for a client.
        /// </summary>
        public event ConnectionWaitingHandlerAsync ConnectionWaiting;

        /// <summary>
        /// Raised when a monitored connection's status changed.
        /// </summary>
        public event EventHandler<ConnectionStatusEventArgs> ConnectionStatusChanged;

        /// <inheritdoc/>
        /// <remarks>
        /// Reverse connect for the https transport listener is not implemeted.
        /// </remarks>
        public void CreateReverseConnection(Uri url, int timeout)
        {
            // suppress warnings
            ConnectionWaiting = null;
            ConnectionWaiting?.Invoke(null, null);
            ConnectionStatusChanged = null;
            ConnectionStatusChanged?.Invoke(null, null);
            throw new NotImplementedException();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Gets the URL for the listener's endpoint.
        /// </summary>
        /// <value>The URL for the listener's endpoint.</value>
        public Uri EndpointUrl => m_uri;

        /// <summary>
        /// Starts listening at the specified port.
        /// </summary>
        public void Start()
        {
            Startup.Listener = this;
            m_hostBuilder = new WebHostBuilder();
            HttpsConnectionAdapterOptions httpsOptions = new HttpsConnectionAdapterOptions();
            httpsOptions.CheckCertificateRevocation = false;
            httpsOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
            httpsOptions.ServerCertificate = m_serverCert;

#if NET462
            // note: although security tools recommend 'None' here,
            // it only works on .NET 4.6.2 if Tls12 is used
#pragma warning disable CA5398 // Avoid hardcoded SslProtocols values
            httpsOptions.SslProtocols = SslProtocols.Tls12;
#pragma warning restore CA5398 // Avoid hardcoded SslProtocols values
#else
            httpsOptions.SslProtocols = SslProtocols.None;
#endif
            bool bindToSpecifiedAddress = true;
            UriHostNameType hostType = Uri.CheckHostName(m_uri.Host);
            if (hostType == UriHostNameType.Dns || hostType == UriHostNameType.Unknown || hostType == UriHostNameType.Basic)
            {
                bindToSpecifiedAddress = false;
            }

            if (bindToSpecifiedAddress)
            {
                IPAddress ipAddress = IPAddress.Parse(m_uri.Host);
                m_hostBuilder.UseKestrel(options => {
                    options.Listen(ipAddress, m_uri.Port, listenOptions => {
                        listenOptions.UseHttps(httpsOptions);
                    });
                });
            }
            else
            {
                m_hostBuilder.UseKestrel(options => {
                    options.ListenAnyIP(m_uri.Port, listenOptions => {
                        listenOptions.UseHttps(httpsOptions);
                    });
                });
            }

            m_hostBuilder.UseContentRoot(Directory.GetCurrentDirectory());
            m_hostBuilder.UseStartup<Startup>();
            m_host = m_hostBuilder.Start(Utils.ReplaceLocalhost(m_uri.ToString()));
        }

        /// <summary>
        /// Stops listening.
        /// </summary>
        public void Stop()
        {
            Dispose();
        }
        #endregion

        #region Private Methods
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
                    await WriteResponseAsync(context.Response, message, HttpStatusCode.NotImplemented, ct).ConfigureAwait(false);
                    return;
                }

                if (context.Request.ContentType != "application/octet-stream")
                {
                    message = "HTTPSLISTENER - Unsupported content type.";
                    await WriteResponseAsync(context.Response, message, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    return;
                }

                int length = (int)context.Request.ContentLength;
                byte[] buffer = await ReadBodyAsync(context.Request).ConfigureAwait(false);

                if (buffer.Length != length)
                {
                    message = "HTTPSLISTENER - Invalid buffer.";
                    await WriteResponseAsync(context.Response, message, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    return;
                }

                IServiceRequest input = (IServiceRequest)BinaryDecoder.DecodeMessage(buffer, null, m_quotas.MessageContext);

                // extract the JWT token from the HTTP headers.
                if (input.RequestHeader == null)
                {
                    input.RequestHeader = new RequestHeader();
                }

                if (NodeId.IsNull(input.RequestHeader.AuthenticationToken) &&
                    input.TypeId != DataTypeIds.CreateSessionRequest)
                {
                    if (context.Request.Headers.ContainsKey(kAuthorizationKey))
                    {
                        foreach (string value in context.Request.Headers[kAuthorizationKey])
                        {
                            if (value.StartsWith(kBearerKey, StringComparison.OrdinalIgnoreCase))
                            {
                                // note: use NodeId(string, uint) to avoid the NodeId.Parse call.
                                input.RequestHeader.AuthenticationToken = new NodeId(value.Substring(kBearerKey.Length + 1).Trim(), 0);
                            }
                        }
                    }
                }

                if (!context.Request.Headers.TryGetValue(Profiles.HttpsSecurityPolicyHeader, out var header))
                {
                    header = SecurityPolicies.None;
                }

                EndpointDescription endpoint = null;
                foreach (var ep in m_descriptions)
                {
                    if (ep.EndpointUrl.StartsWith(Utils.UriSchemeHttps, StringComparison.Ordinal))
                    {
                        if (!string.IsNullOrEmpty(header))
                        {
                            if (!string.Equals(ep.SecurityPolicyUri, header, StringComparison.Ordinal))
                            {
                                continue;
                            }
                        }

                        endpoint = ep;
                        break;
                    }
                }

                if (endpoint == null &&
                    input.TypeId != DataTypeIds.GetEndpointsRequest &&
                    input.TypeId != DataTypeIds.FindServersRequest)
                {
                    message = "Connection refused, invalid security policy.";
                    Utils.LogError(message);
                    await WriteResponseAsync(context.Response, message, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    return;
                }

                IServiceResponse output = await Task.Factory.FromAsync(
                    m_callback.BeginProcessRequest(
                        m_listenerId,
                        endpoint,
                        input as IServiceRequest,
                        null, null),
                    m_callback.EndProcessRequest)
                    .ConfigureAwait(false);

                byte[] response = BinaryEncoder.EncodeMessage(output, m_quotas.MessageContext);
                context.Response.ContentLength = response.Length;
                context.Response.ContentType = context.Request.ContentType;
                context.Response.StatusCode = (int)HttpStatusCode.OK;
#if NETSTANDARD2_1 || NET5_0_OR_GREATER || NETCOREAPP3_1_OR_GREATER
                await context.Response.Body.WriteAsync(response.AsMemory(0, response.Length)).ConfigureAwait(false);
#else
                await context.Response.Body.WriteAsync(response, 0, response.Length, ct).ConfigureAwait(false);
#endif
                return;
            }
            catch (Exception e)
            {
                message = "HTTPSLISTENER - Unexpected error processing request.";
                Utils.LogError(e, message);
            }

            await WriteResponseAsync(context.Response, message, HttpStatusCode.InternalServerError, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Called when a UpdateCertificate event occured.
        /// </summary>
        public void CertificateUpdate(
            ICertificateValidator validator,
            X509Certificate2 serverCertificate,
            X509Certificate2Collection serverCertificateChain)
        {
            Stop();

            m_quotas.CertificateValidator = validator;
            m_serverCert = serverCertificate;
            foreach (var description in m_descriptions)
            {
                if (description.ServerCertificate != null)
                {
                    description.ServerCertificate = serverCertificate.RawData;
                }
            }

            Start();
        }

        private static async Task WriteResponseAsync(HttpResponse response, string message, HttpStatusCode status, CancellationToken ct)
        {
            if (!ct.IsCancellationRequested)
            {
                response.ContentLength = message.Length;
                response.ContentType = kHttpsContentType;
                response.StatusCode = (int)status;
                await response.WriteAsync(message, ct).ConfigureAwait(false);
            }
        }

        private static async Task<byte[]> ReadBodyAsync(HttpRequest req)
        {
            using (var memory = new MemoryStream())
            using (var reader = new StreamReader(req.Body))
            {
                await reader.BaseStream.CopyToAsync(memory).ConfigureAwait(false);
                return memory.ToArray();
            }
        }
        #endregion

        #region Private Fields
        private string m_listenerId;
        private Uri m_uri;
        private EndpointDescriptionCollection m_descriptions;
        private ChannelQuotas m_quotas;
        private ITransportListenerCallback m_callback;
        private IWebHostBuilder m_hostBuilder;
        private IWebHost m_host;
        private X509Certificate2 m_serverCert;
        #endregion
    }
}
