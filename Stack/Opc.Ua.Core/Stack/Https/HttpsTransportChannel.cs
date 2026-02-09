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

#nullable enable

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
using System.Diagnostics;
#if NETSTANDARD2_1 || NET472_OR_GREATER || NET5_0_OR_GREATER
using System.Security.Cryptography;
#endif

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
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            return new HttpsTransportChannel(UriScheme, telemetry);
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
        public ITransportChannel Create(ITelemetryContext telemetry)
        {
            return new HttpsTransportChannel(UriScheme, telemetry);
        }
    }

    /// <summary>
    /// Wraps the HttpsTransportChannel and provides an ITransportChannel implementation.
    /// </summary>
    public class HttpsTransportChannel : ITransportChannel, ISecureChannel
    {
        /// <summary>
        /// limit the number of concurrent service requests on the server
        /// </summary>
        private const int kMaxConnectionsPerServer = 64;

        /// <summary>
        /// Create a transport channel based on the uri scheme.
        /// </summary>
        public HttpsTransportChannel(string uriScheme, ITelemetryContext telemetry)
        {
            UriScheme = uriScheme;
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<HttpsTransportChannel>();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public string UriScheme { get; }

        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures =>
            TransportChannelFeatures.None;

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
        public ChannelToken? CurrentToken => null;

        /// <inheritdoc/>
        public event ChannelTokenActivatedEventHandler OnTokenActivated
        {
            add
            {
            }
            remove
            {
            }
        }

        /// <inheritdoc/>
        public int OperationTimeout { get; set; }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            Uri url,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            SaveSettings(url, settings);
            CreateHttpClient();
            return default;
        }

        /// <inheritdoc/>
        public ValueTask OpenAsync(
            ITransportWaitingConnection connection,
            TransportChannelSettings settings,
            CancellationToken ct)
        {
            SaveSettings(connection.EndpointUrl, settings);
            CreateHttpClient();
            return default;
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken ct)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(HttpsTransportChannel));
            }

            m_logger.LogInformation(
                "{ChannelType} Closing http channel with {Url}.",
                nameof(HttpsTransportChannel),
                m_url);

            Utils.SilentDispose(m_client);
            m_client = null;

            return default;
        }

        /// <inheritdoc/>
        public ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection,
            CancellationToken ct)
        {
            throw ServiceResultException.Create(
                StatusCodes.BadNotSupported,
                "{ChannelType} does not support reconnect.",
                nameof(HttpsTransportChannel));
        }

        /// <inheritdoc/>
        public async ValueTask<IServiceResponse> SendRequestAsync(
            IServiceRequest request,
            CancellationToken ct)
        {
            if (m_disposed)
            {
                // right now this can happen if Dispose is called
                // Before channel users (e.g. keep alive requests)
                // are cancelled and stopped.
                // TODO: Areas like this should be fixed eventually.
                throw BadNotConnected();
                // throw new ObjectDisposedException(nameof(HttpsTransportChannel));
            }
            IServiceMessageContext context = m_quotas?.MessageContext ?? throw BadNotConnected();
            HttpClient client = m_client ?? throw BadNotConnected();
            using Activity? activity = m_telemetry.StartActivity();
            using var cts = new CancellationTokenSource(OperationTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
            try
            {
                var content = new ByteArrayContent(
                    BinaryEncoder.EncodeMessage(request, context));
                content.Headers.ContentType = s_mediaTypeHeaderValue;
                if (EndpointDescription?.SecurityPolicyUri != null &&
                    !string.Equals(
                        EndpointDescription.SecurityPolicyUri,
                        SecurityPolicies.None,
                        StringComparison.Ordinal))
                {
                    content.Headers.Add(
                        Profiles.HttpsSecurityPolicyHeader,
                        EndpointDescription.SecurityPolicyUri);
                }

                HttpResponseMessage response = await client.PostAsync(
                    m_url,
                    content,
                    linkedCts.Token).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

#if NET6_0_OR_GREATER
                Stream responseContent = await response.Content.ReadAsStreamAsync(ct)
                    .ConfigureAwait(false);
#else
                Stream responseContent = await response.Content.ReadAsStreamAsync()
                    .ConfigureAwait(false);
#endif
                if (BinaryDecoder.DecodeMessage(responseContent, null, context)
                    is IServiceResponse serviceResponse)
                {
                    return serviceResponse;
                }
                throw ServiceResultException.Create(
                    StatusCodes.BadUnknownResponse,
                    "Response failed to decode");
            }
            catch (HttpRequestException hre)
            {
                if (hre.InnerException is WebException webex)
                {
                    StatusCode statusCode;
                    switch (webex.Status)
                    {
                        case WebExceptionStatus.Timeout:
                            statusCode = StatusCodes.BadRequestTimeout;
                            break;
                        case WebExceptionStatus.ConnectionClosed:
                        case WebExceptionStatus.ConnectFailure:
                            statusCode = StatusCodes.BadNotConnected;
                            break;
                        default:
                            statusCode = StatusCodes.BadUnknownResponse;
                            break;
                    }
                    m_logger.LogError(webex, "Exception sending HTTPS request.");
                    throw ServiceResultException.Create((uint)statusCode, webex.Message);
                }
                m_logger.LogError(hre, "Exception sending HTTPS request.");
                throw;
            }
            catch (OperationCanceledException e)
            {
                if (cts.IsCancellationRequested)
                {
                    m_logger.LogError(
                        e,
                        "Send request timed out after {OperationTimeout}ms.",
                        OperationTimeout);
                    throw ServiceResultException.Create(
                        StatusCodes.BadRequestTimeout,
                        "Https request timed out after {0}.",
                        OperationTimeout);
                }
                throw;
            }
            catch (Exception ex) when (ex is not ServiceResultException)
            {
                m_logger.LogError(ex, "Exception sending HTTPS request.");
                throw ServiceResultException.Create(
                    StatusCodes.BadUnknownResponse,
                    ex,
                    "Error sending request: {0}",
                    ex.Message);
            }
        }

        /// <summary>
        /// Override this method if you need to release resources.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !m_disposed)
            {
                m_disposed = true;
                Utils.SilentDispose(m_client);
                m_client = null;
            }
        }

        /// <summary>
        /// Save the settings for a connection.
        /// </summary>
        /// <param name="url">The server url.</param>
        /// <param name="settings">The settings for the transport channel.</param>
        /// <exception cref="ObjectDisposedException"></exception>
        private void SaveSettings(Uri url, TransportChannelSettings settings)
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(HttpsTransportChannel));
            }
            m_url = new Uri(url.ToString());
            // remove the opc. prefix, the https client can not handle it
            if (m_url.Scheme == Utils.UriSchemeOpcHttps)
            {
                m_url = new Uri(url.ToString()[4..]);
            }
            m_settings = settings;
            OperationTimeout = settings.Configuration.OperationTimeout;

            // initialize the quotas.
            m_quotas = new ChannelQuotas(new ServiceMessageContext(m_telemetry)
            {
                MaxArrayLength = m_settings.Configuration.MaxArrayLength,
                MaxByteStringLength = m_settings.Configuration.MaxByteStringLength,
                MaxMessageSize = m_settings.Configuration.MaxMessageSize,
                MaxStringLength = m_settings.Configuration.MaxStringLength,
                MaxEncodingNestingLevels = m_settings.Configuration.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = m_settings.Configuration.MaxDecoderRecoveries,
                NamespaceUris = m_settings.NamespaceUris,
                ServerUris = new StringTable(),
                Factory = m_settings.Factory
            })
            {
                MaxBufferSize = m_settings.Configuration.MaxBufferSize,
                MaxMessageSize = m_settings.Configuration.MaxMessageSize,
                ChannelLifetime = m_settings.Configuration.ChannelLifetime,
                SecurityTokenLifetime = m_settings.Configuration.SecurityTokenLifetime,
                CertificateValidator = settings.CertificateValidator
            };
        }

        /// <summary>
        /// Open the channel by creating the http client
        /// </summary>
        private void CreateHttpClient()
        {
            Debug.Assert(m_quotas != null);
            Debug.Assert(m_settings != null);
            try
            {
                m_logger.LogInformation("{ChannelType} Open {Url}.", nameof(HttpsTransportChannel), m_url);

                // auto validate server cert, if supported
                // if unsupported, the TLS server cert must be trusted by a root CA
                var handler = new HttpClientHandler
                {
                    ClientCertificateOptions = ClientCertificateOption.Manual,
                    AllowAutoRedirect = false,
                    // limit the number of concurrent connections, if supported
                    MaxConnectionsPerServer = kMaxConnectionsPerServer,
                    MaxRequestContentBufferSize = m_quotas!.MaxMessageSize
                };

                // send client certificate for servers that require TLS client authentication
                if (m_settings!.ClientCertificate != null)
                {
                    // prepare the server TLS certificate
                    X509Certificate2 clientCertificate = m_settings.ClientCertificate;
#if NETSTANDARD2_1 || NET472_OR_GREATER || NET5_0_OR_GREATER
                    try
                    {
                        // Create a copy of the certificate with the private key on platforms
                        // which default to the ephemeral KeySet. Also a new certificate must be reloaded.
                        // If the key fails to copy, its probably a non exportable key from the X509Store.
                        // Then we can use the original certificate, the private key is already in the key store.
                        clientCertificate = X509Utils.CreateCopyWithPrivateKey(
                            m_settings.ClientCertificate,
                            false);
                    }
                    catch (CryptographicException ce)
                    {
                        m_logger.LogError(ce, "Copy of the private key for https was denied");
                    }
#endif
                    handler.ClientCertificates.Add(clientCertificate);
                }

                Func<
                    HttpRequestMessage,
                    X509Certificate2,
                    X509Chain,
                    SslPolicyErrors,
                    bool
                >? serverCertificateCustomValidationCallback;

                try
                {
                    serverCertificateCustomValidationCallback = (_, cert, chain, _) =>
                    {
                        try
                        {
                            var validationChain = new X509Certificate2Collection();
                            if (chain != null && chain.ChainElements != null)
                            {
                                int i = 0;
                                m_logger.LogInformation(
                                    Utils.TraceMasks.Security,
                                    "{ChannelType} Validate server chain:",
                                    nameof(HttpsTransportChannel));
                                foreach (X509ChainElement element in chain.ChainElements)
                                {
                                    m_logger.LogInformation(
                                        Utils.TraceMasks.Security,
                                        "{Index}: {Certificate}",
                                        i,
                                        element.Certificate.AsLogSafeString());
                                    validationChain.Add(element.Certificate);
                                    i++;
                                }
                            }
                            else
                            {
                                m_logger.LogInformation(
                                    Utils.TraceMasks.Security,
                                    "{ChannelType} Validate Server Certificate: {Certificate}",
                                    cert.AsLogSafeString(),
                                    nameof(HttpsTransportChannel));
                                validationChain.Add(cert);
                            }

                            m_quotas.CertificateValidator?.ValidateAsync(validationChain, default).GetAwaiter().GetResult();

                            return true;
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogError(
                                ex,
                                "{ChannelType} Failed to validate certificate.",
                                nameof(HttpsTransportChannel));
                        }
                        return false;
                    };

                    handler.ServerCertificateCustomValidationCallback = serverCertificateCustomValidationCallback!;

                    m_logger.LogInformation(
                        "{ChannelType} ServerCertificate callback enabled.",
                        nameof(HttpsTransportChannel));
                }
                catch (PlatformNotSupportedException)
                {
                    // client may throw if not supported (e.g. UWP)
                    serverCertificateCustomValidationCallback = null;
                }

#pragma warning disable CA5400 // HttpClient is created without enabling CheckCertificateRevocationList
                m_client = new HttpClient(handler);
#pragma warning restore CA5400 // HttpClient is created without enabling CheckCertificateRevocationList
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Exception creating HTTPS Client.");
                throw;
            }
        }

        private ServiceResultException BadNotConnected()
        {
            return ServiceResultException.Create(
                StatusCodes.BadNotConnected,
                "{0} not open.",
                nameof(HttpsTransportChannel));
        }

        private Uri? m_url;
        private TransportChannelSettings? m_settings;
        private ChannelQuotas? m_quotas;
        private HttpClient? m_client;
        private bool m_disposed;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;

        private static readonly MediaTypeHeaderValue s_mediaTypeHeaderValue = new(
            "application/octet-stream");
    }
}
