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
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
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
        /// Initializes a new instance of the <see cref="HttpsTransportChannelFactory"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Optional HTTP client factory.</param>
        public HttpsTransportChannelFactory(IOpcUaHttpClientFactory? httpClientFactory = null)
        {
            m_httpClientFactory = httpClientFactory ?? DefaultOpcUaHttpClientFactory.Shared;
        }

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
            return new HttpsTransportChannel(UriScheme, telemetry, httpClientFactory: m_httpClientFactory);
        }

        private readonly IOpcUaHttpClientFactory? m_httpClientFactory;
    }

    /// <summary>
    /// Creates a new HttpsTransportChannel with ITransportChannel interface.
    /// The uri scheme opc.https is used.
    /// </summary>
    public class OpcHttpsTransportChannelFactory : ITransportChannelFactory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OpcHttpsTransportChannelFactory"/> class.
        /// </summary>
        /// <param name="httpClientFactory">Optional HTTP client factory.</param>
        public OpcHttpsTransportChannelFactory(IOpcUaHttpClientFactory? httpClientFactory = null)
        {
            m_httpClientFactory = httpClientFactory ?? DefaultOpcUaHttpClientFactory.Shared;
        }

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
            return new HttpsTransportChannel(UriScheme, telemetry, httpClientFactory: m_httpClientFactory);
        }

        private readonly IOpcUaHttpClientFactory? m_httpClientFactory;
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
        /// <param name="uriScheme">The uri scheme.</param>
        /// <param name="telemetry">The telemetry context.</param>
        /// <param name="timeProvider">Optional <see cref="TimeProvider"/>
        /// used for timeout scheduling. Defaults to
        /// <see cref="TimeProvider.System"/> when <c>null</c>.</param>
        /// <param name="httpClientFactory">Optional HTTP client factory.</param>
        public HttpsTransportChannel(
            string uriScheme,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null,
            IOpcUaHttpClientFactory? httpClientFactory = null)
        {
            UriScheme = uriScheme;
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<HttpsTransportChannel>();
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_httpClientFactory = httpClientFactory ?? DefaultOpcUaHttpClientFactory.Shared;
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
        public ChannelToken CurrentToken => new();

        /// <inheritdoc/>
        public byte[] ChannelThumbprint => [];

        /// <inheritdoc/>
        public byte[] ClientChannelCertificate { get; private set; } = [];

        /// <inheritdoc/>
        public byte[] ServerChannelCertificate { get; private set; } = [];

        /// <inheritdoc/>
        public event ChannelTokenActivatedEventHandler OnTokenActivated
        {
            add { }
            remove { }
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

            if (m_disposeClient)
            {
                m_client?.Dispose();
            }
            m_client = null;
            m_disposeClient = false;

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
            using CancellationTokenSource cts = m_timeProvider.CreateCancellationTokenSource(
                TimeSpan.FromMilliseconds(OperationTimeout));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ct);
            try
            {
                using var content = new ByteArrayContent(EncodeRequest(request, context));
                content.Headers.ContentType = MediaType;
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

                // Translate an HTTP 429/503 (e.g. from an AddHttpsRateLimiter
                // gate) into BadServerTooBusy, honoring the standard HTTP
                // Retry-After header so the adaptive reconnect policy can back
                // off without requesting OPC UA diagnostics.
                if ((int)response.StatusCode == 429 ||
                    response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    throw CreateServerTooBusyException(response);
                }

                response.EnsureSuccessStatusCode();

#if NET6_0_OR_GREATER
                Stream responseContent = await response.Content.ReadAsStreamAsync(ct)
                    .ConfigureAwait(false);
#else
                Stream responseContent = await response.Content.ReadAsStreamAsync()
                    .ConfigureAwait(false);
#endif
                IServiceResponse serviceResponse = DecodeResponse(responseContent, context);
                if (serviceResponse != null)
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
        /// Builds a <c>BadServerTooBusy</c> exception from an HTTP 429/503
        /// response, carrying the standard HTTP <c>Retry-After</c> hint (when
        /// present) as a machine-readable token the adaptive reconnect policy can
        /// honor without requesting OPC UA diagnostics.
        /// </summary>
        /// <param name="response">
        /// The throttled HTTP response.
        /// </param>
        /// <returns>
        /// The exception to throw.
        /// </returns>
        internal static ServiceResultException CreateServerTooBusyException(
            HttpResponseMessage response)
        {
            TimeSpan? retryAfter = GetRetryAfter(response);
            if (retryAfter.HasValue)
            {
                long retryAfterMs = (long)Math.Ceiling(retryAfter.Value.TotalMilliseconds);
                return new ServiceResultException(
                    new ServiceResult(
                        null,
                        StatusCodes.BadServerTooBusy,
                        new LocalizedText(
                            Utils.Format(
                                "The server is too busy (HTTP {0}). Retry after {1} ms.",
                                (int)response.StatusCode,
                                retryAfterMs)),
                        Utils.Format("RetryAfterMs={0}", retryAfterMs)));
            }

            return ServiceResultException.Create(
                StatusCodes.BadServerTooBusy,
                "The server is too busy (HTTP {0}).",
                (int)response.StatusCode);
        }

        /// <summary>
        /// Reads the standard HTTP <c>Retry-After</c> header (delay seconds or an
        /// HTTP date) from a response, if present.
        /// </summary>
        /// <param name="response">
        /// The HTTP response.
        /// </param>
        /// <returns>
        /// The retry-after delay, or <c>null</c> when absent.
        /// </returns>
        internal static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
            if (retryAfter == null)
            {
                return null;
            }

            if (retryAfter.Delta.HasValue)
            {
                return retryAfter.Delta.Value;
            }

            if (retryAfter.Date.HasValue)
            {
                TimeSpan delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
            }

            return null;
        }

        /// <summary>
        /// Override this method if you need to release resources.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !m_disposed)
            {
                m_disposed = true;
                if (m_disposeClient)
                {
                    m_client?.Dispose();
                }
                m_client = null;
                m_disposeClient = false;
                m_pinnedClientCertX509?.Dispose();
                m_pinnedClientCertX509 = null;
                m_pinnedClientCert?.Dispose();
                m_pinnedClientCert = null;
                m_settings?.ServerCertificate?.Dispose();
                m_settings?.ClientCertificate?.Dispose();
                m_settings?.ClientCertificateChain?.Dispose();
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
            OperationTimeout = settings.Configuration!.OperationTimeout;

            // initialize the quotas.
            m_quotas = new ChannelQuotas(new ServiceMessageContext(m_telemetry, m_settings.Factory!)
            {
                MaxArrayLength = m_settings.Configuration!.MaxArrayLength,
                MaxByteStringLength = m_settings.Configuration.MaxByteStringLength,
                MaxMessageSize = m_settings.Configuration.MaxMessageSize,
                MaxStringLength = m_settings.Configuration.MaxStringLength,
                MaxEncodingNestingLevels = m_settings.Configuration.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = m_settings.Configuration.MaxDecoderRecoveries,
                NamespaceUris = m_settings.NamespaceUris!,
                ServerUris = new StringTable()
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
        /// Creates the HTTP client used by the channel.
        /// </summary>
        private void CreateHttpClient()
        {
            Debug.Assert(m_quotas != null);
            Debug.Assert(m_settings != null);
            try
            {
                m_logger.LogInformation("{ChannelType} Open {Url}.", nameof(HttpsTransportChannel), m_url);

                if (CanUseHttpClientFactory())
                {
                    string clientName = ReferenceEquals(m_httpClientFactory, DefaultOpcUaHttpClientFactory.Shared)
                        ? m_url!.AbsoluteUri
                        : OpcUaHttpClientDefaults.ClientName;
                    m_client = m_httpClientFactory!.CreateClient(clientName);
                    m_disposeClient = false;
                    return;
                }

                if (m_httpClientFactory != null &&
                    !ReferenceEquals(m_httpClientFactory, DefaultOpcUaHttpClientFactory.Shared) &&
                    m_quotas?.CertificateValidator != null)
                {
                    // Tell operators that the DI-registered HttpClient pipeline
                    // (e.g. AddStandardResilienceHandler) is intentionally NOT
                    // applied to this channel — see CanUseHttpClientFactory.
                    m_logger.LogWarning(
                        "{ChannelType}: Bypassing IOpcUaHttpClientFactory because an OPC UA " +
                        "CertificateValidator is configured for this channel; using a direct " +
                        "HttpClient with OPC UA TLS server-cert validation and the OPC UA " +
                        "client instance certificate for mTLS. The named HttpClient pipeline " +
                        "is NOT applied to this OPC UA HTTPS channel.",
                        nameof(HttpsTransportChannel));
                }

                m_client = CreateDirectHttpClient();
                m_disposeClient = true;
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Exception creating HTTPS Client.");
                throw;
            }
        }

        private bool CanUseHttpClientFactory()
        {
            // SECURITY: The factory path returns a named HttpClient configured
            // purely with standard HTTP middleware (e.g. Polly resilience). Its
            // underlying HttpClientHandler is NOT exposed for us to attach the
            // OPC UA ServerCertificateCustomValidationCallback or the OPC UA
            // client application instance certificate to. Consequently a
            // channel that needs OPC UA-specific server-certificate validation
            // (validation against the OPC UA trust list / ICertificateValidatorEx)
            // and OPC UA mutual TLS MUST go through CreateDirectHttpClient.
            // Bypassing the factory in that case is REQUIRED for security:
            // otherwise the channel would silently fall back to the OS trust
            // chain only — dropping the OPC UA trust list — and would never
            // send the OPC UA client cert for mTLS.
            //
            // The DefaultOpcUaHttpClientFactory.Shared instance is the
            // well-known sentinel used by direct (non-DI) consumers. It
            // ALWAYS triggers the CreateDirectHttpClient path so the OPC UA
            // security wiring runs.
            if (ReferenceEquals(m_httpClientFactory, DefaultOpcUaHttpClientFactory.Shared))
            {
                return false;
            }
            if (m_quotas?.CertificateValidator != null)
            {
                return false;
            }
            return true;
        }

        private HttpClient CreateDirectHttpClient()
        {
            // auto validate server cert, if supported
            // if unsupported, the TLS server cert must be trusted by a root CA
            HttpClientHandler? handler = null;
            try
            {
                handler = new HttpClientHandler
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
                    // prepare the client TLS certificate. AddRef so the
                    // channel owns the cert independent of the source
                    // (m_settings.ClientCertificate is a borrowed reference
                    // owned by the application configuration).
                    Certificate clientCertificate = m_settings.ClientCertificate.AddRef();
#if NETSTANDARD2_1 || NET472_OR_GREATER || NET5_0_OR_GREATER
                    try
                    {
                        // Create a copy of the certificate with the private key on platforms
                        // which default to the ephemeral KeySet. Also a new certificate must be reloaded.
                        // If the key fails to copy, its probably a non exportable key from the X509Store.
                        // Then we can use the original certificate, the private key is already in the key store.
                        using Certificate copy = X509Utils.CreateCopyWithPrivateKey(clientCertificate, false);
                        if (!ReferenceEquals(copy, clientCertificate))
                        {
                            clientCertificate.Dispose();
                            // Take an owned handle over the copy's core; the
                            // 'using copy' handle is released at block end, so
                            // capturing AddRef()'s result (rather than aliasing
                            // 'copy' and discarding the AddRef) keeps the core
                            // alive without leaking a handle.
                            clientCertificate = copy.AddRef();
                        }
                    }
                    catch (CryptographicException ce)
                    {
                        m_logger.LogError(ce, "Copy of the private key for https was denied");
                    }
#endif
                    // pin the cert for the lifetime of the channel so the
                    // OS-level private key handle backing the X509Certificate2
                    // we hand to HttpClientHandler cannot be invalidated by a
                    // concurrent cert reload elsewhere in the process.
                    m_pinnedClientCert?.Dispose();
                    m_pinnedClientCert = clientCertificate;
                    m_pinnedClientCertX509?.Dispose();
                    m_pinnedClientCertX509 = clientCertificate.AsX509Certificate2();

                    handler.ClientCertificates.Add(m_pinnedClientCertX509);
                    ClientChannelCertificate = clientCertificate.RawData;
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
                                        element.Certificate.Subject);
                                    validationChain.Add(element.Certificate);
                                    i++;
                                }
                            }
                            else
                            {
                                m_logger.LogInformation(
                                    Utils.TraceMasks.Security,
                                    "{ChannelType} Validate Server Certificate: {Certificate}",
                                    cert.Subject,
                                    nameof(HttpsTransportChannel));
                                validationChain.Add(cert);
                            }

                            using var validationCollection = CertificateCollection.From(validationChain);
                            if (m_quotas.CertificateValidator != null)
                            {
                                // CA2025: task awaited via GetAwaiter().GetResult(); the disposable's
                                // using scope extends past the await.
#pragma warning disable CA2025
                                CertificateValidationResult validationResult = m_quotas.CertificateValidator
                                    .ValidateAsync(validationCollection, ct: default)
                                    .GetAwaiter()
                                    .GetResult();
#pragma warning restore CA2025
                                if (!validationResult.IsValid)
                                {
                                    throw new ServiceResultException(validationResult.StatusCode);
                                }
                            }

                            // When no OPC UA certificate validator is configured on this
                            // channel (the pre-trust HTTPS discovery / GetEndpoints flow),
                            // the TLS certificate is accepted here and validated at the UA
                            // layer once a concrete endpoint is selected for a session.
                            ServerChannelCertificate = cert.RawData;
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

                // CA5400: revocation (CRL) is enforced by the UA CertificateValidator
                // in the server-certificate callback above, consistent with the rest
                // of the stack, so TLS-layer revocation on the HttpClient handler is
                // intentionally left disabled to avoid duplicate / inconsistent checks.
#pragma warning disable CA5400 // HttpClient is created without enabling CheckCertificateRevocationList
                var client = new HttpClient(handler);
#pragma warning restore CA5400 // HttpClient is created without enabling CheckCertificateRevocationList
                handler = null; // ownership transferred to HttpClient
                return client;
            }
            finally
            {
                handler?.Dispose();
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

        /// <summary>
        /// Media type written to the HTTP <c>Content-Type</c> header on
        /// outbound requests. Defaults to <c>application/octet-stream</c>
        /// for the binary profile (Part 6 §7.4.4); switches to
        /// <c>application/opcua+uajson</c> when the endpoint advertises
        /// the JSON profile (§7.4.5).
        /// </summary>
        protected virtual MediaTypeHeaderValue MediaType =>
            IsJsonProfile ? s_jsonMediaTypeHeaderValue : s_binaryMediaTypeHeaderValue;

        /// <summary>
        /// Encodes a service request to the wire bytes posted in the HTTP
        /// body. Binary (default) uses <see cref="BinaryEncoder"/>; the
        /// JSON profile uses <see cref="JsonEncoder"/> with
        /// <see cref="JsonEncoderOptions.Compact"/> (Part 6 §5.4.9).
        /// </summary>
        protected virtual byte[] EncodeRequest(
            IServiceRequest request,
            IServiceMessageContext context)
        {
            if (IsJsonProfile)
            {
                using var memory = new MemoryStream();
                using (var encoder = new JsonEncoder(memory, context, JsonEncoderOptions.Compact))
                {
                    encoder.EncodeMessage(request, request.TypeId);
                    encoder.Close();
                }
                return memory.ToArray();
            }
            return BinaryEncoder.EncodeMessage(request, context);
        }

        /// <summary>
        /// Decodes a service response from the HTTP body stream. Binary
        /// (default) uses <see cref="BinaryDecoder"/>; the JSON profile
        /// uses <see cref="JsonDecoder"/>.
        /// </summary>
        protected virtual IServiceResponse DecodeResponse(
            Stream stream,
            IServiceMessageContext context)
        {
            if (IsJsonProfile)
            {
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return JsonDecoder.DecodeMessage<IServiceResponse>(memory.ToArray(), context);
            }
            return BinaryDecoder.DecodeMessage<IServiceResponse>(stream, context);
        }

        /// <summary>
        /// True when the endpoint advertises <see cref="Profiles.HttpsJsonTransport"/>.
        /// </summary>
        private bool IsJsonProfile =>
            string.Equals(
                m_settings?.Description?.TransportProfileUri,
                Profiles.HttpsJsonTransport,
                StringComparison.Ordinal);

        private Certificate? m_pinnedClientCert;
        private X509Certificate2? m_pinnedClientCertX509;
        private bool m_disposeClient;
        private bool m_disposed;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private readonly IOpcUaHttpClientFactory? m_httpClientFactory;

        private static readonly MediaTypeHeaderValue s_binaryMediaTypeHeaderValue = new(
            "application/octet-stream");

        private static readonly MediaTypeHeaderValue s_jsonMediaTypeHeaderValue = new(
            Profiles.OpcUaJsonContentType);
    }
}
