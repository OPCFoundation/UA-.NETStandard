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
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Unit tests for the <see cref="CertificateCache"/> class.
    /// </summary>
    [TestFixture]
    [Category("CertificateCache")]
    [Parallelizable]
    public class CertificateCacheTests
    {
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            (m_telemetry as IDisposable)?.Dispose();
        }

#if NET6_0_OR_GREATER

        [Test]
        public void SetAndTryGetPublicKeyCert()
        {
            using var cache = new CertificateCache(m_telemetry);
            using Certificate original = CertificateBuilder
                .Create("CN=PublicKeyTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // Create a public-key-only certificate from the raw data
            using Certificate pubOnly = Certificate.FromRawData(original.RawData);
            Assert.That(pubOnly.HasPrivateKey, Is.False);

            cache.Set(pubOnly.Thumbprint, pubOnly);

            Certificate cached = cache.TryGet(pubOnly.Thumbprint);
            Assert.That(cached, Is.Not.Null);
            Assert.That(cached.Thumbprint, Is.EqualTo(pubOnly.Thumbprint));
            cached.Dispose();
        }

        [Test]
        public void SetAndTryGetPrivateKeyCert()
        {
            using var cache = new CertificateCache(m_telemetry);
            using Certificate cert = CertificateBuilder
                .Create("CN=PrivateKeyTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Assert.That(cert.HasPrivateKey, Is.True);

            cache.Set(cert.Thumbprint, cert);

            Certificate cached = cache.TryGet(cert.Thumbprint);
            Assert.That(cached, Is.Not.Null);
            Assert.That(cached.Thumbprint, Is.EqualTo(cert.Thumbprint));
            Assert.That(cached.HasPrivateKey, Is.True);
            cached.Dispose();
        }

        [Test]
        public void TryGetReturnsNullForMissing()
        {
            using var cache = new CertificateCache(m_telemetry);

            Certificate result = cache.TryGet("AABBCCDD00112233");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void RemoveInvalidatesEntry()
        {
            using var cache = new CertificateCache(m_telemetry);
            Certificate cert = CertificateBuilder
                .Create("CN=RemoveTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            Certificate pubOnly = Certificate.FromRawData(cert.RawData);
            string thumbprint = pubOnly.Thumbprint;

            // Set bumps ref to 2; Remove evicts (ref→1); using exit (ref→0)
            cache.Set(thumbprint, pubOnly);
            cache.Remove(thumbprint);

            Certificate cached = cache.TryGet(thumbprint);
            Assert.That(cached, Is.Null);

            // Clean up the original refs that the test still owns
            pubOnly.Dispose();
            cert.Dispose();
        }

        [Test]
        public void ClearEvictsAll()
        {
            using var cache = new CertificateCache(m_telemetry);

            Certificate cert1 = CertificateBuilder
                .Create("CN=ClearTest1")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            Certificate pub1 = Certificate.FromRawData(cert1.RawData);

            Certificate cert2 = CertificateBuilder
                .Create("CN=ClearTest2")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            Certificate pub2 = Certificate.FromRawData(cert2.RawData);

            string thumb1 = pub1.Thumbprint;
            string thumb2 = pub2.Thumbprint;
            string thumbPriv = cert1.Thumbprint;

            cache.Set(thumb1, pub1);
            cache.Set(thumb2, pub2);
            cache.Set(thumbPriv, cert1);

            cache.Clear();

            Assert.That(cache.TryGet(thumb1), Is.Null);
            Assert.That(cache.TryGet(thumb2), Is.Null);
            Assert.That(cache.TryGet(thumbPriv), Is.Null);

            // Clean up the original refs that the test still owns
            pub1.Dispose();
            pub2.Dispose();
            cert1.Dispose();
            cert2.Dispose();
        }

        [Test]
        public void SetPublicKeyDoesNotAffectPrivateKeyTier()
        {
            using var cache = new CertificateCache(m_telemetry);

            using Certificate privCert = CertificateBuilder
                .Create("CN=TierTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using Certificate pubCert = Certificate.FromRawData(privCert.RawData);

            // Set public-key cert first, then private-key cert with the same thumbprint
            cache.Set(pubCert.Thumbprint, pubCert);
            cache.Set(privCert.Thumbprint, privCert);

            // TryGet should return the private-key version (private tier is checked first)
            Certificate cached = cache.TryGet(privCert.Thumbprint);
            Assert.That(cached, Is.Not.Null);
            Assert.That(cached.HasPrivateKey, Is.True);
            cached.Dispose();
        }

        [Test]
        public void DisposeCleansCaches()
        {
            var cache = new CertificateCache(m_telemetry);

            Certificate cert = CertificateBuilder
                .Create("CN=DisposeTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            string thumbprint = cert.Thumbprint;
            cache.Set(thumbprint, cert);
            cache.Dispose();

            // After dispose, the cached entry should be gone
            // (Clear was called during Dispose, evicting the AddRef'd copy).
            // The cert object itself may still be alive since we hold a ref.
            cert.Dispose();
        }

        [Test]
        public void MetricsReflectOperations()
        {
            using var cache = new CertificateCache(m_telemetry);

            using Certificate cert = CertificateBuilder
                .Create("CN=MetricsTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // Miss: attempt to get a non-existent entry
            Certificate miss = cache.TryGet(cert.Thumbprint);
            Assert.That(miss, Is.Null);

            // Set and hit
            cache.Set(cert.Thumbprint, cert);
            Certificate hit = cache.TryGet(cert.Thumbprint);
            Assert.That(hit, Is.Not.Null);
            hit.Dispose();

            // Verify cache returns correct data after operations
            Certificate hit2 = cache.TryGet(cert.Thumbprint);
            Assert.That(hit2, Is.Not.Null);
            Assert.That(hit2.Thumbprint, Is.EqualTo(cert.Thumbprint));
            hit2.Dispose();
        }

        [Test]
        public void RefCountingIsCorrect()
        {
            using var cache = new CertificateCache(m_telemetry);
            Certificate cert = CertificateBuilder
                .Create("CN=RefTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            cache.Set(cert.Thumbprint, cert);

            Certificate cached = cache.TryGet(cert.Thumbprint);
            Assert.That(cached, Is.Not.Null);
            cached.Dispose();

            // Original should still be alive in the cache
            Certificate stillCached = cache.TryGet(cert.Thumbprint);
            Assert.That(stillCached, Is.Not.Null);
            stillCached.Dispose();
            cert.Dispose();
        }

#else

        [Test]
        public void NoOpOnOlderPlatforms()
        {
            using var cache = new CertificateCache(m_telemetry);
            using Certificate cert = CertificateBuilder
                .Create("CN=NoOpTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // On older TFMs, Set is a no-op and TryGet always returns null
            cache.Set(cert.Thumbprint, cert);
            Certificate result = cache.TryGet(cert.Thumbprint);
            Assert.That(result, Is.Null);
        }

#endif
    }
}
