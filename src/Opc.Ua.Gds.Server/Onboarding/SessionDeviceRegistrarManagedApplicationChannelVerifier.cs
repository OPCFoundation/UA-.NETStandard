/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Verifies managed application credentials through certificate-bound fresh sessions.
    /// </summary>
    public sealed class SessionDeviceRegistrarManagedApplicationChannelVerifier :
        IDeviceRegistrarManagedApplicationChannelVerifier
    {
        /// <summary>
        /// Creates a verifier using the host's client certificate policy and a pre-activation certificate-bound
        /// session factory.
        /// </summary>
        /// <param name="configuration">The initialized client configuration and certificate policy.</param>
        /// <param name="sessionFactory">
        /// The factory used to validate the pinned application certificate before activation.
        /// </param>
        public SessionDeviceRegistrarManagedApplicationChannelVerifier(
            ApplicationConfiguration configuration,
            ISessionFactory sessionFactory)
        {
            configuration.ThrowIfNull(nameof(configuration));
            m_configuration = new ApplicationConfiguration(configuration)
            {
                ClientConfiguration = configuration.ClientConfiguration ?? new ClientConfiguration(),
                CertificateManager = configuration.CertificateManager
            };
            sessionFactory.ThrowIfNull(nameof(sessionFactory));
            if (!HasExplicitPinnedCertificateCapability(sessionFactory))
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError,
                    "Managed application proof requires the default session factory or an explicitly declared " +
                    "pinned-certificate capability.");
            }
            m_sessionFactory = sessionFactory;
        }

        /// <summary>
        /// Accepts the built-in factory or a factory that declares the capability outside its inheritance chain.
        /// </summary>
        private static bool HasExplicitPinnedCertificateCapability(ISessionFactory sessionFactory)
        {
            Type factoryType = sessionFactory.GetType();
            if (factoryType == typeof(DefaultSessionFactory))
            {
                return true;
            }
            if (!typeof(IPinnedServerCertificateSessionFactory).IsAssignableFrom(factoryType))
            {
                return false;
            }
            for (Type? baseType = factoryType.BaseType; baseType != null; baseType = baseType.BaseType)
            {
                if (typeof(IPinnedServerCertificateSessionFactory).IsAssignableFrom(baseType))
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        /// <param name="application">The application identity being verified.</param>
        /// <param name="target">The host-authoritative endpoint, certificate and user identity.</param>
        /// <param name="expectedCertificate">The public leaf certificate expected for the application identity.</param>
        /// <param name="cancellationToken">A token that cancels session creation and verification.</param>
        public async ValueTask VerifyAsync(
            ApplicationDescription application,
            DeviceRegistrarManagedApplicationTarget target,
            ByteString expectedCertificate,
            CancellationToken cancellationToken = default)
        {
            application.ThrowIfNull(nameof(application));
            target.ThrowIfNull(nameof(target));
            cancellationToken.ThrowIfCancellationRequested();
            string? applicationUri = application.ApplicationUri;
            if (!Uri.TryCreate(applicationUri, UriKind.Absolute, out _) ||
                expectedCertificate.IsEmpty ||
                target.Endpoint == null ||
                target.UserIdentity == null ||
                target.DiscoveryUrls.IsEmpty)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }

            expectedCertificate = ByteString.From(expectedCertificate.Span);
            ByteString httpsServerCertificate = ByteString.From(target.HttpsServerCertificate.Span);
            ConfiguredEndpoint endpoint = SnapshotEndpoint(target.Endpoint);
            if (endpoint.ReverseConnect?.Enabled == true)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "Managed application proof requires a reverse-connect session provider for an enabled " +
                    "reverse target; this verifier does not substitute a forward connection.");
            }
            endpoint.Description.ServerCertificate = expectedCertificate;
            EndpointDescription offered = endpoint.Description;
            string? securityPolicyUri = offered.SecurityPolicyUri;
            string? endpointUrl = offered.EndpointUrl;
            if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out _))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "The selected endpoint URL must be absolute.");
            }
            string transportProfile = TransportProfileIdentity.GetEffective(
                offered.TransportProfileUri,
                endpointUrl ?? string.Empty,
                endpoint.Configuration?.UseBinaryEncoding ?? true);
            bool isUaTcp = transportProfile == Profiles.UaTcpTransport;
            bool isHttps = transportProfile is Profiles.HttpsBinaryTransport or Profiles.HttpsJsonTransport;
            if (!isUaTcp && !isHttps)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "Managed application proof requires a UA-TCP or HTTPS channel with authenticated peer identity.");
            }
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                !IsSupportedSecurityPolicy(securityPolicyUri) ||
                string.IsNullOrEmpty(endpointUrl) ||
                !Uri.TryCreate(endpointUrl, UriKind.Absolute, out _) ||
                !string.Equals(
                    offered.Server?.ApplicationUri, applicationUri, StringComparison.Ordinal))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The selected endpoint is not a valid encrypted endpoint for the managed application.");
            }
            bool supportedSecurityMode = isHttps
                ? offered.SecurityMode == MessageSecurityMode.Sign
                : offered.SecurityMode == MessageSecurityMode.SignAndEncrypt;
            if (!supportedSecurityMode)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The selected endpoint uses a security mode that is not supported for its transport.");
            }
            Uri selectedEndpointUrl = new(endpointUrl, UriKind.Absolute);
            bool supportedTransportUrl = isHttps
                ? selectedEndpointUrl.Scheme is Utils.UriSchemeHttps or Utils.UriSchemeOpcHttps
                : selectedEndpointUrl.Scheme == Utils.UriSchemeOpcTcp;
            if (!supportedTransportUrl)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "The endpoint URL does not match its certificate-binding transport profile.");
            }
            var selectedEndpoint = new SelectedEndpoint(
                endpointUrl,
                securityPolicyUri,
                offered.SecurityMode,
                applicationUri,
                expectedCertificate);
            for (int index = 0; index < target.DiscoveryUrls.Count; index++)
            {
                if (!Uri.TryCreate(target.DiscoveryUrls[index], UriKind.Absolute, out _))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
            }

            ITransportChannel? channel = null;
            ISession? session = null;
            bool sessionOwnsChannel = false;
            bool sessionOpened = false;
            bool sessionCleanupFailed = false;
            Exception? failure = null;
            try
            {
                channel = await m_sessionFactory.CreateChannelAsync(
                    m_configuration,
                    null!,
                    endpoint,
                    updateBeforeConnect: false,
                    checkDomain: true,
                    cancellationToken).ConfigureAwait(false) ??
                    throw new ServiceResultException(StatusCodes.BadNotConnected);
                ValidateEndpoint(channel.EndpointDescription, selectedEndpoint);
                if (isHttps)
                {
                    if (channel is not IHttpsServerCertificateValidation httpsChannel)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNotSupported,
                            "The HTTPS channel cannot enforce TLS authentication on every connection.");
                    }
                    httpsChannel.RequireServerCertificateValidation(httpsServerCertificate);
                }
                else
                {
                    ValidateChannelCertificate(channel.ServerChannelCertificate, expectedCertificate);
                }

                session = m_sessionFactory.Create(channel, m_configuration, endpoint);
                if (session == null)
                {
                    throw new ServiceResultException(StatusCodes.BadNotConnected);
                }
                sessionOwnsChannel = ReferenceEquals(session.TransportChannel, channel);
                if (!sessionOwnsChannel)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "The session factory did not retain the verified secure channel.");
                }
                if (session is not IExpectedServerApplicationUriSession applicationUriSession)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "Managed application proof requires a session that can validate the expected application URI " +
                        "before activating credentials.");
                }
                applicationUriSession.RequireServerApplicationUri(applicationUri);
                ValidateEndpoint(session.ConfiguredEndpoint.Description, selectedEndpoint);
                ValidateEndpoint(session.TransportChannel.EndpointDescription, selectedEndpoint);
                if (!isHttps)
                {
                    ValidateChannelCertificate(session.TransportChannel.ServerChannelCertificate, expectedCertificate);
                }

                await session.OpenAsync(
                    m_configuration.ApplicationName ??
                        nameof(SessionDeviceRegistrarManagedApplicationChannelVerifier),
                    60000,
                    target.UserIdentity,
                    default,
                    checkDomain: true,
                    closeChannel: true,
                    cancellationToken).ConfigureAwait(false);
                sessionOpened = true;
                ValidateEndpoint(session.ConfiguredEndpoint.Description, selectedEndpoint);
                ValidateEndpoint(session.TransportChannel.EndpointDescription, selectedEndpoint);
                if (!isHttps)
                {
                    ValidateChannelCertificate(session.TransportChannel.ServerChannelCertificate, expectedCertificate);
                }
            }
            catch (Exception error)
            {
                failure = error;
            }

            if (session != null)
            {
                try
                {
                    if (sessionOpened)
                    {
                        await SessionDeviceRegistrarPushConnectionProvider.CloseAsync(session).ConfigureAwait(false);
                    }
                    else
                    {
                        await session.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception cleanupError)
                {
                    sessionCleanupFailed = true;
                    failure = failure == null ? cleanupError : new AggregateException(failure, cleanupError);
                }
            }
            if (channel != null && (!sessionOwnsChannel || sessionCleanupFailed))
            {
                try
                {
                    channel.Dispose();
                }
                catch (Exception cleanupError)
                {
                    failure = failure == null ? cleanupError : new AggregateException(failure, cleanupError);
                }
            }
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private static void ValidateEndpoint(
            EndpointDescription actual,
            SelectedEndpoint selected)
        {
            if (actual.SecurityMode != selected.SecurityMode ||
                !string.Equals(actual.SecurityPolicyUri, selected.SecurityPolicyUri, StringComparison.Ordinal) ||
                !string.Equals(actual.EndpointUrl, selected.EndpointUrl, StringComparison.Ordinal) ||
                !string.Equals(actual.Server?.ApplicationUri, selected.ApplicationUri, StringComparison.Ordinal) ||
                actual.ServerCertificate != selected.ServerCertificate)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The channel endpoint differs from the selected managed application endpoint.");
            }
        }

        private static void ValidateChannelCertificate(byte[]? certificateBytes, ByteString expectedCertificate)
        {
            if (certificateBytes == null || certificateBytes.Length == 0)
            {
                throw new ServiceResultException(StatusCodes.BadCertificateInvalid);
            }
            using CertificateCollection chain =
                DefaultCertificateFactory.Instance.ParseChainBlob(certificateBytes.AsMemory());
            if (chain.Count == 0 ||
                ByteString.From(chain[0].RawData.AsSpan()) != expectedCertificate)
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "The actual UA-TCP channel certificate differs from the installed application certificate.");
            }
        }

        private readonly record struct SelectedEndpoint(
            string EndpointUrl,
            string SecurityPolicyUri,
            MessageSecurityMode SecurityMode,
            string ApplicationUri,
            ByteString ServerCertificate);

        /// <summary>
        /// Copies standard endpoint, application, token and quota fields into concrete owned instances.
        /// Provider Clone overrides are never called. Deferred names require the shared safe-copy contract.
        /// </summary>
        internal static ConfiguredEndpoint SnapshotEndpoint(ConfiguredEndpoint endpoint)
        {
            endpoint.ThrowIfNull(nameof(endpoint));
            if (endpoint.UserIdentity != null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "Managed endpoint snapshots require the target's explicit user identity.");
            }
            EndpointDescription description = SnapshotDescription(endpoint.Description);
            ArrayOf<EndpointDescription> sourceDiscoveryEndpoints = endpoint.DiscoveryEndpoints;
            var discoveryEndpoints = new EndpointDescription[sourceDiscoveryEndpoints.Count];
            for (int index = 0; index < discoveryEndpoints.Length; index++)
            {
                discoveryEndpoints[index] = SnapshotDescription(sourceDiscoveryEndpoints[index]);
            }
            EndpointConfiguration quotas = endpoint.Configuration ??
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "The selected endpoint configuration is missing.");
            var configuration = new EndpointConfiguration
            {
                OperationTimeout = quotas.OperationTimeout,
                UseBinaryEncoding = quotas.UseBinaryEncoding,
                MaxStringLength = quotas.MaxStringLength,
                MaxByteStringLength = quotas.MaxByteStringLength,
                MaxArrayLength = quotas.MaxArrayLength,
                MaxMessageSize = quotas.MaxMessageSize,
                MaxBufferSize = quotas.MaxBufferSize,
                ChannelLifetime = quotas.ChannelLifetime,
                SecurityTokenLifetime = quotas.SecurityTokenLifetime,
                MaxEncodingNestingLevels = quotas.MaxEncodingNestingLevels,
                MaxDecoderRecoveries = quotas.MaxDecoderRecoveries
            };
            ArrayOf<XmlElement> sourceExtensions = endpoint.Extensions;
            ArrayOf<XmlElement> extensions = sourceExtensions.IsNull
                ? default
                : sourceExtensions.Span.ToArray().ToArrayOf();
            ReverseConnectEndpoint? reverse = endpoint.ReverseConnect;
            var snapshot = new ConfiguredEndpoint(null, description, configuration)
            {
                SelectedUserTokenPolicyIndex = endpoint.SelectedUserTokenPolicyIndex,
                BinaryEncodingSupport = endpoint.BinaryEncodingSupport,
                UpdateBeforeConnect = false,
                ReverseConnect = reverse == null ? null : new ReverseConnectEndpoint
                {
                    Enabled = reverse.Enabled,
                    ServerUri = reverse.ServerUri,
                    Thumbprint = reverse.Thumbprint
                },
                Extensions = extensions,
                DiscoveryEndpoints = sourceDiscoveryEndpoints.IsNull ? default : discoveryEndpoints.ToArrayOf()
            };
            EndpointConfiguration snapshotConfiguration = snapshot.Configuration ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            snapshotConfiguration.UseBinaryEncoding = configuration.UseBinaryEncoding;
            return snapshot;
        }

        /// <summary>
        /// Copies selected and retained discovery descriptions without dispatching provider Clone overrides.
        /// </summary>
        private static EndpointDescription SnapshotDescription(EndpointDescription source)
        {
            if (source == null)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            }
            ApplicationDescription application = source.Server ??
                throw new ServiceResultException(StatusCodes.BadConfigurationError);
            LocalizedText applicationName = application.ApplicationName;
            if (applicationName.Translations != null ||
                !applicationName.TranslationInfo.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "Deferred endpoint names require the shared LocalizedText safe-copy contract.");
            }
            ArrayOf<UserTokenPolicy> sourcePolicies = source.UserIdentityTokens;
            ArrayOf<string> sourceDiscoveryUrls = application.DiscoveryUrls;
            ByteString sourceCertificate = source.ServerCertificate;
            Uri? sourceProxyUrl = source.ProxyUrl;
            var policies = new UserTokenPolicy[sourcePolicies.Count];
            for (int index = 0; index < policies.Length; index++)
            {
                UserTokenPolicy policy = sourcePolicies[index] ??
                    throw new ServiceResultException(StatusCodes.BadConfigurationError);
                policies[index] = new UserTokenPolicy
                {
                    PolicyId = policy.PolicyId,
                    TokenType = policy.TokenType,
                    IssuedTokenType = policy.IssuedTokenType,
                    IssuerEndpointUrl = policy.IssuerEndpointUrl,
                    SecurityPolicyUri = policy.SecurityPolicyUri
                };
            }
            return new EndpointDescription
            {
                EndpointUrl = source.EndpointUrl,
                Server = new ApplicationDescription
                {
                    ApplicationUri = application.ApplicationUri,
                    ProductUri = application.ProductUri,
                    ApplicationName = new LocalizedText(
                        applicationName.Locale, applicationName.Text),
                    ApplicationType = application.ApplicationType,
                    GatewayServerUri = application.GatewayServerUri,
                    DiscoveryProfileUri = application.DiscoveryProfileUri,
                    DiscoveryUrls = sourceDiscoveryUrls.IsNull
                        ? default
                        : sourceDiscoveryUrls.Span.ToArray().ToArrayOf()
                },
                ServerCertificate = sourceCertificate.IsNull
                    ? default
                    : ByteString.From(sourceCertificate.Span),
                SecurityMode = source.SecurityMode,
                SecurityPolicyUri = source.SecurityPolicyUri,
                UserIdentityTokens = sourcePolicies.IsNull ? default : policies.ToArrayOf(),
                TransportProfileUri = source.TransportProfileUri,
                SecurityLevel = source.SecurityLevel,
                ProxyUrl = sourceProxyUrl == null
                    ? null
                    : new Uri(sourceProxyUrl.OriginalString, UriKind.RelativeOrAbsolute)
            };
        }

        /// <summary>
        /// Restricts managed application proof to the stack's supported SHA-256 security policies.
        /// </summary>
        private static bool IsSupportedSecurityPolicy(string securityPolicyUri)
        {
            return securityPolicyUri is SecurityPolicies.Basic256Sha256 or
                SecurityPolicies.Aes128_Sha256_RsaOaep or
                SecurityPolicies.Aes256_Sha256_RsaPss;
        }

        /// <summary>The initialized client-side trust configuration.</summary>
        private readonly ApplicationConfiguration m_configuration;

        /// <summary>The host's session factory, which must create a new owned session per call.</summary>
        private readonly ISessionFactory m_sessionFactory;
    }
}
