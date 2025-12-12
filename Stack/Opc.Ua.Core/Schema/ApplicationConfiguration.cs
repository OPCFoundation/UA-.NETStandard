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
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Opc.Ua.Bindings;
using Opc.Ua.Security;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the configurable configuration information for a UA application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class ApplicationConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ApplicationConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// The default constructor.
        /// </summary>
        public ApplicationConfiguration(ITelemetryContext telemetry)
        {
            Initialize(telemetry);

            Initialize();
        }

        /// <summary>
        /// The constructor from a template.
        /// </summary>
        public ApplicationConfiguration(ApplicationConfiguration template)
        {
            ApplicationName = template.ApplicationName;
            ApplicationType = template.ApplicationType;
            ApplicationUri = template.ApplicationUri;
            DiscoveryServerConfiguration = template.DiscoveryServerConfiguration;
            m_securityConfiguration = template.m_securityConfiguration;
            m_transportConfigurations = template.m_transportConfigurations;
            ServerConfiguration = template.ServerConfiguration;
            ClientConfiguration = template.ClientConfiguration;
            DisableHiResClock = template.DisableHiResClock;
            CertificateValidator = template.CertificateValidator;
            TransportQuotas = template.TransportQuotas;
            TraceConfiguration = template.TraceConfiguration;
            m_extensions = template.m_extensions;
            m_extensionObjects = template.m_extensionObjects;
            SourceFilePath = template.SourceFilePath;
            m_properties = template.m_properties;
            m_telemetry = template.m_telemetry;
            m_logger = template.m_logger;
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            SourceFilePath = null;
            m_securityConfiguration = new SecurityConfiguration();
            m_transportConfigurations = [];
            DisableHiResClock = false;
            m_properties = [];
            m_extensionObjects = [];

            CertificateValidator ??= new CertificateValidator(m_telemetry);
            m_logger ??= m_telemetry.CreateLogger<ApplicationConfiguration>();
        }

        /// <summary>
        /// Initialize telemetry context - after loading
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        internal void Initialize(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ApplicationConfiguration>();
            CertificateValidator = new CertificateValidator(m_telemetry);
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            m_telemetry = AmbientMessageContext.Telemetry;
            Initialize();
        }

        /// <summary>
        /// Gets an object used to synchronize access to the properties dictionary.
        /// </summary>
        /// <value>
        /// The object used to synchronize access to the properties dictionary.
        /// </value>
        public object PropertiesLock => m_properties;

        /// <summary>
        /// Gets a dictionary used to save state associated with the application.
        /// </summary>
        /// <value>
        /// The dictionary used to save state associated with the application.
        /// </value>
        public IDictionary<string, object> Properties => m_properties;

        /// <summary>
        /// Storage for decoded extensions of the application.
        /// Used by ParseExtension if no matching XmlElement is found.
        /// </summary>
        public IList<object> ExtensionObjects => m_extensionObjects;

        /// <summary>
        /// A descriptive name for the application (not necessarily unique).
        /// </summary>
        /// <value>The name of the application.</value>
        [DataMember(IsRequired = true, EmitDefaultValue = false, Order = 0)]
        public string ApplicationName { get; set; }

        /// <summary>
        /// A unique identifier for the application instance.
        /// </summary>
        /// <value>The application URI.</value>
        [DataMember(IsRequired = true, EmitDefaultValue = false, Order = 1)]
        public string ApplicationUri { get; set; }

        /// <summary>
        /// A unique identifier for the product.
        /// </summary>
        /// <value>The product URI.</value>
        [DataMember(IsRequired = false, Order = 2)]
        public string ProductUri { get; set; }

        /// <summary>
        /// The type of application.
        /// </summary>
        /// <value>The type of the application.</value>
        [DataMember(IsRequired = true, Order = 3)]
        public ApplicationType ApplicationType { get; set; }

        /// <summary>
        /// The security configuration for the application.
        /// </summary>
        /// <value>The security configuration.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = true, Order = 4)]
        public SecurityConfiguration SecurityConfiguration
        {
            get => m_securityConfiguration;
            set => m_securityConfiguration = value ?? new SecurityConfiguration();
        }

        /// <summary>
        /// The transport configuration for the application.
        /// </summary>
        /// <value>The transport configurations.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = true, Order = 5)]
        public TransportConfigurationCollection TransportConfigurations
        {
            get => m_transportConfigurations;
            set => m_transportConfigurations = value ?? [];
        }

        /// <summary>
        /// The quotas that are used at the transport layer.
        /// </summary>
        /// <value>The transport quotas.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = true, Order = 6)]
        public TransportQuotas TransportQuotas { get; set; }

        /// <summary>
        /// Additional configuration for server applications.
        /// </summary>
        /// <value>The server configuration.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 7)]
        public ServerConfiguration ServerConfiguration { get; set; }

        /// <summary>
        /// Additional configuration for client applications.
        /// </summary>
        /// <value>The client configuration.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 8)]
        public ClientConfiguration ClientConfiguration { get; set; }

        /// <summary>
        /// Additional configuration of the discovery server.
        /// </summary>
        /// <value>The discovery server configuration.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 9)]
        public DiscoveryServerConfiguration DiscoveryServerConfiguration { get; set; }

        /// <summary>
        /// A bucket to store additional application specific configuration data.
        /// </summary>
        /// <value>The extensions.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 10)]
        public XmlElementCollection Extensions
        {
            get => m_extensions;
            set => m_extensions = value;
        }

        /// <summary>
        /// Configuration of the trace and information about log file
        /// </summary>
        /// <value>The trace configuration.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 11)]
        public TraceConfiguration TraceConfiguration { get; set; }

        /// <summary>
        /// Disabling / enabling high resolution clock
        /// </summary>
        /// <value><c>true</c> if high resolution clock is disabled; otherwise, <c>false</c>.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 12)]
        public bool DisableHiResClock { get; set; }

        private ITelemetryContext m_telemetry;
        private ILogger m_logger;
        private SecurityConfiguration m_securityConfiguration;
        private TransportConfigurationCollection m_transportConfigurations;
        private XmlElementCollection m_extensions;
        private List<object> m_extensionObjects;
        private Dictionary<string, object> m_properties;
    }

    /// <summary>
    /// Specifies various limits that apply to the transport or secure channel layers.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class TransportQuotas
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public TransportQuotas()
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            // encoding limits
            MaxMessageSize = DefaultEncodingLimits.MaxMessageSize;
            MaxStringLength = DefaultEncodingLimits.MaxStringLength;
            MaxByteStringLength = DefaultEncodingLimits.MaxByteStringLength;
            MaxArrayLength = DefaultEncodingLimits.MaxArrayLength;
            MaxEncodingNestingLevels = DefaultEncodingLimits.MaxEncodingNestingLevels;
            MaxDecoderRecoveries = DefaultEncodingLimits.MaxDecoderRecoveries;

            // message limits
            MaxBufferSize = TcpMessageLimits.DefaultMaxBufferSize;
            OperationTimeout = TcpMessageLimits.DefaultOperationTimeout;
            ChannelLifetime = TcpMessageLimits.DefaultChannelLifetime;
            SecurityTokenLifetime = TcpMessageLimits.DefaultSecurityTokenLifeTime;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// The default timeout to use when sending requests (in milliseconds).
        /// </summary>
        /// <value>The operation timeout.</value>
        [DataMember(IsRequired = false, Order = 0)]
        public int OperationTimeout { get; set; }

        /// <summary>
        /// The maximum length of string encoded in a message body.
        /// </summary>
        /// <value>The max length of the string.</value>
        [DataMember(IsRequired = false, Order = 1)]
        public int MaxStringLength { get; set; }

        /// <summary>
        /// The maximum length of a byte string encoded in a message body.
        /// </summary>
        /// <value>The max length of the byte string.</value>
        [DataMember(IsRequired = false, Order = 2)]
        public int MaxByteStringLength { get; set; }

        /// <summary>
        /// The maximum length of an array encoded in a message body.
        /// </summary>
        /// <value>The max length of the array.</value>
        [DataMember(IsRequired = false, Order = 3)]
        public int MaxArrayLength { get; set; }

        /// <summary>
        /// The maximum length of a message body.
        /// </summary>
        /// <value>The max size of the message.</value>
        [DataMember(IsRequired = false, Order = 4)]
        public int MaxMessageSize { get; set; }

        /// <summary>
        /// The maximum size of the buffer to use when sending messages.
        /// </summary>
        /// <value>The max size of the buffer.</value>
        [DataMember(IsRequired = false, Order = 5)]
        public int MaxBufferSize { get; set; }

        /// <summary>
        /// The maximum nesting level accepted while encoding or decoding objects.
        /// </summary>
        [DataMember(IsRequired = false, Order = 6)]
        public int MaxEncodingNestingLevels { get; set; }

        /// <summary>
        /// The number of times the decoder can recover from a decoder error
        /// of an IEncodeable before throwing a decoder error.
        /// </summary>
        [DataMember(IsRequired = false, Order = 7)]
        public int MaxDecoderRecoveries { get; set; }

        /// <summary>
        /// The lifetime of a secure channel (in milliseconds).
        /// </summary>
        /// <value>The channel lifetime.</value>
        [DataMember(IsRequired = false, Order = 8)]
        public int ChannelLifetime { get; set; }

        /// <summary>
        /// The lifetime of a security token (in milliseconds).
        /// </summary>
        /// <value>The security token lifetime.</value>
        [DataMember(IsRequired = false, Order = 9)]
        public int SecurityTokenLifetime { get; set; }
    }

    /// <summary>
    /// Specifies parameters used for tracing.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class TraceConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public TraceConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            OutputFilePath = null;
            DeleteOnLoad = false;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// The output file used to log the trace information.
        /// </summary>
        /// <value>The output file path.</value>
        [DataMember(IsRequired = false, Order = 0)]
        public string OutputFilePath { get; set; }

        /// <summary>
        /// Whether the existing log file should be deleted when the application configuration is loaded.
        /// </summary>
        /// <value><c>true</c> if existing log file should be deleted when the application configuration is loaded; otherwise, <c>false</c>.</value>
        [DataMember(IsRequired = false, Order = 1)]
        public bool DeleteOnLoad { get; set; }

        /// <summary>
        /// The masks used to select what is written to the output
        /// Masks supported by the trace feature:
        /// - Do not output any messages -None = 0x0;
        /// - Output error messages - Error = 0x1;
        /// - Output informational messages - Information = 0x2;
        /// - Output stack traces - StackTrace = 0x4;
        /// - Output basic messages for service calls - Service = 0x8;
        /// - Output detailed messages for service calls - ServiceDetail = 0x10;
        /// - Output basic messages for each operation - Operation = 0x20;
        /// - Output detailed messages for each operation - OperationDetail = 0x40;
        /// - Output messages related to application initialization or shutdown - StartStop = 0x80;
        /// - Output messages related to a call to an external system - ExternalSystem = 0x100;
        /// - Output messages related to security. - Security = 0x200;
        /// </summary>
        /// <value>The trace masks.</value>
        [DataMember(IsRequired = false, Order = 2)]
        public int TraceMasks { get; set; }
    }

    /// <summary>
    /// Specifies the configuration information for a transport protocol
    /// </summary>
    /// <remarks>
    /// Each application is allows to have one transport configure per protocol type.
    /// </remarks>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class TransportConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public TransportConfiguration()
        {
        }

        /// <summary>
        /// The default constructor.
        /// </summary>
        /// <param name="urlScheme">The URL scheme.</param>
        /// <param name="type">The type.</param>
        public TransportConfiguration(string urlScheme, Type type)
        {
            UriScheme = urlScheme;
            TypeName = type.AssemblyQualifiedName;
        }

        /// <summary>
        /// The URL prefix used by the application (http, opc.tcp, net.tpc, etc.).
        /// </summary>
        /// <value>The URI scheme.</value>
        [DataMember(IsRequired = true, EmitDefaultValue = false, Order = 0)]
        public string UriScheme { get; set; }

        /// <summary>
        /// The name of the class that defines the binding for the transport.
        /// </summary>
        /// <value>The name of the type.</value>
        /// <remarks>
        /// <para>
        /// This can be any instance of the System.ServiceModel.Channels.Binding class
        /// that implements these constructors:
        /// </para>
        /// <para>
        /// XxxBinding(EndpointDescription description, EndpointConfiguration configuration);
        /// XxxBinding(IList{EndpointDescription} descriptions, EndpointConfiguration configuration)
        /// XxxBinding(EndpointConfiguration configuration)
        /// </para>
        /// </remarks>
        [DataMember(IsRequired = true, EmitDefaultValue = false, Order = 1)]
        public string TypeName { get; set; }
    }

    /// <summary>
    /// A collection of TransportConfiguration objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfTransportConfiguration",
        Namespace = Namespaces.OpcUaConfig,
        ItemName = "TransportConfiguration"
    )]
    public class TransportConfigurationCollection : List<TransportConfiguration>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public TransportConfigurationCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public TransportConfigurationCollection(IEnumerable<TransportConfiguration> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public TransportConfigurationCollection(int capacity)
            : base(capacity)
        {
        }
    }

    /// <summary>
    /// A class that defines a group of security policies supported by the server.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ServerSecurityPolicy
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ServerSecurityPolicy()
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt;
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Obsolete version of CalculateSecurityLevel that does not take a logger.
        /// </summary>
        [Obsolete("Use CalculateSecurityLevel(MessageSecurityMode mode, string policyUri, ILogger logger) instead.")]
        public static byte CalculateSecurityLevel(
            MessageSecurityMode mode,
            string policyUri)
        {
            return SecuredApplication.CalculateSecurityLevel(mode, policyUri);
        }

        /// <summary>
        /// Calculates the security level, given the security mode and policy
        /// Invalid and none is discouraged
        /// Just signing is always weaker than any use of encryption
        /// </summary>
        public static byte CalculateSecurityLevel(
            MessageSecurityMode mode,
            string policyUri,
            ILogger logger)
        {
            return SecuredApplication.CalculateSecurityLevel(mode, policyUri, logger);
        }

        /// <summary>
        /// Specifies whether the messages are signed and encrypted or simply signed
        /// </summary>
        /// <value>The security mode.</value>
        [DataMember(IsRequired = false, Order = 1)]
        public MessageSecurityMode SecurityMode { get; set; }

        /// <summary>
        /// The security policy to use.
        /// </summary>
        /// <value>The security policy URI.</value>
        [DataMember(IsRequired = false, Order = 2)]
        public string SecurityPolicyUri { get; set; }
    }

    /// <summary>
    /// A collection of ServerSecurityPolicy objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfServerSecurityPolicy",
        Namespace = Namespaces.OpcUaConfig,
        ItemName = "ServerSecurityPolicy"
    )]
    public class ServerSecurityPolicyCollection : List<ServerSecurityPolicy>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ServerSecurityPolicyCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public ServerSecurityPolicyCollection(IEnumerable<ServerSecurityPolicy> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public ServerSecurityPolicyCollection(int capacity)
            : base(capacity)
        {
        }
    }

    /// <summary>
    /// The security configuration for the application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class SecurityConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public SecurityConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_applicationCertificates = [];
            m_trustedIssuerCertificates = new CertificateTrustList();
            m_trustedPeerCertificates = new CertificateTrustList();
            NonceLength = 32;
            MaxRejectedCertificates = 5;
            AutoAcceptUntrustedCertificates = false;
            RejectSHA1SignedCertificates = true;
            RejectUnknownRevocationStatus = false;
            MinimumCertificateKeySize = CertificateFactory.DefaultKeySize;
            AddAppCertToTrustedStore = true;
            SendCertificateChain = true;
            SuppressNonceValidationErrors = false;
            IsDeprecatedConfiguration = false;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// The application instance certificate.
        /// Kept for backward compatibility with configuration files which only support RSA certificates.
        /// </summary>
        /// <value>The application certificate.</value>
        /// <remarks>
        /// This certificate must contain the application uri.
        /// For servers, URLs for each supported protocol must also be present.
        /// </remarks>
        [IgnoreDataMember]
        public CertificateIdentifier ApplicationCertificate
        {
            get
            {
                if (m_applicationCertificates.Count > 0)
                {
                    return m_applicationCertificates[0];
                }
                return null;
            }
            set
            {
                if (m_applicationCertificates.Count > 0)
                {
                    if (value == null)
                    {
                        m_applicationCertificates.RemoveAt(0);
                    }
                    else
                    {
                        m_applicationCertificates[0] = value;
                    }
                }
                else
                {
                    m_applicationCertificates.Add(value);
                }
                SupportedSecurityPolicies = BuildSupportedSecurityPolicies();

                m_applicationCertificates[0].CertificateType = ObjectTypeIds
                    .RsaSha256ApplicationCertificateType;
                IsDeprecatedConfiguration = true;
            }
        }

        // This private property exists solely to control serialization of the legacy single
        // certificate element. It is emitted only when the configuration was marked deprecated.
        [DataMember(Name = "ApplicationCertificate", IsRequired = false, EmitDefaultValue = false, Order = 0)]
        private CertificateIdentifier ApplicationCertificateLegacy
        {
            get => IsDeprecatedConfiguration ? ApplicationCertificate : null;
            set => ApplicationCertificate = value;
        }

        /// <summary>
        /// The application instance certificates in use for the application.
        /// </summary>
        [IgnoreDataMember]
        public CertificateIdentifierCollection ApplicationCertificates
        {
            get => m_applicationCertificates;
            set
            {
                if (value == null || value.Count == 0)
                {
                    m_applicationCertificates = [];
                    return;
                }

                // Do not change IsDeprecatedConfiguration here: if a legacy
                // <ApplicationCertificate> element was present during deserialization,
                // the flag was already set by that setter and must not be cleared by
                // the collection setter that follows.
                // Leaving the flag untouched in the collection setter preserves the “legacy element was encountered” 
                // state and avoids flipping the flag back to false when both legacy and modern elements are in the file.

                var newCertificates = new CertificateIdentifierCollection(value);

                // Remove unsupported certificate types
                for (int i = newCertificates.Count - 1; i >= 0; i--)
                {
                    if (!Utils.IsSupportedCertificateType(newCertificates[i].CertificateType))
                    {
                        // TODO: Log when ITelemetry instance is available
                        newCertificates.RemoveAt(i);
                    }
                }

                // Remove any duplicates based on thumbprint
                // Only perform duplicate detection if we have actual loaded certificates
                for (int i = 0; i < newCertificates.Count; i++)
                {
                    for (int j = newCertificates.Count - 1; j > i; j--)
                    {
                        bool isDuplicate = false;

                        // Only check for duplicates if both certificates are actually loaded
                        if (newCertificates[i].Certificate != null && newCertificates[j].Certificate != null)
                        {
                            // Compare by actual certificate thumbprint
                            isDuplicate = newCertificates[i].Certificate.Thumbprint.Equals(
                                newCertificates[j].Certificate.Thumbprint,
                                StringComparison.OrdinalIgnoreCase);
                        }
                        // If certificates aren't loaded yet, compare by explicit thumbprint configuration
                        else if (!string.IsNullOrEmpty(newCertificates[i].Thumbprint) &&
                            !string.IsNullOrEmpty(newCertificates[j].Thumbprint))
                        {
                            isDuplicate = newCertificates[i].Thumbprint.Equals(
                                newCertificates[j].Thumbprint,
                                StringComparison.OrdinalIgnoreCase);
                        }

                        if (isDuplicate)
                        {
                            newCertificates.RemoveAt(j);
                        }
                    }
                }

                m_applicationCertificates = newCertificates;

                SupportedSecurityPolicies = BuildSupportedSecurityPolicies();
            }
        }

        // This private property exists solely to control the serialization of the modern certificates collection.
        // Emit only when the configuration is not marked deprecated.
        [DataMember(Name = "ApplicationCertificates", IsRequired = false, EmitDefaultValue = false, Order = 1)]
        private CertificateIdentifierCollection ApplicationCertificatesDataContract
        {
            get => IsDeprecatedConfiguration ? null : ApplicationCertificates;
            set => ApplicationCertificates = value;
        }

        /// <summary>
        /// The store containing any additional issuer certificates.
        /// </summary>
        [DataMember(IsRequired = true, EmitDefaultValue = false, Order = 2)]
        public CertificateTrustList TrustedIssuerCertificates
        {
            get => m_trustedIssuerCertificates;
            set => m_trustedIssuerCertificates = value ?? new CertificateTrustList();
        }

        /// <summary>
        /// The trusted certificate store.
        /// </summary>
        [DataMember(IsRequired = true, EmitDefaultValue = false, Order = 4)]
        public CertificateTrustList TrustedPeerCertificates
        {
            get => m_trustedPeerCertificates;
            set => m_trustedPeerCertificates = value ?? new CertificateTrustList();
        }

        /// <summary>
        /// The length of nonce in the CreateSession service.
        /// </summary>
        /// <value>
        /// The length of nonce in the CreateSession service.
        /// </value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 6)]
        public int NonceLength { get; set; }

        /// <summary>
        /// A store where invalid certificates can be placed for later review by the administrator.
        /// </summary>
        /// <value>
        /// A store where invalid certificates can be placed for later review by the administrator.
        /// </value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 7)]
        public CertificateStoreIdentifier RejectedCertificateStore { get; set; }

        /// <summary>
        /// Gets or sets a value indicating how many certificates are kept
        /// in the rejected store before the oldest is removed.
        /// </summary>
        /// <remarks>
        /// This value can be set by applications.
        /// The number of certificates to keep in the rejected store before it is updated.
        /// <see langword="0"/> to keep all rejected certificates.
        /// A negative number to keep no history.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 8)]
        public int MaxRejectedCertificates { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether untrusted certificates should be automatically accepted.
        /// </summary>
        /// <remarks>
        /// This flag can be set to by servers that allow anonymous clients or use user credentials for authentication.
        /// It can be set by clients that connect to URLs specified in configuration rather than with user entry.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 9)]
        public bool AutoAcceptUntrustedCertificates { get; set; }

        /// <summary>
        /// Gets or sets a directory which contains files representing users roles.
        /// </summary>
        [DataMember(Order = 10)]
        public string UserRoleDirectory { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether SHA-1 signed certificates are accepted.
        /// </summary>
        /// <remarks>
        /// This flag can be set to false by servers that accept SHA-1 signed certificates.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 11)]
        public bool RejectSHA1SignedCertificates { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether certificates with unavailable revocation lists are not accepted.
        /// </summary>
        /// <remarks>
        /// This flag can be set to true by servers that must have a revocation list for each CA (even if empty).
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 12)]
        public bool RejectUnknownRevocationStatus { get; set; }

        /// <summary>
        /// Gets or sets a value indicating which minimum certificate key strength is accepted.
        /// The value is ignored for certificates with a ECDSA signature.
        /// </summary>
        /// <remarks>
        /// This value can be set to 1024, 2048 or 4096 by servers
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 13)]
        public ushort MinimumCertificateKeySize { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the Validator skips the full chain validation
        /// for already validated or accepted certificates.
        /// </summary>
        /// <remarks>
        /// This flag can be set to true by applications.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 14)]
        public bool UseValidatedCertificates { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the application cert should be copied to the trusted store.
        /// </summary>
        /// <remarks>
        /// It is useful for client/server applications running on the same host  and sharing the cert store to autotrust.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 15)]
        public bool AddAppCertToTrustedStore { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the application should send the complete certificate chain.
        /// </summary>
        /// <remarks>
        /// If set to true the complete certificate chain will be sent for CA signed certificates.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 16)]
        public bool SendCertificateChain { get; set; }

        /// <summary>
        /// The store containing additional user issuer certificates.
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 17)]
        public CertificateTrustList UserIssuerCertificates
        {
            get => m_userIssuerCertificates;
            set => m_userIssuerCertificates = value ?? new CertificateTrustList();
        }

        /// <summary>
        /// The store containing trusted user certificates.
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 18)]
        public CertificateTrustList TrustedUserCertificates
        {
            get => m_trustedUserCertificates;
            set => m_trustedUserCertificates = value ?? new CertificateTrustList();
        }

        /// <summary>
        /// The store containing additional Https issuer certificates.
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 19)]
        public CertificateTrustList HttpsIssuerCertificates
        {
            get => m_httpsIssuerCertificates;
            set => m_httpsIssuerCertificates = value ?? new CertificateTrustList();
        }

        /// <summary>
        /// The store containing trusted Https certificates.
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 20)]
        public CertificateTrustList TrustedHttpsCertificates
        {
            get => m_trustedHttpsCertificates;
            set => m_trustedHttpsCertificates = value ?? new CertificateTrustList();
        }

        /// <summary>
        /// Gets or sets a value indicating whether the server nonce validation errors should be suppressed.
        /// </summary>
        /// <remarks>
        /// Allows client interoperability with legacy servers which do not comply with the specification for nonce usage.
        /// If set to true the server nonce validation errors are suppressed.
        /// Please set this flag to true only in close and secured networks since it can cause security vulnerabilities.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 21)]
        public bool SuppressNonceValidationErrors { get; set; }

        /// <summary>
        /// The type of Configuration (deprecated or not)
        /// </summary>
        public bool IsDeprecatedConfiguration { get; set; }

        private CertificateIdentifierCollection m_applicationCertificates;
        private CertificateTrustList m_trustedIssuerCertificates;
        private CertificateTrustList m_trustedPeerCertificates;
        private CertificateTrustList m_httpsIssuerCertificates;
        private CertificateTrustList m_trustedHttpsCertificates;
        private CertificateTrustList m_userIssuerCertificates;
        private CertificateTrustList m_trustedUserCertificates;
    }

    /// <summary>
    /// A class that defines a group of sampling rates supported by the server.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class SamplingRateGroup
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public SamplingRateGroup()
        {
            Initialize();
        }

        /// <summary>
        /// Creates a group with the specified settings.
        /// </summary>
        /// <param name="start">The start.</param>
        /// <param name="increment">The increment.</param>
        /// <param name="count">The count.</param>
        public SamplingRateGroup(int start, int increment, int count)
        {
            Start = start;
            Increment = increment;
            Count = count;
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            Start = 1000;
            Increment = 0;
            Count = 0;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// The first sampling rate in the group (in milliseconds).
        /// </summary>
        /// <value>The first sampling rate in the group (in milliseconds).</value>
        [DataMember(IsRequired = false, Order = 1)]
        public double Start { get; set; }

        /// <summary>
        /// The increment between sampling rates in the group (in milliseconds).
        /// </summary>
        /// <value>The increment.</value>
        /// <remarks>
        /// An increment of 0 means the group only contains one sampling rate equal to the start.
        /// </remarks>
        [DataMember(IsRequired = false, Order = 2)]
        public double Increment { get; set; }

        /// <summary>
        /// The number of sampling rates in the group.
        /// </summary>
        /// <value>The count.</value>
        /// <remarks>
        /// A count of 0 means there is no limit.
        /// </remarks>
        [DataMember(IsRequired = false, Order = 3)]
        public int Count { get; set; }
    }

    /// <summary>
    /// A collection of SamplingRateGroup objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfSamplingRateGroup",
        Namespace = Namespaces.OpcUaConfig,
        ItemName = "SamplingRateGroup"
    )]
    public class SamplingRateGroupCollection : List<SamplingRateGroup>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public SamplingRateGroupCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public SamplingRateGroupCollection(IEnumerable<SamplingRateGroup> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public SamplingRateGroupCollection(int capacity)
            : base(capacity)
        {
        }
    }

    /// <summary>
    /// Specifies the configuration for a server application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class ServerBaseConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ServerBaseConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_baseAddresses = [];
            m_alternateBaseAddresses = [];
            m_securityPolicies = [];
            MinRequestThreadCount = 10;
            MaxRequestThreadCount = 100;
            MaxQueuedRequestCount = 200;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Remove unsupported security policies and expand wild cards.
        /// </summary>
        [OnDeserialized]
        private void ValidateSecurityPolicyCollection(StreamingContext context)
        {
            string[] supportedPolicies = Ua.SecurityPolicies.GetDisplayNames();
            var newPolicies = new ServerSecurityPolicyCollection();
            foreach (ServerSecurityPolicy securityPolicy in m_securityPolicies)
            {
                if (string.IsNullOrWhiteSpace(securityPolicy.SecurityPolicyUri))
                {
                    // add wild card policies
                    foreach (string policyUri in Ua.SecurityPolicies.GetDefaultUris())
                    {
                        var newPolicy = new ServerSecurityPolicy
                        {
                            SecurityMode = securityPolicy.SecurityMode,
                            SecurityPolicyUri = policyUri
                        };
                        if (newPolicies.Find(s =>
                                s.SecurityMode == newPolicy.SecurityMode &&
                                string.Equals(
                                    s.SecurityPolicyUri,
                                    newPolicy.SecurityPolicyUri,
                                    StringComparison.Ordinal)
                            ) == null)
                        {
                            newPolicies.Add(newPolicy);
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < supportedPolicies.Length; i++)
                    {
                        if (securityPolicy.SecurityPolicyUri
                            .Contains(supportedPolicies[i], StringComparison.Ordinal))
                        {
                            if (newPolicies.Find(s =>
                                    s.SecurityMode == securityPolicy.SecurityMode &&
                                    string.Equals(
                                        s.SecurityPolicyUri,
                                        securityPolicy.SecurityPolicyUri,
                                        StringComparison.Ordinal)
                                ) == null)
                            {
                                newPolicies.Add(securityPolicy);
                            }
                            break;
                        }
                    }
                }
            }
            m_securityPolicies = newPolicies;
        }

        /// <summary>
        /// The base addresses for the server.
        /// </summary>
        /// <value>The base addresses.</value>
        /// <remarks>
        /// The actually endpoints are constructed from the security policies.
        /// On one base address per supported transport protocol is allowed.
        /// </remarks>
        [DataMember(IsRequired = false, Order = 0)]
        public StringCollection BaseAddresses
        {
            get => m_baseAddresses;
            set => m_baseAddresses = value ?? [];
        }

        /// <summary>
        /// Gets or sets the alternate base addresses.
        /// </summary>
        /// <value>The alternate base addresses.</value>
        /// <remarks>
        /// These addresses are used to specify alternate paths to ther via firewalls, proxies
        /// or similar network infrastructure. If these paths are specified in the configuration
        /// file then the server will use the domain of the URL used by the client to determine
        /// which, if any, or the alternate addresses to use instead of the primary addresses.
        /// </remarks>
        [DataMember(IsRequired = false, Order = 1)]
        public StringCollection AlternateBaseAddresses
        {
            get => m_alternateBaseAddresses;
            set => m_alternateBaseAddresses = value ?? [];
        }

        /// <summary>
        /// The security policies supported by the server.
        /// </summary>
        /// <value>The security policies.</value>
        /// <remarks>
        /// An endpoint description is created for each combination of base address and security policy.
        /// </remarks>
        [DataMember(IsRequired = false, Order = 2)]
        public ServerSecurityPolicyCollection SecurityPolicies
        {
            get => m_securityPolicies;
            set => m_securityPolicies = value ?? [];
        }

        /// <summary>
        /// The minimum number of threads assigned to processing requests.
        /// </summary>
        /// <value>The minimum request thread count.</value>
        [DataMember(IsRequired = false, Order = 3)]
        public int MinRequestThreadCount { get; set; }

        /// <summary>
        /// The maximum number of threads assigned to processing requests.
        /// </summary>
        /// <value>The maximum request thread count.</value>
        [DataMember(IsRequired = false, Order = 4)]
        public int MaxRequestThreadCount { get; set; }

        /// <summary>
        /// The maximum number of requests that will be queued waiting for a thread.
        /// </summary>
        /// <value>The maximum queued request count.</value>
        [DataMember(IsRequired = false, Order = 5)]
        public int MaxQueuedRequestCount { get; set; }

        private StringCollection m_baseAddresses;
        private StringCollection m_alternateBaseAddresses;
        private ServerSecurityPolicyCollection m_securityPolicies;
    }

    /// <summary>
    /// Specifies the configuration for a server application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class ServerConfiguration : ServerBaseConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ServerConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_userTokenPolicies = [];
            DiagnosticsEnabled = false;
            MaxSessionCount = 100;
            MaxChannelCount = 1000;
            MaxSessionTimeout = 3600000;
            MinSessionTimeout = 10000;
            MaxBrowseContinuationPoints = 10;
            MaxQueryContinuationPoints = 10;
            MaxHistoryContinuationPoints = 100;
            MaxRequestAge = 600000;
            MinPublishingInterval = 100;
            MaxPublishingInterval = 3600000;
            PublishingResolution = 100;
            MinSubscriptionLifetime = 10000;
            MaxSubscriptionLifetime = 3600000;
            MaxMessageQueueSize = 10;
            MaxNotificationQueueSize = 100;
            MaxNotificationsPerPublish = 100;
            MinMetadataSamplingInterval = 1000;
            AvailableSamplingRates = [];
            RegistrationEndpoint = null;
            MaxRegistrationInterval = 30000;
            MaxPublishRequestCount = 20;
            MaxSubscriptionCount = 100;
            MaxEventQueueSize = 10000;
            // https://opcfoundation-onlineapplications.org/profilereporting/ for list of available profiles
            m_serverProfileArray = new string[] {
                "http://opcfoundation.org/UA-Profile/Server/StandardUA2017" };
            ShutdownDelay = 5;
            m_serverCapabilities = new string[] { "DA" };
            m_supportedPrivateKeyFormats = new string[] { "PFX", "PEM" };
            MaxTrustListSize = 0;
            MultiCastDnsEnabled = false;
            AuditingEnabled = false;
            HttpsMutualTls = true;
            DurableSubscriptionsEnabled = false;
            MaxDurableNotificationQueueSize = 200000;
            MaxDurableEventQueueSize = 200000;
            MaxDurableSubscriptionLifetimeInHours = 10;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing]
        public new void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// The user tokens accepted by the server.
        /// </summary>
        /// <value>The user token policies.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 3)]
        public UserTokenPolicyCollection UserTokenPolicies
        {
            get => m_userTokenPolicies;
            set => m_userTokenPolicies = value ?? [];
        }

        /// <summary>
        /// Whether diagnostics are enabled.
        /// </summary>
        /// <value><c>true</c> if diagnostic is enabled; otherwise, <c>false</c>.</value>
        [DataMember(IsRequired = false, Order = 4)]
        public bool DiagnosticsEnabled { get; set; }

        /// <summary>
        /// The maximum number of open sessions.
        /// </summary>
        /// <value>The maximum session count.</value>
        [DataMember(IsRequired = false, Order = 5)]
        public int MaxSessionCount { get; set; }

        /// <summary>
        /// The maximum number of supported secure channels.
        /// </summary>
        /// <value>The channel lifetime.</value>
        [DataMember(IsRequired = false, Order = 6)]
        public int MaxChannelCount { get; set; }

        /// <summary>
        /// That minimum period of that a session is allowed to remain
        /// open without communication from the client (in milliseconds).
        /// </summary>
        /// <value>The minimum session timeout.</value>
        [DataMember(IsRequired = false, Order = 7)]
        public int MinSessionTimeout { get; set; }

        /// <summary>
        /// That maximum period of that a session is allowed to remain
        /// open without communication from the client (in milliseconds).
        /// </summary>
        /// <value>The maximum session timeout.</value>
        [DataMember(IsRequired = false, Order = 8)]
        public int MaxSessionTimeout { get; set; }

        /// <summary>
        /// The maximum number of continuation points used for
        /// Browse/BrowseNext operations.
        /// </summary>
        /// <value>The maximum number of continuation points used for Browse/BrowseNext operations</value>
        [DataMember(IsRequired = false, Order = 9)]
        public int MaxBrowseContinuationPoints { get; set; }

        /// <summary>
        /// The maximum number of continuation points used for
        /// Query/QueryNext operations.
        /// </summary>
        /// <value>The maximum number of query continuation points.</value>
        [DataMember(IsRequired = false, Order = 10)]
        public int MaxQueryContinuationPoints { get; set; }

        /// <summary>
        /// The maximum number of continuation points used for HistoryRead operations.
        /// </summary>
        /// <value>The maximum number of  history continuation points.</value>
        [DataMember(IsRequired = false, Order = 11)]
        public int MaxHistoryContinuationPoints { get; set; }

        /// <summary>
        /// The maximum age of an incoming request (old requests are rejected) (in milliseconds).
        /// </summary>
        /// <value>The maximum age of an incoming request.</value>
        [DataMember(IsRequired = false, Order = 12)]
        public int MaxRequestAge { get; set; }

        /// <summary>
        /// The minimum publishing interval supported by the server (in milliseconds).
        /// </summary>
        /// <value>The minimum publishing interval.</value>
        [DataMember(IsRequired = false, Order = 13)]
        public int MinPublishingInterval { get; set; }

        /// <summary>
        /// The maximum publishing interval supported by the server (in milliseconds).
        /// </summary>
        /// <value>The maximum publishing interval.</value>
        [DataMember(IsRequired = false, Order = 14)]
        public int MaxPublishingInterval { get; set; }

        /// <summary>
        /// The minimum difference between supported publishing interval (in milliseconds).
        /// </summary>
        /// <value>The publishing resolution.</value>
        [DataMember(IsRequired = false, Order = 15)]
        public int PublishingResolution { get; set; }

        /// <summary>
        /// How long the subscriptions will remain open without a publish from the client.
        /// </summary>
        /// <value>The maximum subscription lifetime.</value>
        [DataMember(IsRequired = false, Order = 16)]
        public int MaxSubscriptionLifetime { get; set; }

        /// <summary>
        /// The maximum number of messages saved in the queue for each subscription.
        /// </summary>
        /// <value>The maximum size of the  message queue.</value>
        [DataMember(IsRequired = false, Order = 17)]
        public int MaxMessageQueueSize { get; set; }

        /// <summary>
        /// The maximum number of notificates saved in the queue for each monitored item.
        /// </summary>
        /// <value>The maximum size of the notification queue.</value>
        [DataMember(IsRequired = false, Order = 18)]
        public int MaxNotificationQueueSize { get; set; }

        /// <summary>
        /// The maximum number of notifications per publish.
        /// </summary>
        /// <value>The maximum number of notifications per publish.</value>
        [DataMember(IsRequired = false, Order = 19)]
        public int MaxNotificationsPerPublish { get; set; }

        /// <summary>
        /// The minimum sampling interval for metadata.
        /// </summary>
        /// <value>The minimum sampling interval for metadata.</value>
        [DataMember(IsRequired = false, Order = 20)]
        public int MinMetadataSamplingInterval { get; set; }

        /// <summary>
        /// The available sampling rates.
        /// </summary>
        /// <value>The available sampling rates.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 21)]
        public SamplingRateGroupCollection AvailableSamplingRates { get; set; }

        /// <summary>
        /// The endpoint description for the registration endpoint.
        /// </summary>
        /// <value>The registration endpoint.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 22)]
        public EndpointDescription RegistrationEndpoint { get; set; }

        /// <summary>
        /// The maximum time between registration attempts (in milliseconds).
        /// </summary>
        /// <value>The maximum time between registration attempts (in milliseconds).</value>
        [DataMember(IsRequired = false, Order = 23)]
        public int MaxRegistrationInterval { get; set; }

        /// <summary>
        /// The path to the file containing nodes persisted by the core node manager.
        /// </summary>
        /// <value>The path to the file containing nodes persisted by the core node manager.</value>
        [DataMember(IsRequired = false, Order = 24)]
        public string NodeManagerSaveFile { get; set; }

        /// <summary>
        /// The minimum lifetime for a subscription (in milliseconds).
        /// </summary>
        /// <value>The minimum lifetime for a subscription.</value>
        [DataMember(IsRequired = false, Order = 25)]
        public int MinSubscriptionLifetime { get; set; }

        /// <summary>
        /// The max publish request count.
        /// </summary>
        /// <value>The max publish request count.</value>
        [DataMember(IsRequired = false, Order = 26)]
        public int MaxPublishRequestCount { get; set; }

        /// <summary>
        /// The max subscription count.
        /// </summary>
        /// <value>The max subscription count.</value>
        [DataMember(IsRequired = false, Order = 27)]
        public int MaxSubscriptionCount { get; set; }

        /// <summary>
        /// The max size of the event queue.
        /// </summary>
        /// <value>The max size of the event queue.</value>
        [DataMember(IsRequired = false, Order = 28)]
        public int MaxEventQueueSize { get; set; }

        /// <summary>
        /// The server profile array.
        /// </summary>
        /// <value>The array of server profiles.</value>
        [DataMember(IsRequired = false, Order = 29)]
        public StringCollection ServerProfileArray
        {
            get => m_serverProfileArray;
            set => m_serverProfileArray = value ?? [];
        }

        /// <summary>
        /// The server shutdown delay.
        /// </summary>
        /// <value>The number of seconds to delay the shutdown if a client is connected.</value>
        [DataMember(IsRequired = false, Order = 30)]
        public int ShutdownDelay { get; set; }

        /// <summary>
        /// The server capabilities.
        /// The latest set of server capabilities is listed
        /// <see href="http://www.opcfoundation.org/UA/schemas/1.05/ServerCapabilities.csv">here.</see>
        /// </summary>
        /// <value>The array of server capabilites.</value>
        [DataMember(IsRequired = false, Order = 31)]
        public StringCollection ServerCapabilities
        {
            get => m_serverCapabilities;
            set => m_serverCapabilities = value ?? [];
        }

        /// <summary>
        /// Gets or sets the supported private key format.
        /// </summary>
        /// <value>The array of server profiles.</value>
        [DataMember(IsRequired = false, Order = 32)]
        public StringCollection SupportedPrivateKeyFormats
        {
            get => m_supportedPrivateKeyFormats;
            set => m_supportedPrivateKeyFormats = value ?? [];
        }

        /// <summary>
        /// Gets or sets the max size of the trust list.
        /// </summary>
        [DataMember(IsRequired = false, Order = 33)]
        public int MaxTrustListSize { get; set; }

        /// <summary>
        /// Gets or sets if multicast DNS is enabled.
        /// </summary>
        [DataMember(IsRequired = false, Order = 34)]
        public bool MultiCastDnsEnabled { get; set; }

        /// <summary>
        /// Gets or sets reverse connect server configuration.
        /// </summary>
        [DataMember(IsRequired = false, Order = 35)]
        public ReverseConnectServerConfiguration ReverseConnect { get; set; }

        /// <summary>
        /// Gets or sets the operation limits of the OPC UA Server.
        /// </summary>
        [DataMember(IsRequired = false, Order = 36)]
        public OperationLimits OperationLimits { get; set; }

        /// <summary>
        /// Whether auditing is enabled.
        /// </summary>
        /// <value><c>true</c> if auditing is enabled; otherwise, <c>false</c>.</value>
        [DataMember(IsRequired = false, Order = 37)]
        public bool AuditingEnabled { get; set; }

        /// <summary>
        /// Whether mTLS is required/enforced by the HttpsTransportListener
        /// </summary>
        /// <value><c>true</c> if mutual TLS is enabled; otherwise, <c>false</c>.</value>
        [DataMember(IsRequired = false, Order = 38)]
        public bool HttpsMutualTls { get; set; }

        /// <summary>
        /// Enable / disable support for durable subscriptions
        /// </summary>
        /// <value><c>true</c> if durable subscriptions are enabled; otherwise, <c>false</c>.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 39)]
        public bool DurableSubscriptionsEnabled { get; set; }

        /// <summary>
        /// The maximum number of notifications saved in the durable queue for each monitored item.
        /// </summary>
        /// <value>The maximum size of the durable notification queue.</value>
        [DataMember(IsRequired = false, Order = 40)]
        public int MaxDurableNotificationQueueSize { get; set; }

        /// <summary>
        /// The max size of the durable event queue.
        /// </summary>
        /// <value>The max size of the durable event queue.</value>
        [DataMember(IsRequired = false, Order = 41)]
        public int MaxDurableEventQueueSize { get; set; }

        /// <summary>
        /// How long the durable subscriptions will remain open without a publish from the client.
        /// </summary>
        /// <value>The maximum durable subscription lifetime.</value>
        [DataMember(IsRequired = false, Order = 42)]
        public int MaxDurableSubscriptionLifetimeInHours { get; set; }

        private UserTokenPolicyCollection m_userTokenPolicies;
        private StringCollection m_serverProfileArray;
        private StringCollection m_serverCapabilities;
        private StringCollection m_supportedPrivateKeyFormats;
    }

    /// <summary>
    /// Stores the configuration of the reverse connections.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ReverseConnectServerConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ReverseConnectServerConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            ConnectInterval = 15000;
            ConnectTimeout = 30000;
            RejectTimeout = 60000;
        }

        /// <summary>
        /// A collection of reverse connect clients.
        /// </summary>
        [DataMember(Order = 10)]
        public ReverseConnectClientCollection Clients { get; set; }

        /// <summary>
        /// The interval after which a new reverse connection is attempted.
        /// </summary>
        [DataMember(Order = 20)]
        public int ConnectInterval { get; set; }

        /// <summary>
        /// The default timeout to wait for a response to a reverse connection.
        /// </summary>
        [DataMember(Order = 30)]
        public int ConnectTimeout { get; set; }

        /// <summary>
        /// The timeout to wait to establish a new reverse
        /// connection after a rejected attempt.
        /// </summary>
        [DataMember(Order = 40)]
        public int RejectTimeout { get; set; }
    }

    /// <summary>
    /// Stores the operation limits of a OPC UA Server.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class OperationLimits
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public OperationLimits()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            MaxNodesPerRead = 0;
            MaxNodesPerHistoryReadData = 0;
            MaxNodesPerHistoryReadEvents = 0;
            MaxNodesPerWrite = 0;
            MaxNodesPerHistoryUpdateData = 0;
            MaxNodesPerHistoryUpdateEvents = 0;
            MaxNodesPerMethodCall = 0;
            MaxNodesPerBrowse = 0;
            MaxNodesPerRegisterNodes = 0;
            MaxNodesPerTranslateBrowsePathsToNodeIds = 0;
            MaxNodesPerNodeManagement = 0;
            MaxMonitoredItemsPerCall = 0;
        }

        /// <summary>
        /// Indicates the maximum size of the nodesToRead array when a Client calls the Read Service.
        /// </summary>
        [DataMember(Order = 10)]
        public uint MaxNodesPerRead { get; set; }

        /// <summary>
        /// Indicates the maximum size of the nodesToRead array when a Client calls the HistoryRead
        /// Service using the historyReadDetails RAW, PROCESSED, MODIFIED or ATTIME.
        /// </summary>
        [DataMember(Order = 20)]
        public uint MaxNodesPerHistoryReadData { get; set; }

        /// <summary>
        /// Indicates the maximum size of the nodesToRead array when a Client calls the HistoryRead
        /// Service using the historyReadDetails EVENTS.
        /// </summary>
        [DataMember(Order = 30)]
        public uint MaxNodesPerHistoryReadEvents { get; set; }

        /// <summary>
        /// Indicates the maximum size of the nodesToWrite array when a Client calls the Write Service.
        /// </summary>
        [DataMember(Order = 40)]
        public uint MaxNodesPerWrite { get; set; }

        /// <summary>
        /// Indicates the maximum size of the historyUpdateDetails array supported by the Server
        /// when a Client calls the HistoryUpdate Service.
        /// </summary>
        [DataMember(Order = 50)]
        public uint MaxNodesPerHistoryUpdateData { get; set; }

        /// <summary>
        /// Indicates the maximum size of the historyUpdateDetails array
        /// when a Client calls the HistoryUpdate Service.
        /// </summary>
        [DataMember(Order = 60)]
        public uint MaxNodesPerHistoryUpdateEvents { get; set; }

        /// <summary>
        /// Indicates the maximum size of the methodsToCall array when a Client calls the Call Service.
        /// </summary>
        [DataMember(Order = 70)]
        public uint MaxNodesPerMethodCall { get; set; }

        /// <summary>
        /// Indicates the maximum size of the nodesToBrowse array when calling the Browse Service
        /// or the continuationPoints array when a Client calls the BrowseNext Service.
        /// </summary>
        [DataMember(Order = 80)]
        public uint MaxNodesPerBrowse { get; set; }

        /// <summary>
        /// Indicates the maximum size of the nodesToRegister array when a Client calls the RegisterNodes Service
        /// and the maximum size of the nodesToUnregister when calling the UnregisterNodes Service.
        /// </summary>
        [DataMember(Order = 90)]
        public uint MaxNodesPerRegisterNodes { get; set; }

        /// <summary>
        /// Indicates the maximum size of the browsePaths array when a Client calls the TranslateBrowsePathsToNodeIds Service.
        /// </summary>
        [DataMember(Order = 100)]
        public uint MaxNodesPerTranslateBrowsePathsToNodeIds { get; set; }

        /// <summary>
        /// Indicates the maximum size of the nodesToAdd array when a Client calls the AddNodes Service,
        /// the maximum size of the referencesToAdd array when a Client calls the AddReferences Service,
        /// the maximum size of the nodesToDelete array when a Client calls the DeleteNodes Service,
        /// and the maximum size of the referencesToDelete array when a Client calls the DeleteReferences Service.
        /// </summary>
        [DataMember(Order = 110)]
        public uint MaxNodesPerNodeManagement { get; set; }

        /// <summary>
        /// Indicates the maximum size of the itemsToCreate array when a Client calls the CreateMonitoredItems Service,
        /// the maximum size of the itemsToModify array when a Client calls the ModifyMonitoredItems Service,
        /// the maximum size of the monitoredItemIds array when a Client calls the SetMonitoringMode Service or the DeleteMonitoredItems Service,
        /// the maximum size of the sum of the linksToAdd and linksToRemove arrays when a Client calls the SetTriggering Service.
        /// </summary>
        [DataMember(Order = 120)]
        public uint MaxMonitoredItemsPerCall { get; set; }
    }

    /// <summary>
    /// Stores the configuration of the reverse connections.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ReverseConnectClient
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ReverseConnectClient()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            Enabled = true;
        }

        /// <summary>
        /// The endpoint Url of the reverse connect client endpoint.
        /// </summary>
        [DataMember(Order = 10)]
        public string EndpointUrl { get; set; }

        /// <summary>
        /// The timeout to wait for a response to a reverse connection.
        /// Overrides the default reverse connection setting.
        /// </summary>
        [DataMember(Order = 20)]
        public int Timeout { get; set; }

        /// <summary>
        /// The maximum count of active reverse connect sessions.
        ///  0 or undefined means unlimited number of sessions.
        ///  1 means a single connection is created at a time.
        ///  n disables reverse hello once the total number of sessions
        ///  in the server reaches n.
        /// </summary>
        [DataMember(Order = 30)]
        public int MaxSessionCount { get; set; }

        /// <summary>
        /// Specifies whether the sending of reverse connect attempts is enabled.
        /// </summary>
        [DataMember(Order = 40)]
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// A collection of reverse connect clients.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfReverseConnectClient",
        Namespace = Namespaces.OpcUaConfig,
        ItemName = "ReverseConnectClient"
    )]
    public class ReverseConnectClientCollection : List<ReverseConnectClient>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ReverseConnectClientCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public ReverseConnectClientCollection(IEnumerable<ReverseConnectClient> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public ReverseConnectClientCollection(int capacity)
            : base(capacity)
        {
        }
    }

    /// <summary>
    /// The configuration for a client application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class ClientConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ClientConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            DefaultSessionTimeout = 60000;
            MinSubscriptionLifetime = 10000;
            m_wellKnownDiscoveryUrls = [];
            m_discoveryServers = [];
            OperationLimits = new OperationLimits();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// The default session timeout (in milliseconds).
        /// </summary>
        /// <value>The default session timeout.</value>
        [DataMember(IsRequired = false, Order = 0)]
        public int DefaultSessionTimeout { get; set; }

        /// <summary>
        /// The well known URLs for the local discovery servers.
        /// </summary>
        /// <value>The well known discovery URLs.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 1)]
        public StringCollection WellKnownDiscoveryUrls
        {
            get => m_wellKnownDiscoveryUrls;
            set => m_wellKnownDiscoveryUrls = value ?? [];
        }

        /// <summary>
        /// The endpoint descriptions for central discovery servers.
        /// </summary>
        /// <value>The endpoint descriptions for central discovery servers.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 2)]
        public EndpointDescriptionCollection DiscoveryServers
        {
            get => m_discoveryServers;
            set => m_discoveryServers = value ?? [];
        }

        /// <summary>
        /// The path to the file containing the cached endpoints.
        /// </summary>
        /// <value>The path to the file containing the cached endpoints.</value>
        [DataMember(IsRequired = false, Order = 3)]
        public string EndpointCacheFilePath { get; set; }

        /// <summary>
        /// The minimum lifetime for a subscription (in milliseconds).
        /// </summary>
        /// <value>The minimum lifetime for a subscription.</value>
        [DataMember(IsRequired = false, Order = 4)]
        public int MinSubscriptionLifetime { get; set; }

        /// <summary>
        /// The reverse connect Client configuration.
        /// </summary>
        [DataMember(IsRequired = false, Order = 5)]
        public ReverseConnectClientConfiguration ReverseConnect { get; set; }

        /// <summary>
        /// Gets or sets the default operation limits of the OPC UA client.
        /// </summary>
        /// <remarks>
        /// Values not equal to zero are overwritten with smaller values set by the server.
        /// The values are used to limit client service calls.
        /// </remarks>
        [DataMember(IsRequired = false, Order = 6)]
        public OperationLimits OperationLimits { get; set; }

        private StringCollection m_wellKnownDiscoveryUrls;
        private EndpointDescriptionCollection m_discoveryServers;
    }

    /// <summary>
    /// Stores the configuration of the reverse connections.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ReverseConnectClientConfiguration
    {
        /// <summary>
        /// A collection of reverse connect client endpoints.
        /// </summary>
        [DataMember(Order = 10, IsRequired = false)]
        public ReverseConnectClientEndpointCollection ClientEndpoints { get; set; }

        /// <summary>
        /// The time a reverse hello port is held open to wait for a
        /// reverse connection until the request is rejected.
        /// </summary>
        [DataMember(Order = 20, IsRequired = false)]
        public int HoldTime { get; set; } = 15000;

        /// <summary>
        /// The timeout to wait for a reverse hello message.
        /// </summary>
        [DataMember(Order = 30, IsRequired = false)]
        public int WaitTimeout { get; set; } = 20000;
    }

    /// <summary>
    /// Stores the configuration of the reverse connections.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ReverseConnectClientEndpoint
    {
        /// <summary>
        /// The endpoint Url of a reverse connect client.
        /// </summary>
        [DataMember(Order = 1, IsRequired = false)]
        public string EndpointUrl { get; set; }
    }

    /// <summary>
    /// A collection of reverse connect client endpoints.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfReverseConnectClientEndpoint",
        Namespace = Namespaces.OpcUaConfig,
        ItemName = "ClientEndpoint"
    )]
    public class ReverseConnectClientEndpointCollection : List<ReverseConnectClientEndpoint>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ReverseConnectClientEndpointCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public ReverseConnectClientEndpointCollection(
            IEnumerable<ReverseConnectClientEndpoint> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public ReverseConnectClientEndpointCollection(int capacity)
            : base(capacity)
        {
        }
    }

    /// <summary>
    /// Specifies the configuration for a discovery server application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class DiscoveryServerConfiguration : ServerBaseConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public DiscoveryServerConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_serverNames = [];
            ServerRegistrations = [];
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing]
        public new void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// The localized names for the discovery server.
        /// </summary>
        /// <value>The server names.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 2)]
        public LocalizedTextCollection ServerNames
        {
            get => m_serverNames;
            set => m_serverNames = value ?? [];
        }

        /// <summary>
        /// The path to the file containing servers saved by the discovery server.
        /// </summary>
        /// <value>The discovery server cache file.</value>
        [DataMember(IsRequired = false, Order = 3)]
        public string DiscoveryServerCacheFile { get; set; }

        /// <summary>
        /// Gets or sets the server registrations associated with the discovery server.
        /// </summary>
        /// <value>The server registrations.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 4)]
        public ServerRegistrationCollection ServerRegistrations { get; set; }

        private LocalizedTextCollection m_serverNames;
    }

    /// <summary>
    /// Specifies the configuration for a discovery server application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ServerRegistration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ServerRegistration()
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            ApplicationUri = null;
            m_alternateDiscoveryUrls = [];
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Gets or sets the application URI of the server which the registration applies to.
        /// </summary>
        /// <value>The application uri.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 1)]
        public string ApplicationUri { get; set; }

        /// <summary>
        /// Gets or sets the alternate discovery urls.
        /// </summary>
        /// <value>The alternate discovery urls.</value>
        /// <remarks>
        /// <para>
        /// These addresses are used to specify alternate paths to ther via firewalls, proxies
        /// or similar network infrastructure. If these paths are specified in the configuration
        /// file then the server will use the domain of the URL used by the client to determine
        /// which, if any, or the alternate addresses to use instead of the primary addresses.
        /// </para>
        /// <para>
        /// In the ideal world the server would provide these URLs during registration but this
        /// table allows the administrator to provide the information to the discovery server
        /// directly without requiring a patch to the server.
        /// </para>
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 2)]
        public StringCollection AlternateDiscoveryUrls
        {
            get => m_alternateDiscoveryUrls;
            set => m_alternateDiscoveryUrls = value ?? [];
        }

        private StringCollection m_alternateDiscoveryUrls;
    }

    /// <summary>
    /// A collection of AdditionalServerRegistrationInfo objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfServerRegistration",
        Namespace = Namespaces.OpcUaConfig,
        ItemName = "ServerRegistration"
    )]
    public class ServerRegistrationCollection : List<ServerRegistration>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ServerRegistrationCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public ServerRegistrationCollection(IEnumerable<ServerRegistration> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public ServerRegistrationCollection(int capacity)
            : base(capacity)
        {
        }
    }

    /// <summary>
    /// Describes a certificate store.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class CertificateStoreIdentifier
    {
        /// <summary>
        /// The type of certificate store.
        /// </summary>
        /// <value>
        /// If the StoreName is not empty, the CertificateStoreType.X509Store is returned, otherwise the StoreType is returned.
        /// </value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 0)]
        public string StoreType { get; set; }

        /// <summary>
        /// The path that identifies the certificate store.
        /// </summary>
        /// <value>
        /// If the StoreName is not empty and the StoreLocation is empty, the Utils.Format("CurrentUser\\{0}", m_storeName) is returned.
        /// If the StoreName is not empty and the StoreLocation is not empty, the Utils.Format("{1}\\{0}", m_storeName, m_storeLocation) is returned.
        /// If the StoreName is empty, the m_storePath is returned.
        /// </value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 1)]
        public string StorePath
        {
            get => m_storePath;
            set
            {
                m_storePath = value;

                if (!string.IsNullOrEmpty(m_storePath) && string.IsNullOrEmpty(StoreType))
                {
                    StoreType = DetermineStoreType(m_storePath);
                }
            }
        }

        /// <summary>
        /// Options that can be used to suppress certificate validation errors.
        /// </summary>
        [DataMember(
            Name = "ValidationOptions",
            IsRequired = false,
            EmitDefaultValue = false,
            Order = 4)]
        internal int XmlEncodedValidationOptions
        {
            get => (int)ValidationOptions;
            set => ValidationOptions = (CertificateValidationOptions)value;
        }

        private string m_storePath;
    }

    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    [KnownType(typeof(CertificateTrustList))]
    public partial class CertificateTrustList : CertificateStoreIdentifier
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public CertificateTrustList()
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_trustedCertificates = [];
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// The list of trusted certificates.
        /// </summary>
        /// <value>
        /// The list of trusted certificates is set when TrustedCertificates is not a null value,
        /// otherwise new CertificateIdentifierCollection is set.
        /// </value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 3)]
        public CertificateIdentifierCollection TrustedCertificates
        {
            get => m_trustedCertificates;
            set => m_trustedCertificates = value ?? [];
        }

        private CertificateIdentifierCollection m_trustedCertificates;
    }

    [CollectionDataContract(
        Name = "ApplicationCertificates",
        Namespace = Namespaces.OpcUaConfig,
        ItemName = "CertificateIdentifier"
    )]
    public partial class CertificateIdentifierCollection : List<CertificateIdentifier>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public CertificateIdentifierCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        public CertificateIdentifierCollection(IEnumerable<CertificateIdentifier> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        public CertificateIdentifierCollection(int capacity)
            : base(capacity)
        {
        }
    }

    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class CertificateIdentifier
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public CertificateIdentifier()
        {
        }

        /// <summary>
        /// Initializes the identifier with the raw data from a certificate.
        /// </summary>
        public CertificateIdentifier(X509Certificate2 certificate)
        {
            Certificate = certificate;
        }

        /// <summary>
        /// Initializes the identifier with the raw data from a certificate.
        /// </summary>
        public CertificateIdentifier(
            X509Certificate2 certificate,
            CertificateValidationOptions validationOptions)
        {
            Certificate = certificate;
            ValidationOptions = validationOptions;
        }

        /// <summary>
        /// Initializes the identifier with the raw data from a certificate.
        /// </summary>
        public CertificateIdentifier(byte[] rawData)
        {
            Certificate = CertificateFactory.Create(rawData);
        }

        /// <summary>
        /// The type of certificate store.
        /// </summary>
        /// <value>The type of the store - defined in the <see cref="CertificateStoreType"/>.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 10)]
        public string StoreType { get; set; }

        /// <summary>
        /// The path that identifies the certificate store.
        /// </summary>
        /// <value>The store path in the form <c>StoreName\\Store Location</c> .</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 15)]
        public string StorePath
        {
            get => m_storePath;
            set
            {
                m_storePath = value;

                if (!string.IsNullOrEmpty(m_storePath) && string.IsNullOrEmpty(StoreType))
                {
                    StoreType = CertificateStoreIdentifier.DetermineStoreType(m_storePath);
                }
            }
        }

        /// <summary>
        /// The certificate's subject name - the distinguished name of an X509 certificate.
        /// </summary>
        /// <value>
        /// The distinguished name of an X509 certificate acording to the Abstract Syntax Notation One (ASN.1) syntax.
        /// </value>
        /// <remarks> The subject field identifies the entity associated with the public key stored in the subject public
        /// key field.  The subject name MAY be carried in the subject field and/or the subjectAltName extension.
        /// Where it is non-empty, the subject field MUST contain an X.500 distinguished name (DN).
        /// Name is defined by the following ASN.1 structures:
        /// Name ::= CHOICE {RDNSequence }
        /// RDNSequence ::= SEQUENCE OF RelativeDistinguishedName
        /// RelativeDistinguishedName ::= SET OF AttributeTypeAndValue
        /// AttributeTypeAndValue ::= SEQUENCE {type     AttributeType, value    AttributeValue }
        /// AttributeType ::= OBJECT IDENTIFIER
        /// AttributeValue ::= ANY DEFINED BY AttributeType
        /// DirectoryString ::= CHOICE {
        ///   teletexString           TeletexString (SIZE (1..MAX)),
        ///   printableString         PrintableString (SIZE (1..MAX)),
        ///   universalString         UniversalString (SIZE (1..MAX)),
        ///   utf8String              UTF8String (SIZE (1..MAX)),
        ///   bmpString               BMPString (SIZE (1..MAX)) }
        ///  The Name describes a hierarchical name composed of attributes, such as country name, and
        ///  corresponding values, such as US.  The type of the component AttributeValue is determined by
        ///  the AttributeType; in general it will be a DirectoryString.
        /// String X.500 AttributeType:
        /// <list type="bullet">
        /// <item>CN commonName</item>
        /// <item>L localityName</item>
        /// <item>ST stateOrProvinceName</item>
        /// <item>O organizationName</item>
        /// <item>OU organizationalUnitName</item>
        /// <item>C countryName</item>
        /// <item>STREET streetAddress</item>
        /// <item>DC domainComponent</item>
        /// <item>UID userid</item>
        /// </list>
        /// <para>
        /// This notation is designed to be convenient for common forms of name. This section gives a few
        /// examples of distinguished names written using this notation. First is a name containing three relative
        /// distinguished names (RDNs):
        /// <c>CN=Steve Kille,O=Isode Limited,C=GB</c>
        /// </para>
        /// <para>
        /// RFC 3280 Internet X.509 Public Key Infrastructure, April 2002
        /// RFC 2253 LADPv3 Distinguished Names, December 1997
        /// </para>
        /// </remarks>
        /// <seealso cref="X500DistinguishedName"/>
        /// <seealso cref="System.Security.Cryptography.AsnEncodedData"/>
        /// <exception cref="ArgumentException"></exception>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 40)]
        public string SubjectName
        {
            get
            {
                if (m_certificate == null)
                {
                    return m_subjectName;
                }

                return m_certificate.Subject;
            }
            set
            {
                if (m_certificate != null &&
                    !string.IsNullOrEmpty(value) &&
                    m_certificate.Subject != value)
                {
                    throw new ArgumentException(
                        "SubjectName does not match the SubjectName of the current certificate.");
                }

                m_subjectName = value;
            }
        }

        /// <summary>
        /// The certificate's thumbprint.
        /// </summary>
        /// <value>The thumbprint of a certificate..</value>
        /// <seealso cref="X509Certificate2"/>
        /// <exception cref="ArgumentException"></exception>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 50)]
        public string Thumbprint
        {
            get
            {
                if (m_certificate == null)
                {
                    return m_thumbprint;
                }

                return m_certificate.Thumbprint;
            }
            set
            {
                if (m_certificate != null &&
                    !string.IsNullOrEmpty(value) &&
                    m_certificate.Thumbprint != value)
                {
                    throw new ArgumentException(
                        "Thumbprint does not match the thumbprint of the current certificate.");
                }

                m_thumbprint = value;
            }
        }

        /// <summary>
        /// Gets the DER encoded certificate data or create embedded in this instance certificate using the DER encoded certificate data.
        /// </summary>
        /// <value>A byte array containing the X.509 certificate data.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 60)]
        public byte[] RawData
        {
            get
            {
                if (m_certificate == null)
                {
                    return null;
                }

                return m_certificate.RawData;
            }
            set
            {
                if (value == null || value.Length == 0)
                {
                    m_certificate = null;
                    return;
                }

                m_certificate = CertificateFactory.Create(value);
                m_subjectName = m_certificate.Subject;
                m_thumbprint = m_certificate.Thumbprint;
                CertificateType = GetCertificateType(m_certificate);
            }
        }

        /// <summary>
        /// Gets or sets the XML encoded validation options - use to serialize the validation options.
        /// </summary>
        /// <value>The XML encoded validation options.</value>
        [DataMember(
            Name = "ValidationOptions",
            IsRequired = false,
            EmitDefaultValue = false,
            Order = 70)]
        internal int XmlEncodedValidationOptions
        {
            get => (int)ValidationOptions;
            set => ValidationOptions = (CertificateValidationOptions)value;
        }

        /// <summary>
        /// Gets or sets the certificate type.
        /// </summary>
        /// <value>The NodeId of the certificate type, e.g. EccNistP256ApplicationCertificateType.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 80)]
        public NodeId CertificateType { get; set; }

        /// <summary>
        /// The string representation of the certificate
        /// </summary>
        /// <value>Rsa, RsaMin, RsaSha256, NistP256, NistP384, BrainpoolP256r1, BrainpoolP384r1, Curve25519, Curve448</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 90)]
        public string CertificateTypeString
        {
            get => EncodeCertificateType(CertificateType);
            set => CertificateType = DecodeCertificateType(value);
        }

        private string m_storePath;
        private string m_subjectName;
        private string m_thumbprint;
        private X509Certificate2 m_certificate;
    }

    /// <summary>
    /// Stores a list of cached endpoints.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class ConfiguredEndpointCollection
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ConfiguredEndpointCollection()
        {
            Initialize();
        }

        /// <summary>
        /// Called by the .NET framework during deserialization.
        /// </summary>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_knownHosts = [];
            m_discoveryUrls = [.. Utils.DiscoveryUrls];
            m_endpoints = [];
            DefaultConfiguration = EndpointConfiguration.Create();
        }

        /// <summary>
        /// A list of known hosts that can be used for discovery.
        /// </summary>
        [DataMember(Name = "KnownHosts", IsRequired = false, Order = 1)]
        public StringCollection KnownHosts
        {
            get => m_knownHosts;
            set => m_knownHosts = value ?? [];
        }

        /// <summary>
        /// The default configuration to use when connecting to an endpoint.
        /// </summary>
        [DataMember(Name = "Endpoints", IsRequired = false, Order = 2)]
        public List<ConfiguredEndpoint> Endpoints
        {
            get => m_endpoints;
            private set
            {
                m_endpoints = value ?? [];

                foreach (ConfiguredEndpoint endpoint in m_endpoints)
                {
                    endpoint.Collection = this;
                }
            }
        }

        /// <summary>
        /// The URL of the UA TCP proxy server.
        /// </summary>
        [DataMember(Name = "TcpProxyUrl", EmitDefaultValue = false, Order = 3)]
        public Uri TcpProxyUrl { get; set; }

        private string m_filepath;
        private StringCollection m_knownHosts;
        private StringCollection m_discoveryUrls;
        private List<ConfiguredEndpoint> m_endpoints;
    }

    /// <summary>
    /// Stores the configuration information for an endpoint.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    [KnownType(typeof(UserNameIdentityToken))]
    [KnownType(typeof(X509IdentityToken))]
    [KnownType(typeof(IssuedIdentityToken))]
    public partial class ConfiguredEndpoint
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ConfiguredEndpoint()
        {
            Initialize();
        }

        /// <summary>
        /// Called by the .NET framework during deserialization.
        /// </summary>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_collection = null;
            m_description = new EndpointDescription();
            m_configuration = null;
            UpdateBeforeConnect = true;
            BinaryEncodingSupport = BinaryEncodingSupport.Optional;
            SelectedUserTokenPolicyIndex = 0;
            UserIdentity = null;
            ReverseConnect = null;
        }

        /// <summary>
        /// The description for the endpoint.
        /// </summary>
        [DataMember(Name = "Endpoint", Order = 1, IsRequired = true)]
        public EndpointDescription Description
        {
            get => m_description;
            private set => m_description = value ?? new EndpointDescription();
        }

        /// <summary>
        /// The configuration to use when connecting to an endpoint.
        /// </summary>
        [DataMember(Name = "Configuration", Order = 2, IsRequired = false)]
        public EndpointConfiguration Configuration
        {
            get => m_configuration;
            set
            {
                m_configuration = value;

                // copy default configuration if not already set.
                if (m_configuration == null)
                {
                    if (m_collection != null)
                    {
                        Update(m_collection.DefaultConfiguration);
                    }
                    else
                    {
                        Update(EndpointConfiguration.Create());
                    }
                }
            }
        }

        /// <summary>
        /// Whether the endpoint information should be updated before connecting to the server.
        /// </summary>
        [DataMember(Name = "UpdateBeforeConnect", Order = 3, IsRequired = false)]
        public bool UpdateBeforeConnect { get; set; }

        /// <summary>
        /// The user identity to use when connecting to the endpoint.
        /// </summary>
        [DataMember(Name = "BinaryEncodingSupport", Order = 4, IsRequired = false)]
        public BinaryEncodingSupport BinaryEncodingSupport { get; set; }

        /// <summary>
        /// The user identity to use when connecting to the endpoint.
        /// </summary>
        [DataMember(Name = "SelectedUserTokenPolicy", Order = 5, IsRequired = false)]
        public int SelectedUserTokenPolicyIndex { get; set; }

        /// <summary>
        /// The user identity to use when connecting to the endpoint.
        /// </summary>
        [DataMember(Name = "UserIdentity", Order = 6, IsRequired = false)]
        public UserIdentityToken UserIdentity { get; set; }

        /// <summary>
        /// The reverse connect information.
        /// </summary>
        [DataMember(Name = "ReverseConnect", Order = 8, IsRequired = false)]
        public ReverseConnectEndpoint ReverseConnect { get; set; }

        /// <summary>
        /// A bucket to store additional application specific configuration data.
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 9)]
        public XmlElementCollection Extensions
        {
            get => m_extensions;
            set => m_extensions = value;
        }

        private ConfiguredEndpointCollection m_collection;
        private EndpointDescription m_description;
        private EndpointConfiguration m_configuration;
        private XmlElementCollection m_extensions;
    }

    /// <summary>
    /// The type of binary encoding support allowed by a channel.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public enum BinaryEncodingSupport
    {
        /// <summary>
        /// The UA binary encoding may be used.
        /// </summary>
        [EnumMember]
        Optional,

        /// <summary>
        /// The UA binary encoding must be used.
        /// </summary>
        [EnumMember]
        Required,

        /// <summary>
        /// The UA binary encoding may not be used.
        /// </summary>
        [EnumMember]
        None
    }

    /// <summary>
    /// Stores the reverse connect information for an endpoint.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ReverseConnectEndpoint
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ReverseConnectEndpoint()
        {
            Initialize();
        }

        /// <summary>
        /// Called by the .NET framework during deserialization.
        /// </summary>
        [OnDeserializing]
        public void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            Enabled = false;
            ServerUri = null;
            Thumbprint = null;
        }

        /// <summary>
        /// Whether reverse connect is enabled for the endpoint.
        /// </summary>
        [DataMember(Name = "Enabled", Order = 1, IsRequired = false)]
        public bool Enabled { get; set; }

        /// <summary>
        /// The server Uri of the endpoint.
        /// </summary>
        [DataMember(Name = "ServerUri", Order = 2, IsRequired = false)]
        public string ServerUri { get; set; }

        /// <summary>
        /// The thumbprint of the certificate which contains
        /// the server Uri.
        /// </summary>
        [DataMember(Name = "Thumbprint", Order = 3, IsRequired = false)]
        public string Thumbprint { get; set; }
    }
}
