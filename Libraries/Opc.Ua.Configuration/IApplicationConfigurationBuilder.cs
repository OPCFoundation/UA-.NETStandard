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

using System.Threading.Tasks;
using System.Xml;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// A fluent API to build the application configuration.
    /// </summary>
    public interface IApplicationConfigurationBuilder :
        IApplicationConfigurationBuilderTypes,
        IApplicationConfigurationBuilderTransportQuotas,
        IApplicationConfigurationBuilderTransportQuotasSet,
        IApplicationConfigurationBuilderServerSelected,
        IApplicationConfigurationBuilderServerOptions,
        IApplicationConfigurationBuilderClientSelected,
        IApplicationConfigurationBuilderSecurity,
        IApplicationConfigurationBuilderSecurityOptions,
        IApplicationConfigurationBuilderServerPolicies,
        IApplicationConfigurationBuilderCreate
    {
    };

    /// <summary>
    /// The client or server configuration types to chose.
    /// </summary>
    public interface IApplicationConfigurationBuilderTypes :
        IApplicationConfigurationBuilderTransportQuotas,
        IApplicationConfigurationBuilderServer,
        IApplicationConfigurationBuilderClient
    {
    }

    /// <summary>
    /// The set transport quota state.
    /// </summary>
    public interface IApplicationConfigurationBuilderTransportQuotas :
        IApplicationConfigurationBuilderServer,
        IApplicationConfigurationBuilderClient
    {
        /// <summary>
        /// Set the transport quotas for this application (client and server).
        /// </summary>
        /// <param name="transportQuotas">The object with the new transport quotas.</param>
        IApplicationConfigurationBuilderTransportQuotasSet SetTransportQuotas(TransportQuotas transportQuotas);

        /// <inheritdoc cref="TransportQuotas.OperationTimeout"/>
        /// <remarks>applies to <see cref="TransportQuotas.OperationTimeout"/></remarks>
        /// <param name="operationTimeout">The operation timeout in ms.</param>
        IApplicationConfigurationBuilderTransportQuotas SetOperationTimeout(int operationTimeout);

        /// <inheritdoc cref="TransportQuotas.MaxStringLength"/>
        /// <remarks>applies to <see cref="TransportQuotas.MaxStringLength"/></remarks>
        /// <param name="maxStringLength">The max string length.</param>
        IApplicationConfigurationBuilderTransportQuotas SetMaxStringLength(int maxStringLength);

        /// <inheritdoc cref="TransportQuotas.MaxByteStringLength"/>
        /// <remarks>applies to <see cref="TransportQuotas.MaxByteStringLength"/></remarks>
        /// <param name="maxByteStringLength">The max byte string length.</param>
        IApplicationConfigurationBuilderTransportQuotas SetMaxByteStringLength(int maxByteStringLength);

        /// <inheritdoc cref="TransportQuotas.MaxArrayLength"/>
        /// <remarks>applies to <see cref="TransportQuotas.MaxArrayLength"/></remarks>
        /// <param name="maxArrayLength">The max array length.</param>
        IApplicationConfigurationBuilderTransportQuotas SetMaxArrayLength(int maxArrayLength);

        /// <inheritdoc cref="TransportQuotas.MaxMessageSize"/>
        /// <remarks>applies to <see cref="TransportQuotas.MaxMessageSize"/></remarks>
        /// <param name="maxMessageSize">The max message size.</param>
        IApplicationConfigurationBuilderTransportQuotas SetMaxMessageSize(int maxMessageSize);

        /// <inheritdoc cref="TransportQuotas.MaxBufferSize"/>
        /// <remarks>applies to <see cref="TransportQuotas.MaxBufferSize"/></remarks>
        /// <param name="maxBufferSize">The max buffer size.</param>
        IApplicationConfigurationBuilderTransportQuotas SetMaxBufferSize(int maxBufferSize);

        /// <inheritdoc cref="TransportQuotas.ChannelLifetime"/>
        /// <remarks>applies to <see cref="TransportQuotas.ChannelLifetime"/></remarks>
        /// <param name="channelLifetime">The lifetime.</param>
        IApplicationConfigurationBuilderTransportQuotas SetChannelLifetime(int channelLifetime);

        /// <inheritdoc cref="TransportQuotas.SecurityTokenLifetime"/>
        /// <remarks>applies to <see cref="TransportQuotas.SecurityTokenLifetime"/></remarks>
        /// <param name="securityTokenLifetime">The lifetime in milliseconds.</param>
        IApplicationConfigurationBuilderTransportQuotas SetSecurityTokenLifetime(int securityTokenLifetime);
    }

    /// <summary>
    /// The set transport quota state.
    /// </summary>
    public interface IApplicationConfigurationBuilderTransportQuotasSet :
        IApplicationConfigurationBuilderServer,
        IApplicationConfigurationBuilderClient
    {
    }

    /// <summary>
    /// The interfaces to implement if a server is selected.
    /// </summary>
    public interface IApplicationConfigurationBuilderServerSelected :
        IApplicationConfigurationBuilderServerPolicies,
        IApplicationConfigurationBuilderServerOptions,
        IApplicationConfigurationBuilderClient,
        IApplicationConfigurationBuilderSecurity
    {
    }

    /// <summary>
    /// The options which can be set if a server is selected.
    /// </summary>
    public interface IApplicationConfigurationBuilderServerOptions :
        IApplicationConfigurationBuilderClient,
        IApplicationConfigurationBuilderSecurity
    {
        /// <inheritdoc cref="ServerBaseConfiguration.MinRequestThreadCount"/>
        IApplicationConfigurationBuilderServerOptions SetMinRequestThreadCount(int minRequestThreadCount);

        /// <inheritdoc cref="ServerBaseConfiguration.MaxRequestThreadCount"/>
        IApplicationConfigurationBuilderServerOptions SetMaxRequestThreadCount(int maxRequestThreadCount);

        /// <inheritdoc cref="ServerBaseConfiguration.MaxQueuedRequestCount"/>
        IApplicationConfigurationBuilderServerOptions SetMaxQueuedRequestCount(int maxQueuedRequestCount);

        /// <inheritdoc cref="ServerConfiguration.DiagnosticsEnabled"/>
        IApplicationConfigurationBuilderServerOptions SetDiagnosticsEnabled(bool diagnosticsEnabled);

        /// <inheritdoc cref="ServerConfiguration.MaxSessionCount"/>
        IApplicationConfigurationBuilderServerOptions SetMaxSessionCount(int maxSessionCount);

        /// <inheritdoc cref="ServerConfiguration.MinSessionTimeout"/>
        IApplicationConfigurationBuilderServerOptions SetMinSessionTimeout(int minSessionTimeout);

        /// <inheritdoc cref="ServerConfiguration.MaxSessionTimeout"/>
        IApplicationConfigurationBuilderServerOptions SetMaxSessionTimeout(int maxSessionTimeout);

        /// <inheritdoc cref="ServerConfiguration.MaxBrowseContinuationPoints"/>
        IApplicationConfigurationBuilderServerOptions SetMaxBrowseContinuationPoints(int maxBrowseContinuationPoints);

        /// <inheritdoc cref="ServerConfiguration.MaxQueryContinuationPoints"/>
        IApplicationConfigurationBuilderServerOptions SetMaxQueryContinuationPoints(int maxQueryContinuationPoints);

        /// <inheritdoc cref="ServerConfiguration.MaxHistoryContinuationPoints"/>
        IApplicationConfigurationBuilderServerOptions SetMaxHistoryContinuationPoints(int maxHistoryContinuationPoints);

        /// <inheritdoc cref="ServerConfiguration.MaxRequestAge"/>
        IApplicationConfigurationBuilderServerOptions SetMaxRequestAge(int maxRequestAge);

        /// <inheritdoc cref="ServerConfiguration.MinPublishingInterval"/>
        IApplicationConfigurationBuilderServerOptions SetMinPublishingInterval(int minPublishingInterval);

        /// <inheritdoc cref="ServerConfiguration.MaxPublishingInterval"/>
        IApplicationConfigurationBuilderServerOptions SetMaxPublishingInterval(int maxPublishingInterval);

        /// <inheritdoc cref="ServerConfiguration.PublishingResolution"/>
        IApplicationConfigurationBuilderServerOptions SetPublishingResolution(int publishingResolution);

        /// <inheritdoc cref="ServerConfiguration.MaxSubscriptionLifetime"/>
        IApplicationConfigurationBuilderServerOptions SetMaxSubscriptionLifetime(int maxSubscriptionLifetime);

        /// <inheritdoc cref="ServerConfiguration.MaxMessageQueueSize"/>
        IApplicationConfigurationBuilderServerOptions SetMaxMessageQueueSize(int maxMessageQueueSize);

        /// <inheritdoc cref="ServerConfiguration.MaxNotificationQueueSize"/>
        IApplicationConfigurationBuilderServerOptions SetMaxNotificationQueueSize(int maxNotificationQueueSize);

        /// <inheritdoc cref="ServerConfiguration.MaxNotificationsPerPublish"/>
        IApplicationConfigurationBuilderServerOptions SetMaxNotificationsPerPublish(int maxNotificationsPerPublish);

        /// <inheritdoc cref="ServerConfiguration.MinMetadataSamplingInterval"/>
        IApplicationConfigurationBuilderServerOptions SetMinMetadataSamplingInterval(int minMetadataSamplingInterval);

        /// <inheritdoc cref="ServerConfiguration.AvailableSamplingRates"/>
        IApplicationConfigurationBuilderServerOptions SetAvailableSamplingRates(SamplingRateGroupCollection availableSampleRates);

        /// <inheritdoc cref="ServerConfiguration.RegistrationEndpoint"/>
        IApplicationConfigurationBuilderServerOptions SetRegistrationEndpoint(EndpointDescription registrationEndpoint);

        /// <inheritdoc cref="ServerConfiguration.MaxRegistrationInterval"/>
        IApplicationConfigurationBuilderServerOptions SetMaxRegistrationInterval(int maxRegistrationInterval);

        /// <inheritdoc cref="ServerConfiguration.NodeManagerSaveFile"/>
        IApplicationConfigurationBuilderServerOptions SetNodeManagerSaveFile(string nodeManagerSaveFile);

        /// <inheritdoc cref="ServerConfiguration.MinSubscriptionLifetime"/>
        IApplicationConfigurationBuilderServerOptions SetMinSubscriptionLifetime(int minSubscriptionLifetime);

        /// <inheritdoc cref="ServerConfiguration.MaxPublishRequestCount"/>
        IApplicationConfigurationBuilderServerOptions SetMaxPublishRequestCount(int maxPublishRequestCount);

        /// <inheritdoc cref="ServerConfiguration.MaxSubscriptionCount"/>
        IApplicationConfigurationBuilderServerOptions SetMaxSubscriptionCount(int maxSubscriptionCount);

        /// <inheritdoc cref="ServerConfiguration.MaxEventQueueSize"/>
        IApplicationConfigurationBuilderServerOptions SetMaxEventQueueSize(int setMaxEventQueueSize);

        /// <inheritdoc cref="ServerConfiguration.ServerProfileArray" path="/summary"/>
        /// <param name="serverProfile">Add a server profile to the array.</param>
        IApplicationConfigurationBuilderServerOptions AddServerProfile(string serverProfile);

        /// <inheritdoc cref="ServerConfiguration.ShutdownDelay"/>
        IApplicationConfigurationBuilderServerOptions SetShutdownDelay(int shutdownDelay);

        /// <inheritdoc cref="ServerConfiguration.ServerCapabilities" path="/summary"/>
        /// <param name="serverCapability">The server capability to add.</param>
        IApplicationConfigurationBuilderServerOptions AddServerCapabilities(string serverCapability);

        /// <inheritdoc cref="ServerConfiguration.SupportedPrivateKeyFormats"/>
        IApplicationConfigurationBuilderServerOptions SetSupportedPrivateKeyFormats(StringCollection supportedPrivateKeyFormats);

        /// <inheritdoc cref="ServerConfiguration.MaxTrustListSize"/>
        IApplicationConfigurationBuilderServerOptions SetMaxTrustListSize(int maxTrustListSize);

        /// <inheritdoc cref="ServerConfiguration.MultiCastDnsEnabled"/>
        IApplicationConfigurationBuilderServerOptions SetMultiCastDnsEnabled(bool multiCastDnsEnabled);

        /// <inheritdoc cref="ServerConfiguration.ReverseConnect"/>
        IApplicationConfigurationBuilderServerOptions SetReverseConnect(ReverseConnectServerConfiguration reverseConnectConfiguration);

        /// <inheritdoc cref="ServerConfiguration.OperationLimits"/>
        IApplicationConfigurationBuilderServerOptions SetOperationLimits(OperationLimits operationLimits);

        /// <inheritdoc cref="ServerConfiguration.AuditingEnabled"/>
        IApplicationConfigurationBuilderServerOptions SetAuditingEnabled(bool auditingEnabled);
    }

    /// <summary>
    /// The interfaces to implement if a client is selected.
    /// </summary>
    public interface IApplicationConfigurationBuilderClientSelected :
        IApplicationConfigurationBuilderClientOptions,
        IApplicationConfigurationBuilderSecurity
    {
    }

    /// <summary>
    /// The options to set if a client is selected.
    /// </summary>
    public interface IApplicationConfigurationBuilderClientOptions :
        IApplicationConfigurationBuilderSecurity
    {
        /// <inheritdoc cref="ClientConfiguration.DefaultSessionTimeout"/>
        IApplicationConfigurationBuilderClientOptions SetDefaultSessionTimeout(int defaultSessionTimeout);

        /// <inheritdoc cref="ClientConfiguration.WellKnownDiscoveryUrls"/>
        /// <param name="wellKnownDiscoveryUrl">The well known discovery server url to add.</param>
        IApplicationConfigurationBuilderClientOptions AddWellKnownDiscoveryUrls(string wellKnownDiscoveryUrl);

        /// <inheritdoc cref="ClientConfiguration.DiscoveryServers"/>
        /// <param name="discoveryServer">The discovery server endpoint description to add.</param>
        IApplicationConfigurationBuilderClientOptions AddDiscoveryServer(EndpointDescription discoveryServer);

        /// <inheritdoc cref="ClientConfiguration.EndpointCacheFilePath"/>
        IApplicationConfigurationBuilderClientOptions SetEndpointCacheFilePath(string endpointCacheFilePath);

        /// <inheritdoc cref="ClientConfiguration.MinSubscriptionLifetime"/>
        IApplicationConfigurationBuilderClientOptions SetMinSubscriptionLifetime(int minSubscriptionLifetime);

        /// <inheritdoc cref="ClientConfiguration.ReverseConnect"/>
        IApplicationConfigurationBuilderClientOptions SetReverseConnect(ReverseConnectClientConfiguration reverseConnect);

        /// <inheritdoc cref="ClientConfiguration.OperationLimits"/>
        IApplicationConfigurationBuilderClientOptions SetClientOperationLimits(OperationLimits operationLimits);
    }

    /// <summary>
    /// Add the server configuration (optional).
    /// </summary>
    public interface IApplicationConfigurationBuilderServer
    {
        /// <summary>
        /// Configure instance to be used for UA server.
        /// </summary>
        IApplicationConfigurationBuilderServerSelected AsServer(
            string[] baseAddresses,
            string[] alternateBaseAddresses = null);
    }

    /// <summary>
    /// Add the client configuration (optional).
    /// </summary>
    public interface IApplicationConfigurationBuilderClient
    {
        /// <summary>
        /// Configure instance to be used for UA client.
        /// </summary>
        IApplicationConfigurationBuilderClientSelected AsClient();
    }

    /// <summary>
    /// Add the supported server policies.
    /// </summary>
    public interface IApplicationConfigurationBuilderServerPolicies
    {
        /// <summary>
        /// Add the unsecure security policy type none to server configuration.
        /// </summary>
        /// <param name="addPolicy">Add policy if true.</param>
        IApplicationConfigurationBuilderServerSelected AddUnsecurePolicyNone(bool addPolicy = true);

        /// <summary>
        /// Add the sign security policies to the server configuration.
        /// </summary>
        /// <param name="addPolicies">Add policies if true.</param>
        IApplicationConfigurationBuilderServerSelected AddSignPolicies(bool addPolicies = true);

        /// <summary>
        /// Add the sign and encrypt security policies to the server configuration.
        /// </summary>
        /// <param name="addPolicies">Add policies if true.</param>
        IApplicationConfigurationBuilderServerSelected AddSignAndEncryptPolicies(bool addPolicies = true);

        /// <summary>
        /// Add the specified security policy with the specified security mode.
        /// </summary>
        /// <param name="securityMode">The message security mode to add the policy to.</param>
        /// <param name="securityPolicy">The security policy Uri string.</param>
        IApplicationConfigurationBuilderServerSelected AddPolicy(MessageSecurityMode securityMode, string securityPolicy);

        /// <summary>
        /// Add user token policy to the server configuration.
        /// </summary>
        /// <param name="userTokenType">The user token type to add.</param>
        IApplicationConfigurationBuilderServerSelected AddUserTokenPolicy(UserTokenType userTokenType);

        /// <summary>
        /// Add user token policy to the server configuration.
        /// </summary>
        /// <param name="userTokenPolicy">The user token policy to add.</param>
        IApplicationConfigurationBuilderServerSelected AddUserTokenPolicy(UserTokenPolicy userTokenPolicy);

    }

    /// <summary>
    /// Add the security configuration (mandatory).
    /// </summary>
    public interface IApplicationConfigurationBuilderSecurity
    {
        /// <summary>
        /// Add the security configuration.
        /// </summary>
        /// <remarks>
        /// The pki root path default to the certificate store
        /// location as defined in <see cref="CertificateStoreIdentifier.DefaultPKIRoot"/>
        /// A <see cref="CertificateStoreType"/> defaults to the corresponding default store location.
        /// </remarks>
        /// <param name="subjectName">Application certificate subject name as distinguished name. A DC=localhost entry is converted to the hostname. The common name CN= is mandatory.</param>
        /// <param name="pkiRoot">The path to the pki root. By default all cert stores use the pki root.</param>
        /// <param name="appRoot">The path to the app cert store, if different than the pki root.</param>
        /// <param name="rejectedRoot">The path to the rejected certificate store.</param>
        IApplicationConfigurationBuilderSecurityOptions AddSecurityConfiguration(
            string subjectName,
            string pkiRoot = null,
            string appRoot = null,
            string rejectedRoot = null
            );
    }

    /// <summary>
    /// Add security options to the configuration.
    /// </summary>
    public interface IApplicationConfigurationBuilderSecurityOptions :
        IApplicationConfigurationBuilderTraceConfiguration,
        IApplicationConfigurationBuilderExtension,
        IApplicationConfigurationBuilderCreate
    {
        /// <summary>
        /// Whether an unknown application certificate should be accepted
        /// once all other security checks passed.
        /// </summary>
        /// <param name="autoAccept"><see langword="true"/> to accept unknown application certificates.</param>
        IApplicationConfigurationBuilderSecurityOptions SetAutoAcceptUntrustedCertificates(bool autoAccept);

        /// <summary>
        /// Whether a newly created application certificate should be added to the trusted store.
        /// This function is only useful if multiple UA applications share the same trusted store.
        /// </summary>
        /// <param name="addToTrustedStore"><see langword="true"/> to add the cert to the trusted store.</param>
        IApplicationConfigurationBuilderSecurityOptions SetAddAppCertToTrustedStore(bool addToTrustedStore);

        /// <summary>
        /// Reject SHA1 signed certificates.
        /// </summary>
        /// <param name="rejectSHA1Signed"><see langword="false"/> to accept SHA1 signed certificates.</param>
        IApplicationConfigurationBuilderSecurityOptions SetRejectSHA1SignedCertificates(bool rejectSHA1Signed);

        /// <summary>
        /// Reject chain validation with CA certs with unknown revocation status,
        /// e.g. when the CRL is not available or the OCSP provider is offline.
        /// </summary>
        /// <param name="rejectUnknownRevocationStatus"><see langword="false"/> to accept CA certs with unknown revocation status.</param>
        IApplicationConfigurationBuilderSecurityOptions SetRejectUnknownRevocationStatus(bool rejectUnknownRevocationStatus);

        /// <summary>
        /// Use the validated certificates for fast Validation.
        /// </summary>
        /// <param name="useValidatedCertificates"><see langword="true"/> to use the validated certificates.</param>
        IApplicationConfigurationBuilderSecurityOptions SetUseValidatedCertificates(bool useValidatedCertificates);

        /// <summary>
        /// Whether to suppress errors which are caused by clients and servers which provide
        /// zero nonce values or nonce with insufficient entropy.
        /// Suppressing this error is a security risk and may allow an attacker to decrypt user tokens.
        /// Only use if interoperability issues with legacy servers or clients leave no other choice to operate.
        /// </summary>
        /// <param name="suppressNonceValidationErrors"><see langword="true"/> to suppress nonce validation errors.</param>
        IApplicationConfigurationBuilderSecurityOptions SetSuppressNonceValidationErrors(bool suppressNonceValidationErrors);

        /// <summary>
        /// Whether a certificate chain should be sent with the application certificate.
        /// Only used if the application certificate is CA signed.
        /// </summary>
        /// <param name="sendCertificateChain"><see langword="true"/> to send the certificate chain with the application certificate.</param>
        IApplicationConfigurationBuilderSecurityOptions SetSendCertificateChain(bool sendCertificateChain);

        /// <summary>
        /// The minimum RSA key size to accept.
        /// By default the key size is set to <see cref="CertificateFactory.DefaultKeySize"/>.
        /// </summary>
        /// <param name="keySize">The minimum RSA key size to accept.</param>
        IApplicationConfigurationBuilderSecurityOptions SetMinimumCertificateKeySize(ushort keySize);

        /// <summary>
        /// Add a certificate password provider.
        /// </summary>
        /// <param name="certificatePasswordProvider">The certificate password provider to use.</param>
        IApplicationConfigurationBuilderSecurityOptions AddCertificatePasswordProvider(ICertificatePasswordProvider certificatePasswordProvider);
    }

    /// <summary>
    /// Add extensions configuration.
    /// </summary>
    public interface IApplicationConfigurationBuilderExtension :
        IApplicationConfigurationBuilderTraceConfiguration
    {
        /// <summary>
        /// Add an extension to the configuration.
        /// </summary>
        /// <typeparam name="T">The type of the object to add as an extension.</typeparam>
        /// <param name="elementName">The name of the extension, null to use the name.</param>
        /// <param name="value">The object to add and encode.</param>
        IApplicationConfigurationBuilderExtension AddExtension<T>(XmlQualifiedName elementName, object value);
    }

    /// <summary>
    /// Add the trace configuration.
    /// </summary>
    public interface IApplicationConfigurationBuilderTraceConfiguration :
        IApplicationConfigurationBuilderCreate
    {
        /// <inheritdoc cref="TraceConfiguration.OutputFilePath"/>
        IApplicationConfigurationBuilderTraceConfiguration SetOutputFilePath(string outputFilePath);

        /// <inheritdoc cref="TraceConfiguration.DeleteOnLoad"/>
        IApplicationConfigurationBuilderTraceConfiguration SetDeleteOnLoad(bool deleteOnLoad);

        /// <inheritdoc cref="TraceConfiguration.TraceMasks"/>
        IApplicationConfigurationBuilderTraceConfiguration SetTraceMasks(int TraceMasks);
    }

    /// <summary>
    /// Create and validate the application configuration.
    /// </summary>
    public interface IApplicationConfigurationBuilderCreate
    {
        /// <summary>
        /// Creates and updates the application configuration.
        /// </summary>
        Task<ApplicationConfiguration> Create();
    }
}

