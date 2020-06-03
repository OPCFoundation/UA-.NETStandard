/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System.IdentityModel.Selectors;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the configuration settings for a channel.
    /// </summary>
    public class TransportListenerSettings
    {
        #region Public Properties
        /// <summary>
        /// Gets or sets the descriptions for the endpoints supported by the listener.
        /// </summary>
        public EndpointDescriptionCollection Descriptions
        {
            get { return m_descriptions; }
            set { m_descriptions = value; }
        }

        /// <summary>
        /// Gets or sets the configuration for the endpoints.
        /// </summary>
        public EndpointConfiguration Configuration
        {
            get { return m_configuration; }
            set { m_configuration = value; }
        }

        /// <summary>
        /// Gets or sets the server certificate.
        /// </summary>
        public X509Certificate2 ServerCertificate
        {
            get { return m_serverCertificate; }
            set { m_serverCertificate = value; }
        }

        /// <summary>
        /// Gets or sets the server certificate chain.
        /// </summary>
        /// <value>
        /// The server certificate chain.
        /// </value>
        public X509Certificate2Collection ServerCertificateChain
        {
            get { return m_serverCertificateChain; }
            set { m_serverCertificateChain = value; }
        }

        /// <summary>
        /// Gets or Sets the certificate validator.
        /// </summary>
        /// <remarks>
        /// This is the object used by the channel to validate received certificates.
        /// Validatation errors are reported to the application via this object.
        /// </remarks>
        public ICertificateValidator CertificateValidator
        {
            get { return m_certificateValidator; }
            set { m_certificateValidator = value; }
        }

        /// <summary>
        /// Gets or sets a reference to the table of namespaces for the server.
        /// </summary>
        /// <remarks>
        /// This is a thread safe object that may be updated by the application at any time.
        /// This table is used to lookup the NamespaceURI for the DataTypeEncodingId when decoding ExtensionObjects.
        /// If the NamespaceURI can be found the decoder will use the Factory to create an instance of a .NET object.
        /// The raw data is passed to application if the NamespaceURI cannot be found or there is no .NET class 
        /// associated with the DataTypeEncodingId then.
        /// </remarks>
        /// <seealso cref="Factory" />
        public NamespaceTable NamespaceUris
        {
            get { return m_namespaceUris; }
            set { m_namespaceUris = value; }
        }

        /// <summary>
        /// Gets or sets the table of known encodeable objects.
        /// </summary>
        /// <remarks>
        /// This is a thread safe object that may be updated by the application at any time.
        /// This is a table of .NET types indexed by their DataTypeEncodingId.
        /// The decoder uses this table to automatically create the appropriate .NET objects when it
        /// encounters an ExtensionObject in the message being decoded.
        /// The table uses DataTypeEncodingIds with the URI explicitly specified so multiple channels
        /// with different servers can share the same table.
        /// The NamespaceUris table is used to lookup the NamespaceURI from the NamespaceIndex provide
        /// in the encoded message.
        /// </remarks>
        /// <seealso cref="NamespaceUris" />
        public EncodeableFactory Factory
        {
            get { return m_channelFactory; }
            set { m_channelFactory = value; }
        }

        /// <summary>
        /// Indicates if the transport listener is used as an endpoint for a reverse connection.
        /// </summary>
        public bool ReverseConnectListener
        {
            get { return m_reverseConnectListener; }
            set { m_reverseConnectListener = value; }
        }
        #endregion

        #region Private Fields
        private EndpointDescriptionCollection m_descriptions;
        private EndpointConfiguration m_configuration;
        private X509Certificate2 m_serverCertificate;
        private X509Certificate2Collection m_serverCertificateChain;
        private ICertificateValidator m_certificateValidator;
        private NamespaceTable m_namespaceUris;
        private EncodeableFactory m_channelFactory;
        private bool m_reverseConnectListener;
        #endregion
    }
}
