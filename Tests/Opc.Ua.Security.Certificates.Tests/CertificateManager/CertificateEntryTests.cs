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

            // CertificateEntry stores an independent AddRef handle (distinct
            // object, value-equal) rather than the caller's instance.
            Assert.That(entry.Certificate, Is.EqualTo(cert));
            Assert.That(entry.IssuerChain, Is.Not.SameAs(chain));
            Assert.That(entry.IssuerChain, Has.Count.EqualTo(chain.Count));
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
            Assert.That(blob, Has.Length.EqualTo(expectedLength));

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

            using var chain = new CertificateCollection { issuer };
            var certType = new NodeId(12345);

            var entry = new CertificateEntry(cert, chain, certType);

            // CertificateEntry AddRefs both cert and the certs in chain.
            // Dispose the caller's original references first.
            cert.Dispose();
            issuer.Dispose();

            // Entry holds AddRef'd references to cert and each cert in
            // chain. Disposing the entry releases its references.
            entry.Dispose();

            // After both refs are disposed, accessing RawData should throw
            Assert.That(() => cert.RawData, Throws.Exception);
        }

        [Test]
        public void AddRefReturnsIndependentOwnedClone()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=AddRefSource")
                .CreateForRSA();

            using var chain = new CertificateCollection();
            var certType = new NodeId(12345);

            using var entry = new CertificateEntry(cert, chain, certType);

            CertificateEntry clone = entry.AddRef();

            Assert.That(clone, Is.Not.SameAs(entry));
            Assert.That(clone.Certificate, Is.EqualTo(entry.Certificate));
            Assert.That(clone.CertificateType, Is.EqualTo(certType));

            // Disposing the clone releases only its own handles; the original
            // entry (and the underlying certificate) remains usable.
            clone.Dispose();
            Assert.That(entry.Certificate.RawData, Is.EqualTo(cert.RawData));
        }

        [Test]
        public void CertificateEntryCollectionOwnsIndependentHandles()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=SnapshotSource")
                .CreateForRSA();

            using var chain = new CertificateCollection();
            var certType = new NodeId(12345);

            using var entry = new CertificateEntry(cert, chain, certType);

            // The collection takes independent owning handles over each entry.
            var snapshot = new CertificateEntryCollection([entry]);
            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(snapshot[0], Is.Not.SameAs(entry));

            // Disposing the snapshot must not affect the source entry.
            snapshot.Dispose();
            Assert.That(entry.Certificate.RawData, Is.EqualTo(cert.RawData));
        }

        [Test]
        public void CertificateEntryCollectionThrowsOnNullEntries()
        {
            Assert.That(
                () => new CertificateEntryCollection(null!),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void CertificateEntryCollectionEnumeratesOwnedHandles()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=SnapshotEnumerate")
                .CreateForRSA();

            using var chain = new CertificateCollection();
            var certType = new NodeId(12345);

            using var entry = new CertificateEntry(cert, chain, certType);
            using var snapshot = new CertificateEntryCollection([entry]);

            // Generic enumerator (foreach) yields the collection's own handle.
            int generic = 0;
            foreach (CertificateEntry item in snapshot)
            {
                Assert.That(item, Is.SameAs(snapshot[0]));
                generic++;
            }
            Assert.That(generic, Is.EqualTo(1));

            // The non-generic IEnumerable.GetEnumerator is also supported.
            int nonGeneric = 0;
            foreach (object item in (System.Collections.IEnumerable)snapshot)
            {
                Assert.That(item, Is.InstanceOf<CertificateEntry>());
                nonGeneric++;
            }
            Assert.That(nonGeneric, Is.EqualTo(1));
        }

        [Test]
        public void CertificateEntryCollectionDisposeIsIdempotent()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=SnapshotIdempotent")
                .CreateForRSA();

            using var chain = new CertificateCollection();
            var certType = new NodeId(12345);

            using var entry = new CertificateEntry(cert, chain, certType);
            var snapshot = new CertificateEntryCollection([entry]);

            snapshot.Dispose();
            // A second dispose hits the early-return guard and must not throw.
            Assert.That(() => snapshot.Dispose(), Throws.Nothing);

            // The source entry remains usable after the snapshot is disposed.
            Assert.That(entry.Certificate.RawData, Is.EqualTo(cert.RawData));
        }
    }
}
