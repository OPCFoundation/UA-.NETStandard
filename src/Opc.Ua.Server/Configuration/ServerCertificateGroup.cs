// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server
{
    /// <summary>
    /// Server-side view of one certificate group under
    /// <c>ServerConfiguration.CertificateGroups</c> (OPC 10000-12 §7.8.4): the
    /// group node, the certificate types it accepts, the application
    /// certificates occupying its slots and the stores its TrustList serves.
    /// Shared by <see cref="ConfigurationNodeManager"/> and its collaborators.
    /// </summary>
    internal sealed class ServerCertificateGroup
    {
        /// <summary>
        /// The BrowseName of the group node.
        /// </summary>
        public string BrowseName { get; set; } = null!;

        /// <summary>
        /// The NodeId of the group node.
        /// </summary>
        public NodeId NodeId { get; set; }

        /// <summary>
        /// The group node once the address space has been created.
        /// </summary>
        public CertificateGroupState Node { get; set; } = null!;

        /// <summary>
        /// The certificate types the group accepts.
        /// </summary>
        public NodeId[] CertificateTypes { get; set; } = null!;

        /// <summary>
        /// The application certificates occupying the group's slots.
        /// </summary>
        public ArrayOf<CertificateIdentifier> ApplicationCertificates { get; set; }

        /// <summary>
        /// The issuer store backing the group's TrustList.
        /// </summary>
        public CertificateStoreIdentifier IssuerStore { get; set; } = null!;

        /// <summary>
        /// The trusted store backing the group's TrustList.
        /// </summary>
        public CertificateStoreIdentifier TrustedStore { get; set; } = null!;
    }
}
