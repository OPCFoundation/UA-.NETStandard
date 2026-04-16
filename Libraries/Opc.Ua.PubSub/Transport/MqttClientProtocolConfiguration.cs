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
using System.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// The certificates used by the tls/ssl layer
    /// </summary>
    public class MqttTlsCertificates
    {
        private readonly Certificate m_caCertificate;
        private readonly Certificate m_clientCertificate;

        /// <summary>
        /// Constructor
        /// </summary>
        public MqttTlsCertificates(
            string caCertificatePath = null,
            string clientCertificatePath = null,
            char[] clientCertificatePassword = null)
        {
            CaCertificatePath = caCertificatePath ?? string.Empty;
            ClientCertificatePath = clientCertificatePath ?? string.Empty;
            ClientCertificatePassword = clientCertificatePassword;

            if (!string.IsNullOrEmpty(CaCertificatePath))
            {
                m_caCertificate = new Certificate(CaCertificatePath);
            }
            if (!string.IsNullOrEmpty(clientCertificatePath))
            {
                m_clientCertificate = new Certificate(
                    clientCertificatePath,
                    ClientCertificatePassword);
            }

            KeyValuePairs = [];

            var qCaCertificatePath = QualifiedName.From(
                nameof(EnumMqttClientConfigurationParameters.TlsCertificateCaCertificatePath));
            KeyValuePairs +=
                new KeyValuePair { Key = qCaCertificatePath, Value = CaCertificatePath };

            var qClientCertificatePath = QualifiedName.From(
                nameof(EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePath));
            KeyValuePairs +=
                new KeyValuePair { Key = qClientCertificatePath, Value = ClientCertificatePath };

            var qClientCertificatePassword = QualifiedName.From(
                nameof(EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePassword));
            KeyValuePairs += new KeyValuePair
            {
                Key = qClientCertificatePassword,
                Value = ClientCertificatePassword == null ?
                    string.Empty :
                    new string(ClientCertificatePassword)
            };
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public MqttTlsCertificates(ArrayOf<KeyValuePair> keyValuePairs)
        {
            CaCertificatePath = string.Empty;
            var qCaCertificatePath = QualifiedName.From(
                nameof(EnumMqttClientConfigurationParameters.TlsCertificateCaCertificatePath));
            CaCertificatePath =
                keyValuePairs
                    .Find(kvp => kvp.Key.Name
                        .Equals(qCaCertificatePath.Name, StringComparison.Ordinal))?
                    .Value.GetString();

            ClientCertificatePath = string.Empty;
            var qClientCertificatePath = QualifiedName.From(
                nameof(EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePath));
            ClientCertificatePath =
                keyValuePairs
                    .Find(kvp => kvp.Key.Name
                        .Equals(qClientCertificatePath.Name, StringComparison.Ordinal))?
                    .Value.GetString();

            ClientCertificatePassword = null;
            var qClientCertificatePassword = QualifiedName.From(
                nameof(EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePassword));
            ClientCertificatePassword =
                ((keyValuePairs
                    .Find(kvp => kvp.Key.Name
                        .Equals(qClientCertificatePassword.Name, StringComparison.Ordinal))?
                    .Value.GetString())?.ToCharArray());

            KeyValuePairs = keyValuePairs;

            if (!string.IsNullOrEmpty(CaCertificatePath))
            {
                m_caCertificate = new Certificate(CaCertificatePath);
            }
            if (!string.IsNullOrEmpty(ClientCertificatePath))
            {
                m_clientCertificate = new Certificate(
                    ClientCertificatePath,
                    ClientCertificatePassword);
            }
        }

        internal string CaCertificatePath { get; set; }
        internal string ClientCertificatePath { get; set; }
        internal char[] ClientCertificatePassword { get; set; }

        internal ArrayOf<KeyValuePair> KeyValuePairs { get; set; }

        internal List<Certificate> X509Certificates
        {
            get
            {
                var values = new List<Certificate>();
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
    }

    /// <summary>
    /// The implementation of the Tls client options
    /// </summary>
    public class MqttTlsOptions
    {
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
        public MqttTlsOptions(ArrayOf<KeyValuePair> kvpMqttOptions)
        {
            Certificates = new MqttTlsCertificates(kvpMqttOptions);

            var qSslProtocolVersion = QualifiedName.From(
                nameof(EnumMqttClientConfigurationParameters.TlsProtocolVersion));
#pragma warning disable CA5397 // TODO: Use None as default fallback
            SslProtocolVersion =
                (SslProtocols)(kvpMqttOptions
                    .Find(kvp => kvp.Key.Name
                        .Equals(qSslProtocolVersion.Name, StringComparison.Ordinal))?
                    .Value.ConvertToInt32().GetInt32() ??
                    default);
#pragma warning restore CA5397

            var qAllowUntrustedCertificates = QualifiedName.From(
                nameof(EnumMqttClientConfigurationParameters.TlsAllowUntrustedCertificates));
            AllowUntrustedCertificates =
                kvpMqttOptions
                    .Find(kvp => kvp.Key.Name
                        .Equals(qAllowUntrustedCertificates.Name, StringComparison.Ordinal))?
                    .Value.ConvertToBoolean().GetBoolean() ??
                false;

            var qIgnoreCertificateChainErrors = QualifiedName.From(
                nameof(EnumMqttClientConfigurationParameters.TlsIgnoreCertificateChainErrors));
            IgnoreCertificateChainErrors =
                kvpMqttOptions
                    .Find(kvp => kvp.Key.Name
                        .Equals(qIgnoreCertificateChainErrors.Name, StringComparison.Ordinal))?
                    .Value.ConvertToBoolean().GetBoolean() ??
                false;

            var qIgnoreRevocationListErrors = QualifiedName.From(
                nameof(EnumMqttClientConfigurationParameters.TlsIgnoreRevocationListErrors));
            IgnoreRevocationListErrors =
                kvpMqttOptions
                    .Find(kvp => kvp.Key.Name
                        .Equals(qIgnoreRevocationListErrors.Name, StringComparison.Ordinal))?
                    .Value.ConvertToBoolean().GetBoolean() ??
                false;

            var qTrustedIssuerCertificatesStoreType = QualifiedName.From(
                nameof(EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStoreType));
            string issuerCertificatesStoreType =
                kvpMqttOptions
                    .Find(kvp =>
                        kvp.Key.Name.Equals(
                            qTrustedIssuerCertificatesStoreType.Name,
                            StringComparison.Ordinal)
                    )?
                    .Value.GetString();
            var qTrustedIssuerCertificatesStorePath = QualifiedName.From(
                nameof(EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStorePath));
            string issuerCertificatesStorePath =
                kvpMqttOptions
                    .Find(kvp =>
                        kvp.Key.Name.Equals(
                            qTrustedIssuerCertificatesStorePath.Name,
                            StringComparison.Ordinal)
                    )?
                    .Value.GetString();

            TrustedIssuerCertificates = new CertificateTrustList
            {
                StoreType = issuerCertificatesStoreType,
                StorePath = issuerCertificatesStorePath
            };

            var qTrustedPeerCertificatesStoreType = QualifiedName.From(nameof(
                EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStoreType));
            string peerCertificatesStoreType =
                kvpMqttOptions
                    .Find(kvp => kvp.Key.Name
                        .Equals(qTrustedPeerCertificatesStoreType.Name, StringComparison.Ordinal))?
                    .Value.GetString();
            var qTrustedPeerCertificatesStorePath = QualifiedName.From(nameof(
                EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStorePath));
            string peerCertificatesStorePath =
                kvpMqttOptions
                    .Find(kvp => kvp.Key.Name
                        .Equals(qTrustedPeerCertificatesStorePath.Name, StringComparison.Ordinal))?
                    .Value.GetString();

            TrustedPeerCertificates = new CertificateTrustList
            {
                StoreType = peerCertificatesStoreType,
                StorePath = peerCertificatesStorePath
            };

            var qRejectedCertificateStoreStoreType = QualifiedName.From(nameof(
                EnumMqttClientConfigurationParameters.RejectedCertificateStoreStoreType));
            string rejectedCertificateStoreStoreType =
                kvpMqttOptions
                    .Find(
                        kvp => kvp.Key.Name.Equals(
                            qRejectedCertificateStoreStoreType.Name,
                            StringComparison.Ordinal))?
                    .Value.GetString();
            var qRejectedCertificateStoreStorePath = QualifiedName.From(nameof(
                EnumMqttClientConfigurationParameters.RejectedCertificateStoreStorePath));
            string rejectedCertificateStoreStorePath =
                kvpMqttOptions
                    .Find(
                        kvp => kvp.Key.Name.Equals(
                            qRejectedCertificateStoreStorePath.Name,
                            StringComparison.Ordinal))?
                    .Value.GetString();

            RejectedCertificateStore = new CertificateTrustList
            {
                StoreType = rejectedCertificateStoreStoreType,
                StorePath = rejectedCertificateStoreStorePath
            };

            KeyValuePairs = kvpMqttOptions;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="certificates">The certificates used for encrypted communication including the CA certificate</param>
        /// <param name="sslProtocolVersion">The preferred version of SSL protocol - defaults to None to let OS choose the best version</param>
        /// <param name="allowUntrustedCertificates">Specifies if untrusted certificates should be accepted in the process of certificate validation</param>
        /// <param name="ignoreCertificateChainErrors">Specifies if Certificate Chain errors should be validated in the process of certificate validation</param>
        /// <param name="ignoreRevocationListErrors">Specifies if Certificate Revocation List errors should be validated in the process of certificate validation</param>
        /// <param name="trustedIssuerCertificates">The trusted issuer certificates store identifier</param>
        /// <param name="trustedPeerCertificates">The trusted peer certificates store identifier</param>
        /// <param name="rejectedCertificateStore">The rejected certificates store identifier</param>
        public MqttTlsOptions(
            MqttTlsCertificates certificates = null,
            SslProtocols sslProtocolVersion = SslProtocols.None,
            bool allowUntrustedCertificates = false,
            bool ignoreCertificateChainErrors = false,
            bool ignoreRevocationListErrors = false,
            CertificateStoreIdentifier trustedIssuerCertificates = null,
            CertificateStoreIdentifier trustedPeerCertificates = null,
            CertificateStoreIdentifier rejectedCertificateStore = null)
        {
            Certificates = certificates;
            SslProtocolVersion = sslProtocolVersion;
            AllowUntrustedCertificates = allowUntrustedCertificates;
            IgnoreCertificateChainErrors = ignoreCertificateChainErrors;
            IgnoreRevocationListErrors = ignoreRevocationListErrors;

            TrustedIssuerCertificates = trustedIssuerCertificates;
            TrustedPeerCertificates = trustedPeerCertificates;
            RejectedCertificateStore = rejectedCertificateStore;

            KeyValuePairs = Certificates.KeyValuePairs;

            var kvpTlsProtocolVersion = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.TlsProtocolVersion)),
                Value = (int)SslProtocolVersion
            };
            KeyValuePairs += kvpTlsProtocolVersion;
            var kvpAllowUntrustedCertificates = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.TlsAllowUntrustedCertificates)),
                Value = AllowUntrustedCertificates
            };
            KeyValuePairs += kvpAllowUntrustedCertificates;
            var kvpIgnoreCertificateChainErrors = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.TlsIgnoreCertificateChainErrors)),
                Value = IgnoreCertificateChainErrors
            };
            KeyValuePairs += kvpIgnoreCertificateChainErrors;
            var kvpIgnoreRevocationListErrors = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.TlsIgnoreRevocationListErrors)),
                Value = IgnoreRevocationListErrors
            };
            KeyValuePairs += kvpIgnoreRevocationListErrors;

            var kvpTrustedIssuerCertificatesStoreType = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStoreType)),
                Value = TrustedIssuerCertificates?.StoreType
            };
            KeyValuePairs += kvpTrustedIssuerCertificatesStoreType;
            var kvpTrustedIssuerCertificatesStorePath = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStorePath)),
                Value = TrustedIssuerCertificates?.StorePath
            };
            KeyValuePairs += kvpTrustedIssuerCertificatesStorePath;

            var kvpTrustedPeerCertificatesStoreType = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStoreType)),
                Value = TrustedPeerCertificates?.StoreType
            };
            KeyValuePairs += kvpTrustedPeerCertificatesStoreType;
            var kvpTrustedPeerCertificatesStorePath = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStorePath)),
                Value = TrustedPeerCertificates?.StorePath
            };
            KeyValuePairs += kvpTrustedPeerCertificatesStorePath;

            var kvpRejectedCertificateStoreStoreType = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.RejectedCertificateStoreStoreType)),
                Value = RejectedCertificateStore?.StoreType
            };
            KeyValuePairs += kvpRejectedCertificateStoreStoreType;
            var kvpRejectedCertificateStoreStorePath = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.RejectedCertificateStoreStorePath)),
                Value = RejectedCertificateStore?.StorePath
            };
            KeyValuePairs += kvpRejectedCertificateStoreStorePath;
        }

        internal MqttTlsCertificates Certificates { get; set; }
        internal SslProtocols SslProtocolVersion { get; set; }
        internal bool AllowUntrustedCertificates { get; set; }
        internal bool IgnoreCertificateChainErrors { get; set; }
        internal bool IgnoreRevocationListErrors { get; set; }
        internal CertificateStoreIdentifier TrustedIssuerCertificates { get; set; }
        internal CertificateStoreIdentifier TrustedPeerCertificates { get; set; }
        internal CertificateStoreIdentifier RejectedCertificateStore { get; set; }
        internal ArrayOf<KeyValuePair> KeyValuePairs { get; set; }
    }

    /// <summary>
    /// The implementation of the Mqtt specific client configuration
    /// </summary>
    public class MqttClientProtocolConfiguration : ITransportProtocolConfiguration
    {
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
            ConnectionProperties = default;
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
        public MqttClientProtocolConfiguration(

            SecureString userName = null,
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

            ConnectionProperties = [];

            var kvpUserName = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.UserName)),
                Value = new System.Net.NetworkCredential(string.Empty, UserName).Password
            };
            ConnectionProperties = ConnectionProperties.AddItem(kvpUserName);
            var kvpPassword = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.Password)),
                Value = new System.Net.NetworkCredential(string.Empty, Password).Password
            };
            ConnectionProperties = ConnectionProperties.AddItem(kvpPassword);
            var kvpAzureClientId = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.AzureClientId)),
                Value = AzureClientId
            };
            ConnectionProperties = ConnectionProperties.AddItem(kvpAzureClientId);
            var kvpCleanSession = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.CleanSession)),
                Value = CleanSession
            };
            ConnectionProperties = ConnectionProperties.AddItem(kvpCleanSession);
            var kvpProtocolVersion = new KeyValuePair
            {
                Key = QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.ProtocolVersion)),
                Value = (int)ProtocolVersion
            };
            ConnectionProperties = ConnectionProperties.AddItem(kvpProtocolVersion);

            if (MqttTlsOptions != null)
            {
                ConnectionProperties = ConnectionProperties.AddItems(MqttTlsOptions.KeyValuePairs);
            }
        }

        /// <summary>
        /// Constructs a MqttClientProtocolConfiguration from given keyValuePairs
        /// </summary>
        public MqttClientProtocolConfiguration(
            ArrayOf<KeyValuePair> connectionProperties,
            ILogger logger)
        {
            UserName = new SecureString();
            var qUserName =
                QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.UserName));
            if ((connectionProperties
                    .Find(kvp => kvp.Key.Name.Equals(qUserName.Name, StringComparison.Ordinal))?
                    .Value ??
                default).TryGet(out string sUserName))
            {
                foreach (char c in sUserName)
                {
                    UserName.AppendChar(c);
                }
            }

            Password = new SecureString();
            var qPassword =
                QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.Password));
            if ((connectionProperties
                    .Find(kvp => kvp.Key.Name.Equals(qPassword.Name, StringComparison.Ordinal))?
                    .Value ??
                default).TryGet(out string sPassword))
            {
                foreach (char c in sPassword)
                {
                    Password.AppendChar(c);
                }
            }

            var qAzureClientId =
                QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.AzureClientId));
            AzureClientId =
                connectionProperties
                    .Find(
                        kvp => kvp.Key.Name.Equals(qAzureClientId.Name, StringComparison.Ordinal))?
                    .Value.ConvertToString().GetString();

            var qCleanSession =
                QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.CleanSession));
            CleanSession =
                connectionProperties
                    .Find(kvp => kvp.Key.Name.Equals(qCleanSession.Name, StringComparison.Ordinal))?
                    .Value.ConvertToBoolean().GetBoolean() ??
                false;

            var qProtocolVersion =
                QualifiedName.From(nameof(EnumMqttClientConfigurationParameters.ProtocolVersion));
            ProtocolVersion =
                (EnumMqttProtocolVersion)(connectionProperties
                    .Find(kvp => kvp.Key.Name
                        .Equals(qProtocolVersion.Name, StringComparison.Ordinal))?
                    .Value.ConvertToInt32().GetInt32() ??
                    default);
            if (ProtocolVersion == EnumMqttProtocolVersion.Unknown)
            {
                logger.LogInformation(
                    "Mqtt protocol version is Unknown and it will default to V310");
                ProtocolVersion = EnumMqttProtocolVersion.V310;
            }

            MqttTlsOptions = new MqttTlsOptions(connectionProperties);

            ConnectionProperties = connectionProperties;
        }

        internal SecureString UserName { get; set; }

        internal SecureString Password { get; set; }

        internal string AzureClientId { get; set; }

        internal bool CleanSession { get; set; }

        internal bool UseCredentials => (UserName != null) && (UserName.Length != 0);

        internal bool UseAzureClientId => !string.IsNullOrEmpty(AzureClientId);

        internal EnumMqttProtocolVersion ProtocolVersion { get; set; }

        internal MqttTlsOptions MqttTlsOptions { get; set; }

        /// <summary>
        /// The key value pairs representing the parameters of a MqttClientProtocolConfiguration
        /// </summary>
        public ArrayOf<KeyValuePair> ConnectionProperties { get; set; }
    }
}
