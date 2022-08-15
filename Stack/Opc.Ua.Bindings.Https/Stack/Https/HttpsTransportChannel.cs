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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

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
        // limit the number of concurrent service requests on the server
        private const int kMaxConnectionsPerServer = 64;

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
                var handler = new HttpClientHandler();
                handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                handler.MaxConnectionsPerServer = kMaxConnectionsPerServer;

                // send client certificate for servers that require TLS client authentication
                if (m_settings.ClientCertificate != null)
                {
                    var propertyInfo = handler.GetType().GetProperty("ClientCertificates");
                    if (propertyInfo != null)
                    {
                        X509CertificateCollection clientCertificates = (X509CertificateCollection)propertyInfo.GetValue(handler);
                        clientCertificates?.Add(m_settings.ClientCertificate);
                    }
                }

                // OSX platform cannot auto validate certs and throws
                // on PostAsync, do not set validation handler
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var propertyInfo = handler.GetType().GetProperty("ServerCertificateCustomValidationCallback");
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
                                        m_quotas.CertificateValidator?.Validate(cert);
                                        return true;
                                    }
                                    catch (Exception ex)
                                    {
                                        Utils.LogError(ex, "HTTPS: Exception:");
                                        Utils.LogCertificate(LogLevel.Error, "HTTPS: Failed to validate server cert: ", cert);
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
                Task.Run(async () => {
                    try
                    {
                        var ct = new CancellationTokenSource(m_operationTimeout).Token;
                        response = await m_client.PostAsync(m_url, content, ct).ConfigureAwait(false);
                        response.EnsureSuccessStatusCode();
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
                Utils.LogError(ex, "Exception reading HTTPS response.");
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

                var result = await m_client.PostAsync(m_url, content, ct).ConfigureAwait(false);
                result.EnsureSuccessStatusCode();
#if NET6_0_OR_GREATER
                Stream responseContent = await result.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
#else
                Stream responseContent = await result.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                return BinaryDecoder.DecodeMessage(responseContent, null, m_quotas.MessageContext) as IServiceResponse;
            }
            catch (Exception ex)
            {
                Utils.LogError(ex, "Exception sending HTTPS request.");
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
        private static readonly MediaTypeHeaderValue s_mediaTypeHeaderValue = new MediaTypeHeaderValue("application/octet-stream");
    }
}

