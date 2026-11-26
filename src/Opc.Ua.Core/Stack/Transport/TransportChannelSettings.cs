// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using Opc.Ua.Security.Certificates;

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
        public EndpointDescription? Description { get; set; }

        /// <summary>
        /// Gets or sets the configuration for the endpoint.
        /// </summary>
        public EndpointConfiguration? Configuration { get; set; }

        /// <summary>
        /// Gets or sets the client certificate.
        /// </summary>
        /// <remarks>May be null if no security is used.</remarks>
        public Certificate? ClientCertificate { get; set; }

        /// <summary>
        /// Gets or sets the client certificate chain.
        /// </summary>
        /// <value>
        /// The client certificate chain.
        /// </value>
        public CertificateCollection? ClientCertificateChain { get; set; }

        /// <summary>
        /// Gets or Sets the server certificate.
        /// </summary>
        /// <remarks>May be null if no security is used.</remarks>
        public Certificate? ServerCertificate { get; set; }

        /// <summary>
        /// Gets or sets the certificate validator for the application.
        /// </summary>
        /// <remarks>
        /// May be null if no security is used.
        /// This is the object used by the channel to validate received certificates.
        /// Validatation errors are reported to the application via this object.
        /// </remarks>
        public ICertificateValidatorEx? CertificateValidator { get; set; }

        /// <summary>
        /// The security policies the channel negotiates against, or <c>null</c>
        /// to use <see cref="SecurityPolicies.Default"/>.
        /// </summary>
        /// <remarks>
        /// Set this from the registry the application configured so a policy it
        /// contributed through <c>AddSecurityPolicy</c> is reachable by the
        /// server the channel connects to.
        /// </remarks>
        public ISecurityPolicyRegistry? SecurityPolicyRegistry { get; set; }

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
        public NamespaceTable? NamespaceUris { get; set; }

        /// <summary>
        /// Gets or sets the table of server uris used to map the server indexes
        /// of expanded node ids.
        /// </summary>
        /// <remarks>
        /// When set, the channel shares this table with its owner (for example the
        /// session), so updates the owner makes after the channel opened, and
        /// replacement channels opened with the same table, keep one mapping.
        /// When null the channel creates an empty table of its own.
        /// </remarks>
        /// <seealso cref="NamespaceUris" />
        public StringTable? ServerUris { get; set; }

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
        public IEncodeableFactory? Factory { get; set; }
    }
}
