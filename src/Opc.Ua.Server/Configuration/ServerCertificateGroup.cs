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
