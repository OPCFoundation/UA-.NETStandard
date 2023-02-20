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
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    #region ApplicationConfiguration
    /// <summary>
    /// Stores the configurable configuration information for a UA application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class ApplicationConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ApplicationConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// The constructor from a template.
        /// </summary>
        public ApplicationConfiguration(ApplicationConfiguration template)
        {
            Initialize();

            m_applicationName = template.m_applicationName;
            m_applicationType = template.m_applicationType;
            m_applicationUri = template.m_applicationUri;
            m_discoveryServerConfiguration = template.m_discoveryServerConfiguration;
            m_securityConfiguration = template.m_securityConfiguration;
            m_transportConfigurations = template.m_transportConfigurations;
            m_serverConfiguration = template.m_serverConfiguration;
            m_clientConfiguration = template.m_clientConfiguration;
            m_disableHiResClock = template.m_disableHiResClock;
            m_certificateValidator = template.m_certificateValidator;
            m_transportQuotas = template.m_transportQuotas;
            m_traceConfiguration = template.m_traceConfiguration;
            m_extensions = template.m_extensions;
            m_extensionObjects = template.m_extensionObjects;
            m_sourceFilePath = template.m_sourceFilePath;
            m_messageContext = template.m_messageContext;
            m_properties = template.m_properties;
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_sourceFilePath = null;
            m_securityConfiguration = new SecurityConfiguration();
            m_transportConfigurations = new TransportConfigurationCollection();
            m_disableHiResClock = false;
            m_properties = new Dictionary<string, object>();
            m_certificateValidator = new CertificateValidator();
            m_extensionObjects = new List<object>();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing()]
        public void Initialize(StreamingContext context) => Initialize();
        #endregion

        #region Public Properties
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
        #endregion

        #region Persistent Properties
        /// <summary>
        /// A descriptive name for the application (not necessarily unique).
        /// </summary>
        /// <value>The name of the application.</value>
        [DataMember(IsRequired = true, EmitDefaultValue = false, Order = 0)]
        public string ApplicationName
        {
            get { return m_applicationName; }
            set { m_applicationName = value; }
        }

        /// <summary>
        /// A unique identifier for the application instance.
        /// </summary>
        /// <value>The application URI.</value>
        [DataMember(IsRequired = true, EmitDefaultValue = false, Order = 1)]
        public string ApplicationUri
        {
            get { return m_applicationUri; }
            set { m_applicationUri = value; }
        }

        /// <summary>
        /// A unique identifier for the product.
        /// </summary>
        /// <value>The product URI.</value>
        [DataMember(IsRequired = false, Order = 2)]
        public string ProductUri
        {
            get { return m_productUri; }
            set { m_productUri = value; }
        }

        /// <summary>
        /// The type of application.
        /// </summary>
        /// <value>The type of the application.</value>
        [DataMember(IsRequired = true, Order = 3)]
        public ApplicationType ApplicationType
        {
            get { return m_applicationType; }
            set { m_applicationType = value; }
        }

        /// <summary>
        /// The security configuration for the application.
        /// </summary>
        /// <value>The security configuration.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = true, Order = 4)]
        public SecurityConfiguration SecurityConfiguration
        {
            get
            {
                return m_securityConfiguration;
            }

            set
            {
                m_securityConfiguration = value ?? new SecurityConfiguration();
            }
        }

        /// <summary>
        /// The transport configuration for the application.
        /// </summary>
        /// <value>The transport configurations.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = true, Order = 5)]
        public TransportConfigurationCollection TransportConfigurations
        {
            get
            {
                return m_transportConfigurations;
            }

            set
            {
                m_transportConfigurations = value ?? new TransportConfigurationCollection();
            }
        }

        /// <summary>
        /// The quotas that are used at the transport layer.
        /// </summary>
        /// <value>The transport quotas.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = true, Order = 6)]
        public TransportQuotas TransportQuotas
        {
            get { return m_transportQuotas; }
            set { m_transportQuotas = value; }
        }

        /// <summary>
        /// Additional configuration for server applications.
        /// </summary>
        /// <value>The server configuration.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 7)]
        public ServerConfiguration ServerConfiguration
        {
            get { return m_serverConfiguration; }
            set { m_serverConfiguration = value; }
        }

        /// <summary>
        /// Additional configuration for client applications.
        /// </summary>
        /// <value>The client configuration.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 8)]
        public ClientConfiguration ClientConfiguration
        {
            get { return m_clientConfiguration; }
            set { m_clientConfiguration = value; }
        }

        /// <summary>
        /// Additional configuration of the discovery server.
        /// </summary>
        /// <value>The discovery server configuration.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 9)]
        public DiscoveryServerConfiguration DiscoveryServerConfiguration
        {
            get { return m_discoveryServerConfiguration; }
            set { m_discoveryServerConfiguration = value; }
        }

        /// <summary>
        /// A bucket to store additional application specific configuration data.
        /// </summary>
        /// <value>The extensions.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 10)]
        public XmlElementCollection Extensions
        {
            get { return m_extensions; }
            set { m_extensions = value; }
        }

        /// <summary>
        /// Configuration of the trace and information about log file
        /// </summary>
        /// <value>The trace configuration.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 11)]
        public TraceConfiguration TraceConfiguration
        {
            get { return m_traceConfiguration; }
            set { m_traceConfiguration = value; }
        }

        /// <summary>
        /// Disabling / enabling high resolution clock 
        /// </summary>
        /// <value><c>true</c> if high resolution clock is disabled; otherwise, <c>false</c>.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 12)]
        public bool DisableHiResClock
        {
            get { return m_disableHiResClock; }
            set { m_disableHiResClock = value; }
        }
        #endregion

        #region Private Fields
        private string m_applicationName;
        private string m_applicationUri;
        private string m_productUri;
        private ApplicationType m_applicationType;

        private SecurityConfiguration m_securityConfiguration;
        private TransportConfigurationCollection m_transportConfigurations;

        private TransportQuotas m_transportQuotas;
        private ServerConfiguration m_serverConfiguration;
        private ClientConfiguration m_clientConfiguration;
        private DiscoveryServerConfiguration m_discoveryServerConfiguration;
        private TraceConfiguration m_traceConfiguration;
        private bool m_disableHiResClock;
        private XmlElementCollection m_extensions;
        private List<object> m_extensionObjects;
        private string m_sourceFilePath;

        private IServiceMessageContext m_messageContext;
        private CertificateValidator m_certificateValidator;
        private Dictionary<string, object> m_properties;
        #endregion
    }
    #endregion

    #region TransportQuotas Class
    /// <summary>
    /// Specifies various limits that apply to the transport or secure channel layers.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class TransportQuotas
    {
        #region Constructors
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
            m_operationTimeout = 120000;
            m_maxStringLength = 65535;
            m_maxByteStringLength = 65535;
            m_maxArrayLength = 65535;
            m_maxMessageSize = 1048576;
            m_maxBufferSize = 65535;
            m_channelLifetime = 600000;
            m_securityTokenLifetime = 3600000;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing()]
        public void Initialize(StreamingContext context) => Initialize();
        #endregion

        #region Persistent Properties
        /// <summary>
        /// The default timeout to use when sending requests (in milliseconds).
        /// </summary>
        /// <value>The operation timeout.</value>
        [DataMember(IsRequired = false, Order = 0)]
        public int OperationTimeout
        {
            get { return m_operationTimeout; }
            set { m_operationTimeout = value; }
        }

        /// <summary>
        /// The maximum length of string encoded in a message body.
        /// </summary>
        /// <value>The max length of the string.</value>
        [DataMember(IsRequired = false, Order = 1)]
        public int MaxStringLength
        {
            get { return m_maxStringLength; }
            set { m_maxStringLength = value; }
        }

        /// <summary>
        /// The maximum length of a byte string encoded in a message body.
        /// </summary>
        /// <value>The max length of the byte string.</value>
        [DataMember(IsRequired = false, Order = 2)]
        public int MaxByteStringLength
        {
            get { return m_maxByteStringLength; }
            set { m_maxByteStringLength = value; }
        }

        /// <summary>
        /// The maximum length of an array encoded in a message body.
        /// </summary>
        /// <value>The max length of the array.</value>
        [DataMember(IsRequired = false, Order = 3)]
        public int MaxArrayLength
        {
            get { return m_maxArrayLength; }
            set { m_maxArrayLength = value; }
        }

        /// <summary>
        /// The maximum length of a message body.
        /// </summary>
        /// <value>The max size of the message.</value>
        [DataMember(IsRequired = false, Order = 4)]
        public int MaxMessageSize
        {
            get { return m_maxMessageSize; }
            set { m_maxMessageSize = value; }
        }

        /// <summary>
        /// The maximum size of the buffer to use when sending messages.
        /// </summary>
        /// <value>The max size of the buffer.</value>
        [DataMember(IsRequired = false, Order = 5)]
        public int MaxBufferSize
        {
            get { return m_maxBufferSize; }
            set { m_maxBufferSize = value; }
        }

        /// <summary>
        /// The lifetime of a secure channel (in milliseconds).
        /// </summary>
        /// <value>The channel lifetime.</value>
        [DataMember(IsRequired = false, Order = 6)]
        public int ChannelLifetime
        {
            get { return m_channelLifetime; }
            set { m_channelLifetime = value; }
        }

        /// <summary>
        /// The lifetime of a security token (in milliseconds).
        /// </summary>
        /// <value>The security token lifetime.</value>
        [DataMember(IsRequired = false, Order = 7)]
        public int SecurityTokenLifetime
        {
            get { return m_securityTokenLifetime; }
            set { m_securityTokenLifetime = value; }
        }
        #endregion

        #region Private Fields
        private int m_operationTimeout;
        private int m_maxStringLength;
        private int m_maxByteStringLength;
        private int m_maxArrayLength;
        private int m_maxMessageSize;
        private int m_maxBufferSize;
        private int m_channelLifetime;
        private int m_securityTokenLifetime;
        #endregion
    }
    #endregion

    #region TraceConfiguration Class
    /// <summary>
    /// Specifies parameters used for tracing.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class TraceConfiguration
    {
        #region Constructors
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
            m_outputFilePath = null;
            m_deleteOnLoad = false;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing()]
        public void Initialize(StreamingContext context) => Initialize();
        #endregion

        #region Persistent Properties
        /// <summary>
        /// The output file used to log the trace information.
        /// </summary>
        /// <value>The output file path.</value>
        [DataMember(IsRequired = false, Order = 0)]
        public string OutputFilePath
        {
            get { return m_outputFilePath; }
            set { m_outputFilePath = value; }
        }

        /// <summary>
        /// Whether the existing log file should be deleted when the application configuration is loaded.
        /// </summary>
        /// <value><c>true</c> if existing log file should be deleted when the application configuration is loaded; otherwise, <c>false</c>.</value>
        [DataMember(IsRequired = false, Order = 1)]
        public bool DeleteOnLoad
        {
            get { return m_deleteOnLoad; }
            set { m_deleteOnLoad = value; }
        }

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
        public int TraceMasks
        {
            get { return m_traceMasks; }
            set { m_traceMasks = value; }
        }
        #endregion

        #region Private Fields
        private string m_outputFilePath;
        private bool m_deleteOnLoad;
        private int m_traceMasks;
        #endregion
    }
    #endregion

    #region TransportConfiguration Class
    /// <summary>
    /// Specifies the configuration information for a transport protocol
    /// </summary>
    /// <remarks>
    /// Each application is allows to have one transport configure per protocol type.
    /// </remarks>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class TransportConfiguration
    {
        #region Constructors
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
            m_uriScheme = urlScheme;
            m_typeName = type.AssemblyQualifiedName;
        }
        #endregion

        #region Persistent Properties
        /// <summary>
        /// The URL prefix used by the application (http, opc.tcp, net.tpc, etc.).
        /// </summary>
        /// <value>The URI scheme.</value>
        [DataMember(IsRequired = true, EmitDefaultValue = false, Order = 0)]
        public string UriScheme
        {
            get { return m_uriScheme; }
            set { m_uriScheme = value; }
        }

        /// <summary>
        /// The name of the class that defines the binding for the transport.
        /// </summary>
        /// <value>The name of the type.</value>
        /// <remarks>
        /// This can be any instance of the System.ServiceModel.Channels.Binding class 
        /// that implements these constructors:
        /// 
        /// XxxBinding(EndpointDescription description, EndpointConfiguration configuration);
        /// XxxBinding(IList{EndpointDescription} descriptions, EndpointConfiguration configuration)
        /// XxxBinding(EndpointConfiguration configuration)
        /// </remarks>
        [DataMember(IsRequired = true, EmitDefaultValue = false, Order = 1)]
        public string TypeName
        {
            get { return m_typeName; }
            set { m_typeName = value; }
        }
        #endregion

        #region Private Fields
        private string m_uriScheme;
        private string m_typeName;
        #endregion
    }
    #endregion

    #region TransportConfigurationCollection Class
    /// <summary>
    /// A collection of TransportConfiguration objects.
    /// </summary>
    [CollectionDataContract(Name = "ListOfTransportConfiguration", Namespace = Namespaces.OpcUaConfig, ItemName = "TransportConfiguration")]
    public partial class TransportConfigurationCollection : List<TransportConfiguration>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public TransportConfigurationCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public TransportConfigurationCollection(IEnumerable<TransportConfiguration> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public TransportConfigurationCollection(int capacity) : base(capacity) { }
    }
    #endregion

    #region ServerSecurityPolicy Class
    /// <summary>
    /// A class that defines a group of sampling rates supported by the server.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ServerSecurityPolicy
    {
        #region Constructors
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
            m_securityMode = MessageSecurityMode.SignAndEncrypt;
            m_securityPolicyUri = SecurityPolicies.Basic256Sha256;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing()]
        public void Initialize(StreamingContext context) => Initialize();
        #endregion

        #region Public Properties
        /// <summary>
        /// Calculates the security level, given the security mode and policy
        /// Invalid and none is discouraged
        /// Just signing is always weaker than any use of encryption
        /// </summary>
        public static byte CalculateSecurityLevel(MessageSecurityMode mode, string policyUri)
        {
            if ((mode == MessageSecurityMode.Invalid) || (mode == MessageSecurityMode.None))
            {
                return 0;
            }

            byte result = 0;
            switch (policyUri)
            {
                case SecurityPolicies.Basic128Rsa15: result = 2; break;
                case SecurityPolicies.Basic256: result = 4; break;
                case SecurityPolicies.Basic256Sha256: result = 6; break;
                case SecurityPolicies.Aes128_Sha256_RsaOaep: result = 8; break;
                case SecurityPolicies.Aes256_Sha256_RsaPss: result = 10; break;
                case SecurityPolicies.None:
                default: return 0;
            }

            if (mode == MessageSecurityMode.SignAndEncrypt)
            {
                result += 100;
            }

            return result;
        }

        /// <summary>
        /// Specifies whether the messages are signed and encrypted or simply signed
        /// </summary>
        /// <value>The security mode.</value>
        [DataMember(IsRequired = false, Order = 1)]
        public MessageSecurityMode SecurityMode
        {
            get { return m_securityMode; }
            set { m_securityMode = value; }
        }

        /// <summary>
        /// The security policy to use.
        /// </summary>
        /// <value>The security policy URI.</value>
        [DataMember(IsRequired = false, Order = 2)]
        public string SecurityPolicyUri
        {
            get { return m_securityPolicyUri; }
            set { m_securityPolicyUri = value; }
        }
        #endregion

        #region Private Members
        private MessageSecurityMode m_securityMode;
        private string m_securityPolicyUri;
        #endregion
    }
    #endregion

    #region ServerSecurityPolicyCollection Class
    /// <summary>
    /// A collection of ServerSecurityPolicy objects.
    /// </summary>
    [CollectionDataContract(Name = "ListOfServerSecurityPolicy", Namespace = Namespaces.OpcUaConfig, ItemName = "ServerSecurityPolicy")]
    public partial class ServerSecurityPolicyCollection : List<ServerSecurityPolicy>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ServerSecurityPolicyCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="System.ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public ServerSecurityPolicyCollection(IEnumerable<ServerSecurityPolicy> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public ServerSecurityPolicyCollection(int capacity) : base(capacity) { }
    }
    #endregion

    #region SecurityConfiguration Class
    /// <summary>
    /// The security configuration for the application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class SecurityConfiguration
    {
        #region Constructors
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
            m_trustedIssuerCertificates = new CertificateTrustList();
            m_trustedPeerCertificates = new CertificateTrustList();
            m_nonceLength = 32;
            m_autoAcceptUntrustedCertificates = false;
            m_rejectSHA1SignedCertificates = true;
            m_rejectUnknownRevocationStatus = false;
            m_minCertificateKeySize = CertificateFactory.DefaultKeySize;
            m_addAppCertToTrustedStore = true;
            m_sendCertificateChain = true;
            m_suppressNonceValidationErrors = false;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        public void Initialize(StreamingContext context) => Initialize();
        #endregion

        #region Persistent Properties
        /// <summary>
        /// The application instance certificate.
        /// </summary>
        /// <value>The application certificate.</value>
        /// <remarks>
        /// This certificate must contain the application uri.
        /// For servers, URLs for each supported protocol must also be present.
        /// </remarks>
        [DataMember(IsRequired = true, EmitDefaultValue = false, Order = 0)]
        public CertificateIdentifier ApplicationCertificate
        {
            get { return m_applicationCertificate; }
            set { m_applicationCertificate = value; }
        }

        /// <summary>
        /// The store containing any additional issuer certificates.
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 2)]
        public CertificateTrustList TrustedIssuerCertificates
        {
            get
            {
                return m_trustedIssuerCertificates;
            }

            set
            {
                m_trustedIssuerCertificates = value ?? new CertificateTrustList();
            }
        }

        /// <summary>
        /// The trusted certificate store.
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 4)]
        public CertificateTrustList TrustedPeerCertificates
        {
            get
            {
                return m_trustedPeerCertificates;
            }

            set
            {
                m_trustedPeerCertificates = value ?? new CertificateTrustList();
            }
        }

        /// <summary>
        /// The length of nonce in the CreateSession service.
        /// </summary>
        /// <value>
        /// The length of nonce in the CreateSession service.
        /// </value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 6)]
        public int NonceLength
        {
            get { return m_nonceLength; }
            set { m_nonceLength = value; }
        }

        /// <summary>
        /// A store where invalid certificates can be placed for later review by the administrator.
        /// </summary>
        /// <value> 
        /// A store where invalid certificates can be placed for later review by the administrator.
        /// </value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 7)]
        public CertificateStoreIdentifier RejectedCertificateStore
        {
            get { return m_rejectedCertificateStore; }
            set { m_rejectedCertificateStore = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether untrusted certificates should be automatically accepted.
        /// </summary>
        /// <remarks>
        /// This flag can be set to by servers that allow anonymous clients or use user credentials for authentication.
        /// It can be set by clients that connect to URLs specified in configuration rather than with user entry.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 8)]
        public bool AutoAcceptUntrustedCertificates
        {
            get { return m_autoAcceptUntrustedCertificates; }
            set { m_autoAcceptUntrustedCertificates = value; }
        }

        /// <summary>
        /// Gets or sets a directory which contains files representing users roles.
        /// </summary>
        [DataMember(Order = 9)]
        public string UserRoleDirectory
        {
            get { return m_userRoleDirectory; }
            set { m_userRoleDirectory = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether SHA-1 signed certificates are accepted.
        /// </summary>
        /// <remarks>
        /// This flag can be set to false by servers that accept SHA-1 signed certificates.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 10)]
        public bool RejectSHA1SignedCertificates
        {
            get { return m_rejectSHA1SignedCertificates; }
            set { m_rejectSHA1SignedCertificates = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether certificates with unavailable revocation lists are not accepted.
        /// </summary>
        /// <remarks>
        /// This flag can be set to true by servers that must have a revocation list for each CA (even if empty).
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 11)]
        public bool RejectUnknownRevocationStatus
        {
            get { return m_rejectUnknownRevocationStatus; }
            set { m_rejectUnknownRevocationStatus = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating which minimum certificate key strength is accepted.
        /// </summary>
        /// <remarks>
        /// This value can be set to 1024, 2048 or 4096 by servers
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 12)]
        public ushort MinimumCertificateKeySize
        {
            get { return m_minCertificateKeySize; }
            set { m_minCertificateKeySize = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the Validator skips the full chain validation
        /// for already validated or accepted certificates.
        /// </summary>
        /// <remarks>
        /// This flag can be set to true by applications.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 13)]
        public bool UseValidatedCertificates
        {
            get { return m_useValidatedCertificates; }
            set { m_useValidatedCertificates = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the application cert should be copied to the trusted store.
        /// </summary>
        /// <remarks>
        /// It is useful for client/server applications running on the same host  and sharing the cert store to autotrust.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 14)]
        public bool AddAppCertToTrustedStore
        {
            get { return m_addAppCertToTrustedStore; }
            set { m_addAppCertToTrustedStore = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the application should send the complete certificate chain.
        /// </summary>
        /// <remarks>
        /// If set to true the complete certificate chain will be sent for CA signed certificates.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 15)]
        public bool SendCertificateChain
        {
            get { return m_sendCertificateChain; }
            set { m_sendCertificateChain = value; }
        }

        /// <summary>
        /// The store containing additional user issuer certificates.
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 16)]
        public CertificateTrustList UserIssuerCertificates
        {
            get
            {
                return m_userIssuerCertificates;
            }

            set
            {
                m_userIssuerCertificates = value;

                if (m_userIssuerCertificates == null)
                {
                    m_userIssuerCertificates = new CertificateTrustList();
                }
            }
        }

        /// <summary>
        /// The store containing trusted user certificates.
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 17)]
        public CertificateTrustList TrustedUserCertificates
        {
            get
            {
                return m_trustedUserCertificates;
            }

            set
            {
                m_trustedUserCertificates = value;

                if (m_trustedUserCertificates == null)
                {
                    m_trustedUserCertificates = new CertificateTrustList();
                }
            }
        }

        /// <summary>
        /// The store containing additional Https issuer certificates.
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 18)]
        public CertificateTrustList HttpsIssuerCertificates
        {
            get
            {
                return m_httpsIssuerCertificates;
            }

            set
            {
                m_httpsIssuerCertificates = value;

                if (m_httpsIssuerCertificates == null)
                {
                    m_httpsIssuerCertificates = new CertificateTrustList();
                }
            }
        }

        /// <summary>
        /// The store containing trusted Https certificates.
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 19)]
        public CertificateTrustList TrustedHttpsCertificates
        {
            get
            {
                return m_trustedHttpsCertificates;
            }

            set
            {
                m_trustedHttpsCertificates = value;

                if (m_trustedHttpsCertificates == null)
                {
                    m_trustedHttpsCertificates = new CertificateTrustList();
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the server nonce validation errors should be suppressed.
        /// </summary>
        /// <remarks>
        /// Allows client interoperability with legacy servers which do not comply with the specification for nonce usage.
        /// If set to true the server nonce validation errors are suppressed.
        /// Please set this flag to true only in close and secured networks since it can cause security vulnerabilities.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 20)]
        public bool SuppressNonceValidationErrors
        {
            get { return m_suppressNonceValidationErrors; }
            set { m_suppressNonceValidationErrors = value; }
        }
        #endregion

        #region Private Fields
        private CertificateIdentifier m_applicationCertificate;
        private CertificateTrustList m_trustedIssuerCertificates;
        private CertificateTrustList m_trustedPeerCertificates;
        private CertificateTrustList m_httpsIssuerCertificates;
        private CertificateTrustList m_trustedHttpsCertificates;
        private CertificateTrustList m_userIssuerCertificates;
        private CertificateTrustList m_trustedUserCertificates;
        private int m_nonceLength;
        private CertificateStoreIdentifier m_rejectedCertificateStore;
        private bool m_autoAcceptUntrustedCertificates;
        private string m_userRoleDirectory;
        private bool m_rejectSHA1SignedCertificates;
        private bool m_rejectUnknownRevocationStatus;
        private ushort m_minCertificateKeySize;
        private bool m_useValidatedCertificates;
        private bool m_addAppCertToTrustedStore;
        private bool m_sendCertificateChain;
        private bool m_suppressNonceValidationErrors;
        #endregion
    }
    #endregion

    #region SamplingRateGroup Class
    /// <summary>
    /// A class that defines a group of sampling rates supported by the server.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class SamplingRateGroup
    {
        #region Constructors
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
            m_start = start;
            m_increment = increment;
            m_count = count;
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_start = 1000;
            m_increment = 0;
            m_count = 0;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing()]
        public void Initialize(StreamingContext context) => Initialize();
        #endregion

        #region Public Properties
        /// <summary>
        /// The first sampling rate in the group (in milliseconds).
        /// </summary>
        /// <value>The first sampling rate in the group (in milliseconds).</value>
        [DataMember(IsRequired = false, Order = 1)]
        public double Start
        {
            get { return m_start; }
            set { m_start = value; }
        }

        /// <summary>
        /// The increment between sampling rates in the group (in milliseconds).
        /// </summary>
        /// <value>The increment.</value>
        /// <remarks>
        /// An increment of 0 means the group only contains one sampling rate equal to the start.
        /// </remarks>
        [DataMember(IsRequired = false, Order = 2)]
        public double Increment
        {
            get { return m_increment; }
            set { m_increment = value; }
        }

        /// <summary>
        /// The number of sampling rates in the group.
        /// </summary>
        /// <value>The count.</value>
        /// <remarks>
        /// A count of 0 means there is no limit.
        /// </remarks>
        [DataMember(IsRequired = false, Order = 3)]
        public int Count
        {
            get { return m_count; }
            set { m_count = value; }
        }
        #endregion

        #region Private Members
        private double m_start;
        private double m_increment;
        private int m_count;
        #endregion
    }
    #endregion

    #region SamplingRateGroupCollection Class
    /// <summary>
    /// A collection of SamplingRateGroup objects.
    /// </summary>
    [CollectionDataContract(Name = "ListOfSamplingRateGroup", Namespace = Namespaces.OpcUaConfig, ItemName = "SamplingRateGroup")]
    public partial class SamplingRateGroupCollection : List<SamplingRateGroup>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public SamplingRateGroupCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public SamplingRateGroupCollection(IEnumerable<SamplingRateGroup> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public SamplingRateGroupCollection(int capacity) : base(capacity) { }
    }
    #endregion

    #region ServerBaseConfiguration Class
    /// <summary>
    /// Specifies the configuration for a server application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class ServerBaseConfiguration
    {
        #region Constructors
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
            m_baseAddresses = new StringCollection();
            m_alternateBaseAddresses = new StringCollection();
            m_securityPolicies = new ServerSecurityPolicyCollection();
            m_minRequestThreadCount = 10;
            m_maxRequestThreadCount = 100;
            m_maxQueuedRequestCount = 200;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing()]
        public void Initialize(StreamingContext context) => Initialize();

        /// <summary>
        /// Remove unsupported security policies and expand wild cards.
        /// </summary>
        [OnDeserialized()]
        private void ValidateSecurityPolicyCollection(StreamingContext context)
        {
            var supportedPolicies = Opc.Ua.SecurityPolicies.GetDisplayNames();
            var newPolicies = new ServerSecurityPolicyCollection();
            foreach (var securityPolicy in m_securityPolicies)
            {
                if (String.IsNullOrWhiteSpace(securityPolicy.SecurityPolicyUri))
                {
                    // add wild card policies
                    foreach (var policyUri in Opc.Ua.SecurityPolicies.GetDefaultUris())
                    {
                        var newPolicy = new ServerSecurityPolicy() {
                            SecurityMode = securityPolicy.SecurityMode,
                            SecurityPolicyUri = policyUri
                        };
                        if (newPolicies.Find(s =>
                            s.SecurityMode == newPolicy.SecurityMode &&
                            string.Equals(s.SecurityPolicyUri, newPolicy.SecurityPolicyUri, StringComparison.Ordinal)) == null)
                        {
                            newPolicies.Add(newPolicy);
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < supportedPolicies.Length; i++)
                    {
                        if (securityPolicy.SecurityPolicyUri.Contains(supportedPolicies[i]))
                        {
                            if (newPolicies.Find(s =>
                                s.SecurityMode == securityPolicy.SecurityMode &&
                                string.Equals(s.SecurityPolicyUri, securityPolicy.SecurityPolicyUri,
                                    StringComparison.Ordinal)) == null)
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
        #endregion

        #region Persistent Properties
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
            get
            {
                return m_baseAddresses;
            }

            set
            {
                m_baseAddresses = value;

                if (m_baseAddresses == null)
                {
                    m_baseAddresses = new StringCollection();
                }
            }
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
            get
            {
                return m_alternateBaseAddresses;
            }

            set
            {
                m_alternateBaseAddresses = value;

                if (m_alternateBaseAddresses == null)
                {
                    m_alternateBaseAddresses = new StringCollection();
                }
            }
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
            get
            {
                return m_securityPolicies;
            }

            set
            {
                m_securityPolicies = value;

                if (m_securityPolicies == null)
                {
                    m_securityPolicies = new ServerSecurityPolicyCollection();
                }
            }
        }

        /// <summary>
        /// The minimum number of threads assigned to processing requests.
        /// </summary>
        /// <value>The minimum request thread count.</value>
        [DataMember(IsRequired = false, Order = 3)]
        public int MinRequestThreadCount
        {
            get { return m_minRequestThreadCount; }
            set { m_minRequestThreadCount = value; }
        }

        /// <summary>
        /// The maximum number of threads assigned to processing requests.
        /// </summary>
        /// <value>The maximum request thread count.</value>
        [DataMember(IsRequired = false, Order = 4)]
        public int MaxRequestThreadCount
        {
            get { return m_maxRequestThreadCount; }
            set { m_maxRequestThreadCount = value; }
        }

        /// <summary>
        /// The maximum number of requests that will be queued waiting for a thread.
        /// </summary>
        /// <value>The maximum queued request count.</value>
        [DataMember(IsRequired = false, Order = 5)]
        public int MaxQueuedRequestCount
        {
            get { return m_maxQueuedRequestCount; }
            set { m_maxQueuedRequestCount = value; }
        }
        #endregion

        #region Private Members
        private StringCollection m_baseAddresses;
        private StringCollection m_alternateBaseAddresses;
        private ServerSecurityPolicyCollection m_securityPolicies;
        private int m_minRequestThreadCount;
        private int m_maxRequestThreadCount;
        private int m_maxQueuedRequestCount;
        #endregion
    }
    #endregion

    #region ServerConfiguration Class
    /// <summary>
    /// Specifies the configuration for a server application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class ServerConfiguration : ServerBaseConfiguration
    {
        #region Constructors
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
            m_userTokenPolicies = new UserTokenPolicyCollection();
            m_diagnosticsEnabled = false;
            m_maxSessionCount = 100;
            m_maxSessionTimeout = 3600000;
            m_minSessionTimeout = 10000;
            m_maxBrowseContinuationPoints = 10;
            m_maxQueryContinuationPoints = 10;
            m_maxHistoryContinuationPoints = 100;
            m_maxRequestAge = 600000;
            m_minPublishingInterval = 100;
            m_maxPublishingInterval = 3600000;
            m_publishingResolution = 100;
            m_minSubscriptionLifetime = 10000;
            m_maxSubscriptionLifetime = 3600000;
            m_maxMessageQueueSize = 10;
            m_maxNotificationQueueSize = 100;
            m_maxNotificationsPerPublish = 100;
            m_minMetadataSamplingInterval = 1000;
            m_availableSamplingRates = new SamplingRateGroupCollection();
            m_registrationEndpoint = null;
            m_maxRegistrationInterval = 30000;
            m_maxPublishRequestCount = 20;
            m_maxSubscriptionCount = 100;
            m_maxEventQueueSize = 10000;
            // https://opcfoundation-onlineapplications.org/profilereporting/ for list of available profiles
            m_serverProfileArray = new string[] { "http://opcfoundation.org/UA-Profile/Server/StandardUA2017" };
            m_shutdownDelay = 5;
            m_serverCapabilities = new string[] { "DA" };
            m_supportedPrivateKeyFormats = new string[] { "PFX", "PEM" };
            m_maxTrustListSize = 0;
            m_multicastDnsEnabled = false;
            m_auditingEnabled = false;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing()]
        public new void Initialize(StreamingContext context) => Initialize();
        #endregion

        #region Persistent Properties
        /// <summary>
        /// The user tokens accepted by the server.
        /// </summary>
        /// <value>The user token policies.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 3)]
        public UserTokenPolicyCollection UserTokenPolicies
        {
            get
            {
                return m_userTokenPolicies;
            }

            set
            {
                m_userTokenPolicies = value;

                if (m_userTokenPolicies == null)
                {
                    m_userTokenPolicies = new UserTokenPolicyCollection();
                }
            }
        }

        /// <summary>
        /// Whether diagnostics are enabled.
        /// </summary>
        /// <value><c>true</c> if diagnostic is enabled; otherwise, <c>false</c>.</value>
        [DataMember(IsRequired = false, Order = 4)]
        public bool DiagnosticsEnabled
        {
            get { return m_diagnosticsEnabled; }
            set { m_diagnosticsEnabled = value; }
        }

        /// <summary>
        /// The maximum number of open sessions.
        /// </summary>
        /// <value>The maximum session count.</value>
        [DataMember(IsRequired = false, Order = 5)]
        public int MaxSessionCount
        {
            get { return m_maxSessionCount; }
            set { m_maxSessionCount = value; }
        }

        /// <summary>
        /// That minimum period of that a session is allowed to remain
        /// open without communication from the client (in milliseconds).
        /// </summary>
        /// <value>The minimum session timeout.</value>
        [DataMember(IsRequired = false, Order = 6)]
        public int MinSessionTimeout
        {
            get { return m_minSessionTimeout; }
            set { m_minSessionTimeout = value; }
        }

        /// <summary>
        /// That maximum period of that a session is allowed to remain
        /// open without communication from the client (in milliseconds).
        /// </summary>
        /// <value>The maximum session timeout.</value>
        [DataMember(IsRequired = false, Order = 7)]
        public int MaxSessionTimeout
        {
            get { return m_maxSessionTimeout; }
            set { m_maxSessionTimeout = value; }
        }

        /// <summary>
        /// The maximum number of continuation points used for
        /// Browse/BrowseNext operations.
        /// </summary>
        /// <value>The maximum number of continuation points used for Browse/BrowseNext operations</value>
        [DataMember(IsRequired = false, Order = 8)]
        public int MaxBrowseContinuationPoints
        {
            get { return m_maxBrowseContinuationPoints; }
            set { m_maxBrowseContinuationPoints = value; }
        }

        /// <summary>
        /// The maximum number of continuation points used for
        /// Query/QueryNext operations.
        /// </summary>
        /// <value>The maximum number of query continuation points.</value>
        [DataMember(IsRequired = false, Order = 9)]
        public int MaxQueryContinuationPoints
        {
            get { return m_maxQueryContinuationPoints; }
            set { m_maxQueryContinuationPoints = value; }
        }

        /// <summary>
        /// The maximum number of continuation points used for HistoryRead operations.
        /// </summary>
        /// <value>The maximum number of  history continuation points.</value>
        [DataMember(IsRequired = false, Order = 10)]
        public int MaxHistoryContinuationPoints
        {
            get { return m_maxHistoryContinuationPoints; }
            set { m_maxHistoryContinuationPoints = value; }
        }

        /// <summary>
        /// The maximum age of an incoming request (old requests are rejected) (in milliseconds).
        /// </summary>
        /// <value>The maximum age of an incoming request.</value>
        [DataMember(IsRequired = false, Order = 11)]
        public int MaxRequestAge
        {
            get { return m_maxRequestAge; }
            set { m_maxRequestAge = value; }
        }

        /// <summary>
        /// The minimum publishing interval supported by the server (in milliseconds).
        /// </summary>
        /// <value>The minimum publishing interval.</value>
        [DataMember(IsRequired = false, Order = 12)]
        public int MinPublishingInterval
        {
            get { return m_minPublishingInterval; }
            set { m_minPublishingInterval = value; }
        }

        /// <summary>
        /// The maximum publishing interval supported by the server (in milliseconds).
        /// </summary>
        /// <value>The maximum publishing interval.</value>
        [DataMember(IsRequired = false, Order = 13)]
        public int MaxPublishingInterval
        {
            get { return m_maxPublishingInterval; }
            set { m_maxPublishingInterval = value; }
        }

        /// <summary>
        /// The minimum difference between supported publishing interval (in milliseconds).
        /// </summary>
        /// <value>The publishing resolution.</value>
        [DataMember(IsRequired = false, Order = 14)]
        public int PublishingResolution
        {
            get { return m_publishingResolution; }
            set { m_publishingResolution = value; }
        }

        /// <summary>
        /// How long the subscriptions will remain open without a publish from the client.
        /// </summary>
        /// <value>The maximum subscription lifetime.</value>
        [DataMember(IsRequired = false, Order = 15)]
        public int MaxSubscriptionLifetime
        {
            get { return m_maxSubscriptionLifetime; }
            set { m_maxSubscriptionLifetime = value; }
        }

        /// <summary>
        /// The maximum number of messages saved in the queue for each subscription.
        /// </summary>
        /// <value>The maximum size of the  message queue.</value>
        [DataMember(IsRequired = false, Order = 16)]
        public int MaxMessageQueueSize
        {
            get { return m_maxMessageQueueSize; }
            set { m_maxMessageQueueSize = value; }
        }

        /// <summary>
        /// The maximum number of notificates saved in the queue for each monitored item.
        /// </summary>
        /// <value>The maximum size of the notification queue.</value>
        [DataMember(IsRequired = false, Order = 17)]
        public int MaxNotificationQueueSize
        {
            get { return m_maxNotificationQueueSize; }
            set { m_maxNotificationQueueSize = value; }
        }

        /// <summary>
        /// The maximum number of notifications per publish.
        /// </summary>
        /// <value>The maximum number of notifications per publish.</value>
        [DataMember(IsRequired = false, Order = 18)]
        public int MaxNotificationsPerPublish
        {
            get { return m_maxNotificationsPerPublish; }
            set { m_maxNotificationsPerPublish = value; }
        }

        /// <summary>
        /// The minimum sampling interval for metadata.
        /// </summary>
        /// <value>The minimum sampling interval for metadata.</value>
        [DataMember(IsRequired = false, Order = 19)]
        public int MinMetadataSamplingInterval
        {
            get { return m_minMetadataSamplingInterval; }
            set { m_minMetadataSamplingInterval = value; }
        }

        /// <summary>
        /// The available sampling rates.
        /// </summary>
        /// <value>The available sampling rates.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 20)]
        public SamplingRateGroupCollection AvailableSamplingRates
        {
            get { return m_availableSamplingRates; }
            set { m_availableSamplingRates = value; }
        }

        /// <summary>
        /// The endpoint description for the registration endpoint.
        /// </summary>
        /// <value>The registration endpoint.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 21)]
        public EndpointDescription RegistrationEndpoint
        {
            get { return m_registrationEndpoint; }
            set { m_registrationEndpoint = value; }
        }

        /// <summary>
        /// The maximum time between registration attempts (in milliseconds).
        /// </summary>
        /// <value>The maximum time between registration attempts (in milliseconds).</value>
        [DataMember(IsRequired = false, Order = 22)]
        public int MaxRegistrationInterval
        {
            get { return m_maxRegistrationInterval; }
            set { m_maxRegistrationInterval = value; }
        }

        /// <summary>
        /// The path to the file containing nodes persisted by the core node manager.
        /// </summary>
        /// <value>The path to the file containing nodes persisted by the core node manager.</value>
        [DataMember(IsRequired = false, Order = 23)]
        public string NodeManagerSaveFile
        {
            get { return m_nodeManagerSaveFile; }
            set { m_nodeManagerSaveFile = value; }
        }

        /// <summary>
        /// The minimum lifetime for a subscription (in milliseconds).
        /// </summary>
        /// <value>The minimum lifetime for a subscription.</value>
        [DataMember(IsRequired = false, Order = 24)]
        public int MinSubscriptionLifetime
        {
            get { return m_minSubscriptionLifetime; }
            set { m_minSubscriptionLifetime = value; }
        }

        /// <summary>
        /// The max publish request count.
        /// </summary>
        /// <value>The max publish request count.</value>
        [DataMember(IsRequired = false, Order = 25)]
        public int MaxPublishRequestCount
        {
            get { return m_maxPublishRequestCount; }
            set { m_maxPublishRequestCount = value; }
        }

        /// <summary>
        /// The max subscription count.
        /// </summary>
        /// <value>The max subscription count.</value>
        [DataMember(IsRequired = false, Order = 26)]
        public int MaxSubscriptionCount
        {
            get { return m_maxSubscriptionCount; }
            set { m_maxSubscriptionCount = value; }
        }

        /// <summary>
        /// The max size of the event queue.
        /// </summary>
        /// <value>The max size of the event queue.</value>
        [DataMember(IsRequired = false, Order = 27)]
        public int MaxEventQueueSize
        {
            get { return m_maxEventQueueSize; }
            set { m_maxEventQueueSize = value; }
        }

        /// <summary>
        /// The server profile array.
        /// </summary>
        /// <value>The array of server profiles.</value>
        [DataMember(IsRequired = false, Order = 28)]
        public StringCollection ServerProfileArray
        {
            get { return m_serverProfileArray; }
            set
            {
                m_serverProfileArray = value;
                if (m_serverProfileArray == null)
                {
                    m_serverProfileArray = new StringCollection();
                }
            }
        }

        /// <summary>
        /// The server shutdown delay.
        /// </summary>
        /// <value>The number of seconds to delay the shutdown if a client is connected.</value>
        [DataMember(IsRequired = false, Order = 29)]
        public int ShutdownDelay
        {
            get { return m_shutdownDelay; }
            set
            {
                m_shutdownDelay = value;
            }
        }

        /// <summary>
        /// The server capabilities.
        /// The latest set of server capabilities is listed 
        /// <see href="http://www.opcfoundation.org/UA/schemas/1.04/ServerCapabilities.csv">here.</see>
        /// </summary>
        /// <value>The array of server capabilites.</value>
        [DataMember(IsRequired = false, Order = 30)]
        public StringCollection ServerCapabilities
        {
            get { return m_serverCapabilities; }
            set
            {
                m_serverCapabilities = value;
                if (m_serverCapabilities == null)
                {
                    m_serverCapabilities = new StringCollection();
                }
            }
        }

        /// <summary>
        /// Gets or sets the supported private key format.
        /// </summary>
        /// <value>The array of server profiles.</value>
        [DataMember(IsRequired = false, Order = 31)]
        public StringCollection SupportedPrivateKeyFormats
        {
            get { return m_supportedPrivateKeyFormats; }
            set
            {
                m_supportedPrivateKeyFormats = value;
                if (m_supportedPrivateKeyFormats == null)
                {
                    m_supportedPrivateKeyFormats = new StringCollection();
                }
            }
        }

        /// <summary>
        /// Gets or sets the max size of the trust list.
        /// </summary>
        [DataMember(IsRequired = false, Order = 32)]
        public int MaxTrustListSize
        {
            get { return m_maxTrustListSize; }
            set { m_maxTrustListSize = value; }
        }

        /// <summary>
        /// Gets or sets if multicast DNS is enabled.
        /// </summary>
        [DataMember(IsRequired = false, Order = 33)]
        public bool MultiCastDnsEnabled
        {
            get { return m_multicastDnsEnabled; }
            set { m_multicastDnsEnabled = value; }
        }

        /// <summary>
        /// Gets or sets reverse connect server configuration.
        /// </summary>
        [DataMember(IsRequired = false, Order = 34)]
        public ReverseConnectServerConfiguration ReverseConnect
        {
            get { return m_reverseConnect; }
            set { m_reverseConnect = value; }
        }

        /// <summary>
        /// Gets or sets the operation limits of the OPC UA Server.
        /// </summary>
        [DataMember(IsRequired = false, Order = 35)]
        public OperationLimits OperationLimits
        {
            get { return m_operationLimits; }
            set { m_operationLimits = value; }
        }

        /// <summary>
        /// Whether auditing is enabled.
        /// </summary>
        /// <value><c>true</c> if auditing is enabled; otherwise, <c>false</c>.</value>
        [DataMember(IsRequired = false, Order = 36)]
        public bool AuditingEnabled
        {
            get { return m_auditingEnabled; }
            set { m_auditingEnabled = value; }
        }
        #endregion

        #region Private Members
        private UserTokenPolicyCollection m_userTokenPolicies;
        private bool m_diagnosticsEnabled;
        private int m_maxSessionCount;
        private int m_minSessionTimeout;
        private int m_maxSessionTimeout;
        private int m_maxBrowseContinuationPoints;
        private int m_maxQueryContinuationPoints;
        private int m_maxHistoryContinuationPoints;
        private int m_maxRequestAge;
        private int m_minPublishingInterval;
        private int m_maxPublishingInterval;
        private int m_publishingResolution;
        private int m_minSubscriptionLifetime;
        private int m_maxSubscriptionLifetime;
        private int m_maxMessageQueueSize;
        private int m_maxNotificationQueueSize;
        private int m_maxNotificationsPerPublish;
        private int m_minMetadataSamplingInterval;
        private SamplingRateGroupCollection m_availableSamplingRates;
        private EndpointDescription m_registrationEndpoint;
        private int m_maxRegistrationInterval;
        private string m_nodeManagerSaveFile;
        private int m_maxPublishRequestCount;
        private int m_maxSubscriptionCount;
        private int m_maxEventQueueSize;
        private StringCollection m_serverProfileArray;
        private int m_shutdownDelay;
        private StringCollection m_serverCapabilities;
        private StringCollection m_supportedPrivateKeyFormats;
        private int m_maxTrustListSize;
        private bool m_multicastDnsEnabled;
        private ReverseConnectServerConfiguration m_reverseConnect;
        private OperationLimits m_operationLimits;
        private bool m_auditingEnabled;
        #endregion
    }
    #endregion

    #region ReverseConnectServerConfiguration Class
    /// <summary>
    /// Stores the configuration of the reverse connections.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ReverseConnectServerConfiguration
    {
        #region Constructors
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
        private void Initialize(StreamingContext context) => Initialize();

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            ConnectInterval = 15000;
            ConnectTimeout = 30000;
            RejectTimeout = 60000;
        }
        #endregion

        #region Public Properties
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
        #endregion
    }
    #endregion

    #region OperationLimits Class
    /// <summary>
    /// Stores the operation limits of a OPC UA Server.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class OperationLimits
    {
        #region Constructors
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
        private void Initialize(StreamingContext context) => Initialize();

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
        #endregion

        #region Public Properties
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
        #endregion
    }
    #endregion

    #region ReverseConnectClient Class
    /// <summary>
    /// Stores the configuration of the reverse connections.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ReverseConnectClient
    {
        #region Constructors
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
        private void Initialize(StreamingContext context) => Initialize();

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            Enabled = true;
        }
        #endregion

        #region Persistent Properties
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
        #endregion
    }
    #endregion

    #region ReverseConnectClientCollection Class
    /// <summary>
    /// A collection of reverse connect clients.
    /// </summary>
    [CollectionDataContract(Name = "ListOfReverseConnectClient", Namespace = Namespaces.OpcUaConfig, ItemName = "ReverseConnectClient")]
    public class ReverseConnectClientCollection : List<ReverseConnectClient>
    {
        #region Constructors
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ReverseConnectClientCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public ReverseConnectClientCollection(IEnumerable<ReverseConnectClient> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public ReverseConnectClientCollection(int capacity) : base(capacity) { }
        #endregion
    }
    #endregion

    #region ClientConfiguration Class
    /// <summary>
    /// The configuration for a client application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class ClientConfiguration
    {
        #region Constructors
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
            m_defaultSessionTimeout = 60000;
            m_minSubscriptionLifetime = 10000;
            m_wellKnownDiscoveryUrls = new StringCollection();
            m_discoveryServers = new EndpointDescriptionCollection();
            m_operationLimits = new OperationLimits();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing()]
        public void Initialize(StreamingContext context) => Initialize();
        #endregion

        #region Persistent Properties
        /// <summary>
        /// The default session timeout (in milliseconds).
        /// </summary>
        /// <value>The default session timeout.</value>
        [DataMember(IsRequired = false, Order = 0)]
        public int DefaultSessionTimeout
        {
            get { return m_defaultSessionTimeout; }
            set { m_defaultSessionTimeout = value; }
        }

        /// <summary>
        /// The well known URLs for the local discovery servers.
        /// </summary>
        /// <value>The well known discovery URLs.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 1)]
        public StringCollection WellKnownDiscoveryUrls
        {
            get
            {
                return m_wellKnownDiscoveryUrls;
            }

            set
            {
                m_wellKnownDiscoveryUrls = value;

                if (m_wellKnownDiscoveryUrls == null)
                {
                    m_wellKnownDiscoveryUrls = new StringCollection();
                }
            }
        }

        /// <summary>
        /// The endpoint descriptions for central discovery servers.
        /// </summary>
        /// <value>The endpoint descriptions for central discovery servers.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 2)]
        public EndpointDescriptionCollection DiscoveryServers
        {
            get
            {
                return m_discoveryServers;
            }

            set
            {
                m_discoveryServers = value;

                if (m_discoveryServers == null)
                {
                    m_discoveryServers = new EndpointDescriptionCollection();
                }
            }
        }

        /// <summary>
        /// The path to the file containing the cached endpoints.
        /// </summary>
        /// <value>The path to the file containing the cached endpoints.</value>
        [DataMember(IsRequired = false, Order = 3)]
        public string EndpointCacheFilePath
        {
            get { return m_endpointCacheFilePath; }
            set { m_endpointCacheFilePath = value; }
        }

        /// <summary>
        /// The minimum lifetime for a subscription (in milliseconds).
        /// </summary>
        /// <value>The minimum lifetime for a subscription.</value>
        [DataMember(IsRequired = false, Order = 4)]
        public int MinSubscriptionLifetime
        {
            get { return m_minSubscriptionLifetime; }
            set { m_minSubscriptionLifetime = value; }
        }

        /// <summary>
        /// The reverse connect Client configuration.
        /// </summary>
        [DataMember(IsRequired = false, Order = 5)]
        public ReverseConnectClientConfiguration ReverseConnect
        {
            get { return m_reverseConnect; }
            set { m_reverseConnect = value; }
        }

        /// <summary>
        /// Gets or sets the default operation limits of the OPC UA client.
        /// </summary>
        /// <remarks>
        /// Values not equal to zero are overwritten with smaller values set by the server.
        /// The values are used to limit client service calls.
        /// </remarks>
        [DataMember(IsRequired = false, Order = 6)]
        public OperationLimits OperationLimits
        {
            get { return m_operationLimits; }
            set { m_operationLimits = value; }
        }
        #endregion

        #region Private Members
        private StringCollection m_wellKnownDiscoveryUrls;
        private EndpointDescriptionCollection m_discoveryServers;
        private int m_defaultSessionTimeout;
        private string m_endpointCacheFilePath;
        private int m_minSubscriptionLifetime;
        private ReverseConnectClientConfiguration m_reverseConnect;
        private OperationLimits m_operationLimits;
        #endregion
    }
    #endregion

    #region ReverseConnectClientConfiguration Class
    /// <summary>
    /// Stores the configuration of the reverse connections.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ReverseConnectClientConfiguration
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ReverseConnectClientConfiguration()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context) => Initialize();

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
        }
        #endregion

        #region Public Properties
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
        #endregion
    }
    #endregion

    #region ReverseConnectClientEndpoint Class
    /// <summary>
    /// Stores the configuration of the reverse connections.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ReverseConnectClientEndpoint
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ReverseConnectClientEndpoint()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context) => Initialize();

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
        }
        #endregion

        #region Persistent Properties
        /// <summary>
        /// The endpoint Url of a reverse connect client.
        /// </summary>
        [DataMember(Order = 1, IsRequired = false)]
        public string EndpointUrl { get; set; }
        #endregion
    }
    #endregion

    #region ReverseConnectClientEndpointCollection Class
    /// <summary>
    /// A collection of reverse connect client endpoints.
    /// </summary>
    [CollectionDataContract(Name = "ListOfReverseConnectClientEndpoint", Namespace = Namespaces.OpcUaConfig, ItemName = "ClientEndpoint")]
    public class ReverseConnectClientEndpointCollection : List<ReverseConnectClientEndpoint>
    {
        #region Constructors
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ReverseConnectClientEndpointCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public ReverseConnectClientEndpointCollection(IEnumerable<ReverseConnectClientEndpoint> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public ReverseConnectClientEndpointCollection(int capacity) : base(capacity) { }
        #endregion
    }
    #endregion

    #region DiscoveryServerConfiguration Class
    /// <summary>
    /// Specifies the configuration for a discovery server application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class DiscoveryServerConfiguration : ServerBaseConfiguration
    {
        #region Constructors
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
            m_serverNames = new LocalizedTextCollection();
            m_serverRegistrations = new ServerRegistrationCollection();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing()]
        public new void Initialize(StreamingContext context) => Initialize();
        #endregion

        #region Persistent Properties
        /// <summary>
        /// The localized names for the discovery server.
        /// </summary>
        /// <value>The server names.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 2)]
        public LocalizedTextCollection ServerNames
        {
            get
            {
                return m_serverNames;
            }

            set
            {
                m_serverNames = value;

                if (m_serverNames == null)
                {
                    m_serverNames = new LocalizedTextCollection();
                }
            }
        }

        /// <summary>
        /// The path to the file containing servers saved by the discovery server.
        /// </summary>
        /// <value>The discovery server cache file.</value>
        [DataMember(IsRequired = false, Order = 3)]
        public string DiscoveryServerCacheFile
        {
            get { return m_discoveryServerCacheFile; }
            set { m_discoveryServerCacheFile = value; }
        }

        /// <summary>
        /// Gets or sets the server registrations associated with the discovery server.
        /// </summary>
        /// <value>The server registrations.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 4)]
        public ServerRegistrationCollection ServerRegistrations
        {
            get { return m_serverRegistrations; }
            set { m_serverRegistrations = value; }
        }
        #endregion

        #region Private Members
        private LocalizedTextCollection m_serverNames;
        private string m_discoveryServerCacheFile;
        private ServerRegistrationCollection m_serverRegistrations;
        #endregion
    }
    #endregion

    #region ServerRegistration Class
    /// <summary>
    /// Specifies the configuration for a discovery server application.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public class ServerRegistration
    {
        #region Constructors
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
            m_applicationUri = null;
            m_alternateDiscoveryUrls = new StringCollection();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        /// <param name="context">The context.</param>
        [OnDeserializing()]
        public void Initialize(StreamingContext context) => Initialize();
        #endregion

        #region Persistent Properties
        /// <summary>
        /// Gets or sets the application URI of the server which the registration applies to.
        /// </summary>
        /// <value>The application uri.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 1)]
        public string ApplicationUri
        {
            get
            {
                return m_applicationUri;
            }

            set
            {
                m_applicationUri = value;
            }
        }

        /// <summary>
        /// Gets or sets the alternate discovery urls.
        /// </summary>
        /// <value>The alternate discovery urls.</value>
        /// <remarks>
        /// These addresses are used to specify alternate paths to ther via firewalls, proxies
        /// or similar network infrastructure. If these paths are specified in the configuration
        /// file then the server will use the domain of the URL used by the client to determine
        /// which, if any, or the alternate addresses to use instead of the primary addresses.
        /// 
        /// In the ideal world the server would provide these URLs during registration but this
        /// table allows the administrator to provide the information to the disovery server 
        /// directly without requiring a patch to the server.
        /// </remarks>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 2)]
        public StringCollection AlternateDiscoveryUrls
        {
            get
            {
                return m_alternateDiscoveryUrls;
            }

            set
            {
                m_alternateDiscoveryUrls = value;

                if (m_alternateDiscoveryUrls == null)
                {
                    m_alternateDiscoveryUrls = new StringCollection();
                }
            }
        }
        #endregion

        #region Private Members
        private string m_applicationUri;
        private StringCollection m_alternateDiscoveryUrls;
        #endregion
    }
    #endregion

    #region ServerRegistrationCollection Class
    /// <summary>
    /// A collection of AdditionalServerRegistrationInfo objects.
    /// </summary>
    [CollectionDataContract(Name = "ListOfServerRegistration", Namespace = Namespaces.OpcUaConfig, ItemName = "ServerRegistration")]
    public partial class ServerRegistrationCollection : List<ServerRegistration>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public ServerRegistrationCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        /// <exception cref="ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public ServerRegistrationCollection(IEnumerable<ServerRegistration> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The capacity.</param>
        public ServerRegistrationCollection(int capacity) : base(capacity) { }
    }
    #endregion

    #region CertificateStoreIdentifier Class
    /// <summary>
    /// Describes a certificate store.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class CertificateStoreIdentifier
    {
        #region Persistent Properties
        /// <summary>
        /// The type of certificate store.
        /// </summary>
        /// <value>
        /// If the StoreName is not empty, the CertificateStoreType.X509Store is returned, otherwise the StoreType is returned.
        /// </value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 0)]
        public string StoreType
        {
            get
            {
                if (!String.IsNullOrEmpty(m_storeName))
                {
                    return CertificateStoreType.X509Store;
                }

                return m_storeType;
            }

            set
            {
                m_storeType = value;
            }
        }

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
            get
            {
                if (!String.IsNullOrEmpty(m_storeName))
                {
                    if (String.IsNullOrEmpty(m_storeLocation))
                    {
                        return CurrentUser + m_storeName;
                    }

                    return Utils.Format("{1}\\{0}", m_storeName, m_storeLocation);
                }

                return m_storePath;
            }

            set
            {
                m_storePath = value;

                if (!String.IsNullOrEmpty(m_storePath))
                {
                    if (String.IsNullOrEmpty(m_storeType))
                    {
                        m_storeType = CertificateStoreIdentifier.DetermineStoreType(m_storePath);
                    }
                }
            }
        }

        /// <summary>
        /// The name of the certifcate store that contains the trusted certficates. 
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 2)]
        [Obsolete("Use StoreType/StorePath instead")]
        public string StoreName
        {
            get { return m_storeName; }
            set { m_storeName = value; }
        }

        /// <summary>
        /// The location of the certifcate store that contains the trusted certficates. 
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 3)]
        [Obsolete("Use StoreType/StorePath instead")]
        public string StoreLocation
        {
            get { return m_storeLocation; }
            set { m_storeLocation = value; }
        }

        /// <summary>
        /// Options that can be used to suppress certificate validation errors.
        /// </summary>
        [DataMember(Name = "ValidationOptions", IsRequired = false, EmitDefaultValue = false, Order = 4)]
        private int XmlEncodedValidationOptions
        {
            get { return (int)m_validationOptions; }
            set { m_validationOptions = (CertificateValidationOptions)value; }
        }
        #endregion

        #region Private Fields
        private string m_storeType;
        private string m_storePath;
        private string m_storeLocation;
        private string m_storeName;
        private CertificateValidationOptions m_validationOptions;
        #endregion
    }
    #endregion

    #region CertificateTrustList Class
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class CertificateTrustList : CertificateStoreIdentifier
    {
        #region Constructors
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
            m_lock = new object();
            m_trustedCertificates = new CertificateIdentifierCollection();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        public void Initialize(StreamingContext context) => Initialize();
        #endregion

        #region Persistent Properties
        /// <summary>
        /// The list of trusted certificates.
        /// </summary>
        /// <value>
        /// The list of trusted certificates is set when TrustedCertificates is not a null value, otherwise new CertificateIdentifierCollection is set.
        /// </value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 3)]
        public CertificateIdentifierCollection TrustedCertificates
        {
            get
            {
                return m_trustedCertificates;
            }

            set
            {
                m_trustedCertificates = value;

                if (m_trustedCertificates == null)
                {
                    m_trustedCertificates = new CertificateIdentifierCollection();
                }
            }
        }
        #endregion

        #region Private Fields
        private CertificateIdentifierCollection m_trustedCertificates;
        #endregion
    }
    #endregion

    #region CertificateIdentifierCollection Class
    [CollectionDataContract(Name = "ListOfCertificateIdentifier", Namespace = Namespaces.OpcUaConfig, ItemName = "CertificateIdentifier")]
    public partial class CertificateIdentifierCollection : List<CertificateIdentifier>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public CertificateIdentifierCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">A collection of values to add to this new collection</param>
        public CertificateIdentifierCollection(IEnumerable<CertificateIdentifier> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        public CertificateIdentifierCollection(int capacity) : base(capacity) { }
    }
    #endregion

    #region CertificateIdentifier Class
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class CertificateIdentifier
    {
        #region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public CertificateIdentifier()
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the identifier with the raw data from a certificate.
        /// </summary>
        public CertificateIdentifier(X509Certificate2 certificate)
        {
            Initialize();
            m_certificate = certificate;
        }

        /// <summary>
        /// Initializes the identifier with the raw data from a certificate.
        /// </summary>
        public CertificateIdentifier(X509Certificate2 certificate, CertificateValidationOptions validationOptions)
        {
            Initialize();
            m_certificate = certificate;
            m_validationOptions = validationOptions;
        }


        /// <summary>
        /// Initializes the identifier with the raw data from a certificate.
        /// </summary>
        public CertificateIdentifier(byte[] rawData)
        {
            Initialize();
            m_certificate = CertificateFactory.Create(rawData, true);
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        public void Initialize(StreamingContext context) => Initialize();
        #endregion

        #region Public Properties
        /// <summary>
        /// The type of certificate store.
        /// </summary>
        /// <value>The type of the store - defined in the <see cref="CertificateStoreType"/>.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 0)]
        public string StoreType
        {
            get
            {
                if (!String.IsNullOrEmpty(m_storeName))
                {
                    return CertificateStoreType.X509Store;
                }

                return m_storeType;
            }

            set
            {
                m_storeType = value;
            }
        }

        /// <summary>
        /// The path that identifies the certificate store.
        /// </summary>
        /// <value>The store path in the form <c>StoreName\\Store Location</c> .</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 1)]
        public string StorePath
        {
            get
            {
                if (!String.IsNullOrEmpty(m_storeName))
                {
                    if (String.IsNullOrEmpty(m_storeLocation))
                    {
                        return Utils.Format("LocalMachine\\{0}", m_storeName);
                    }

                    return Utils.Format("{1}\\{0}", m_storeName, m_storeLocation);
                }

                return m_storePath;
            }

            set
            {
                m_storePath = value;

                if (!String.IsNullOrEmpty(m_storePath))
                {
                    if (String.IsNullOrEmpty(m_storeType))
                    {
                        m_storeType = CertificateStoreIdentifier.DetermineStoreType(m_storePath);
                    }
                }
            }
        }

        /// <summary>
        /// The name of the store that contains the certificate.
        /// </summary>
        /// <value>The name of the store.</value>
        /// <seealso cref="System.Security.Cryptography.X509Certificates.StoreName"/>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 2)]
        [Obsolete("Use StoreType/StorePath instead")]
        public string StoreName
        {
            get { return m_storeName; }
            set { m_storeName = value; }
        }

        /// <summary>
        /// The location of the store that contains the certificate.
        /// </summary>
        /// <value>The store location.</value>
        /// <seealso cref="System.Security.Cryptography.X509Certificates.StoreLocation"/>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 3)]
        [Obsolete("Use StoreType/StorePath instead")]
        public string StoreLocation
        {
            get { return m_storeLocation; }
            set { m_storeLocation = value; }
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
        /// This notation is designed to be convenient for common forms of name. This section gives a few 
        /// examples of distinguished names written using this notation. First is a name containing three relative
        /// distinguished names (RDNs):
        /// <code>CN=Steve Kille,O=Isode Limited,C=GB</code>
        /// 
        /// RFC 3280 Internet X.509 Public Key Infrastructure, April 2002
        /// RFC 2253 LADPv3 Distinguished Names, December 1997
        /// </remarks>
        /// <seealso cref="System.Security.Cryptography.X509Certificates.X500DistinguishedName"/>
        /// <seealso cref="System.Security.Cryptography.AsnEncodedData"/>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 4)]
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
                if (m_certificate != null && !String.IsNullOrEmpty(value))
                {
                    if (m_certificate.Subject != value)
                    {
                        throw new ArgumentException("SubjectName does not match the SubjectName of the current certificate.");
                    }
                }

                m_subjectName = value;
            }
        }

        /// <summary>
        /// The certificate's thumbprint.
        /// </summary>
        /// <value>The thumbprint of a certificate..</value>
        /// <seealso cref="X509Certificate2"/>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 5)]
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
                if (m_certificate != null)
                {
                    if (!String.IsNullOrEmpty(value) && m_certificate.Thumbprint != value)
                    {
                        throw new ArgumentException("Thumbprint does not match the thumbprint of the current certificate.");
                    }
                }

                m_thumbprint = value;
            }
        }

        /// <summary>
        /// Gets the DER encoded certificate data or create emebeded in this instance certifcate using the DER encoded certificate data.
        /// </summary>
        /// <value>A byte array containing the X.509 certificate data.</value>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 6)]
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

                m_certificate = CertificateFactory.Create(value, true);
                m_subjectName = m_certificate.Subject;
                m_thumbprint = m_certificate.Thumbprint;
            }
        }

        /// <summary>
        /// Gets or sets the XML encoded validation options - use to serialize the validation options.
        /// </summary>
        /// <value>The XML encoded validation options.</value>
        [DataMember(Name = "ValidationOptions", IsRequired = false, EmitDefaultValue = false, Order = 7)]
        private int XmlEncodedValidationOptions
        {
            get { return (int)m_validationOptions; }
            set { m_validationOptions = (CertificateValidationOptions)value; }
        }
        #endregion

        #region Private Fields
        private string m_storeType;
        private string m_storePath;
        private string m_storeLocation;
        private string m_storeName;
        private string m_subjectName;
        private string m_thumbprint;
        private X509Certificate2 m_certificate;
        private CertificateValidationOptions m_validationOptions;
        #endregion
    }
    #endregion

    #region ConfiguredEndpointCollection Class
    /// <summary>
    /// Stores a list of cached enpoints.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class ConfiguredEndpointCollection
    {
        #region Constructors
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
        public void Initialize(StreamingContext context) => Initialize();

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_knownHosts = new StringCollection();
            m_discoveryUrls = new StringCollection(Utils.DiscoveryUrls);
            m_endpoints = new List<ConfiguredEndpoint>();
            m_defaultConfiguration = EndpointConfiguration.Create();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// A list of known hosts that can be used for discovery.
        /// </summary>
        [DataMember(Name = "KnownHosts", IsRequired = false, Order = 1)]
        public StringCollection KnownHosts
        {
            get
            {
                return m_knownHosts;
            }

            set
            {
                if (value == null)
                {
                    m_knownHosts = new StringCollection();
                }
                else
                {
                    m_knownHosts = value;
                }
            }
        }

        /// <summary>
        /// The default configuration to use when connecting to an endpoint.
        /// </summary>
        [DataMember(Name = "Endpoints", IsRequired = false, Order = 2)]
        public List<ConfiguredEndpoint> Endpoints
        {
            get
            {
                return m_endpoints;
            }

            private set
            {
                if (value == null)
                {
                    m_endpoints = new List<ConfiguredEndpoint>();
                }
                else
                {
                    m_endpoints = value;
                }

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
        public Uri TcpProxyUrl
        {
            get
            {
                return m_tcpProxyUrl;
            }

            set
            {
                m_tcpProxyUrl = value;
            }
        }
        #endregion

        #region Private Fields
        private string m_filepath;
        private StringCollection m_knownHosts;
        private StringCollection m_discoveryUrls;
        private EndpointConfiguration m_defaultConfiguration;
        private List<ConfiguredEndpoint> m_endpoints;
        private Uri m_tcpProxyUrl;
        #endregion
    }
    #endregion

    #region ConfiguredEndpoint Class
    /// <summary>
    /// Stores the configuration information for an endpoint.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    [KnownType(typeof(UserNameIdentityToken))]
    [KnownType(typeof(X509IdentityToken))]
    [KnownType(typeof(IssuedIdentityToken))]
    public partial class ConfiguredEndpoint
    {
        #region Constructors
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
        public void Initialize(StreamingContext context) => Initialize();

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_collection = null;
            m_description = new EndpointDescription();
            m_configuration = null;
            m_updateBeforeConnect = true;
            m_binaryEncodingSupport = BinaryEncodingSupport.Optional;
            m_selectedUserTokenPolicyIndex = 0;
            m_userIdentity = null;
            m_reverseConnect = null;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The description for the endpoint.
        /// </summary>
        [DataMember(Name = "Endpoint", Order = 1, IsRequired = true)]
        public EndpointDescription Description
        {
            get
            {
                return m_description;
            }

            private set
            {
                if (value == null)
                {
                    m_description = new EndpointDescription();
                }
                else
                {
                    m_description = value;
                }
            }
        }

        /// <summary>
        /// The configuration to use when connecting to an endpoint.
        /// </summary>
        [DataMember(Name = "Configuration", Order = 2, IsRequired = false)]
        public EndpointConfiguration Configuration
        {
            get
            {
                return m_configuration;
            }

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
        public bool UpdateBeforeConnect
        {
            get { return m_updateBeforeConnect; }
            set { m_updateBeforeConnect = value; }
        }

        /// <summary>
        /// The user identity to use when connecting to the endpoint.
        /// </summary>
        [DataMember(Name = "BinaryEncodingSupport", Order = 4, IsRequired = false)]
        public BinaryEncodingSupport BinaryEncodingSupport
        {
            get { return m_binaryEncodingSupport; }
            set { m_binaryEncodingSupport = value; }
        }

        /// <summary>
        /// The user identity to use when connecting to the endpoint.
        /// </summary>
        [DataMember(Name = "SelectedUserTokenPolicy", Order = 5, IsRequired = false)]
        public int SelectedUserTokenPolicyIndex
        {
            get { return m_selectedUserTokenPolicyIndex; }
            set { m_selectedUserTokenPolicyIndex = value; }
        }

        /// <summary>
        /// The user identity to use when connecting to the endpoint.
        /// </summary>
        [DataMember(Name = "UserIdentity", Order = 6, IsRequired = false)]
        public UserIdentityToken UserIdentity
        {
            get { return m_userIdentity; }
            set { m_userIdentity = value; }
        }

        /// <summary>
        /// The reverse connect information.
        /// </summary>
        [DataMember(Name = "ReverseConnect", Order = 8, IsRequired = false)]
        public ReverseConnectEndpoint ReverseConnect
        {
            get { return m_reverseConnect; }
            set { m_reverseConnect = value; }
        }

        /// <summary>
        /// A bucket to store additional application specific configuration data.
        /// </summary>
        [DataMember(IsRequired = false, EmitDefaultValue = false, Order = 9)]
        public XmlElementCollection Extensions
        {
            get { return m_extensions; }
            set { m_extensions = value; }
        }
        #endregion

        #region Private Fields
        private ConfiguredEndpointCollection m_collection;
        private EndpointDescription m_description;
        private EndpointConfiguration m_configuration;
        private bool m_updateBeforeConnect;
        private BinaryEncodingSupport m_binaryEncodingSupport;
        private int m_selectedUserTokenPolicyIndex;
        private UserIdentityToken m_userIdentity;
        private ReverseConnectEndpoint m_reverseConnect;
        private XmlElementCollection m_extensions;
        #endregion
    }
    #endregion

    #region BinaryEncodingSupport Enumeration
    /// <summary>
    /// The type of binary encoding support allowed by a channel.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public enum BinaryEncodingSupport
    {
        /// <summary>
        /// The UA binary encoding may be used.
        /// </summary>
        [EnumMember()]
        Optional,

        /// <summary>
        /// The UA binary encoding must be used.
        /// </summary>
        [EnumMember()]
        Required,

        /// <summary>
        /// The UA binary encoding may not be used.
        /// </summary>
        [EnumMember()]
        None
    }
    #endregion

    #region ReverseConnectEndpoint Class
    /// <summary>
    /// Stores the reverse connect information for an endpoint.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaConfig)]
    public partial class ReverseConnectEndpoint
    {
        #region Constructors
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
        public void Initialize(StreamingContext context) => Initialize();

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_enabled = false;
            m_serverUri = null;
            m_thumbprint = null;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Whether reverse connect is enabled for the endpoint.
        /// </summary>
        [DataMember(Name = "Enabled", Order = 1, IsRequired = false)]
        public bool Enabled
        {
            get { return m_enabled; }
            set { m_enabled = value; }
        }

        /// <summary>
        /// The server Uri of the endpoint.
        /// </summary>
        [DataMember(Name = "ServerUri", Order = 2, IsRequired = false)]
        public string ServerUri
        {
            get { return m_serverUri; }
            set { m_serverUri = value; }
        }

        /// <summary>
        /// The thumbprint of the certificate which contains
        /// the server Uri.
        /// </summary>
        [DataMember(Name = "Thumbprint", Order = 3, IsRequired = false)]
        public string Thumbprint
        {
            get { return m_thumbprint; }
            set { m_thumbprint = value; }
        }
        #endregion

        #region Private Fields
        private bool m_enabled;
        private string m_serverUri;
        private string m_thumbprint;
        #endregion
    }
    #endregion
}
