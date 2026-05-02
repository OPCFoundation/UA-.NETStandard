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
using System.IO;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Holds a certificate together with its issuer chain and
    /// OPC UA certificate type <see cref="NodeId"/>.
    /// </summary>
    public sealed class CertificateEntry : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CertificateEntry"/> class.
        /// </summary>
        /// <param name="certificate">
        /// The certificate (with private key if available).
        /// </param>
        /// <param name="issuerChain">
        /// The issuer chain (may be empty for self-signed certificates).
        /// </param>
        /// <param name="certificateType">
        /// The OPC UA certificate type <see cref="NodeId"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="certificate"/>,
        /// <paramref name="issuerChain"/>, or
        /// <paramref name="certificateType"/> is <c>null</c>.
        /// </exception>
        public CertificateEntry(
            Certificate certificate,
            CertificateCollection issuerChain,
            NodeId certificateType)
        {
            Certificate = certificate?.AddRef() ?? throw new ArgumentNullException(nameof(certificate));
            IssuerChain = issuerChain?.AddRef() ?? throw new ArgumentNullException(nameof(issuerChain));
            CertificateType = certificateType;
        }

        /// <summary>
        /// Gets the certificate (with private key if available).
        /// </summary>
        public Certificate Certificate { get; }

        /// <summary>
        /// Gets the issuer chain (may be empty for self-signed certificates).
        /// </summary>
        public CertificateCollection IssuerChain { get; }

        /// <summary>
        /// Gets the OPC UA certificate type <see cref="NodeId"/>.
        /// </summary>
        public NodeId CertificateType { get; }

        /// <summary>
        /// Gets the expiration date of the certificate.
        /// </summary>
        public DateTime NotAfter => Certificate.NotAfter;

        /// <summary>
        /// Determines whether the certificate is within
        /// <paramref name="threshold"/> of its expiry date.
        /// </summary>
        /// <param name="threshold">
        /// The time span before expiry at which the certificate is
        /// considered near expiry.
        /// </param>
        /// <returns>
        /// <c>true</c> if the certificate expires within the given
        /// threshold; otherwise <c>false</c>.
        /// </returns>
        public bool IsNearExpiry(TimeSpan threshold)
        {
            return DateTime.UtcNow.Add(threshold) >= NotAfter;
        }

        /// <summary>
        /// Returns a DER-encoded blob containing the certificate
        /// followed by each issuer in the chain, suitable for
        /// wire transmission.
        /// </summary>
        /// <returns>The concatenated DER-encoded certificate data.</returns>
        public byte[] GetEncodedChainBlob()
        {
            using var stream = new MemoryStream();
            stream.Write(Certificate.RawData, 0, Certificate.RawData.Length);

            foreach (Certificate issuer in IssuerChain)
            {
                stream.Write(issuer.RawData, 0, issuer.RawData.Length);
            }

            return stream.ToArray();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Certificate.Dispose();
            IssuerChain.Dispose();
        }
    }
}
