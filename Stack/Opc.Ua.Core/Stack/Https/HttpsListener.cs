/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Net;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;

namespace Opc.Ua.Bindings
{
    public class Startup
    {
        public static UaHttpsChannelListener Listener { get; set; }

        public void Configure(IApplicationBuilder appBuilder, ILoggerFactory loggerFactory)
        {
            appBuilder.Run(async context =>
            {
                Utils.Trace("{0} {1}{2}{3}",
                    context.Request.Method,
                    context.Request.PathBase,
                    context.Request.Path,
                    context.Request.QueryString);
                Utils.Trace($"Method: {context.Request.Method}");
                Utils.Trace($"PathBase: {context.Request.PathBase}");
                Utils.Trace($"Path: {context.Request.Path}");
                Utils.Trace($"QueryString: {context.Request.QueryString}");

                ConnectionInfo connectionInfo = context.Connection;
                Utils.Trace($"Peer: {connectionInfo.RemoteIpAddress?.ToString()} {connectionInfo.RemotePort}");
                Utils.Trace($"Sock: {connectionInfo.LocalIpAddress?.ToString()} {connectionInfo.LocalPort}");

                if (context.Request.Method != "POST")
                {
                    context.Response.ContentLength = 0;
                    context.Response.ContentType = "text/plain";
                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    await context.Response.WriteAsync(string.Empty);
                }
                else
                {
                    Listener.SendAsync(context);
                }
            });
        }
    }

    /// <summary>
    /// Manages the connections for a UA HTTPS server.
    /// </summary>
    public partial class UaHttpsChannelListener : IDisposable, ITransportListener
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="UaTcpChannelListener"/> class.
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
                lock (m_lock)
                {
                    Utils.SilentDispose(m_host);
                    m_host = null;
                }
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
            m_configuration = settings.Configuration;

            // initialize the quotas.
            m_quotas = new TcpChannelQuotas();

            m_quotas.MaxBufferSize = m_configuration.MaxBufferSize;
            m_quotas.MaxMessageSize = m_configuration.MaxMessageSize;
            m_quotas.ChannelLifetime = m_configuration.ChannelLifetime;
            m_quotas.SecurityTokenLifetime = m_configuration.SecurityTokenLifetime;

            m_quotas.MessageContext = new ServiceMessageContext();

            m_quotas.MessageContext.MaxArrayLength = m_configuration.MaxArrayLength;
            m_quotas.MessageContext.MaxByteStringLength = m_configuration.MaxByteStringLength;
            m_quotas.MessageContext.MaxMessageSize = m_configuration.MaxMessageSize;
            m_quotas.MessageContext.MaxStringLength = m_configuration.MaxStringLength;
            m_quotas.MessageContext.NamespaceUris = settings.NamespaceUris;
            m_quotas.MessageContext.ServerUris = new StringTable();
            m_quotas.MessageContext.Factory = settings.Factory;

            m_quotas.CertificateValidator = settings.CertificateValidator;

            // save the callback to the server.
            m_callback = callback;

            m_serverCert = settings.ServerCertificate;

            // start the listener.
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
           
            m_host = new WebHostBuilder();
            m_host.UseKestrel(options =>
            {
                options.NoDelay = true;
                options.UseHttps(m_serverCert);
                options.UseConnectionLogging();
            });
            m_host.UseUrls(m_uri.ToString());
            m_host.UseContentRoot(Directory.GetCurrentDirectory());
            m_host.UseStartup<Startup>();
            m_host.Build();
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
        public async void SendAsync(HttpContext context)
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
                }

                byte[] buffer = new byte[(int)context.Request.ContentLength];
                await context.Request.Body.ReadAsync(buffer, 0, (int)context.Request.ContentLength);
                
                IServiceRequest input = (IServiceRequest) BinaryDecoder.DecodeMessage(buffer, null, m_quotas.MessageContext);

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
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
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
        /// Handles requests arriving from a channel.
        /// </summary>
        private IAsyncResult BeginProcessRequest(Stream istrm, string action, string securityPolicyUri, object callbackData)
        {
            IAsyncResult result = null;

            try
            {
                if (m_callback != null)
                {
                    Uri uri = m_uri; // context.Request.UriTemplateMatch.RequestUri;

                    string scheme = uri.Scheme + ":";

                    EndpointDescription endpoint = null;

                    for (int ii = 0; ii < m_descriptions.Count; ii++)
                    {
                        if (m_descriptions[ii].EndpointUrl.StartsWith(scheme))
                        {
                            if (endpoint == null)
                            {
                                endpoint = m_descriptions[ii];
                            }

                            if (m_descriptions[ii].SecurityPolicyUri == securityPolicyUri)
                            {
                                endpoint = m_descriptions[ii];
                                break;
                            }
                        }
                    }

                    IEncodeable request = BinaryDecoder.DecodeMessage(istrm, null, this.m_quotas.MessageContext);

                    result = m_callback.BeginProcessRequest(
                        m_listenerId,
                        endpoint,
                        request as IServiceRequest,
                        null,
                        callbackData);
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "HTTPSLISTENER - Unexpected error processing request.");
            }

            return result;
        }

        private Stream EndProcessRequest(IAsyncResult result)
        {
            MemoryStream ostrm = new MemoryStream();

            try
            {
                if (m_callback != null)
                {
                    IServiceResponse response = m_callback.EndProcessRequest(result);
                    
                    BinaryEncoder encoder = new BinaryEncoder(ostrm, this.m_quotas.MessageContext);
                    encoder.EncodeMessage(response);
                    
                    ostrm.Position = 0;
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "TCPLISTENER - Unexpected error sending result.");
            }

            return ostrm;
        }

        /// <summary>
        /// Sets the URI for the listener.
        /// </summary>
        private void SetUri(Uri baseAddress, string relativeAddress)
        {
            if (baseAddress == null) throw new ArgumentNullException("baseAddress");

            // validate uri.
            if (!baseAddress.IsAbsoluteUri)
            {
                throw new ArgumentException(Utils.Format("Base address must be an absolute URI."), "baseAddress");
            }

            if (String.Compare(baseAddress.Scheme, Utils.UriSchemeOpcTcp, StringComparison.OrdinalIgnoreCase) != 0)
            {
                throw new ArgumentException(Utils.Format("Invalid URI scheme: {0}.", baseAddress.Scheme), "baseAddress");
            }

            m_uri = baseAddress;

            // append the relative path to the base address.
            if (!String.IsNullOrEmpty(relativeAddress))
            {
                if (!baseAddress.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
                {
                    UriBuilder uriBuilder = new UriBuilder(baseAddress);
                    uriBuilder.Path = uriBuilder.Path + "/";
                    baseAddress = uriBuilder.Uri;
                }

                m_uri = new Uri(baseAddress, relativeAddress);
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();

        private string m_listenerId;
        private Uri m_uri;
        private EndpointDescriptionCollection m_descriptions;
        private EndpointConfiguration m_configuration;
        private TcpChannelQuotas m_quotas;
        private ITransportListenerCallback m_callback;
        private WebHostBuilder m_host;
        private X509Certificate2 m_serverCert;
        #endregion
    }
}

