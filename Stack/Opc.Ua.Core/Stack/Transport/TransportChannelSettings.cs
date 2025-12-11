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

using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the configuration settings for a channel.
    /// </summary>
    public class TransportChannelSettings
    {
        /// <summary>
        /// Gets or sets the description for the endpoint.
        /// </summary>
        /// <remarks>May be null if no security is used.</remarks>
        public EndpointDescription Description { get; set; }

        /// <summary>
        /// Gets or sets the configuration for the endpoint.
        /// </summary>
        public EndpointConfiguration Configuration { get; set; }

        /// <summary>
        /// Gets or sets the client certificate.
        /// </summary>
        /// <remarks>May be null if no security is used.</remarks>
        public X509Certificate2 ClientCertificate { get; set; }

        /// <summary>
        /// Gets or sets the client certificate chain.
        /// </summary>
        /// <value>
        /// The client certificate chain.
        /// </value>
        public X509Certificate2Collection ClientCertificateChain { get; set; }

        /// <summary>
        /// Gets or Sets the server certificate.
        /// </summary>
        /// <remarks>May be null if no security is used.</remarks>
        public X509Certificate2 ServerCertificate { get; set; }

        /// <summary>
        /// Gets or sets the certificate validator for the application.
        /// </summary>
        /// <remarks>
        /// May be null if no security is used.
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
    }
}
