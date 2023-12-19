/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
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
            return new HttpsTransportChannel(UriScheme);
        }
    }

    /// <summary>
    /// Creates a new HttpsTransportChannel with ITransportChannel interface.
    /// The uri scheme opc.https is used.
    /// </summary>
    public class OpcHttpsTransportChannelFactory : ITransportChannelFactory
    {
        /// <summary>
        /// The protocol supported by the channel.
        /// </summary>
        public string UriScheme => Utils.UriSchemeOpcHttps;

        /// <summary>
        /// The method creates a new instance of a Https transport channel
        /// </summary>
        /// <returns>The transport channel</returns>
        public ITransportChannel Create()
        {
            return new HttpsTransportChannel(UriScheme);
        }
    }

    /// <summary>
    /// Wraps the HttpsTransportChannel and provides an ITransportChannel implementation.
    /// </summary>
    public class HttpsTransportChannel : ITransportChannel
    {
        // limit the number of concurrent service requests on the server
        private const int kMaxConnectionsPerServer = 64;

        /// <summary>
        /// Create a transport channel based on the uri scheme.
        /// </summary>
        public HttpsTransportChannel(string uriScheme)
        {
            m_uriScheme = uriScheme;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        /// <inheritdoc/>
        public string UriScheme => m_uriScheme;

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
            get => m_operationTimeout;
            set => m_operationTimeout = value;
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
                Utils.LogInfo("{0} Open {1}.", nameof(HttpsTransportChannel), m_url);

                // auto validate server cert, if supported
                // if unsupported, the TLS server cert must be trusted by a root CA
                var handler = new HttpClientHandler {
                    ClientCertificateOptions = ClientCertificateOption.Manual,
                    AllowAutoRedirect = false,
                    MaxRequestContentBufferSize = m_quotas.MaxMessageSize,
                };

                // limit the number of concurrent connections, if supported
                PropertyInfo maxConnectionsPerServerProperty = handler.GetType().GetProperty("MaxConnectionsPerServer");
                maxConnectionsPerServerProperty?.SetValue(handler, kMaxConnectionsPerServer);

                // send client certificate for servers that require TLS client authentication
                if (m_settings.ClientCertificate != null)
                {
                    PropertyInfo certProperty = handler.GetType().GetProperty("ClientCertificates");
                    if (certProperty != null)
                    {
                        X509CertificateCollection clientCertificates = (X509CertificateCollection)certProperty.GetValue(handler);
                        _ = clientCertificates?.Add(m_settings.ClientCertificate);
                    }
                }

                PropertyInfo propertyInfo = handler.GetType().GetProperty("ServerCertificateCustomValidationCallback");
                if (propertyInfo != null)
                {
                    Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>
                        serverCertificateCustomValidationCallback;

                    try
                    {
                        serverCertificateCustomValidationCallback =
                            (httpRequestMessage, cert, chain, policyErrors) => {
                                try
                                {
                                    var validationChain = new X509Certificate2Collection();
                                    if (chain != null && chain.ChainElements != null)
                                    {
                                        int i = 0;
                                        Utils.LogInfo(Utils.TraceMasks.Security, "{0} Validate server chain:", nameof(HttpsTransportChannel));
                                        foreach (X509ChainElement element in chain.ChainElements)
                                        {
                                            Utils.LogCertificate(Utils.TraceMasks.Security, "{0}: ", element.Certificate, i);
                                            validationChain.Add(element.Certificate);
                                            i++;
                                        }
                                    }
                                    else
                                    {
                                        Utils.LogCertificate(Utils.TraceMasks.Security, "{0} Validate Server Certificate: ", cert, nameof(HttpsTransportChannel));
                                        validationChain.Add(cert);
                                    }

                                    m_quotas.CertificateValidator?.Validate(validationChain);

                                    return true;
                                }
                                catch (Exception ex)
                                {
                                    Utils.LogError(ex, "{0} Failed to validate certificate.", nameof(HttpsTransportChannel));
                                }
                                return false;
                            };
                        propertyInfo.SetValue(handler, serverCertificateCustomValidationCallback);

                        Utils.LogInfo("{0} ServerCertificate callback enabled.", nameof(HttpsTransportChannel));
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // client may throw if not supported (e.g. UWP)
                        serverCertificateCustomValidationCallback = null;
                    }
                }

                m_client = new HttpClient(handler);
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Exception creating HTTPS Client.");
                throw;
            }
        }

        /// <inheritdoc/>
        public void Close()
        {
            Utils.LogInfo("{0} Close {1}.", nameof(HttpsTransportChannel), m_url);
            m_client?.Dispose();
        }

        /// <inheritdoc/>
        public Task CloseAsync(CancellationToken ct)
        {
            Close();
            return Task.CompletedTask;
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
                var content = new ByteArrayContent(BinaryEncoder.EncodeMessage(request, m_quotas.MessageContext));
                content.Headers.ContentType = s_mediaTypeHeaderValue;
                if (EndpointDescription?.SecurityPolicyUri != null &&
                    !string.Equals(EndpointDescription.SecurityPolicyUri, SecurityPolicies.None, StringComparison.Ordinal))
                {
                    content.Headers.Add(Profiles.HttpsSecurityPolicyHeader, EndpointDescription.SecurityPolicyUri);
                }

                var result = new HttpsAsyncResult(callback, callbackData, m_operationTimeout, request, null);

                _ = Task.Run(async () => {
                    try
                    {
                        using (var cts = new CancellationTokenSource(m_operationTimeout))
                        {
                            response = await m_client.PostAsync(m_url, content, cts.Token).ConfigureAwait(false);
                            response.EnsureSuccessStatusCode();
                        }
                    }
                    catch (Exception ex)
                    {
                        Utils.LogError(ex, "Exception sending HTTPS request.");
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
                Utils.LogError(ex, "Exception sending HTTPS request.");
                var result = new HttpsAsyncResult(callback, callbackData, m_operationTimeout, request, response);
                result.Exception = ex;
                result.OperationCompleted();
                return result;
            }
        }

        /// <inheritdoc/>
        public IServiceResponse EndSendRequest(IAsyncResult result)
        {
            var result2 = result as HttpsAsyncResult;
            if (result2 == null)
            {
                throw new ArgumentException("Invalid result object passed.", nameof(result));
            }

            try
            {
                result2.WaitForComplete();
                if (result2.Response != null)
                {
#if NET6_0_OR_GREATER
                    Stream responseContent = result2.Response.Content.ReadAsStream();
#else
                    Stream responseContent = result2.Response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
#endif
                    return BinaryDecoder.DecodeMessage(responseContent, null, m_quotas.MessageContext) as IServiceResponse;
                }
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Exception reading HTTPS response.");
                result2.Exception = ex;
            }
            return result2 as IServiceResponse;
        }

        /// <inheritdoc/>
        public Task<IServiceResponse> EndSendRequestAsync(IAsyncResult result, CancellationToken ct)
        {
            throw new NotImplementedException();
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
            Utils.LogInfo("HttpsTransportChannel RECONNECT: Reconnecting to {0}.", m_url);
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
                var content = new ByteArrayContent(BinaryEncoder.EncodeMessage(request, m_quotas.MessageContext));
                content.Headers.ContentType = s_mediaTypeHeaderValue;
                if (EndpointDescription?.SecurityPolicyUri != null &&
                    !string.Equals(EndpointDescription.SecurityPolicyUri, SecurityPolicies.None, StringComparison.Ordinal))
                {
                    content.Headers.Add(Profiles.HttpsSecurityPolicyHeader, EndpointDescription.SecurityPolicyUri);
                }

                HttpResponseMessage response;
                using (var cts = new CancellationTokenSource(m_operationTimeout))
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct))
                {
                    response = await m_client.PostAsync(m_url, content, linkedCts.Token).ConfigureAwait(false);
                }
                response.EnsureSuccessStatusCode();

#if NET6_0_OR_GREATER
                Stream responseContent = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
#else
                Stream responseContent = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                return BinaryDecoder.DecodeMessage(responseContent, null, m_quotas.MessageContext) as IServiceResponse;
            }
            catch (HttpRequestException hre)
            {
                if (hre.InnerException is WebException webex)
                {
                    StatusCode statusCode = StatusCodes.BadUnknownResponse;
                    switch (webex.Status)
                    {
                        case WebExceptionStatus.Timeout: statusCode = StatusCodes.BadRequestTimeout; break;
                        case WebExceptionStatus.ConnectionClosed:
                        case WebExceptionStatus.ConnectFailure: statusCode = StatusCodes.BadNotConnected; break;
                    }
                    Utils.LogError(webex, "Exception sending HTTPS request.");
                    throw ServiceResultException.Create((uint)statusCode, webex.Message);
                }
                Utils.LogError(hre, "Exception sending HTTPS request.");
                throw;
            }
            catch (TaskCanceledException tce)
            {
                Utils.LogError(tce, "Send request cancelled.");
                throw ServiceResultException.Create(StatusCodes.BadRequestTimeout, "Https request was cancelled.");
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Exception sending HTTPS request.");
                throw ServiceResultException.Create(StatusCodes.BadUnknownResponse, ex.Message);
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
            // remove the opc. prefix, the https client can not handle it
            if (m_url.Scheme == Utils.UriSchemeOpcHttps)
            {
                m_url = new Uri(url.ToString().Substring(4));
            }
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

        private string m_uriScheme;
        private Uri m_url;
        private int m_operationTimeout;
        private TransportChannelSettings m_settings;
        private ChannelQuotas m_quotas;
        private HttpClient m_client;
        private static readonly MediaTypeHeaderValue s_mediaTypeHeaderValue = new MediaTypeHeaderValue("application/octet-stream");
    }
}

