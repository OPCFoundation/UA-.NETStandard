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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Provides file-based access to a trust-list for reading and writing
    /// its contents as a serialized blob. This maps to the OPC UA
    /// TrustListType (Part 12 §7.5) Open/Read/Write/Close interface.
    /// </summary>
    public interface ITrustListFileAccess
    {
        /// <summary>
        /// Reads the complete trust-list contents as a serialized blob.
        /// The blob contains trusted certificates, trusted CRLs,
        /// issuer certificates, and issuer CRLs.
        /// </summary>
        /// <param name="trustList">
        /// The trust list identifier to read from.
        /// </param>
        /// <param name="masks">
        /// A bit mask indicating which parts of the trust list to read.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A <see cref="TrustListData"/> containing the requested
        /// trust-list contents.
        /// </returns>
        Task<TrustListData> ReadTrustListAsync(
            TrustListIdentifier trustList,
            TrustListMasks masks = TrustListMasks.All,
            CancellationToken ct = default);

        /// <summary>
        /// Writes trust-list contents from a serialized blob,
        /// replacing the current contents.
        /// </summary>
        /// <param name="trustList">
        /// The trust list identifier to write to.
        /// </param>
        /// <param name="data">The trust-list data to write.</param>
        /// <param name="masks">
        /// A bit mask indicating which parts of the trust list to write.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        Task WriteTrustListAsync(
            TrustListIdentifier trustList,
            TrustListData data,
            TrustListMasks masks = TrustListMasks.All,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Represents the contents of a trust-list, including trusted
    /// certificates and CRLs as well as issuer certificates and CRLs.
    /// </summary>
    public sealed class TrustListData : IDisposable
    {
        /// <summary>Trusted certificates.</summary>
        public CertificateCollection TrustedCertificates { get; set; } = new();

        /// <summary>Trusted CRLs.</summary>
        public X509CRLCollection TrustedCrls { get; set; } = new();

        /// <summary>Issuer certificates.</summary>
        public CertificateCollection IssuerCertificates { get; set; } = new();

        /// <summary>Issuer CRLs.</summary>
        public X509CRLCollection IssuerCrls { get; set; } = new();

        /// <inheritdoc/>
        public void Dispose()
        {
            TrustedCertificates?.Dispose();
            IssuerCertificates?.Dispose();
        }
    }
}
