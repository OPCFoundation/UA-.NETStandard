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

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Structural checks on the distinguished names carried by a certificate.
    /// </summary>
    /// <remarks>
    /// RFC 5280 §4.1.2.4 states that the issuer field MUST contain a non-empty
    /// distinguished name, and §4.1.2.6 only permits an empty subject for an
    /// end entity that carries a critical subjectAltName - which a CA
    /// certificate may never do. An empty name is encoded as an empty
    /// RDNSequence, so it identifies nothing and two unrelated issuers become
    /// indistinguishable, which is why such a certificate can never take part
    /// in a trust decision.
    /// <para>
    /// The platform disagrees about these certificates: .NET's own X.509 and
    /// PEM readers load them, while BouncyCastle 2.7.0 and later refuse to
    /// parse them at all. Applying this check explicitly is what keeps the
    /// behaviour identical on every target framework rather than depending on
    /// whichever parser the framework happens to use.
    /// </para>
    /// </remarks>
    public static class DistinguishedNameUtils
    {
        /// <summary>
        /// Returns whether a distinguished name is absent or encodes an empty
        /// RDNSequence.
        /// </summary>
        /// <param name="name">
        /// The name to test.
        /// </param>
        /// <returns>
        /// True if the name is null, carries no encoded content, or encodes the
        /// empty RDNSequence; otherwise false.
        /// </returns>
        public static bool IsEmpty(X500DistinguishedName? name)
        {
            // An empty RDNSequence is the two byte DER encoding 30 00, so any
            // name at or below that length carries no relative distinguished
            // name at all.
            return name?.RawData == null ||
                name.RawData.Length <= 2 ||
                string.IsNullOrEmpty(name.Name);
        }

        /// <summary>
        /// Returns whether either the subject or the issuer of a certificate is
        /// an empty distinguished name.
        /// </summary>
        /// <param name="certificate">
        /// The certificate to test.
        /// </param>
        /// <returns>
        /// True if the certificate carries an empty subject or issuer name;
        /// otherwise false.
        /// </returns>
        public static bool HasEmptyDistinguishedName(X509Certificate2? certificate)
        {
            return certificate == null ||
                IsEmpty(certificate.SubjectName) ||
                IsEmpty(certificate.IssuerName);
        }

        /// <summary>
        /// Returns whether either the subject or the issuer of a certificate is
        /// an empty distinguished name.
        /// </summary>
        /// <param name="certificate">
        /// The certificate to test.
        /// </param>
        /// <returns>
        /// True if the certificate carries an empty subject or issuer name;
        /// otherwise false.
        /// </returns>
        public static bool HasEmptyDistinguishedName(Certificate? certificate)
        {
            return certificate == null ||
                IsEmpty(certificate.SubjectName) ||
                IsEmpty(certificate.IssuerName);
        }
    }
}
