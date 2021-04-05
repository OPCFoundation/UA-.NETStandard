/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using MQTTnet;
using MQTTnet.Client.ExtendedAuthenticationExchange;
using MQTTnet.Client.Options;
using MQTTnet.Diagnostics.PacketInspection;
using MQTTnet.Formatter;
using MQTTnet.Packets;

namespace Opc.Ua.PubSub.Mqtt
{

    /// <summary>
    /// The identifiers of the MqttClientConfigurationParameters
    /// </summary>
    internal enum EnumMqttClientConfigurationParameters
    {
        UserName,
        Password,
        AzureClientId,
        CleanSession,
        ProtocolVersion,

        TlsCertificateCaCertificatePath,
        TlsCertificateClientCertificatePath,
        TlsCertificateClientCertificatePassword,
        TlsProtocolVersion,
        TlsAllowUntrustedCertificates,
        TlsIgnoreCertificateChainErrors,
        TlsIgnoreRevocationListErrors,

        TrustedIssuerCertificatesStoreType,
        TrustedIssuerCertificatesStorePath,
        TrustedPeerCertificatesStoreType,
        TrustedPeerCertificatesStorePath,
        RejectedCertificateStoreStoreType,
        RejectedCertificateStoreStorePath
    }

    /// <summary>
    /// Extension for providing string values corresponding to EnumMqttClientConfigurationParameters
    /// </summary>
    internal static class MqttClientConfigurationParametersExtensions
    {
        internal static QualifiedName GetQualifiedName(this EnumMqttClientConfigurationParameters mqttClientConfigurationParameters)
        {
            switch (mqttClientConfigurationParameters)
            {
                case EnumMqttClientConfigurationParameters.UserName:
                    return "UserName";
                case EnumMqttClientConfigurationParameters.Password:
                    return "Password";
                case EnumMqttClientConfigurationParameters.AzureClientId:
                    return "AzureClientId";
                case EnumMqttClientConfigurationParameters.CleanSession:
                    return "CleanSession";
                case EnumMqttClientConfigurationParameters.ProtocolVersion:
                    return "ProtocolVersion";
                case EnumMqttClientConfigurationParameters.TlsCertificateCaCertificatePath:
                    return "TlsCertificateCaCertificatePath";
                case EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePath:
                    return "TlsCertificateClientCertificatePath";
                case EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePassword:
                    return "TlsCertificateClientCertificatePassword";
                case EnumMqttClientConfigurationParameters.TlsProtocolVersion:
                    return "TlsProtocolVersion";
                case EnumMqttClientConfigurationParameters.TlsAllowUntrustedCertificates:
                    return "TlsAllowUntrustedCertificates";
                case EnumMqttClientConfigurationParameters.TlsIgnoreCertificateChainErrors:
                    return "TlsIgnoreCertificateChainErrors";
                case EnumMqttClientConfigurationParameters.TlsIgnoreRevocationListErrors:
                    return "TlsIgnoreRevocationListErrors";
                case EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStoreType:
                    return "TrustedIssuerCertificatesStoreType";
                case EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStorePath:
                    return "TrustedIssuerCertificatesStorePath";
                case EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStoreType:
                    return "TrustedPeerCertificatesStoreType";
                case EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStorePath:
                    return "TrustedPeerCertificatesStorePath";
                case EnumMqttClientConfigurationParameters.RejectedCertificateStoreStoreType:
                    return "RejectedCertificateStoreStoreType";
                case EnumMqttClientConfigurationParameters.RejectedCertificateStoreStorePath:
                    return "RejectedCertificateStoreStorePath";
                default:
                    return "";
            }
        }
    }

    /// <summary>
    /// The Mqtt Protocol Version
    /// </summary>
    public enum EnumMqttProtocolVersion
    {
        /// <summary>
        /// Unknown version
        /// </summary>
        Unknown = MqttProtocolVersion.Unknown,
        /// <summary>
        /// Mqtt V310
        /// </summary>
        V310 = MqttProtocolVersion.V310,
        /// <summary>
        /// Mqtt V311
        /// </summary>
        V311 = MqttProtocolVersion.V311,
        /// <summary>
        /// Mqtt V500
        /// </summary>
        V500 = MqttProtocolVersion.V500
    }

    /// <summary>
    /// The certificates used by the tls/ssl layer 
    /// </summary>
    public class MqttTlsCertificates
    {
        #region Private menbers

        private X509Certificate m_caCertificate;
        private X509Certificate m_clientCertificate;

        private string m_caCertificatePath;
        private string m_clientCertificatePath;
        private string m_clientCertificatePassword;

        KeyValuePairCollection m_keyValuePairs;

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
            m_caCertificatePath = caCertificatePath ?? "";
            m_clientCertificatePath = clientCertificatePath ?? "";
            m_clientCertificatePassword = clientCertificatePassword ?? "";

            if (!string.IsNullOrEmpty(m_caCertificatePath))
            {
                m_caCertificate = X509Certificate.CreateFromCertFile(m_caCertificatePath);
            }
            if (!string.IsNullOrEmpty(clientCertificatePath))
            {
                m_clientCertificate = new X509Certificate2(clientCertificatePath, m_clientCertificatePassword);
            }

            m_keyValuePairs = new KeyValuePairCollection();

            QualifiedName qCaCertificatePath = EnumMqttClientConfigurationParameters.TlsCertificateCaCertificatePath.GetQualifiedName();
            m_keyValuePairs.Add(new KeyValuePair { Key = qCaCertificatePath, Value = m_caCertificatePath });

            QualifiedName qClientCertificatePath = EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePath.GetQualifiedName();
            m_keyValuePairs.Add(new KeyValuePair { Key = qClientCertificatePath, Value = m_clientCertificatePath });

            QualifiedName qClientCertificatePassword = EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePassword.GetQualifiedName();
            m_keyValuePairs.Add(new KeyValuePair { Key = qClientCertificatePassword, Value = m_clientCertificatePassword });

        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="keyValuePairs"></param>
        public MqttTlsCertificates(KeyValuePairCollection keyValuePairs)
        {
            m_caCertificatePath = "";
            QualifiedName qCaCertificatePath = EnumMqttClientConfigurationParameters.TlsCertificateCaCertificatePath.GetQualifiedName();
            m_caCertificatePath = keyValuePairs.Find(kvp => kvp.Key.Name.Equals(qCaCertificatePath.Name))?.Value.Value as string;

            m_clientCertificatePath = "";
            QualifiedName qClientCertificatePath = EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePath.GetQualifiedName();
            m_clientCertificatePath = keyValuePairs.Find(kvp => kvp.Key.Name.Equals(qClientCertificatePath.Name))?.Value.Value as string;

            m_clientCertificatePassword = "";
            QualifiedName qClientCertificatePassword = EnumMqttClientConfigurationParameters.TlsCertificateClientCertificatePassword.GetQualifiedName();
            m_clientCertificatePassword = keyValuePairs.Find(kvp => kvp.Key.Name.Equals(qClientCertificatePassword.Name))?.Value.Value as string;

            m_keyValuePairs = keyValuePairs;

            if (!string.IsNullOrEmpty(m_caCertificatePath))
            {
                m_caCertificate = X509Certificate.CreateFromCertFile(m_caCertificatePath);
            }
            if (!string.IsNullOrEmpty(clientCertificatePath))
            {
                m_clientCertificate = new X509Certificate2(clientCertificatePath, m_clientCertificatePassword);
            }

        }
        #endregion Constructor

        #region Internal Properties
        internal string caCertificatePath { get { return m_caCertificatePath; } set { m_caCertificatePath = value; } }
        internal string clientCertificatePath { get { return m_clientCertificatePath; } set { m_clientCertificatePath = value; } }
        internal string clientCertificatePassword { get { return m_clientCertificatePassword; } set { m_clientCertificatePassword = value; } }

        internal KeyValuePairCollection KeyValuePairs { get { return m_keyValuePairs; } set { m_keyValuePairs = value; } }

        internal List<X509Certificate> X509Certificates
        {
            get
            {

                List<X509Certificate> values = new List<X509Certificate>();
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
        #region Private
        MqttTlsCertificates m_certificates;
        SslProtocols m_SslProtocolVersion;
        bool m_allowUntrustedCertificates;
        bool m_ignoreCertificateChainErrors;
        bool m_ignoreRevocationListErrors;

        CertificateStoreIdentifier m_trustedIssuerCertificates;
        CertificateStoreIdentifier m_trustedPeerCertificates;
        CertificateStoreIdentifier m_rejectedCertificateStore;

        KeyValuePairCollection m_keyValuePairs;
        #endregion

        #region Constructor
        /// <summary>
        /// Default constructor
        /// </summary>
        public MqttTlsOptions()
        {
            m_certificates = null;
            m_SslProtocolVersion = SslProtocols.None;
            m_allowUntrustedCertificates = false;
            m_ignoreCertificateChainErrors = false;
            m_ignoreRevocationListErrors = false;

            m_trustedIssuerCertificates = null;
            m_trustedPeerCertificates = null;
            m_rejectedCertificateStore = null;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="kvpMqttOptions">The key value pairs representing the values from which to construct MqttTlsOptions</param>
        public MqttTlsOptions(KeyValuePairCollection kvpMqttOptions)
        {
            m_certificates = new MqttTlsCertificates(kvpMqttOptions);

            QualifiedName qSslProtocolVersion = EnumMqttClientConfigurationParameters.TlsProtocolVersion.GetQualifiedName();
            m_SslProtocolVersion = (SslProtocols)Convert.ToInt32(kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qSslProtocolVersion.Name))?.Value.Value);

            QualifiedName qAllowUntrustedCertificates = EnumMqttClientConfigurationParameters.TlsAllowUntrustedCertificates.GetQualifiedName();
            m_allowUntrustedCertificates = Convert.ToBoolean(kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qAllowUntrustedCertificates.Name))?.Value.Value);

            QualifiedName qIgnoreCertificateChainErrors = EnumMqttClientConfigurationParameters.TlsIgnoreCertificateChainErrors.GetQualifiedName();
            m_ignoreCertificateChainErrors = Convert.ToBoolean(kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qIgnoreCertificateChainErrors.Name))?.Value.Value);

            QualifiedName qIgnoreRevocationListErrors = EnumMqttClientConfigurationParameters.TlsIgnoreRevocationListErrors.GetQualifiedName();
            m_ignoreRevocationListErrors = Convert.ToBoolean(kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qIgnoreRevocationListErrors.Name))?.Value.Value);

            QualifiedName qTrustedIssuerCertificatesStoreType = EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStoreType.GetQualifiedName();
            string issuerCertificatesStoreType = kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qTrustedIssuerCertificatesStoreType.Name))?.Value.Value as string;
            QualifiedName qTrustedIssuerCertificatesStorePath = EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStorePath.GetQualifiedName();
            string issuerCertificatesStorePath = kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qTrustedIssuerCertificatesStorePath.Name))?.Value.Value as string;

            m_trustedIssuerCertificates = new CertificateTrustList {
                StoreType = issuerCertificatesStoreType,
                StorePath = issuerCertificatesStorePath
            };

            QualifiedName qTrustedPeerCertificatesStoreType = EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStoreType.GetQualifiedName();
            string peerCertificatesStoreType = kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qTrustedPeerCertificatesStoreType.Name))?.Value.Value as string;
            QualifiedName qTrustedPeerCertificatesStorePath = EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStorePath.GetQualifiedName();
            string peerCertificatesStorePath = kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qTrustedPeerCertificatesStorePath.Name))?.Value.Value as string;

            m_trustedPeerCertificates = new CertificateTrustList {
                StoreType = peerCertificatesStoreType,
                StorePath = peerCertificatesStorePath
            };

            QualifiedName qRejectedCertificateStoreStoreType = EnumMqttClientConfigurationParameters.RejectedCertificateStoreStoreType.GetQualifiedName();
            string rejectedCertificateStoreStoreType = kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qRejectedCertificateStoreStoreType.Name))?.Value.Value as string;
            QualifiedName qRejectedCertificateStoreStorePath = EnumMqttClientConfigurationParameters.RejectedCertificateStoreStorePath.GetQualifiedName();
            string rejectedCertificateStoreStorePath = kvpMqttOptions.Find(kvp => kvp.Key.Name.Equals(qRejectedCertificateStoreStorePath.Name))?.Value.Value as string;

            m_rejectedCertificateStore = new CertificateTrustList {
                StoreType = rejectedCertificateStoreStoreType,
                StorePath = rejectedCertificateStoreStorePath
            };

            m_keyValuePairs = kvpMqttOptions;
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
            m_certificates = certificates;
            m_SslProtocolVersion = sslProtocolVersion;
            m_allowUntrustedCertificates = allowUntrustedCertificates;
            m_ignoreCertificateChainErrors = ignoreCertificateChainErrors;
            m_ignoreRevocationListErrors = ignoreRevocationListErrors;

            m_trustedIssuerCertificates = trustedIssuerCertificates;
            m_trustedPeerCertificates = trustedPeerCertificates;
            m_rejectedCertificateStore = rejectedCertificateStore;

            m_keyValuePairs = new KeyValuePairCollection();

            if (m_certificates != null)
            {
                m_keyValuePairs.AddRange(m_certificates.KeyValuePairs);
            }

            KeyValuePair kvpTlsProtocolVersion = new KeyValuePair();
            kvpTlsProtocolVersion.Key = EnumMqttClientConfigurationParameters.TlsProtocolVersion.GetQualifiedName();
            kvpTlsProtocolVersion.Value = (int)m_SslProtocolVersion;
            m_keyValuePairs.Add(kvpTlsProtocolVersion);
            KeyValuePair kvpAllowUntrustedCertificates = new KeyValuePair();
            kvpAllowUntrustedCertificates.Key = EnumMqttClientConfigurationParameters.TlsAllowUntrustedCertificates.GetQualifiedName();
            kvpAllowUntrustedCertificates.Value = m_allowUntrustedCertificates;
            m_keyValuePairs.Add(kvpAllowUntrustedCertificates);
            KeyValuePair kvpIgnoreCertificateChainErrors = new KeyValuePair();
            kvpIgnoreCertificateChainErrors.Key = EnumMqttClientConfigurationParameters.TlsIgnoreCertificateChainErrors.GetQualifiedName();
            kvpIgnoreCertificateChainErrors.Value = m_ignoreCertificateChainErrors;
            m_keyValuePairs.Add(kvpIgnoreCertificateChainErrors);
            KeyValuePair kvpIgnoreRevocationListErrors = new KeyValuePair();
            kvpIgnoreRevocationListErrors.Key = EnumMqttClientConfigurationParameters.TlsIgnoreRevocationListErrors.GetQualifiedName();
            kvpIgnoreRevocationListErrors.Value = m_ignoreRevocationListErrors;
            m_keyValuePairs.Add(kvpIgnoreRevocationListErrors);

            KeyValuePair kvpTrustedIssuerCertificatesStoreType = new KeyValuePair();
            kvpTrustedIssuerCertificatesStoreType.Key = EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStoreType.GetQualifiedName();
            kvpTrustedIssuerCertificatesStoreType.Value = m_trustedIssuerCertificates?.StoreType;
            m_keyValuePairs.Add(kvpTrustedIssuerCertificatesStoreType);
            KeyValuePair kvpTrustedIssuerCertificatesStorePath = new KeyValuePair();
            kvpTrustedIssuerCertificatesStorePath.Key = EnumMqttClientConfigurationParameters.TrustedIssuerCertificatesStorePath.GetQualifiedName();
            kvpTrustedIssuerCertificatesStorePath.Value = m_trustedIssuerCertificates?.StorePath;
            m_keyValuePairs.Add(kvpTrustedIssuerCertificatesStorePath);

            KeyValuePair kvpTrustedPeerCertificatesStoreType = new KeyValuePair();
            kvpTrustedPeerCertificatesStoreType.Key = EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStoreType.GetQualifiedName();
            kvpTrustedPeerCertificatesStoreType.Value = m_trustedPeerCertificates?.StoreType;
            m_keyValuePairs.Add(kvpTrustedPeerCertificatesStoreType);
            KeyValuePair kvpTrustedPeerCertificatesStorePath = new KeyValuePair();
            kvpTrustedPeerCertificatesStorePath.Key = EnumMqttClientConfigurationParameters.TrustedPeerCertificatesStorePath.GetQualifiedName();
            kvpTrustedPeerCertificatesStorePath.Value = m_trustedPeerCertificates?.StorePath;
            m_keyValuePairs.Add(kvpTrustedPeerCertificatesStorePath);

            KeyValuePair kvpRejectedCertificateStoreStoreType = new KeyValuePair();
            kvpRejectedCertificateStoreStoreType.Key = EnumMqttClientConfigurationParameters.RejectedCertificateStoreStoreType.GetQualifiedName();
            kvpRejectedCertificateStoreStoreType.Value = m_rejectedCertificateStore?.StoreType;
            m_keyValuePairs.Add(kvpRejectedCertificateStoreStoreType);
            KeyValuePair kvpRejectedCertificateStoreStorePath = new KeyValuePair();
            kvpRejectedCertificateStoreStorePath.Key = EnumMqttClientConfigurationParameters.RejectedCertificateStoreStorePath.GetQualifiedName();
            kvpRejectedCertificateStoreStorePath.Value = m_rejectedCertificateStore?.StorePath;
            m_keyValuePairs.Add(kvpRejectedCertificateStoreStorePath);

        }
        #endregion

        #region Internal Properties
        internal MqttTlsCertificates Certificates { get => m_certificates; set => m_certificates = value; }
        internal SslProtocols SslProtocolVersion { get => m_SslProtocolVersion; set => m_SslProtocolVersion = value; }
        internal bool AllowUntrustedCertificates { get => m_allowUntrustedCertificates; set => m_allowUntrustedCertificates = value; }
        internal bool IgnoreCertificateChainErrors { get => m_ignoreCertificateChainErrors; set => m_ignoreCertificateChainErrors = value; }
        internal bool IgnoreRevocationListErrors { get => m_ignoreRevocationListErrors; set => m_ignoreRevocationListErrors = value; }
        internal CertificateStoreIdentifier TrustedIssuerCertificates { get => m_trustedIssuerCertificates; set => m_trustedIssuerCertificates = value; }
        internal CertificateStoreIdentifier TrustedPeerCertificates { get => m_trustedPeerCertificates; set => m_trustedPeerCertificates = value; }
        internal CertificateStoreIdentifier RejectedCertificateStore { get => m_rejectedCertificateStore; set => m_rejectedCertificateStore = value; }
        internal KeyValuePairCollection KeyValuePairs { get => m_keyValuePairs; set => m_keyValuePairs = value; }
        #endregion
    }

    /// <summary>
    /// The implementation of the Mqtt specific client configuration
    /// </summary>
    public class MqttClientProtocolConfiguration : ITransportProtocolConfiguration
    {
        #region Private
        SecureString m_userName;
        SecureString m_password;
        string m_azureClientId;
        bool m_cleanSession;
        EnumMqttProtocolVersion m_protocolVersion;
        MqttTlsOptions m_mqttTlsOptions;
        KeyValuePairCollection m_keyValuePairs;
        #endregion

        #region Constructor
        /// <summary>
        /// Constructor
        /// </summary>
        public MqttClientProtocolConfiguration()
        {
            m_userName = null;
            m_password = null;
            m_azureClientId = null;
            m_cleanSession = true;
            m_protocolVersion = EnumMqttProtocolVersion.V310;
            m_mqttTlsOptions = null;
            m_keyValuePairs = null;
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
            m_userName = userName;
            m_password = password;
            m_azureClientId = azureClientId;
            m_cleanSession = cleanSession;
            m_protocolVersion = version;
            m_mqttTlsOptions = mqttTlsOptions;

            m_keyValuePairs = new KeyValuePairCollection();

            KeyValuePair kvpUserName = new KeyValuePair();
            kvpUserName.Key = EnumMqttClientConfigurationParameters.UserName.GetQualifiedName();
            kvpUserName.Value = new System.Net.NetworkCredential(string.Empty, m_userName).Password;
            m_keyValuePairs.Add(kvpUserName);
            KeyValuePair kvpPassword = new KeyValuePair();
            kvpPassword.Key = EnumMqttClientConfigurationParameters.Password.GetQualifiedName();
            kvpPassword.Value = new System.Net.NetworkCredential(string.Empty, m_password).Password;
            m_keyValuePairs.Add(kvpPassword);
            KeyValuePair kvpAzureClientId = new KeyValuePair();
            kvpAzureClientId.Key = EnumMqttClientConfigurationParameters.AzureClientId.GetQualifiedName();
            kvpAzureClientId.Value = m_azureClientId;
            m_keyValuePairs.Add(kvpAzureClientId);
            KeyValuePair kvpCleanSession = new KeyValuePair();
            kvpCleanSession.Key = EnumMqttClientConfigurationParameters.CleanSession.GetQualifiedName();
            kvpCleanSession.Value = m_cleanSession;
            m_keyValuePairs.Add(kvpCleanSession);
            KeyValuePair kvpProtocolVersion = new KeyValuePair();
            kvpProtocolVersion.Key = EnumMqttClientConfigurationParameters.ProtocolVersion.GetQualifiedName();
            kvpProtocolVersion.Value = (int)m_protocolVersion;
            m_keyValuePairs.Add(kvpProtocolVersion);

            if (m_mqttTlsOptions != null)
            {
                m_keyValuePairs.AddRange(m_mqttTlsOptions.KeyValuePairs);
            }

        }

        /// <summary>
        /// Constructs a MqttClientProtocolConfiguration from given keyValuePairs
        /// </summary>
        /// <param name="keyValuePairs"></param>
        public MqttClientProtocolConfiguration(KeyValuePairCollection keyValuePairs)
        {
            m_userName = new SecureString();
            QualifiedName qUserName = EnumMqttClientConfigurationParameters.UserName.GetQualifiedName();
            string sUserName = keyValuePairs.Find(kvp => kvp.Key.Name.Equals(qUserName.Name))?.Value.Value as string;
            if (sUserName != null)
            {
                foreach (char c in sUserName?.ToCharArray())
                {
                    m_userName.AppendChar(c);
                }
            }

            m_password = new SecureString();
            QualifiedName qPassword = EnumMqttClientConfigurationParameters.Password.GetQualifiedName();
            string sPassword = keyValuePairs.Find(kvp => kvp.Key.Name.Equals(qPassword.Name))?.Value.Value as string;
            if (sPassword != null)
            {
                foreach (char c in sPassword?.ToCharArray())
                {
                    m_password.AppendChar(c);
                }
            }

            QualifiedName qAzureClientId = EnumMqttClientConfigurationParameters.AzureClientId.GetQualifiedName();
            m_azureClientId = Convert.ToString(keyValuePairs.Find(kvp => kvp.Key.Name.Equals(qAzureClientId.Name))?.Value.Value);

            QualifiedName qCleanSession = EnumMqttClientConfigurationParameters.CleanSession.GetQualifiedName();
            m_cleanSession = Convert.ToBoolean(keyValuePairs.Find(kvp => kvp.Key.Name.Equals(qCleanSession.Name))?.Value.Value);

            QualifiedName qProtocolVersion = EnumMqttClientConfigurationParameters.ProtocolVersion.GetQualifiedName();
            m_protocolVersion = (EnumMqttProtocolVersion)Convert.ToInt32(keyValuePairs.Find(kvp => kvp.Key.Name.Equals(qProtocolVersion.Name))?.Value.Value);

            m_mqttTlsOptions = new MqttTlsOptions(keyValuePairs);

            m_keyValuePairs = keyValuePairs;

        }
        #endregion

        #region Internal Properties
        internal SecureString UserName { get => m_userName; set => m_userName = value; }

        internal SecureString Password { get => m_password; set => m_password = value; }

        internal string AzureClientId { get => m_azureClientId; set => m_azureClientId = value; }

        internal bool CleanSession { get => m_cleanSession; set => m_cleanSession = value; }

        internal bool UseCredentials { get => (m_userName != null) && (m_userName.Length != 0); }

        internal bool UseAzureClientId { get => (m_azureClientId != null) && (m_azureClientId.Length != 0); }

        internal EnumMqttProtocolVersion ProtocolVersion { get => m_protocolVersion; set => m_protocolVersion = value; }

        internal MqttTlsOptions MqttTlsOptions { get => m_mqttTlsOptions; set => m_mqttTlsOptions = value; }


        #endregion Internal Properties

        #region Public Properties
        /// <summary>
        /// The key value pairs representing the values of a MqttClientProtocolConfiguration
        /// </summary>
        public KeyValuePairCollection KeyValuePairs { get => m_keyValuePairs; set => m_keyValuePairs = value; }
        #endregion Public Propertis

    }
}
