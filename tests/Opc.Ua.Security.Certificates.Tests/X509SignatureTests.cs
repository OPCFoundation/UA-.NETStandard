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

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    /// <summary>
    /// Exercises DER-to-fixed-width ECDSA verification, including the non-byte-aligned P-521 curve.
    /// </summary>
    [TestFixture]
    [Category("Certificate")]
    [Category("ECDsa")]
    [Parallelizable(ParallelScope.All)]
    public sealed class X509SignatureTests
    {
        /// <summary>
        /// Uses the actual curve width and verifies the same certificate through both public verification paths.
        /// </summary>
        [TestCase(256)]
        [TestCase(384)]
        [TestCase(521)]
        public void NamedCurveSelfSignatureUsesTheCompleteKeyWidth(int bits)
        {
            using Certificate certificate = CreateCertificate(bits);
            using X509Certificate2 x509 = certificate.AsX509Certificate2();
            using ECDsa key = x509.GetECDsaPublicKey()!;
            var signature = new X509Signature(x509.RawData);
            Assert.Multiple(() =>
            {
                Assert.That(key.KeySize, Is.EqualTo(bits));
                Assert.That(signature.Verify(x509), Is.True);
                Assert.That(X509Utils.VerifySelfSigned(certificate), Is.True);
            });
        }

        /// <summary>
        /// Correct padding never makes modified signed bytes or a different key acceptable.
        /// </summary>
        [TestCase(256, false)]
        [TestCase(384, false)]
        [TestCase(521, false)]
        [TestCase(256, true)]
        [TestCase(384, true)]
        [TestCase(521, true)]
        public void ModifiedDataAndDifferentKeysRemainRejected(int bits, bool differentKey)
        {
            using Certificate certificate = CreateCertificate(bits);
            using X509Certificate2 x509 = certificate.AsX509Certificate2();
            var signature = new X509Signature(x509.RawData);
            if (differentKey)
            {
                using Certificate other = CreateCertificate(bits);
                using X509Certificate2 wrongKey = other.AsX509Certificate2();
                Assert.That(signature.Verify(wrongKey), Is.False);
            }
            else
            {
                signature.Tbs[^1] ^= 1;
                Assert.That(signature.Verify(x509), Is.False);
            }
        }

        private static Certificate CreateCertificate(int bits)
        {
            ECCurve curve = bits switch
            {
                256 => ECCurve.NamedCurves.nistP256,
                384 => ECCurve.NamedCurves.nistP384,
                521 => ECCurve.NamedCurves.nistP521,
                _ => throw new ArgumentOutOfRangeException(nameof(bits))
            };
            HashAlgorithmName hash = bits switch
            {
                256 => HashAlgorithmName.SHA256,
                384 => HashAlgorithmName.SHA384,
                _ => HashAlgorithmName.SHA512
            };
            return CertificateBuilder.Create("CN=ECDSA signature width")
                .SetHashAlgorithm(hash)
                .SetECCurve(curve)
                .CreateForECDsa();
        }
    }
}
