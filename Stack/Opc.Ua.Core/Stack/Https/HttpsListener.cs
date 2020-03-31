/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

#if !NO_HTTPS

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging;
using System;
using System.IdentityModel.Selectors;
using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

#if NETSTANDARD2_0
using Microsoft.AspNetCore.Server.Kestrel.Core;
#endif


namespace Opc.Ua.Bindings
{
    public class Startup
    {
        public static UaHttpsChannelListener Listener { get; set; }

        public void Configure(IApplicationBuilder appBuilder)
        {
            appBuilder.Run(async context =>
            {
                if (context.Request.Method != "POST")
                {
                    context.Response.ContentLength = 0;
                    context.Response.ContentType = "text/plain";
                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    await context.Response.WriteAsync(string.Empty);
                }
                else
                {
                    await Listener.SendAsync(context);
                }
            });
        }
    }

    /// <summary>
    /// Manages the connections for a UA HTTPS server.
    /// </summary>
    public partial class UaHttpsChannelListener : ITransportListener
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="UaHttpsChannelListener"/> class.
        /// </summary>
        public UaHttpsChannelListener()
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
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_simulator")]
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Utils.SilentDispose(m_host);
                m_host = null;
            }
        }
        #endregion

        #region ITransportListener Members
        /// <summary>
        /// Opens the listener and starts accepting connection.
        /// </summary>
        /// <param name="baseAddress">The base address.</param>
        /// <param name="settings">The settings to use when creating the listener.</param>
        /// <param name="callback">The callback to use when requests arrive via the channel.</param>
        /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Open(Uri baseAddress, TransportListenerSettings settings, ITransportListenerCallback callback)
        {
            // assign a unique guid to the listener.
            m_listenerId = Guid.NewGuid().ToString();

            m_uri = baseAddress;
            m_descriptions = settings.Descriptions;
            var configuration = settings.Configuration;

            // initialize the quotas.
            m_quotas = new ChannelQuotas();

            m_quotas.MaxBufferSize = configuration.MaxBufferSize;
            m_quotas.MaxMessageSize = configuration.MaxMessageSize;
            m_quotas.ChannelLifetime = configuration.ChannelLifetime;
            m_quotas.SecurityTokenLifetime = configuration.SecurityTokenLifetime;

            m_quotas.MessageContext = new ServiceMessageContext();

            m_quotas.MessageContext.MaxArrayLength = configuration.MaxArrayLength;
            m_quotas.MessageContext.MaxByteStringLength = configuration.MaxByteStringLength;
            m_quotas.MessageContext.MaxMessageSize = configuration.MaxMessageSize;
            m_quotas.MessageContext.MaxStringLength = configuration.MaxStringLength;
            m_quotas.MessageContext.NamespaceUris = settings.NamespaceUris;
            m_quotas.MessageContext.ServerUris = new StringTable();
            m_quotas.MessageContext.Factory = settings.Factory;

            m_quotas.CertificateValidator = settings.CertificateValidator;

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
        #endregion

        #region Public Methods
        /// <summary>
        /// Gets the URL for the listener's endpoint.
        /// </summary>
        /// <value>The URL for the listener's endpoint.</value>
        public Uri EndpointUrl
        {
            get { return m_uri; }
        }

        /// <summary>
        /// Starts listening at the specified port.
        /// </summary>
        public void Start()
        {
            Startup.Listener = this;
            m_hostBuilder = new WebHostBuilder();
#if NETSTANDARD2_0 || NETSTANDARD2_1
            HttpsConnectionAdapterOptions httpsOptions = new HttpsConnectionAdapterOptions();
            httpsOptions.CheckCertificateRevocation = false;
            httpsOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
            httpsOptions.ServerCertificate = m_serverCert;
            httpsOptions.SslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
            m_hostBuilder.UseKestrel(options =>
            {              
                options.Listen(IPAddress.Any, m_uri.Port, listenOptions =>
                {
                    listenOptions.NoDelay = true;
                    listenOptions.UseHttps(httpsOptions);
                });
            });
#else
            HttpsConnectionFilterOptions httpsOptions = new HttpsConnectionFilterOptions();
            httpsOptions.CheckCertificateRevocation = false;
            httpsOptions.ClientCertificateMode = ClientCertificateMode.NoCertificate;
            httpsOptions.ServerCertificate = m_serverCert;
            httpsOptions.SslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
            m_hostBuilder.UseKestrel(options =>
            {
                options.NoDelay = true;
                options.UseHttps(httpsOptions);
            });
#endif
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
            IAsyncResult result = null;

            try
            {
                if (m_callback == null)
                {
                    context.Response.ContentLength = 0;
                    context.Response.ContentType = "text/plain";
                    context.Response.StatusCode = (int)HttpStatusCode.NotImplemented;
                    await context.Response.WriteAsync(string.Empty);
                    return;
                }

                if (context.Request.ContentType != "application/octet-stream")
                {
                    context.Response.ContentLength = 0;
                    context.Response.ContentType = "text/plain";
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await context.Response.WriteAsync("HTTPSLISTENER - Unsupported content type.");
                    return;
                }

                int length = (int) context.Request.ContentLength;
                byte[] buffer = await ReadBodyAsync(context.Request);

                if (buffer.Length != length)
                {
                    context.Response.ContentLength = 0;
                    context.Response.ContentType = "text/plain";
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await context.Response.WriteAsync("HTTPSLISTENER - Couldn't decode buffer.");
                    return;
                }

                IServiceRequest input = (IServiceRequest)BinaryDecoder.DecodeMessage(buffer, null, m_quotas.MessageContext);

                // extract the JWT token from the HTTP headers.
                if (input.RequestHeader == null)
                {
                    input.RequestHeader = new RequestHeader();
                }

                if (NodeId.IsNull(input.RequestHeader.AuthenticationToken) && input.TypeId != DataTypeIds.CreateSessionRequest)
                {
                    if (context.Request.Headers.Keys.Contains("Authorization"))
                    {
                        foreach (string value in context.Request.Headers["Authorization"])
                        {
                            if (value.StartsWith("Bearer"))
                            {
                                input.RequestHeader.AuthenticationToken = new NodeId(value.Substring("Bearer ".Length).Trim());
                            }
                        }
                    }
                }

                EndpointDescription endpoint = null;

                foreach (var ep in m_descriptions)
                {
                    if (ep.EndpointUrl.StartsWith(Utils.UriSchemeHttps))
                    {
                        endpoint = ep;
                        break;
                    }
                }

                result = m_callback.BeginProcessRequest(
                    m_listenerId,
                    endpoint,
                    input as IServiceRequest,
                    null,
                    null);

                IServiceResponse output = m_callback.EndProcessRequest(result);

                byte[] response = BinaryEncoder.EncodeMessage(output, m_quotas.MessageContext);
                context.Response.ContentLength = response.Length;
                context.Response.ContentType = context.Request.ContentType;
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                await context.Response.Body.WriteAsync(response, 0, response.Length);
            }
            catch (Exception e)
            {
                Utils.Trace(e, "HTTPSLISTENER - Unexpected error processing request.");
                context.Response.ContentLength = e.Message.Length;
                context.Response.ContentType = "text/plain";
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await context.Response.WriteAsync(e.Message);
            }
        }

        /// <summary>
        /// Called when a UpdateCertificate event occured.
        /// </summary>
        internal void CertificateUpdate(
            X509CertificateValidator validator,
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

        private async Task<byte[]> ReadBodyAsync(HttpRequest req)
        {
            using (var memory = new MemoryStream())
            using (var reader = new StreamReader(req.Body))
            {
                await reader.BaseStream.CopyToAsync(memory);
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

#endif
