/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/


using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Bindings
{

    /// <summary>
    /// Creates a new HttpsTransportChannel with ITransportChannel interface.
    /// </summary>
    public class HttpsTransportChannelFactory : ITransportChannelFactory
    {
        /// <summary>
        /// The protocol supported by the channel.
        /// </summary>
        public string UriScheme => Utils.UriSchemeHttps;

        /// <summary>
        /// The method creates a new instance of a Https transport channel
        /// </summary>
        /// <returns>The transport channel</returns>
        public ITransportChannel Create()
        {
            return new HttpsTransportChannel();
        }
    }

    /// <summary>
    /// Wraps the HttpsTransportChannel and provides an ITransportChannel implementation.
    /// </summary>
    public class HttpsTransportChannel : ITransportChannel
    {
        /// <inheritdoc/>
        public void Dispose()
        {
        }

        /// <inheritdoc/>
        public string UriScheme => Utils.UriSchemeHttps;

        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures =>
            TransportChannelFeatures.Open |
            TransportChannelFeatures.Reconnect |
            TransportChannelFeatures.BeginSendRequest |
            TransportChannelFeatures.SendRequestAsync;

        /// <inheritdoc/>
        public EndpointDescription EndpointDescription => m_settings.Description;

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration => m_settings.Configuration;

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext => m_quotas.MessageContext;

        /// <inheritdoc/>
        public ChannelToken CurrentToken => null;

        /// <inheritdoc/>
        public int OperationTimeout
        {
            get { return m_operationTimeout; }
            set { m_operationTimeout = value; }
        }

        /// <inheritdoc/>
        public void Initialize(
            Uri url,
            TransportChannelSettings settings)
        {
            SaveSettings(url, settings);
        }

        /// <summary>
        /// Initializes a secure channel with a waiting reverse connection.
        /// </summary>
        /// <param name="connection">The connection to use.</param>
        /// <param name="settings">The settings to use when creating the channel.</param>
        /// <exception cref="ServiceResultException">Thrown if any communication error occurs.</exception>
        public void Initialize(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings)
        {
            SaveSettings(connection.EndpointUrl, settings);
        }

        /// <inheritdoc/>
        public void Open()
        {
            try
            {
                // auto validate server cert, if supported
                // if unsupported, the TLS server cert must be trusted by a root CA
                var handler = new HttpClientHandler();
                handler.ClientCertificateOptions = ClientCertificateOption.Manual;

                // send client certificate for servers that require TLS client authentication
                if (m_settings.ClientCertificate != null)
                {
                    handler.ClientCertificates.Add(m_settings.ClientCertificate);
                }

                // OSX platform cannot auto validate certs and throws
                // on PostAsync, do not set validation handler
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    try
                    {
                        handler.ServerCertificateCustomValidationCallback =
                            (httpRequestMessage, cert, chain, policyErrors) => {
                                try
                                {
                                    m_quotas.CertificateValidator?.Validate(cert);
                                    return true;
                                }
                                catch (Exception ex)
                                {
                                    Utils.Trace("HTTPS: Failed to validate server cert: " + cert.Subject);
                                    Utils.Trace("HTTPS: Exception:" + ex.Message);
                                }
                                return false;
                            };
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // client may throw if not supported (e.g. UWP)
                        handler.ServerCertificateCustomValidationCallback = null;
                    }
                }

                m_client = new HttpClient(handler);
            }
            catch (Exception ex)
            {
                Utils.Trace("Exception creating HTTPS Client: " + ex.Message);
                throw;
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            if (m_client != null)
            {
                m_client.Dispose();
            }
        }

        /// <summary>
        /// The async result class for the Https transport.
        /// </summary>
        private class HttpsAsyncResult : AsyncResultBase
        {
            public IServiceRequest Request;
            public HttpResponseMessage Response;

            public HttpsAsyncResult(
                AsyncCallback callback,
                object callbackData,
                int timeout,
                IServiceRequest request,
                HttpResponseMessage response)
            :
                base(callback, callbackData, timeout)
            {
                Request = request;
                Response = response;
            }
        }

        /// <inheritdoc/>
        public IAsyncResult BeginSendRequest(IServiceRequest request, AsyncCallback callback, object callbackData)
        {
            HttpResponseMessage response = null;

            try
            {
                ByteArrayContent content = new ByteArrayContent(BinaryEncoder.EncodeMessage(request, m_quotas.MessageContext));
                content.Headers.ContentType = m_mediaTypeHeaderValue;

                var result = new HttpsAsyncResult(callback, callbackData, m_operationTimeout, request, null);
                Task.Run(async () => {
                    try
                    {
                        var ct = new CancellationTokenSource(m_operationTimeout).Token;
                        response = await m_client.PostAsync(m_url, content, ct).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
                    }
                    catch (Exception ex)
                    {
                        Utils.Trace("Exception sending HTTPS request: " + ex.Message);
                        result.Exception = ex;
                        response = null;
                    }
                    result.Response = response;
                    result.OperationCompleted();
                });

                return result;
            }
            catch (Exception ex)
            {
                Utils.Trace("Exception sending HTTPS request: " + ex.Message);
                HttpsAsyncResult result = new HttpsAsyncResult(callback, callbackData, m_operationTimeout, request, response);
                result.Exception = ex;
                result.OperationCompleted();
                return result;
            }
        }

        /// <inheritdoc/>
        public IServiceResponse EndSendRequest(IAsyncResult result)
        {
            HttpsAsyncResult result2 = result as HttpsAsyncResult;
            if (result2 == null)
            {
                throw new ArgumentException("Invalid result object passed.", nameof(result));
            }

            try
            {
                result2.WaitForComplete();
                if (result2.Response != null)
                {
                    Stream responseContent = result2.Response.Content.ReadAsStreamAsync().Result;
                    return BinaryDecoder.DecodeMessage(responseContent, null, m_quotas.MessageContext) as IServiceResponse;
                }
            }
            catch (Exception ex)
            {
                Utils.Trace("Exception reading HTTPS response: " + ex.Message);
                result2.Exception = ex;
            }
            return result2 as IServiceResponse;
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public IAsyncResult BeginOpen(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public void EndOpen(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public void Reconnect()
        {
            Utils.Trace("HttpsTransportChannel RECONNECT: Reconnecting to {0}.", m_url);
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        void ITransportChannel.Reconnect(ITransportWaitingConnection connection)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public IAsyncResult BeginReconnect(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public void EndReconnect(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public IAsyncResult BeginClose(AsyncCallback callback, object callbackData)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        /// <remarks>Not implemented here.</remarks>
        public void EndClose(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public IServiceResponse SendRequest(IServiceRequest request)
        {
            IAsyncResult result = BeginSendRequest(request, null, null);
            return EndSendRequest(result);
        }

        /// <inheritdoc/>
        public async Task<IServiceResponse> SendRequestAsync(IServiceRequest request, CancellationToken ct)
        {
            try
            {
                ByteArrayContent content = new ByteArrayContent(BinaryEncoder.EncodeMessage(request, m_quotas.MessageContext));
                content.Headers.ContentType = m_mediaTypeHeaderValue;
                var result = await m_client.PostAsync(m_url, content, ct).ConfigureAwait(false);
                result.EnsureSuccessStatusCode();
                Stream responseContent = await result.Content.ReadAsStreamAsync().ConfigureAwait(false);
                return BinaryDecoder.DecodeMessage(responseContent, null, m_quotas.MessageContext) as IServiceResponse;
            }
            catch (Exception ex)
            {
                Utils.Trace("Exception sending HTTPS request: " + ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Save the settings for a connection.
        /// </summary>
        /// <param name="url">The server url.</param>
        /// <param name="settings">The settings for the transport channel.</param>
        private void SaveSettings(Uri url, TransportChannelSettings settings)
        {
            m_url = new Uri(url.ToString());

            m_settings = settings;
            m_operationTimeout = settings.Configuration.OperationTimeout;

            // initialize the quotas.
            m_quotas = new ChannelQuotas {
                MaxBufferSize = m_settings.Configuration.MaxBufferSize,
                MaxMessageSize = m_settings.Configuration.MaxMessageSize,
                ChannelLifetime = m_settings.Configuration.ChannelLifetime,
                SecurityTokenLifetime = m_settings.Configuration.SecurityTokenLifetime,

                MessageContext = new ServiceMessageContext {
                    MaxArrayLength = m_settings.Configuration.MaxArrayLength,
                    MaxByteStringLength = m_settings.Configuration.MaxByteStringLength,
                    MaxMessageSize = m_settings.Configuration.MaxMessageSize,
                    MaxStringLength = m_settings.Configuration.MaxStringLength,
                    NamespaceUris = m_settings.NamespaceUris,
                    ServerUris = new StringTable(),
                    Factory = m_settings.Factory
                },

                CertificateValidator = settings.CertificateValidator
            };
        }

        private Uri m_url;
        private int m_operationTimeout;
        private TransportChannelSettings m_settings;
        private ChannelQuotas m_quotas;
        private HttpClient m_client;
        private static readonly MediaTypeHeaderValue m_mediaTypeHeaderValue = new MediaTypeHeaderValue("application/octet-stream");
    }
}

