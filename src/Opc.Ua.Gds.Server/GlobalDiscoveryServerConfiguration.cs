// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.IO;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Stores the configuration for the Global Discovery Server.
    /// Serialized via source-generated IEncodeable implementation.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaGds + "Configuration.xsd")]
    public partial class GlobalDiscoveryServerConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public GlobalDiscoveryServerConfiguration()
        {
        }

        [DataTypeField(Order = 0)]
        public string? AuthoritiesStorePath { get; set; }

        [DataTypeField(Order = 1)]
        public string? ApplicationCertificatesStorePath { get; set; }

        [DataTypeField(Order = 2)]
        public string? BaseCertificateGroupStorePath { get; set; }

        [DataTypeField(Order = 3)]
        public string? DefaultSubjectNameContext { get; set; }

        [DataTypeField(Order = 4)]
        public ArrayOf<CertificateGroupConfiguration> CertificateGroups { get; set; }

        [DataTypeField(Order = 5)]
        public ArrayOf<string> KnownHostNames { get; set; }

        [DataTypeField(Order = 6)]
        public string? DatabaseStorePath { get; set; }

        [DataTypeField(Order = 7)]
        public string? UsersDatabaseStorePath { get; set; }

        /// <summary>
        /// Enables the GDS AliasName Server facet (OPC 10000-17 Annex C):
        /// the GDS reads the AliasNames of every registered Server whose
        /// ServerCapabilities contain <c>ALIAS</c>, merges them into its own
        /// <c>Aliases</c> hierarchy and adds the Server to its
        /// <c>ServerArray</c>.
        /// </summary>
        /// <remarks>
        /// Off by default: the GDS then opens client connections to the
        /// DiscoveryUrls of registered applications.
        /// </remarks>
        [DataTypeField(Order = 8)]
        public bool EnableAliasNameAggregation { get; set; }

        /// <summary>
        /// Interval, in seconds, at which the GDS reads the AliasNames of
        /// the registered AliasName Servers again, which also retries a
        /// Server that could not be read when it registered. 0 reads them
        /// only on RegisterApplication and UpdateApplication.
        /// </summary>
        [DataTypeField(Order = 9)]
        public int AliasNameRefreshInterval { get; set; }
    }

    /// <summary>
    /// Stores the configuration for a certificate group.
    /// Serialized via source-generated IEncodeable implementation.
    /// </summary>
    [DataType(Namespace = Namespaces.OpcUaGds + "Configuration.xsd")]
    public sealed partial class CertificateGroupConfiguration
    {
        /// <summary>
        /// The default constructor.
        /// </summary>
        public CertificateGroupConfiguration()
        {
            DefaultCertificateLifetime = CertificateFactory.DefaultLifeTime;
            DefaultCertificateKeySize = CertificateFactory.DefaultKeySize;
            DefaultCertificateHashSize = CertificateFactory.DefaultHashSize;
            CACertificateLifetime = CertificateFactory.DefaultLifeTime;
            CACertificateKeySize = CertificateFactory.DefaultKeySize;
            CACertificateHashSize = CertificateFactory.DefaultHashSize;
            CertificateTypes = [];
        }

        [DataTypeField(Order = 0, IsRequired = true)]
        public string? Id { get; set; }

        [DataTypeField(Order = 1)]
        public ArrayOf<string> CertificateTypes { get; set; }

        [DataTypeField(Order = 2, IsRequired = true)]
        public string? SubjectName { get; set; }

        [DataTypeField(Order = 3, IsRequired = true)]
        public string? BaseStorePath { get; set; }

        [DataTypeField(Order = 4)]
        public ushort DefaultCertificateLifetime { get; set; }

        [DataTypeField(Order = 5)]
        public ushort DefaultCertificateKeySize { get; set; }

        [DataTypeField(Order = 6)]
        public ushort DefaultCertificateHashSize { get; set; }

        [DataTypeField(Order = 7)]
        public ushort CACertificateLifetime { get; set; }

        [DataTypeField(Order = 8)]
        public ushort CACertificateKeySize { get; set; }

        [DataTypeField(Order = 9)]
        public ushort CACertificateHashSize { get; set; }

        public string TrustedListPath
            => BaseStorePath + Path.DirectorySeparatorChar + "trusted";

        public string IssuerListPath
            => BaseStorePath + Path.DirectorySeparatorChar + "issuer";
    }
}
