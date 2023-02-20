/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// A class that builds a configuration for a UA application.
    /// </summary>
    public class ApplicationConfigurationBuilder :
        IApplicationConfigurationBuilder
    {
        #region ctor
        /// <summary>
        /// Create the application instance builder.
        /// </summary>
        public ApplicationConfigurationBuilder(ApplicationInstance applicationInstance)
        {
            ApplicationInstance = applicationInstance;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The application instance used to build the configuration.
        /// </summary>
        public ApplicationInstance ApplicationInstance { get; private set; }
        /// <summary>
        /// The application configuration.
        /// </summary>
        public ApplicationConfiguration ApplicationConfiguration => ApplicationInstance.ApplicationConfiguration;
        #endregion

        #region Public Methods
        /// <inheritdoc/>
        public IApplicationConfigurationBuilderClientSelected AsClient()
        {
            switch (ApplicationInstance.ApplicationType)
            {
                case ApplicationType.Client:
                case ApplicationType.ClientAndServer:
                    break;
                case ApplicationType.Server:
                    ApplicationInstance.ApplicationType =
                        m_typeSelected ? ApplicationType.ClientAndServer : ApplicationType.Client;
                    break;
                default:
                    throw new ArgumentException("Invalid application type for client.");
            }

            m_typeSelected = true;

            ApplicationConfiguration.ClientConfiguration = new ClientConfiguration();

            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderSecurityOptions AddSecurityConfiguration(
            string subjectName,
            string pkiRoot = null,
            string appRoot = null,
            string rejectedRoot = null
            )
        {
            pkiRoot = DefaultPKIRoot(pkiRoot);
            appRoot = appRoot == null ? pkiRoot : DefaultPKIRoot(appRoot);
            rejectedRoot = rejectedRoot == null ? pkiRoot : DefaultPKIRoot(rejectedRoot);
            var appStoreType = CertificateStoreIdentifier.DetermineStoreType(appRoot);
            var pkiRootType = CertificateStoreIdentifier.DetermineStoreType(pkiRoot);
            var rejectedRootType = CertificateStoreIdentifier.DetermineStoreType(rejectedRoot);
            ApplicationConfiguration.SecurityConfiguration = new SecurityConfiguration {
                // app cert store
                ApplicationCertificate = new CertificateIdentifier() {
                    StoreType = appStoreType,
                    StorePath = DefaultCertificateStorePath(TrustlistType.Application, appRoot),
                    SubjectName = Utils.ReplaceDCLocalhost(subjectName)
                },
                // App trusted & issuer
                TrustedPeerCertificates = new CertificateTrustList() {
                    StoreType = pkiRootType,
                    StorePath = DefaultCertificateStorePath(TrustlistType.Trusted, pkiRoot)
                },
                TrustedIssuerCertificates = new CertificateTrustList() {
                    StoreType = pkiRootType,
                    StorePath = DefaultCertificateStorePath(TrustlistType.Issuer, pkiRoot)
                },
                // Https trusted & issuer
                TrustedHttpsCertificates = new CertificateTrustList() {
                    StoreType = pkiRootType,
                    StorePath = DefaultCertificateStorePath(TrustlistType.TrustedHttps, pkiRoot)
                },
                HttpsIssuerCertificates = new CertificateTrustList() {
                    StoreType = pkiRootType,
                    StorePath = DefaultCertificateStorePath(TrustlistType.IssuerHttps, pkiRoot)
                },
                // User trusted & issuer
                TrustedUserCertificates = new CertificateTrustList() {
                    StoreType = pkiRootType,
                    StorePath = DefaultCertificateStorePath(TrustlistType.TrustedUser, pkiRoot)
                },
                UserIssuerCertificates = new CertificateTrustList() {
                    StoreType = pkiRootType,
                    StorePath = DefaultCertificateStorePath(TrustlistType.IssuerUser, pkiRoot)
                },
                // rejected store
                RejectedCertificateStore = new CertificateTrustList() {
                    StoreType = rejectedRootType,
                    StorePath = DefaultCertificateStorePath(TrustlistType.Rejected, rejectedRoot)
                },
                // ensure secure default settings
                AutoAcceptUntrustedCertificates = false,
                AddAppCertToTrustedStore = false,
                RejectSHA1SignedCertificates = true,
                RejectUnknownRevocationStatus = true,
                SuppressNonceValidationErrors = false,
                SendCertificateChain = true,
                MinimumCertificateKeySize = CertificateFactory.DefaultKeySize
            };

            return this;
        }

        /// <inheritdoc/>
        public async Task<ApplicationConfiguration> Create()
        {
            // sanity checks
            if (ApplicationInstance.ApplicationType == ApplicationType.Server ||
                ApplicationInstance.ApplicationType == ApplicationType.ClientAndServer)
            {
                if (ApplicationConfiguration.ServerConfiguration == null) throw new ArgumentException("ApplicationType Server is not configured.");
            }
            if (ApplicationInstance.ApplicationType == ApplicationType.Client ||
                ApplicationInstance.ApplicationType == ApplicationType.ClientAndServer)
            {
                if (ApplicationConfiguration.ClientConfiguration == null) throw new ArgumentException("ApplicationType Client is not configured.");
            }

            // ensure for a user token policy
            if (ApplicationConfiguration.ServerConfiguration?.UserTokenPolicies.Count == 0)
            {
                ApplicationConfiguration.ServerConfiguration.UserTokenPolicies.Add(new UserTokenPolicy(UserTokenType.Anonymous));
            }

            // ensure for secure transport profiles
            if (ApplicationConfiguration.ServerConfiguration?.SecurityPolicies.Count == 0)
            {
                AddSecurityPolicies();
            }

            ApplicationConfiguration.TraceConfiguration?.ApplySettings();

            await ApplicationConfiguration.Validate(ApplicationInstance.ApplicationType).ConfigureAwait(false);

            await ApplicationConfiguration.CertificateValidator.
                Update(ApplicationConfiguration.SecurityConfiguration).ConfigureAwait(false);

            return ApplicationConfiguration;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerSelected AsServer(
            string[] baseAddresses,
            string[] alternateBaseAddresses = null)
        {
            switch (ApplicationInstance.ApplicationType)
            {
                case ApplicationType.Client:
                    ApplicationInstance.ApplicationType =
                        m_typeSelected ? ApplicationType.ClientAndServer : ApplicationType.Server;
                    break;
                case ApplicationType.Server:
                case ApplicationType.ClientAndServer: break;
                default:
                    throw new ArgumentException("Invalid application type for server.");
            }

            m_typeSelected = true;

            // configure a server
            var serverConfiguration = new ServerConfiguration();

            // by default disable LDS registration
            serverConfiguration.MaxRegistrationInterval = 0;

            // base addresses
            foreach (var baseAddress in baseAddresses)
            {
                serverConfiguration.BaseAddresses.Add(Utils.ReplaceLocalhost(baseAddress));
            }

            // alternate base addresses
            if (alternateBaseAddresses != null)
            {
                foreach (var alternateBaseAddress in alternateBaseAddresses)
                {
                    serverConfiguration.AlternateBaseAddresses.Add(Utils.ReplaceLocalhost(alternateBaseAddress));
                }
            }

            // add container for policies
            serverConfiguration.SecurityPolicies = new ServerSecurityPolicyCollection();

            // add user token policy container and Anonymous
            serverConfiguration.UserTokenPolicies = new UserTokenPolicyCollection();

            ApplicationConfiguration.ServerConfiguration = serverConfiguration;

            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerSelected AddUnsecurePolicyNone(bool addPolicy = true)
        {
            if (addPolicy)
            {
                var policies = ApplicationConfiguration.ServerConfiguration.SecurityPolicies;
                InternalAddPolicy(policies, MessageSecurityMode.None, SecurityPolicies.None);
            }
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerSelected AddSignPolicies(bool addPolicies = true)
        {
            if (addPolicies)
            {
                AddSecurityPolicies(true, false, false);
            }
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerSelected AddSignAndEncryptPolicies(bool addPolicies = true)
        {
            if (addPolicies)
            {
                AddSecurityPolicies(false, false, false);
            }
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerSelected AddPolicy(MessageSecurityMode securityMode, string securityPolicy)
        {
            if (SecurityPolicies.GetDisplayName(securityPolicy) == null) throw new ArgumentException("Unknown security policy", nameof(securityPolicy));
            if (securityMode == MessageSecurityMode.None || securityPolicy.Equals(SecurityPolicies.None)) throw new ArgumentException("Use AddUnsecurePolicyNone to add no security policy.");
            InternalAddPolicy(ApplicationConfiguration.ServerConfiguration.SecurityPolicies, securityMode, securityPolicy);
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerSelected AddUserTokenPolicy(UserTokenType userTokenType)
        {
            ApplicationConfiguration.ServerConfiguration.UserTokenPolicies.Add(new UserTokenPolicy(userTokenType));
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerSelected AddUserTokenPolicy(UserTokenPolicy userTokenPolicy)
        {
            if (userTokenPolicy == null) throw new ArgumentNullException(nameof(userTokenPolicy));
            ApplicationConfiguration.ServerConfiguration.UserTokenPolicies.Add(userTokenPolicy);
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderSecurityOptions SetAutoAcceptUntrustedCertificates(bool autoAccept)
        {
            ApplicationConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates = autoAccept;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderSecurityOptions SetAddAppCertToTrustedStore(bool addToTrustedStore)
        {
            ApplicationConfiguration.SecurityConfiguration.AddAppCertToTrustedStore = addToTrustedStore;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderSecurityOptions SetRejectSHA1SignedCertificates(bool rejectSHA1Signed)
        {
            ApplicationConfiguration.SecurityConfiguration.RejectSHA1SignedCertificates = rejectSHA1Signed;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderSecurityOptions SetRejectUnknownRevocationStatus(bool rejectUnknownRevocationStatus)
        {
            ApplicationConfiguration.SecurityConfiguration.RejectUnknownRevocationStatus = rejectUnknownRevocationStatus;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderSecurityOptions SetUseValidatedCertificates(bool useValidatedCertificates)
        {
            ApplicationConfiguration.SecurityConfiguration.UseValidatedCertificates = useValidatedCertificates;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderSecurityOptions SetSuppressNonceValidationErrors(bool suppressNonceValidationErrors)
        {
            ApplicationConfiguration.SecurityConfiguration.SuppressNonceValidationErrors = suppressNonceValidationErrors;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderSecurityOptions SetSendCertificateChain(bool sendCertificateChain)
        {
            ApplicationConfiguration.SecurityConfiguration.SendCertificateChain = sendCertificateChain;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderSecurityOptions SetMinimumCertificateKeySize(ushort keySize)
        {
            ApplicationConfiguration.SecurityConfiguration.MinimumCertificateKeySize = keySize;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderSecurityOptions AddCertificatePasswordProvider(ICertificatePasswordProvider certificatePasswordProvider)
        {
            ApplicationConfiguration.SecurityConfiguration.CertificatePasswordProvider = certificatePasswordProvider;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderTransportQuotasSet SetTransportQuotas(TransportQuotas transportQuotas)
        {
            ApplicationConfiguration.TransportQuotas = transportQuotas;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderTransportQuotas SetOperationTimeout(int operationTimeout)
        {
            ApplicationConfiguration.TransportQuotas.OperationTimeout = operationTimeout;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderTransportQuotas SetMaxStringLength(int maxStringLength)
        {
            ApplicationConfiguration.TransportQuotas.MaxStringLength = maxStringLength;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderTransportQuotas SetMaxByteStringLength(int maxByteStringLength)
        {
            ApplicationConfiguration.TransportQuotas.MaxByteStringLength = maxByteStringLength;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderTransportQuotas SetMaxArrayLength(int maxArrayLength)
        {
            ApplicationConfiguration.TransportQuotas.MaxArrayLength = maxArrayLength;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderTransportQuotas SetMaxMessageSize(int maxMessageSize)
        {
            ApplicationConfiguration.TransportQuotas.MaxMessageSize = maxMessageSize;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderTransportQuotas SetMaxBufferSize(int maxBufferSize)
        {
            ApplicationConfiguration.TransportQuotas.MaxBufferSize = maxBufferSize;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderTransportQuotas SetChannelLifetime(int channelLifetime)
        {
            ApplicationConfiguration.TransportQuotas.ChannelLifetime = channelLifetime;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderTransportQuotas SetSecurityTokenLifetime(int securityTokenLifetime)
        {
            ApplicationConfiguration.TransportQuotas.SecurityTokenLifetime = securityTokenLifetime;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMinRequestThreadCount(int minRequestThreadCount)
        {
            ApplicationConfiguration.ServerConfiguration.MinRequestThreadCount = minRequestThreadCount;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxRequestThreadCount(int maxRequestThreadCount)
        {
            ApplicationConfiguration.ServerConfiguration.MaxRequestThreadCount = maxRequestThreadCount;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxQueuedRequestCount(int maxQueuedRequestCount)
        {
            ApplicationConfiguration.ServerConfiguration.MaxQueuedRequestCount = maxQueuedRequestCount;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetDiagnosticsEnabled(bool diagnosticsEnabled)
        {
            ApplicationConfiguration.ServerConfiguration.DiagnosticsEnabled = diagnosticsEnabled;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxSessionCount(int maxSessionCount)
        {
            ApplicationConfiguration.ServerConfiguration.MaxSessionCount = maxSessionCount;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMinSessionTimeout(int minSessionTimeout)
        {
            ApplicationConfiguration.ServerConfiguration.MinSessionTimeout = minSessionTimeout;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxSessionTimeout(int maxSessionTimeout)
        {
            ApplicationConfiguration.ServerConfiguration.MaxSessionTimeout = maxSessionTimeout;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxBrowseContinuationPoints(int maxBrowseContinuationPoints)
        {
            ApplicationConfiguration.ServerConfiguration.MaxBrowseContinuationPoints = maxBrowseContinuationPoints;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxQueryContinuationPoints(int maxQueryContinuationPoints)
        {
            ApplicationConfiguration.ServerConfiguration.MaxQueryContinuationPoints = maxQueryContinuationPoints;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxHistoryContinuationPoints(int maxHistoryContinuationPoints)
        {
            ApplicationConfiguration.ServerConfiguration.MaxHistoryContinuationPoints = maxHistoryContinuationPoints;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxRequestAge(int maxRequestAge)
        {
            ApplicationConfiguration.ServerConfiguration.MaxRequestAge = maxRequestAge;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMinPublishingInterval(int minPublishingInterval)
        {
            ApplicationConfiguration.ServerConfiguration.MinPublishingInterval = minPublishingInterval;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxPublishingInterval(int maxPublishingInterval)
        {
            ApplicationConfiguration.ServerConfiguration.MaxPublishingInterval = maxPublishingInterval;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetPublishingResolution(int publishingResolution)
        {
            ApplicationConfiguration.ServerConfiguration.PublishingResolution = publishingResolution;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxSubscriptionLifetime(int maxSubscriptionLifetime)
        {
            ApplicationConfiguration.ServerConfiguration.MaxSubscriptionLifetime = maxSubscriptionLifetime;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxMessageQueueSize(int maxMessageQueueSize)
        {
            ApplicationConfiguration.ServerConfiguration.MaxMessageQueueSize = maxMessageQueueSize;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxNotificationQueueSize(int maxNotificationQueueSize)
        {
            ApplicationConfiguration.ServerConfiguration.MaxNotificationQueueSize = maxNotificationQueueSize;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxNotificationsPerPublish(int maxNotificationsPerPublish)
        {
            ApplicationConfiguration.ServerConfiguration.MaxNotificationsPerPublish = maxNotificationsPerPublish;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMinMetadataSamplingInterval(int minMetadataSamplingInterval)
        {
            ApplicationConfiguration.ServerConfiguration.MinMetadataSamplingInterval = minMetadataSamplingInterval;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetAvailableSamplingRates(SamplingRateGroupCollection availableSampleRates)
        {
            ApplicationConfiguration.ServerConfiguration.AvailableSamplingRates = availableSampleRates;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetRegistrationEndpoint(EndpointDescription registrationEndpoint)
        {
            ApplicationConfiguration.ServerConfiguration.RegistrationEndpoint = registrationEndpoint;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxRegistrationInterval(int maxRegistrationInterval)
        {
            ApplicationConfiguration.ServerConfiguration.MaxRegistrationInterval = maxRegistrationInterval;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetNodeManagerSaveFile(string nodeManagerSaveFile)
        {
            ApplicationConfiguration.ServerConfiguration.NodeManagerSaveFile = nodeManagerSaveFile;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMinSubscriptionLifetime(int minSubscriptionLifetime)
        {
            ApplicationConfiguration.ServerConfiguration.MinSubscriptionLifetime = minSubscriptionLifetime;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxPublishRequestCount(int maxPublishRequestCount)
        {
            ApplicationConfiguration.ServerConfiguration.MaxPublishRequestCount = maxPublishRequestCount;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxSubscriptionCount(int maxSubscriptionCount)
        {
            ApplicationConfiguration.ServerConfiguration.MaxSubscriptionCount = maxSubscriptionCount;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxEventQueueSize(int maxEventQueueSize)
        {
            ApplicationConfiguration.ServerConfiguration.MaxEventQueueSize = maxEventQueueSize;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions AddServerProfile(string serverProfile)
        {
            ApplicationConfiguration.ServerConfiguration.ServerProfileArray.Add(serverProfile);
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetShutdownDelay(int shutdownDelay)
        {
            ApplicationConfiguration.ServerConfiguration.ShutdownDelay = shutdownDelay;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions AddServerCapabilities(string serverCapability)
        {
            ApplicationConfiguration.ServerConfiguration.ServerCapabilities.Add(serverCapability);
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetSupportedPrivateKeyFormats(StringCollection supportedPrivateKeyFormats)
        {
            ApplicationConfiguration.ServerConfiguration.SupportedPrivateKeyFormats = supportedPrivateKeyFormats;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMaxTrustListSize(int maxTrustListSize)
        {
            ApplicationConfiguration.ServerConfiguration.MaxTrustListSize = maxTrustListSize;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetMultiCastDnsEnabled(bool multiCastDnsEnabled)
        {
            ApplicationConfiguration.ServerConfiguration.MultiCastDnsEnabled = multiCastDnsEnabled;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetReverseConnect(ReverseConnectServerConfiguration reverseConnectConfiguration)
        {
            ApplicationConfiguration.ServerConfiguration.ReverseConnect = reverseConnectConfiguration;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetOperationLimits(OperationLimits operationLimits)
        {
            ApplicationConfiguration.ServerConfiguration.OperationLimits = operationLimits;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderServerOptions SetAuditingEnabled(bool auditingEnabled)
        {
            ApplicationConfiguration.ServerConfiguration.AuditingEnabled = auditingEnabled;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderClientOptions SetDefaultSessionTimeout(int defaultSessionTimeout)
        {
            ApplicationConfiguration.ClientConfiguration.DefaultSessionTimeout = defaultSessionTimeout;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderClientOptions AddWellKnownDiscoveryUrls(string wellKnownDiscoveryUrl)
        {
            ApplicationConfiguration.ClientConfiguration.WellKnownDiscoveryUrls.Add(wellKnownDiscoveryUrl);
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderClientOptions AddDiscoveryServer(EndpointDescription discoveryServer)
        {
            ApplicationConfiguration.ClientConfiguration.DiscoveryServers.Add(discoveryServer);
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderClientOptions SetEndpointCacheFilePath(string endpointCacheFilePath)
        {
            ApplicationConfiguration.ClientConfiguration.EndpointCacheFilePath = endpointCacheFilePath;
            return this;
        }

        /// <inheritdoc/>
        IApplicationConfigurationBuilderClientOptions IApplicationConfigurationBuilderClientOptions.SetMinSubscriptionLifetime(int minSubscriptionLifetime)
        {
            ApplicationConfiguration.ClientConfiguration.MinSubscriptionLifetime = minSubscriptionLifetime;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderClientOptions SetReverseConnect(ReverseConnectClientConfiguration reverseConnect)
        {
            ApplicationConfiguration.ClientConfiguration.ReverseConnect = reverseConnect;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderClientOptions SetClientOperationLimits(OperationLimits operationLimits)
        {
            ApplicationConfiguration.ClientConfiguration.OperationLimits = operationLimits;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderTraceConfiguration SetOutputFilePath(string outputFilePath)
        {
            ApplicationConfiguration.TraceConfiguration.OutputFilePath = outputFilePath;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderTraceConfiguration SetDeleteOnLoad(bool deleteOnLoad)
        {
            ApplicationConfiguration.TraceConfiguration.DeleteOnLoad = deleteOnLoad;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderTraceConfiguration SetTraceMasks(int traceMasks)
        {
            ApplicationConfiguration.TraceConfiguration.TraceMasks = traceMasks;
            return this;
        }

        /// <inheritdoc/>
        public IApplicationConfigurationBuilderExtension AddExtension<T>(XmlQualifiedName elementName, object value)
        {
            ApplicationConfiguration.UpdateExtension<T>(elementName, value);
            return this;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Internal enumeration of supported trust lists.
        /// </summary>
        private enum TrustlistType
        {
            Application,
            Trusted,
            Issuer,
            TrustedHttps,
            IssuerHttps,
            TrustedUser,
            IssuerUser,
            Rejected
        };

        /// <summary>
        /// Return the default PKI root path if root is unspecified, directory or X509Store.
        /// </summary>
        /// <param name="root">A real root path or the store type.</param>
        private string DefaultPKIRoot(string root)
        {
            if (root == null ||
                root.Equals(CertificateStoreType.Directory, StringComparison.OrdinalIgnoreCase))
            {
                return CertificateStoreIdentifier.DefaultPKIRoot;
            }
            else if (root.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase))
            {
                return CertificateStoreIdentifier.CurrentUser;
            }
            return root;
        }

        /// <summary>
        /// Determine the default store path for a given trust list type.
        /// </summary>
        /// <param name="trustListType">The trust list type.</param>
        /// <param name="pkiRoot">A PKI root for which the store path is needed.</param>
        private string DefaultCertificateStorePath(TrustlistType trustListType, string pkiRoot)
        {
            var pkiRootType = CertificateStoreIdentifier.DetermineStoreType(pkiRoot);
            if (pkiRootType.Equals(CertificateStoreType.Directory, StringComparison.OrdinalIgnoreCase))
            {
                string leafPath = "";
                // see https://reference.opcfoundation.org/v104/GDS/docs/F.1/
                switch (trustListType)
                {
                    case TrustlistType.Application: leafPath = "own"; break;
                    case TrustlistType.Trusted: leafPath = "trusted"; break;
                    case TrustlistType.Issuer: leafPath = "issuer"; break;
                    case TrustlistType.TrustedHttps: leafPath = "trustedHttps"; break;
                    case TrustlistType.IssuerHttps: leafPath = "issuerHttps"; break;
                    case TrustlistType.TrustedUser: leafPath = "trustedUser"; break;
                    case TrustlistType.IssuerUser: leafPath = "issuerUser"; break;
                    case TrustlistType.Rejected: leafPath = "rejected"; break;
                }
                // Caller may have already provided the leaf path, then no need to add.
                int startIndex = pkiRoot.Length - leafPath.Length;
                char lastChar = pkiRoot.Last();
                if (lastChar == Path.DirectorySeparatorChar ||
                    lastChar == Path.AltDirectorySeparatorChar)
                {
                    startIndex--;
                }
                if (startIndex > 0)
                {
                    if (pkiRoot.Substring(startIndex, leafPath.Length).Equals(leafPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return pkiRoot;
                    }
                }
                return Path.Combine(pkiRoot, leafPath);
            }
            else if (pkiRootType.Equals(CertificateStoreType.X509Store, StringComparison.OrdinalIgnoreCase))
            {
                switch (trustListType)
                {
                    case TrustlistType.Application:
#if !NETFRAMEWORK
                        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                            pkiRoot.StartsWith(CertificateStoreIdentifier.CurrentUser, StringComparison.OrdinalIgnoreCase))
                        {
                            return pkiRoot + "My";
                        }
#endif
                        return pkiRoot + "UA_MachineDefault";
                    case TrustlistType.Trusted:
                        return pkiRoot + "UA_Trusted";
                    case TrustlistType.Issuer:
                        return pkiRoot + "UA_Issuer";
                    case TrustlistType.TrustedHttps:
                        return pkiRoot + "UA_Trusted_Https";
                    case TrustlistType.IssuerHttps:
                        return pkiRoot + "UA_Issuer_Https";
                    case TrustlistType.TrustedUser:
                        return pkiRoot + "UA_Trusted_User";
                    case TrustlistType.IssuerUser:
                        return pkiRoot + "UA_Issuer_User";
                    case TrustlistType.Rejected:
                        return pkiRoot + "UA_Rejected";
                }
            }
            throw new NotSupportedException("Unsupported store type.");
        }

        /// <summary>
        /// Add specified groups of security policies and security modes.
        /// </summary>
        /// <param name="includeSign">Include the Sign only policies.</param>
        /// <param name="deprecated">Include the deprecated policies.</param>
        /// <param name="policyNone">Include policy 'None'. (no security!)</param>
        private void AddSecurityPolicies(bool includeSign = false, bool deprecated = false, bool policyNone = false)
        {
            // create list of supported policies
            string[] defaultPolicyUris = SecurityPolicies.GetDefaultUris();
            if (deprecated)
            {
                var names = SecurityPolicies.GetDisplayNames();
                var deprecatedPolicyList = new List<string>();
                foreach (var name in names)
                {
                    var uri = SecurityPolicies.GetUri(name);
                    if (uri != null)
                    {
                        deprecatedPolicyList.Add(uri);
                    }
                }
                defaultPolicyUris = deprecatedPolicyList.ToArray();
            }

            foreach (MessageSecurityMode securityMode in typeof(MessageSecurityMode).GetEnumValues())
            {
                var policies = ApplicationConfiguration.ServerConfiguration.SecurityPolicies;
                if (policyNone && securityMode == MessageSecurityMode.None)
                {
                    InternalAddPolicy(policies, MessageSecurityMode.None, SecurityPolicies.None);
                }
                else if (securityMode >= MessageSecurityMode.SignAndEncrypt ||
                    (includeSign && securityMode == MessageSecurityMode.Sign))
                {
                    foreach (var policyUri in defaultPolicyUris)
                    {
                        InternalAddPolicy(policies, securityMode, policyUri);
                    }
                }
            }
        }

        /// <summary>
        /// Add security policy if it doesn't exist yet.
        /// </summary>
        /// <param name="policies">The collection to which the policies are added.</param>
        /// <param name="securityMode">The message security mode.</param>
        /// <param name="policyUri">The security policy Uri.</param>
        private bool InternalAddPolicy(ServerSecurityPolicyCollection policies, MessageSecurityMode securityMode, string policyUri)
        {
            if (securityMode == MessageSecurityMode.Invalid) throw new ArgumentException("Invalid security mode selected", nameof(securityMode));
            var newPolicy = new ServerSecurityPolicy() {
                SecurityMode = securityMode,
                SecurityPolicyUri = policyUri
            };
            if (policies.Find(s =>
                s.SecurityMode == newPolicy.SecurityMode &&
                string.Equals(s.SecurityPolicyUri, newPolicy.SecurityPolicyUri, StringComparison.Ordinal)
                ) == null)
            {
                policies.Add(newPolicy);
                return true;
            }
            return false;
        }
        #endregion

        #region Private Fields
        private bool m_typeSelected;
        #endregion
    }
}
