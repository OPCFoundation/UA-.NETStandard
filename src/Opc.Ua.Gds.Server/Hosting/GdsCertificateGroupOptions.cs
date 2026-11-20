/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;

namespace Opc.Ua.Gds.Server.Hosting
{
    /// <summary>
    /// A certificate group of a GDS hosted through
    /// <see cref="GdsServerOptions.CertificateGroups"/>. It mirrors
    /// <see cref="CertificateGroupConfiguration"/> with plain collection
    /// types so it can be bound from <c>IConfiguration</c> (e.g.
    /// <c>appsettings.json</c>).
    /// </summary>
    public sealed class GdsCertificateGroupOptions
    {
        /// <summary>
        /// The certificate group id, e.g. <c>Default</c>.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The certificate types of the group, e.g.
        /// <c>RsaSha256ApplicationCertificateType</c>. At least one is required.
        /// </summary>
        public IList<string> CertificateTypes { get; } = [];

        /// <summary>
        /// The subject name of the group's CA certificate.
        /// </summary>
        public string SubjectName { get; set; } = string.Empty;

        /// <summary>
        /// The store path of the group's CA. When empty, defaults to
        /// <c>{BaseCertificateGroupStorePath}/{Id}</c>.
        /// </summary>
        public string BaseStorePath { get; set; } = string.Empty;

        /// <summary>
        /// The lifetime in months of issued certificates.
        /// </summary>
        public ushort DefaultCertificateLifetime { get; set; } = CertificateFactory.DefaultLifeTime;

        /// <summary>
        /// The key size of issued certificates.
        /// </summary>
        public ushort DefaultCertificateKeySize { get; set; } = CertificateFactory.DefaultKeySize;

        /// <summary>
        /// The hash size of issued certificates.
        /// </summary>
        public ushort DefaultCertificateHashSize { get; set; } = CertificateFactory.DefaultHashSize;

        /// <summary>
        /// The lifetime in months of the CA certificate.
        /// </summary>
        public ushort CACertificateLifetime { get; set; } = CertificateFactory.DefaultLifeTime;

        /// <summary>
        /// The key size of the CA certificate.
        /// </summary>
        public ushort CACertificateKeySize { get; set; } = CertificateFactory.DefaultKeySize;

        /// <summary>
        /// The hash size of the CA certificate.
        /// </summary>
        public ushort CACertificateHashSize { get; set; } = CertificateFactory.DefaultHashSize;
    }
}
