/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Supplies independently owned DER trust material for one certificate-validation operation.
    /// </summary>
    public sealed class CertificateValidationScope
    {
        /// <summary>
        /// Copies the trusted anchors, untrusted issuer material and certificate revocation lists.
        /// Empty anchors establish no trust; they never fall back to a configured or platform trust list.
        /// </summary>
        public CertificateValidationScope(
            ArrayOf<ByteString> trustedCertificates,
            ArrayOf<ByteString> issuerCertificates = default,
            ArrayOf<ByteString> certificateRevocationLists = default)
        {
            m_trustedCertificates = Snapshot(trustedCertificates);
            m_issuerCertificates = Snapshot(issuerCertificates);
            m_certificateRevocationLists = Snapshot(certificateRevocationLists);
        }

        /// <summary>
        /// Gets independent DER copies of the certificates which establish trust for this operation.
        /// </summary>
        public ArrayOf<ByteString> TrustedCertificates => Snapshot(m_trustedCertificates);

        /// <summary>
        /// Gets independent DER copies of issuer certificates which do not themselves establish trust.
        /// </summary>
        public ArrayOf<ByteString> IssuerCertificates => Snapshot(m_issuerCertificates);

        /// <summary>
        /// Gets independent DER copies of the CRLs available during this operation.
        /// </summary>
        public ArrayOf<ByteString> CertificateRevocationLists => Snapshot(m_certificateRevocationLists);

        /// <summary>
        /// Copies both the collection and each byte string instead of retaining caller-owned storage.
        /// </summary>
        private static ArrayOf<ByteString> Snapshot(ArrayOf<ByteString> values)
        {
            var snapshot = new ByteString[values.Count];
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i] = ByteString.From(values[i].Span);
            }
            return snapshot;
        }

        /// <summary>
        /// The independently owned trusted-anchor DER.
        /// </summary>
        private readonly ArrayOf<ByteString> m_trustedCertificates;

        /// <summary>
        /// The independently owned untrusted issuer DER.
        /// </summary>
        private readonly ArrayOf<ByteString> m_issuerCertificates;

        /// <summary>
        /// The independently owned CRL DER.
        /// </summary>
        private readonly ArrayOf<ByteString> m_certificateRevocationLists;
    }
}
