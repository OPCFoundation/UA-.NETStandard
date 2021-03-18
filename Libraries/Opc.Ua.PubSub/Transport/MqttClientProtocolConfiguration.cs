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
    public enum EnumMqttProtocolVersion
    {
        Unknown = MqttProtocolVersion.Unknown,
        V310 = MqttProtocolVersion.V310,
        V311 = MqttProtocolVersion.V311,
        V500 = MqttProtocolVersion.V500
    }

    /// <summary>
    /// The implementation of the Tls client options
    /// </summary>
    public class MqttTlsOptions
    {
        #region Private
        List<X509Certificate> m_certificates;
        SslProtocols m_SslProtocolVersion;
        bool m_allowUntrustedCertificates;
        bool m_ignoreCertificateChainErrors;
        bool m_ignoreRevocationListErrors;

        CertificateStoreIdentifier m_trustedIssuerCertificates;
        CertificateStoreIdentifier m_trustedPeerCertificates;
        CertificateStoreIdentifier m_rejectedCertificateStore;
        #endregion

        #region Constructor
        /// <summary>
        /// Default constructor
        /// </summary>
        public MqttTlsOptions()
        {
            m_certificates = null;
            m_SslProtocolVersion = SslProtocols.Default;
            m_allowUntrustedCertificates   = false;
            m_ignoreCertificateChainErrors = false;
            m_ignoreRevocationListErrors   = false;

            m_trustedIssuerCertificates = null;
            m_trustedPeerCertificates   = null;
            m_rejectedCertificateStore = null;
        }
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="certificates">The certificates used for encrypted communication including the CA certificate</param>
        /// <param name="sslProtocolVersion">The preferred version of SSL protocol</param>
        /// <param name="allowUntrustedCertificates">Specifies if untrusted certificates should be accepted in the process of certificate validation</param>
        /// <param name="ignoreCertificateChainErrors">Specifies if Certificate Chain errors should be validated in the process of certificate validation</param>
        /// <param name="ignoreRevocationListErrors">Specifies if Certificate Revocation List errors should be validated in the process of certificate validation</param>
        /// <param name="trustedIssuerCertificates">The trusted </param>
        /// <param name="trustedPeerCertificates"></param>
        /// <param name="rejectedCertificateStore"></param>
        /// <param name=""></param>
        public MqttTlsOptions(List<X509Certificate> certificates = null,
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
        }
        #endregion

        #region Public Properties
        public List<X509Certificate> X509Certificates { get => m_certificates; set => m_certificates = value; }
        public SslProtocols SslProtocolVersion { get => m_SslProtocolVersion; set => m_SslProtocolVersion = value; }
        public bool AllowUntrustedCertificates { get => m_allowUntrustedCertificates; set => m_allowUntrustedCertificates = value; }
        public bool IgnoreCertificateChainErrors { get => m_ignoreCertificateChainErrors; set => m_ignoreCertificateChainErrors = value; }
        public bool IgnoreRevocationListErrors { get => m_ignoreRevocationListErrors; set => m_ignoreRevocationListErrors = value; }
        public CertificateStoreIdentifier TrustedIssuerCertificates { get => m_trustedIssuerCertificates; set => m_trustedIssuerCertificates = value; }
        public CertificateStoreIdentifier TrustedPeerCertificates { get => m_trustedPeerCertificates; set => m_trustedPeerCertificates = value; }
        public CertificateStoreIdentifier RejectedCertificateStore { get => m_rejectedCertificateStore; set => m_rejectedCertificateStore = value; }
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
        bool m_cleanSession;
        EnumMqttProtocolVersion m_protocolVersion;
        MqttTlsOptions m_mqttTlsOptions;
        #endregion

        #region Constructor
        /// <summary>
        /// Constructor
        /// </summary>
        public MqttClientProtocolConfiguration()
        {
            m_userName = null;
            m_password = null;
            m_cleanSession = true;
            m_protocolVersion = EnumMqttProtocolVersion.V310;
            m_mqttTlsOptions = null;
        }

        /// <summary>
        /// Constructor 
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="password"></param>
        /// <param name="cleanSession"></param>
        /// <param name="version"></param>
        /// <param name="mqttTlsOptions"></param>
        public MqttClientProtocolConfiguration(SecureString userName = null,
                                               SecureString password = null,
                                               bool cleanSession = true,
                                               EnumMqttProtocolVersion version = EnumMqttProtocolVersion.V310,
                                               MqttTlsOptions  mqttTlsOptions = null)
        {
            m_userName = userName;
            m_password = password;
            m_cleanSession = cleanSession;
            m_protocolVersion = version;
            m_mqttTlsOptions = mqttTlsOptions;
        }
        #endregion

        #region Public Properties
        public SecureString UserName { get => m_userName; set => m_userName = value; }

        public SecureString Password { get => m_password; set => m_password = value; }

        public bool CleanSession { get => m_cleanSession; set => m_cleanSession = value; }

        public bool UseCredentials { get => m_userName != null; }

        public EnumMqttProtocolVersion ProtocolVersion { get => m_protocolVersion; set => m_protocolVersion = value; }

        public MqttTlsOptions MqttTlsOptions { get => m_mqttTlsOptions; set => m_mqttTlsOptions = value; }

        #region Implement IEncodeable interface

        public ExpandedNodeId TypeId => throw new NotImplementedException();

        public ExpandedNodeId BinaryEncodingId => throw new NotImplementedException();

        public ExpandedNodeId XmlEncodingId => throw new NotImplementedException();

        public void Decode(IDecoder decoder)
        {
            throw new NotImplementedException();
        }

        public void Encode(IEncoder encoder)
        {
            throw new NotImplementedException();
        }

        public bool IsEqual(IEncodeable encodeable)
        {
            throw new NotImplementedException();
        }

        #endregion IEncodeable

        #endregion Public Properties
    }
}
