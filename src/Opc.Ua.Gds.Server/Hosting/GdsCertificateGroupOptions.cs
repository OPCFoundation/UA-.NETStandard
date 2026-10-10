/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
