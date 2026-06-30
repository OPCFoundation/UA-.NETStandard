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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Provides read-only access to the application's own certificates.
    /// </summary>
    public interface ICertificateRegistry
    {
        /// <summary>
        /// Gets a value indicating whether the application should send
        /// the complete certificate chain when establishing a secure
        /// channel.
        /// </summary>
        /// <remarks>
        /// Mirrors <see cref="SecurityConfiguration.SendCertificateChain"/>.
        /// When <see langword="true"/>, transports include the full DER-
        /// encoded chain blob (instance certificate followed by issuers)
        /// in the channel handshake; when <see langword="false"/>, only
        /// the instance certificate is sent.
        /// </remarks>
        bool SendCertificateChain { get; }

        /// <summary>
        /// Returns a caller-owned snapshot of all application certificate
        /// entries.
        /// </summary>
        /// <remarks>
        /// The returned <see cref="CertificateEntryCollection"/> owns an
        /// independent reference-counted handle on every entry. The caller is
        /// responsible for disposing it (a <c>using</c> is recommended);
        /// disposing it has no effect on the registry's own entries, and the
        /// registry may concurrently replace its entries (e.g. a certificate
        /// hot-update) without affecting the returned snapshot.
        /// </remarks>
        /// <returns>A new owned snapshot the caller must dispose.</returns>
        CertificateEntryCollection SnapshotApplicationCertificates();

        /// <summary>
        /// Returns a caller-owned application certificate entry that matches
        /// the specified OPC UA certificate type <see cref="NodeId"/>.
        /// </summary>
        /// <param name="certificateType">
        /// The OPC UA certificate type node identifier.
        /// </param>
        /// <returns>
        /// A new owned <see cref="CertificateEntry"/> the caller must dispose,
        /// or <see langword="null"/> if no certificate of that type is
        /// registered.
        /// </returns>
        CertificateEntry? AcquireApplicationCertificateByType(NodeId certificateType);

        /// <summary>
        /// Returns a caller-owned instance certificate entry that is
        /// appropriate for the specified security policy URI.
        /// </summary>
        /// <param name="securityPolicyUri">
        /// The OPC UA security policy URI (e.g.
        /// <c>http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256</c>).
        /// </param>
        /// <returns>
        /// A new owned <see cref="CertificateEntry"/> the caller must dispose,
        /// or <see langword="null"/> if no suitable certificate is found.
        /// </returns>
        CertificateEntry? AcquireApplicationCertificateBySecurityPolicy(string securityPolicyUri);

        /// <summary>
        /// Resolves the issuers for the supplied <paramref name="certificate"/>
        /// using the registry's trust list state and appends them to
        /// <paramref name="issuers"/>.
        /// </summary>
        /// <remarks>
        /// Walks the trusted, issuer, and any untrusted stores looking for
        /// the issuers of <paramref name="certificate"/>. Each returned
        /// <see cref="CertificateIssuerReference"/> carries an
        /// <see cref="Certificate.AddRef"/>'d certificate that the
        /// caller is responsible for disposing.
        /// </remarks>
        /// <param name="certificate">The certificate to resolve issuers for.</param>
        /// <param name="issuers">
        /// The output list which receives the resolved issuer
        /// <see cref="CertificateIssuerReference"/> entries.
        /// </param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>
        /// <see langword="true"/> when at least one issuer was resolved
        /// from a trusted store.
        /// </returns>
        Task<bool> GetIssuersAsync(
            Certificate certificate,
            IList<CertificateIssuerReference> issuers,
            CancellationToken ct = default);
    }
}
