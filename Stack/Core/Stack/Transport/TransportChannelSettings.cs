/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the configuration settings for a channel.
    /// </summary>
    public class TransportChannelSettings
    {
        #region Public Properties
        /// <summary>
        /// Gets or sets the description for the endpoint.
        /// </summary>
        /// <remarks>May be null if no security is used.</remarks>
        public EndpointDescription Description
        {
            get { return m_description; }
            set { m_description = value; }
        }

        /// <summary>
        /// Gets or sets the configuration for the endpoint.
        /// </summary>
        public EndpointConfiguration Configuration
        {
            get { return m_configuration; }
            set { m_configuration = value; }
        }

        /// <summary>
        /// Gets or sets the client certificate.
        /// </summary>
        /// <remarks>May be null if no security is used.</remarks>
        public X509Certificate2 ClientCertificate
        {
            get { return m_clientCertificate; }
            set { m_clientCertificate = value; }
        }

        /// <summary>
        /// Gets or sets the client certificate chain.
        /// </summary>
        /// <value>
        /// The client certificate chain.
        /// </value>
        //public X509Certificate2Collection ClientCertificateChain
        //{
        //    get { return m_clientCertificateChain; }
        //    set { m_clientCertificateChain = value; }
        //}

        /// <summary>
        /// Gets or Sets the server certificate.
        /// </summary>
        /// <remarks>May be null if no security is used.</remarks>
        public X509Certificate2 ServerCertificate
        {
            get { return m_serverCertificate; }
            set { m_serverCertificate = value; }
        }

        /// <summary>
        /// Gets or sets the certificate validator.
        /// </summary>
        /// <remarks>
        /// May be null if no security is used.
        /// This is the object used by the channel to validate received certificates.
        /// Validatation errors are reported to the application via this object.
        /// </remarks>
        public X509CertificateValidator CertificateValidator
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
        #endregion

        #region Private Fields
        private EndpointDescription m_description;
        private EndpointConfiguration m_configuration;
        private X509Certificate2 m_clientCertificate;
        //private X509Certificate2Collection m_clientCertificateChain;
        private X509Certificate2 m_serverCertificate;
        private X509CertificateValidator m_certificateValidator;
        private NamespaceTable m_namespaceUris;
        private EncodeableFactory m_channelFactory;
        #endregion
    }
}
