// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// The retention contract of <see cref="ICertificateStore.AddRejectedAsync"/>
    /// as the two shipped store implementations have to honour it.
    /// </summary>
    /// <remarks>
    /// Zero keeps unlimited history; a positive value caps the history.
    /// Negative limits disable new additions while retaining each backend's
    /// existing pruning contract: Directory removes prior entries, while
    /// shared key/value storage leaves them intact.
    /// </remarks>
    [TestFixture]
    [Category("CertificateStore")]
    [NonParallelizable]
    public class RejectedCertificateRetentionTests
    {
        private ITelemetryContext m_telemetry;
        private string m_tempDir;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_tempDir = Path.Combine(
                Path.GetTempPath(),
                "OpcUaRejectedStoreTest_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            (m_telemetry as IDisposable)?.Dispose();

            if (Directory.Exists(m_tempDir))
            {
                Directory.Delete(m_tempDir, true);
            }
        }

        /// <summary>
        /// Verifies the directory store retains all submitted certificates at zero and none for negative limits.
        /// </summary>
        [TestCase(0, 3)]
        [TestCase(-1, 0)]
        [TestCase(-5, 0)]
        public async Task DirectoryStoreHonorsZeroAndNegativeLimitsAsync(int maxCertificates, int expectedCount)
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using CertificateCollection rejected = CreateCertificates(3);
            await store.AddRejectedAsync(rejected, maxCertificates).ConfigureAwait(false);

            using CertificateCollection stored = await store.EnumerateAsync()
                .ConfigureAwait(false);
            Assert.That(stored, Has.Count.EqualTo(expectedCount));
        }

        /// <summary>
        /// Verifies switching a directory store to unlimited retention preserves existing history when adding a
        /// certificate.
        /// </summary>
        [Test]
        public async Task DirectoryStorePreservesHistoryWhenTheMaximumBecomesZeroAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using CertificateCollection rejected = CreateCertificates(3);
            await store.AddRejectedAsync(rejected, 3).ConfigureAwait(false);

            using (CertificateCollection before = await store.EnumerateAsync()
                .ConfigureAwait(false))
            {
                Assert.That(before, Has.Count.EqualTo(3));
            }

            using CertificateCollection more = CreateCertificates(1);
            await store.AddRejectedAsync(more, 0).ConfigureAwait(false);

            using CertificateCollection after = await store.EnumerateAsync()
                .ConfigureAwait(false);
            Assert.That(after, Has.Count.EqualTo(4));
            Assert.That(after.Select(certificate => certificate.Thumbprint),
                Is.EquivalentTo(rejected.Concat(more).Select(certificate => certificate.Thumbprint)));
        }

        [Test]
        public async Task DirectoryStoreCapsHistoryAtTheMaximumAsync()
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using CertificateCollection rejected = CreateCertificates(5);
            await store.AddRejectedAsync(rejected, 2).ConfigureAwait(false);

            using CertificateCollection stored = await store.EnumerateAsync()
                .ConfigureAwait(false);
            Assert.That(stored, Has.Count.EqualTo(2));
        }

        /// <summary>
        /// Verifies the shared store retains all submitted certificates at zero and none for negative limits.
        /// </summary>
        [TestCase(0, 3)]
        [TestCase(-1, 0)]
        [TestCase(-5, 0)]
        public async Task SharedKeyValueStoreHonorsZeroAndNegativeLimitsAsync(int maxCertificates, int expectedCount)
        {
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateKeyValueStore(backend);

            using CertificateCollection rejected = CreateCertificates(3);
            await store.AddRejectedAsync(rejected, maxCertificates).ConfigureAwait(false);

            using CertificateCollection stored = await store.EnumerateAsync()
                .ConfigureAwait(false);
            Assert.That(stored, Has.Count.EqualTo(expectedCount));
        }

        /// <summary>
        /// Unlimited retention accepts new entries; disabling storage leaves
        /// the previously retained certificates untouched.
        /// </summary>
        [TestCase(0, 4)]
        [TestCase(-1, 3)]
        [TestCase(-5, 3)]
        public async Task SharedKeyValueStorePreservesHistoryWhenTheMaximumIsNotPositiveAsync(
            int maxCertificates,
            int expectedCount)
        {
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateKeyValueStore(backend);

            using CertificateCollection rejected = CreateCertificates(3);
            await store.AddRejectedAsync(rejected, 3).ConfigureAwait(false);

            using (CertificateCollection before = await store.EnumerateAsync()
                .ConfigureAwait(false))
            {
                Assert.That(before, Has.Count.EqualTo(3));
            }

            using CertificateCollection more = CreateCertificates(1);
            await store.AddRejectedAsync(more, maxCertificates).ConfigureAwait(false);

            using CertificateCollection after = await store.EnumerateAsync()
                .ConfigureAwait(false);
            Assert.That(after, Has.Count.EqualTo(expectedCount));
            var expected = maxCertificates == 0 ? rejected.Concat(more) : rejected;
            Assert.That(after.Select(certificate => certificate.Thumbprint),
                Is.EquivalentTo(expected.Select(certificate => certificate.Thumbprint)));
        }

        [Test]
        public async Task SharedKeyValueStoreCapsHistoryAtTheMaximumAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateKeyValueStore(backend);

            using CertificateCollection rejected = CreateCertificates(5);
            await store.AddRejectedAsync(rejected, 2).ConfigureAwait(false);

            using CertificateCollection stored = await store.EnumerateAsync()
                .ConfigureAwait(false);
            Assert.That(stored, Has.Count.EqualTo(2));
        }

        private SharedKeyValueCertificateStore CreateKeyValueStore(
            ISharedKeyValueStore backend)
        {
            var store = new SharedKeyValueCertificateStore(backend, null, m_telemetry);
            store.Open("kv:pki/rejected");
            return store;
        }

        private static CertificateCollection CreateCertificates(int count)
        {
            var certificates = new CertificateCollection();

            for (int ii = 0; ii < count; ii++)
            {
                using Certificate certificate = CertificateBuilder
                    .Create($"CN=Rejected {ii} {Guid.NewGuid():N}")
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                certificates.Add(certificate);
            }

            return certificates;
        }
    }
}
