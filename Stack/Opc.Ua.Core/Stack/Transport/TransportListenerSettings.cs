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

using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the configuration settings for a channel.
    /// </summary>
    public class TransportListenerSettings
    {
        /// <summary>
        /// Gets or sets the descriptions for the endpoints supported by the listener.
        /// </summary>
        public EndpointDescriptionCollection Descriptions { get; set; }

        /// <summary>
        /// Gets or sets the configuration for the endpoints.
        /// </summary>
        public EndpointConfiguration Configuration { get; set; }

        /// <summary>
        /// Gets or sets the server certificate type provider.
        /// </summary>
        public CertificateTypesProvider ServerCertificateTypesProvider { get; set; }

        /// <summary>
        /// Gets or Sets the certificate validator.
        /// </summary>
        /// <remarks>
        /// This is the object used by the channel to validate received certificates.
        /// Validatation errors are reported to the application via this object.
        /// </remarks>
        public ICertificateValidator CertificateValidator { get; set; }

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
        public NamespaceTable NamespaceUris { get; set; }

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
        public IEncodeableFactory Factory { get; set; }

        /// <summary>
        /// Indicates if the transport listener is used as an endpoint for a reverse connection.
        /// </summary>
        public bool ReverseConnectListener { get; set; }

        /// <summary>
        /// Indicates the max number of channels that can be created by the listener.
        /// 0 indicates no limit.
        /// </summary>
        public int MaxChannelCount { get; set; }

        /// <summary>
        /// Indicates if Http listener requires mutual TLS
        /// Handled only by HttpsTransportListener
        /// In case true, the client should provide it's own valid TLS certificate to the TLS layer for the connection to succeed.
        /// </summary>
        public bool HttpsMutualTls { get; set; }
    }
}
