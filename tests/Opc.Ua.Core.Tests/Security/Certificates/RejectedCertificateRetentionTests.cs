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
 *
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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.IO;
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
    /// Zero or less keeps no history at all and discards whatever is stored; a
    /// positive value caps the history at that many, oldest discarded first.
    /// <see cref="DirectoryCertificateStore"/> always behaved this way but did it
    /// by writing every certificate and then deleting all of them again;
    /// <see cref="SharedKeyValueCertificateStore"/> read zero as unlimited, so
    /// the two store types did opposite things at the same setting and
    /// <c>CertificateManager.MaxRejectedCertificates = 0</c> meant one thing on
    /// a directory store and another on a key-value store.
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

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(-5)]
        public async Task DirectoryStoreKeepsNoHistoryAtOrBelowZeroAsync(int maxCertificates)
        {
            using var store = new DirectoryCertificateStore(m_telemetry);
            store.Open(m_tempDir);

            using CertificateCollection rejected = CreateCertificates(3);
            await store.AddRejectedAsync(rejected, maxCertificates).ConfigureAwait(false);

            using CertificateCollection stored = await store.EnumerateAsync()
                .ConfigureAwait(false);
            Assert.That(stored, Is.Empty);
        }

        [Test]
        public async Task DirectoryStoreDiscardsHistoryWhenTheMaximumDropsToZeroAsync()
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
            Assert.That(after, Is.Empty);
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

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(-5)]
        public async Task SharedKeyValueStoreKeepsNoHistoryAtOrBelowZeroAsync(int maxCertificates)
        {
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateKeyValueStore(backend);

            using CertificateCollection rejected = CreateCertificates(3);
            await store.AddRejectedAsync(rejected, maxCertificates).ConfigureAwait(false);

            using CertificateCollection stored = await store.EnumerateAsync()
                .ConfigureAwait(false);
            Assert.That(stored, Is.Empty);
        }

        /// <summary>
        /// The one behaviour that changed relative to the previous release: a
        /// maximum of zero used to be read as unlimited here, so the history kept
        /// growing while the directory store kept nothing.
        /// </summary>
        [Test]
        public async Task SharedKeyValueStoreDiscardsHistoryWhenTheMaximumDropsToZeroAsync()
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
            await store.AddRejectedAsync(more, 0).ConfigureAwait(false);

            using CertificateCollection after = await store.EnumerateAsync()
                .ConfigureAwait(false);
            Assert.That(after, Is.Empty);
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
