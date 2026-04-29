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

using System;
using NUnit.Framework;

namespace Opc.Ua.Security.Certificates.Tests
{
    [TestFixture]
    [Category("CertificateManager")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CertificateEntryTests
    {
        [Test]
        public void CreateWithCertificateAndChain()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=TestCert")
                .CreateForRSA();

            using var chain = new CertificateCollection();
            var certType = new NodeId(12345);

            using var entry = new CertificateEntry(cert, chain, certType);

            Assert.That(entry.Certificate, Is.SameAs(cert));
            Assert.That(entry.IssuerChain, Is.SameAs(chain));
            Assert.That(entry.CertificateType, Is.EqualTo(certType));
        }

        [Test]
        public void GetEncodedChainBlobReturnsDerData()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=TestCert")
                .CreateForRSA();

            using Certificate issuer = CertificateBuilder
                .Create("CN=Issuer")
                .SetCAConstraint()
                .CreateForRSA();

            using var chain = new CertificateCollection { issuer };
            var certType = new NodeId(12345);

            using var entry = new CertificateEntry(cert, chain, certType);
            byte[] blob = entry.GetEncodedChainBlob();

            int expectedLength = cert.RawData.Length + issuer.RawData.Length;
            Assert.That(blob, Is.Not.Null);
            Assert.That(blob.Length, Is.EqualTo(expectedLength));

            // Verify first bytes match the certificate raw data
            byte[] certPortion = new byte[cert.RawData.Length];
            Array.Copy(blob, 0, certPortion, 0, certPortion.Length);
            Assert.That(certPortion, Is.EqualTo(cert.RawData));
        }

        [Test]
        public void IsNearExpiryReturnsTrueWhenNearExpiry()
        {
            // Create a certificate that expires in 1 day
            using Certificate cert = CertificateBuilder
                .Create("CN=ShortLived")
                .SetNotBefore(DateTime.UtcNow.AddHours(-1))
                .SetNotAfter(DateTime.UtcNow.AddDays(1))
                .CreateForRSA();

            using var chain = new CertificateCollection();
            var certType = new NodeId(12345);

            using var entry = new CertificateEntry(cert, chain, certType);

            // Threshold of 2 days should trigger near-expiry
            Assert.That(entry.IsNearExpiry(TimeSpan.FromDays(2)), Is.True);
        }

        [Test]
        public void IsNearExpiryReturnsFalseWhenFarFromExpiry()
        {
            // Default cert has long lifetime
            using Certificate cert = CertificateBuilder
                .Create("CN=LongLived")
                .SetLifeTime(TimeSpan.FromDays(365))
                .CreateForRSA();

            using var chain = new CertificateCollection();
            var certType = new NodeId(12345);

            using var entry = new CertificateEntry(cert, chain, certType);

            // Threshold of 1 day should not trigger near-expiry
            Assert.That(entry.IsNearExpiry(TimeSpan.FromDays(1)), Is.False);
        }

        [Test]
        public void DisposeDisposesCertificateAndChain()
        {
            Certificate cert = CertificateBuilder
                .Create("CN=Disposable")
                .CreateForRSA();

            Certificate issuer = CertificateBuilder
                .Create("CN=Issuer")
                .SetCAConstraint()
                .CreateForRSA();

            var chain = new CertificateCollection { issuer };
            var certType = new NodeId(12345);

            var entry = new CertificateEntry(cert, chain, certType);

            // CertificateEntry AddRefs both cert and chain, so dispose
            // the caller's references first.
            cert.Dispose();
            issuer.Dispose();
            chain.Dispose();

            // Entry still holds the last ref; disposing the entry
            // should release the underlying certificates.
            entry.Dispose();

            // After both refs are disposed, accessing RawData should throw
            Assert.That(() => cert.RawData, Throws.Exception);
        }

        [Test]
        public void NotAfterMatchesCertificate()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=TestCert")
                .CreateForRSA();

            using var chain = new CertificateCollection();
            var certType = new NodeId(12345);

            using var entry = new CertificateEntry(cert, chain, certType);

            Assert.That(entry.NotAfter, Is.EqualTo(cert.NotAfter));
        }
    }
}
