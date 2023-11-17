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
using System.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// The certificates used by the tls/ssl layer 
    /// </summary>
    public class MqttTlsCertificates
    {
        #region Private menbers

        private X509Certificate m_caCertificate;
        private X509Certificate m_clientCertificate;

        #endregion Private menbers

        #region Constructor
        /// <summary>
        /// Constructor 
        /// </summary>
        /// <param name="caCertificatePath"></param>
        /// <param name="clientCertificatePath"></param>
        /// <param name="clientCertificatePassword"></param>
        public MqttTlsCertificates(string caCertificatePath = null,
            string clientCertificatePath = null, string clientCertificatePassword = null)
        {
            CaCertificatePath = caCertificatePath ?? "";
            ClientCertificatePath = clientCertificatePath ?? "";
            ClientCertificatePassword = clientCertificatePassword ?? "";

            if (!string.IsNullOrEmpty(CaCertificatePath))
            {
                m_caCertificate = X509Certificate.CreateFromCertFile(CaCertificatePath);
            }
            if (!string.IsNullOrEmpty(clientCertificatePath))
            {
                m_clientCertificate = new X509Certificate2(clientCertificatePath, ClientCertificatePassword);
            }

            KeyValuePairs = new KeyValuePairCollection();

            QualifiedName qCaCertificatePath = EnumMqttClientConfigurationParameters.TlsCertificateCaCertificatePath.ToString();
            KeyValuePairs.Add(new KeyValuePair { Key = qCaCertificatePath, Value = CaCertificatePath });

            QualifiedName qClientCertificatePath = EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePath.ToString();
            KeyValuePairs.Add(new KeyValuePair { Key = qClientCertificatePath, Value = ClientCertificatePath });

            QualifiedName qClientCertificatePassword = EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePassword.ToString();
            KeyValuePairs.Add(new KeyValuePair { Key = qClientCertificatePassword, Value = ClientCertificatePassword });
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="keyValuePairs"></param>
        public MqttTlsCertificates(KeyValuePairCollection keyValuePairs)
        {
            CaCertificatePath = "";
            QualifiedName qCaCertificatePath = EnumMqttClientConfigurationParameters.TlsCertificateCaCertificatePath.ToString();
            CaCertificatePath = keyValuePairs.Find(kvp => kvp.Key.Name.Equals(qCaCertificatePath.Name))?.Value.Value as string;

            ClientCertificatePath = "";
            QualifiedName qClientCertificatePath = EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePath.ToString();
            ClientCertificatePath = keyValuePairs.Find(kvp => kvp.Key.Name.Equals(qClientCertificatePath.Name))?.Value.Value as string;

            ClientCertificatePassword = "";
            QualifiedName qClientCertificatePassword = EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePassword.ToString();
            ClientCertificatePassword = keyValuePairs.Find(kvp => kvp.Key.Name.Equals(qClientCertificatePassword.Name))?.Value.Value as string;

            KeyValuePairs = keyValuePairs;

            if (!string.IsNullOrEmpty(CaCertificatePath))
            {
                m_caCertificate = X509Certificate.CreateFromCertFile(CaCertificatePath);
            }
            if (!string.IsNullOrEmpty(ClientCertificatePath))
            {
                m_clientCertificate = new X509Certificate2(ClientCertificatePath, ClientCertificatePassword);
            }

        }
        #endregion Constructor

        #region Internal Properties
        internal string CaCertificatePath { get; set; }
        internal string ClientCertificatePath { get; set; }
        internal string ClientCertificatePassword { get; set; }

        internal KeyValuePairCollection KeyValuePairs { get; set; }

        internal List<X509Certificate> X509Certificates
        {
            get
            {
                var values = new List<X509Certificate>();
                if (m_caCertificate != null)
                {
                    values.Add(m_caCertificate);
                }
                if (m_clientCertificate != null)
                {
                    values.Add(m_clientCertificate);
                }

                return values;
            }
        }
        #endregion  Internal Properties
    }

    /// <summary>
    /// The implementation of the Tls client options
    /// </summary>
    public class MqttTlsOptions
    {
        #region Constructor
        /// <summary>
        /// Default constructor
        /// </summary>
        public MqttTlsOptions()
        {
            Certificates = null;
            SslProtocolVersion = SslProtocols.None;
            AllowUntrustedCertificates = false;
            IgnoreCertificateChainErrors = false;
            IgnoreRevocationListErrors = false;

            TrustedIssuerCertificates = null;
            TrustedPeerCertificates = null;
            RejectedCertificateStore = null;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="kvpMqttOptions">The key value pairs representing the values from which to construct MqttTlsOptions</param>
        public MqttTlsOptions(KeyValuePairCollection kvpMqttOptions)
        {
            Certificates = new MqttTlsCertificates(kvpMqttOptions);

            QualifiedName qSslProtocolVersion = EnumMqttClientConfigurationParameters.TlsProtocolVersion.ToString();
            SslProtocolVersion = (SslProtocols)Convert.ToInt32(kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qSslProtocolVersion.Name))?.Value.Value);

            QualifiedName qAllowUntrustedCertificates = EnumMqttClientConfigurationParameters.TlsAllowUntrustedCertificates.ToString();
            AllowUntrustedCertificates = Convert.ToBoolean(kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qAllowUntrustedCertificates.Name))?.Value.Value);

            QualifiedName qIgnoreCertificateChainErrors = EnumMqttClientConfigurationParameters.TlsIgnoreCertificateChainErrors.ToString();
            IgnoreCertificateChainErrors = Convert.ToBoolean(kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qIgnoreCertificateChainErrors.Name))?.Value.Value);

            QualifiedName qIgnoreRevocationListErrors = EnumMqttClientConfigurationParameters.TlsIgnoreRevocationListErrors.ToString();
            IgnoreRevocationListErrors = Convert.ToBoolean(kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qIgnoreRevocationListErrors.Name))?.Value.Value);

            QualifiedName qTrustedIssuerCertificatesStoreType = EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStoreType.ToString();
            string issuerCertificatesStoreType = kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qTrustedIssuerCertificatesStoreType.Name))?.Value.Value as string;
            QualifiedName qTrustedIssuerCertificatesStorePath = EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStorePath.ToString();
            string issuerCertificatesStorePath = kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qTrustedIssuerCertificatesStorePath.Name))?.Value.Value as string;

            TrustedIssuerCertificates = new CertificateTrustList {
                StoreType = issuerCertificatesStoreType,
                StorePath = issuerCertificatesStorePath
            };

            QualifiedName qTrustedPeerCertificatesStoreType = EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStoreType.ToString();
            string peerCertificatesStoreType = kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qTrustedPeerCertificatesStoreType.Name))?.Value.Value as string;
            QualifiedName qTrustedPeerCertificatesStorePath = EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStorePath.ToString();
            string peerCertificatesStorePath = kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qTrustedPeerCertificatesStorePath.Name))?.Value.Value as string;

            TrustedPeerCertificates = new CertificateTrustList {
                StoreType = peerCertificatesStoreType,
                StorePath = peerCertificatesStorePath
            };

            QualifiedName qRejectedCertificateStoreStoreType = EnumMqttClientConfigurationParameters.RejectedCertificateStoreStoreType.ToString();
            string rejectedCertificateStoreStoreType = kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qRejectedCertificateStoreStoreType.Name))?.Value.Value as string;
            QualifiedName qRejectedCertificateStoreStorePath = EnumMqttClientConfigurationParameters.RejectedCertificateStoreStorePath.ToString();
            string rejectedCertificateStoreStorePath = kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qRejectedCertificateStoreStorePath.Name))?.Value.Value as string;

            RejectedCertificateStore = new CertificateTrustList {
                StoreType = rejectedCertificateStoreStoreType,
                StorePath = rejectedCertificateStoreStorePath
            };

            KeyValuePairs = kvpMqttOptions;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="certificates">The certificates used for encrypted communication including the CA certificate</param>
        /// <param name="sslProtocolVersion">The preferred version of SSL protocol</param>
        /// <param name="allowUntrustedCertificates">Specifies if untrusted certificates should be accepted in the process of certificate validation</param>
        /// <param name="ignoreCertificateChainErrors">Specifies if Certificate Chain errors should be validated in the process of certificate validation</param>
        /// <param name="ignoreRevocationListErrors">Specifies if Certificate Revocation List errors should be validated in the process of certificate validation</param>
        /// <param name="trustedIssuerCertificates">The trusted issuer certifficates store identifier</param>
        /// <param name="trustedPeerCertificates">The trusted peer certifficates store identifier</param>
        /// <param name="rejectedCertificateStore">The rejected certifficates store identifier</param>
        public MqttTlsOptions(MqttTlsCertificates certificates = null,
            SslProtocols sslProtocolVersion = SslProtocols.Tls12,
            bool allowUntrustedCertificates = false,
            bool ignoreCertificateChainErrors = false,
            bool ignoreRevocationListErrors = false,
            CertificateStoreIdentifier trustedIssuerCertificates = null,
            CertificateStoreIdentifier trustedPeerCertificates = null,
            CertificateStoreIdentifier rejectedCertificateStore = null
            )
        {
            Certificates = certificates;
            SslProtocolVersion = sslProtocolVersion;
            AllowUntrustedCertificates = allowUntrustedCertificates;
            IgnoreCertificateChainErrors = ignoreCertificateChainErrors;
            IgnoreRevocationListErrors = ignoreRevocationListErrors;

            TrustedIssuerCertificates = trustedIssuerCertificates;
            TrustedPeerCertificates = trustedPeerCertificates;
            RejectedCertificateStore = rejectedCertificateStore;

            KeyValuePairs = new KeyValuePairCollection();

            if (Certificates != null)
            {
                KeyValuePairs.AddRange(Certificates.KeyValuePairs);
            }

            var kvpTlsProtocolVersion = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.TlsProtocolVersion.ToString(),
                Value = (int)SslProtocolVersion
            };
            KeyValuePairs.Add(kvpTlsProtocolVersion);
            var kvpAllowUntrustedCertificates = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.TlsAllowUntrustedCertificates.ToString(),
                Value = AllowUntrustedCertificates
            };
            KeyValuePairs.Add(kvpAllowUntrustedCertificates);
            var kvpIgnoreCertificateChainErrors = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.TlsIgnoreCertificateChainErrors.ToString(),
                Value = IgnoreCertificateChainErrors
            };
            KeyValuePairs.Add(kvpIgnoreCertificateChainErrors);
            var kvpIgnoreRevocationListErrors = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.TlsIgnoreRevocationListErrors.ToString(),
                Value = IgnoreRevocationListErrors
            };
            KeyValuePairs.Add(kvpIgnoreRevocationListErrors);

            var kvpTrustedIssuerCertificatesStoreType = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStoreType.ToString(),
                Value = TrustedIssuerCertificates?.StoreType
            };
            KeyValuePairs.Add(kvpTrustedIssuerCertificatesStoreType);
            var kvpTrustedIssuerCertificatesStorePath = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStorePath.ToString(),
                Value = TrustedIssuerCertificates?.StorePath
            };
            KeyValuePairs.Add(kvpTrustedIssuerCertificatesStorePath);

            var kvpTrustedPeerCertificatesStoreType = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStoreType.ToString(),
                Value = TrustedPeerCertificates?.StoreType
            };
            KeyValuePairs.Add(kvpTrustedPeerCertificatesStoreType);
            var kvpTrustedPeerCertificatesStorePath = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStorePath.ToString(),
                Value = TrustedPeerCertificates?.StorePath
            };
            KeyValuePairs.Add(kvpTrustedPeerCertificatesStorePath);

            var kvpRejectedCertificateStoreStoreType = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.RejectedCertificateStoreStoreType.ToString(),
                Value = RejectedCertificateStore?.StoreType
            };
            KeyValuePairs.Add(kvpRejectedCertificateStoreStoreType);
            var kvpRejectedCertificateStoreStorePath = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.RejectedCertificateStoreStorePath.ToString(),
                Value = RejectedCertificateStore?.StorePath
            };
            KeyValuePairs.Add(kvpRejectedCertificateStoreStorePath);
        }
        #endregion

        #region Internal Properties
        internal MqttTlsCertificates Certificates { get; set; }
        internal SslProtocols SslProtocolVersion { get; set; }
        internal bool AllowUntrustedCertificates { get; set; }
        internal bool IgnoreCertificateChainErrors { get; set; }
        internal bool IgnoreRevocationListErrors { get; set; }
        internal CertificateStoreIdentifier TrustedIssuerCertificates { get; set; }
        internal CertificateStoreIdentifier TrustedPeerCertificates { get; set; }
        internal CertificateStoreIdentifier RejectedCertificateStore { get; set; }
        internal KeyValuePairCollection KeyValuePairs { get; set; }
        #endregion
    }

    /// <summary>
    /// The implementation of the Mqtt specific client configuration
    /// </summary>
    public class MqttClientProtocolConfiguration : ITransportProtocolConfiguration
    {
        #region Private
        #endregion

        #region Constructor
        /// <summary>
        /// Constructor
        /// </summary>
        public MqttClientProtocolConfiguration()
        {
            UserName = null;
            Password = null;
            AzureClientId = null;
            CleanSession = true;
            ProtocolVersion = EnumMqttProtocolVersion.V310;
            MqttTlsOptions = null;
            ConnectionProperties = null;
        }

        /// <summary>
        /// Constructor 
        /// </summary>
        /// <param name="userName">UserName part of user credentials</param>
        /// <param name="password">Password part of user credentials</param>
        /// <param name="azureClientId">The Client Id used in an Azure connection</param>
        /// <param name="cleanSession">Specifies if the MQTT session to the broker should be clean</param>
        /// <param name="version">The version of the MQTT protocol (default V310)</param>
        /// <param name="mqttTlsOptions">Instance of <see cref="MqttTlsOptions"/></param>
        public MqttClientProtocolConfiguration(SecureString userName = null,
                                               SecureString password = null,
                                               string azureClientId = null,
                                               bool cleanSession = true,
                                               EnumMqttProtocolVersion version = EnumMqttProtocolVersion.V310,
                                               MqttTlsOptions mqttTlsOptions = null)
        {
            UserName = userName;
            Password = password;
            AzureClientId = azureClientId;
            CleanSession = cleanSession;
            ProtocolVersion = version;
            MqttTlsOptions = mqttTlsOptions;

            ConnectionProperties = new KeyValuePairCollection();

            var kvpUserName = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.UserName.ToString(),
                Value = new System.Net.NetworkCredential(string.Empty, UserName).Password
            };
            ConnectionProperties.Add(kvpUserName);
            var kvpPassword = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.Password.ToString(),
                Value = new System.Net.NetworkCredential(string.Empty, Password).Password
            };
            ConnectionProperties.Add(kvpPassword);
            var kvpAzureClientId = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.AzureClientId.ToString(),
                Value = AzureClientId
            };
            ConnectionProperties.Add(kvpAzureClientId);
            var kvpCleanSession = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.CleanSession.ToString(),
                Value = CleanSession
            };
            ConnectionProperties.Add(kvpCleanSession);
            var kvpProtocolVersion = new KeyValuePair {
                Key = EnumMqttClientConfigurationParameters.ProtocolVersion.ToString(),
                Value = (int)ProtocolVersion
            };
            ConnectionProperties.Add(kvpProtocolVersion);

            if (MqttTlsOptions != null)
            {
                ConnectionProperties.AddRange(MqttTlsOptions.KeyValuePairs);
            }
        }

        /// <summary>
        /// Constructs a MqttClientProtocolConfiguration from given keyValuePairs
        /// </summary>
        /// <param name="connectionProperties"></param>
        public MqttClientProtocolConfiguration(KeyValuePairCollection connectionProperties)
        {
            UserName = new SecureString();
            QualifiedName qUserName = EnumMqttClientConfigurationParameters.UserName.ToString();
            if (connectionProperties.Find(kvp => kvp.Key.Name.Equals(qUserName.Name))?.Value.Value is string sUserName)
            {
                foreach (char c in sUserName?.ToCharArray())
                {
                    UserName.AppendChar(c);
                }
            }

            Password = new SecureString();
            QualifiedName qPassword = EnumMqttClientConfigurationParameters.Password.ToString();
            if (connectionProperties.Find(kvp => kvp.Key.Name.Equals(qPassword.Name))?.Value.Value is string sPassword)
            {
                foreach (char c in sPassword?.ToCharArray())
                {
                    Password.AppendChar(c);
                }
            }

            QualifiedName qAzureClientId = EnumMqttClientConfigurationParameters.AzureClientId.ToString();
            AzureClientId = Convert.ToString(connectionProperties.Find(kvp => kvp.Key.Name.Equals(qAzureClientId.Name))?.Value.Value);

            QualifiedName qCleanSession = EnumMqttClientConfigurationParameters.CleanSession.ToString();
            CleanSession = Convert.ToBoolean(connectionProperties.Find(kvp => kvp.Key.Name.Equals(qCleanSession.Name))?.Value.Value);

            QualifiedName qProtocolVersion = EnumMqttClientConfigurationParameters.ProtocolVersion.ToString();
            ProtocolVersion = (EnumMqttProtocolVersion)Convert.ToInt32(connectionProperties.Find(kvp => kvp.Key.Name.Equals(qProtocolVersion.Name))?.Value.Value);
            if (ProtocolVersion == EnumMqttProtocolVersion.Unknown)
            {
                Utils.Trace(Utils.TraceMasks.Information, "Mqtt protocol version is Unknown and it will default to V310");
                ProtocolVersion = EnumMqttProtocolVersion.V310;
            }

            MqttTlsOptions = new MqttTlsOptions(connectionProperties);

            ConnectionProperties = connectionProperties;
        }
        #endregion

        #region Internal Properties
        internal SecureString UserName { get; set; }

        internal SecureString Password { get; set; }

        internal string AzureClientId { get; set; }

        internal bool CleanSession { get; set; }

        internal bool UseCredentials => (UserName != null) && (UserName.Length != 0);

        internal bool UseAzureClientId { get => (AzureClientId != null) && (AzureClientId.Length != 0); }

        internal EnumMqttProtocolVersion ProtocolVersion { get; set; }

        internal MqttTlsOptions MqttTlsOptions { get; set; }
        #endregion Internal Properties

        #region Public Properties
        /// <summary>
        /// The key value pairs representing the parameters of a MqttClientProtocolConfiguration
        /// </summary>
        public KeyValuePairCollection ConnectionProperties { get; set; }
        #endregion Public Propertis
    }
}
