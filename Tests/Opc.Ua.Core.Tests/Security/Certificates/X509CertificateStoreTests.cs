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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for <see cref="X509CertificateStore.AddRejectedAsync"/>, including
    /// single-certificate and multi-certificate chain scenarios.
    /// </summary>
    [TestFixture]
    [Category("CertificateStore")]
    [NonParallelizable]
    [SetCulture("en-us")]
    public class X509CertificateStoreAddRejectedTests
    {
        // Safe general-purpose store. Tests here use maxCertificates=0 (unlimited/no trim)
        // to avoid accidentally removing real user certificates from CurrentUser\My.
        private const string kStorePath = "CurrentUser\\My";

        // Custom store used exclusively by trim/cap tests so that store.Certificates
        // contains only test certificates and the count is predictable.
        // Custom named stores are not supported on macOS – tests skip there.
        private const string kCustomStorePath = "CurrentUser\\UA_MachineDefault";

        // Subject DNs used by this test class — TearDown filters on all three.
        private const string kLeafSubject =
            "CN=Opc.Ua.Core.Tests, O=OPC Foundation, OU=AddRejectedTest, C=US";
        private const string kIntermediateCaSubject =
            "CN=Opc.Ua.Core.Tests Intermediate CA, O=OPC Foundation, OU=AddRejectedTest, C=US";
        private const string kRootCaSubject =
            "CN=Opc.Ua.Core.Tests Root CA, O=OPC Foundation, OU=AddRejectedTest, C=US";

        private static readonly ICertificateFactory s_factory = DefaultCertificateFactory.Instance;

        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await CleanStoreAsync(kStorePath).ConfigureAwait(false);

            // Custom store is not available on macOS.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                await CleanStoreAsync(kCustomStorePath).ConfigureAwait(false);
            }
        }

        // Removes every certificate whose Subject matches any of our test DNs.
        private async Task CleanStoreAsync(string storePath)
        {
            using var store = new X509CertificateStore(m_telemetry);
            store.Open(storePath);
            using CertificateCollection all = await store.EnumerateAsync().ConfigureAwait(false);

            foreach (Certificate cert in all)
            {
                if (IsTestCertificate(cert))
                {
                    await store.DeleteAsync(cert.Thumbprint).ConfigureAwait(false);
                }
            }
        }

        // Returns true when the certificate was created by this test class.
        private static bool IsTestCertificate(Certificate cert)
        {
            return X509Utils.CompareDistinguishedName(cert.Subject, kLeafSubject)
                || X509Utils.CompareDistinguishedName(cert.Subject, kIntermediateCaSubject)
                || X509Utils.CompareDistinguishedName(cert.Subject, kRootCaSubject);
        }

        /// <summary>
        /// Verify that a negative <c>maxCertificates</c> causes an immediate return without
        /// adding any certificate to the store.
        /// </summary>
        [Test]
        public async Task AddRejectedAsyncWithNegativeMaxCertificatesAddsNothingAsync()
        {
            using var store = new X509CertificateStore(m_telemetry);
            store.Open(kStorePath);

            using Certificate cert = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -1);
            using CertificateCollection collection = new() { cert };

            await store.AddRejectedAsync(collection, maxCertificates: -1).ConfigureAwait(false);

            using CertificateCollection found = await store
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);

            Assert.That(found, Is.Empty,
                "No certificate should be stored when maxCertificates is negative.");
        }

        /// <summary>
        /// Verify that certificates in the collection are added to the store.
        /// </summary>
        [Test]
        public async Task AddRejectedAsyncAddsCertificatesToStoreAsync()
        {
            using var store = new X509CertificateStore(m_telemetry);
            store.Open(kStorePath);

            using Certificate cert = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -1);
            using CertificateCollection collection = new() { cert };

            await store.AddRejectedAsync(collection, maxCertificates: 0).ConfigureAwait(false);

            using CertificateCollection found = await store
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);

            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Thumbprint, Is.EqualTo(cert.Thumbprint));
        }

        /// <summary>
        /// Verify that adding the same certificate twice does not create a duplicate entry.
        /// </summary>
        [Test]
        public async Task AddRejectedAsyncSkipsDuplicateCertificatesAsync()
        {
            using var store = new X509CertificateStore(m_telemetry);
            store.Open(kStorePath);

            using Certificate cert = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -1);
            using CertificateCollection collection = new() { cert };

            await store.AddRejectedAsync(collection, maxCertificates: 0).ConfigureAwait(false);
            await store.AddRejectedAsync(collection, maxCertificates: 0).ConfigureAwait(false);

            using CertificateCollection found = await store
                .FindByThumbprintAsync(cert.Thumbprint)
                .ConfigureAwait(false);

            Assert.That(found, Has.Count.EqualTo(1),
                "The same certificate must not be added twice.");
        }

        /// <summary>
        /// Verify that all members of a two-certificate chain (leaf + root CA) are stored.
        /// This mirrors how <see cref="CertificateValidator"/> passes the chain to
        /// <see cref="ICertificateStore.AddRejectedAsync"/>.
        /// </summary>
        [Test]
        public async Task AddRejectedAsyncStoresAllMembersOfTwoCertChainAsync()
        {
            using var store = new X509CertificateStore(m_telemetry);
            store.Open(kStorePath);

            // Build a two-cert chain: leaf signed by root CA.
            using Certificate rootCa = BuildRootCa(notBeforeOffsetDays: -10);
            using Certificate leaf = BuildLeafSignedBy(rootCa, notBeforeOffsetDays: -5);

            // Validator puts leaf first, then issuer chain members — public keys only.
            using Certificate leafPublic = Certificate.FromRawData(leaf.RawData);
            using Certificate rootPublic = Certificate.FromRawData(rootCa.RawData);

            using CertificateCollection chain = new() { leafPublic, rootPublic };
            await store.AddRejectedAsync(chain, maxCertificates: 0).ConfigureAwait(false);

            using CertificateCollection foundLeaf = await store
                .FindByThumbprintAsync(leafPublic.Thumbprint).ConfigureAwait(false);
            using CertificateCollection foundRoot = await store
                .FindByThumbprintAsync(rootPublic.Thumbprint).ConfigureAwait(false);

            Assert.That(foundLeaf, Has.Count.EqualTo(1), "Leaf certificate must be stored.");
            Assert.That(foundRoot, Has.Count.EqualTo(1), "Root CA certificate must be stored.");
        }

        /// <summary>
        /// Verify that all members of a three-certificate chain
        /// (leaf → intermediate CA → root CA) are individually stored.
        /// </summary>
        [Test]
        public async Task AddRejectedAsyncStoresAllMembersOfThreeCertChainAsync()
        {
            using var store = new X509CertificateStore(m_telemetry);
            store.Open(kStorePath);

            ChainFixture chain = BuildChain(notBeforeOffsetDays: -5);
            using CertificateCollection chainCollection = chain.ToCollection();

            await store.AddRejectedAsync(chainCollection, maxCertificates: 0).ConfigureAwait(false);

            foreach (Certificate cert in chainCollection)
            {
                using CertificateCollection found = await store
                    .FindByThumbprintAsync(cert.Thumbprint).ConfigureAwait(false);

                Assert.That(found, Has.Count.EqualTo(1),
                    $"Chain member '{cert.Subject}' must be stored in the rejected store.");
            }

            chain.Dispose();
        }

        /// <summary>
        /// Verify that submitting the same chain twice produces no duplicate entries —
        /// each chain member appears exactly once regardless of how many times the
        /// chain is submitted.
        /// </summary>
        [Test]
        public async Task AddRejectedAsyncSubmittingChainTwiceProducesNoDuplicatesAsync()
        {
            using var store = new X509CertificateStore(m_telemetry);
            store.Open(kStorePath);

            ChainFixture chain = BuildChain(notBeforeOffsetDays: -5);
            using CertificateCollection chainCollection = chain.ToCollection();

            await store.AddRejectedAsync(chainCollection, maxCertificates: 0).ConfigureAwait(false);
            await store.AddRejectedAsync(chainCollection, maxCertificates: 0).ConfigureAwait(false);

            foreach (Certificate cert in chainCollection)
            {
                using CertificateCollection found = await store
                    .FindByThumbprintAsync(cert.Thumbprint).ConfigureAwait(false);

                Assert.That(found, Has.Count.EqualTo(1),
                    $"Chain member '{cert.Subject}' must appear exactly once after two submissions.");
            }

            chain.Dispose();
        }

        /// <summary>
        /// Verify that only the first <c>maxCertificates</c> entries from the input collection
        /// are processed per call; any remaining entries in the input are silently ignored.
        /// </summary>
        [Test]
        public async Task AddRejectedAsyncCapsInputAtMaxCertificatesAsync()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Custom named X509 stores are not supported on macOS.");
            }

            using var store = new X509CertificateStore(m_telemetry);
            store.Open(kCustomStorePath);

            // cert3 is beyond the per-call cap and must never reach the store.
            using Certificate cert1 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -3);
            using Certificate cert2 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -2);
            using Certificate cert3 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -1);

            using CertificateCollection collection = new() { cert1, cert2, cert3 };
            await store.AddRejectedAsync(collection, maxCertificates: 2).ConfigureAwait(false);

            using CertificateCollection found1 = await store.FindByThumbprintAsync(cert1.Thumbprint).ConfigureAwait(false);
            Assert.That(found1, Has.Count.EqualTo(1), "cert1 (1st) should be in store.");

            using CertificateCollection found2 = await store.FindByThumbprintAsync(cert2.Thumbprint).ConfigureAwait(false);
            Assert.That(found2, Has.Count.EqualTo(1), "cert2 (2nd) should be in store.");

            using CertificateCollection found3 = await store.FindByThumbprintAsync(cert3.Thumbprint).ConfigureAwait(false);
            Assert.That(found3, Is.Empty, "cert3 (3rd) must not be stored: input is capped at maxCertificates=2.");
        }

        /// <summary>
        /// Verify that when a three-certificate chain (leaf → intermediate → root) is submitted
        /// with <c>maxCertificates=2</c>, only the first two chain members (leaf, intermediate)
        /// are stored and the root is silently ignored because the input cap is reached.
        /// </summary>
        [Test]
        public async Task AddRejectedAsyncCapsChainInputAtMaxCertificatesAsync()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Custom named X509 stores are not supported on macOS.");
            }

            using var store = new X509CertificateStore(m_telemetry);
            store.Open(kCustomStorePath);

            ChainFixture chain = BuildChain(notBeforeOffsetDays: -5);
            using CertificateCollection chainCollection = chain.ToCollection();

            // Cap = 2: only leaf (position 0) and intermediate (position 1) should be stored.
            // Root (position 2) must be skipped by the input cap.
            await store.AddRejectedAsync(chainCollection, maxCertificates: 2).ConfigureAwait(false);

            using CertificateCollection foundLeaf = await store.FindByThumbprintAsync(chain.Leaf.Thumbprint).ConfigureAwait(false);
            Assert.That(foundLeaf, Has.Count.EqualTo(1), "Leaf (1st) should be stored.");

            using CertificateCollection foundIntermediate = await store.FindByThumbprintAsync(chain.Intermediate.Thumbprint).ConfigureAwait(false);
            Assert.That(foundIntermediate, Has.Count.EqualTo(1), "Intermediate CA (2nd) should be stored.");

            using CertificateCollection foundRoot = await store.FindByThumbprintAsync(chain.Root.Thumbprint).ConfigureAwait(false);
            Assert.That(foundRoot, Is.Empty, "Root CA (3rd) must not be stored: input is capped at maxCertificates=2.");

            chain.Dispose();
        }

        /// <summary>
        /// Verify that when the store count stays at or below <c>maxCertificates</c>,
        /// no existing certificate is trimmed.
        /// </summary>
        [Test]
        public async Task AddRejectedAsyncWithinLimitDoesNotTrimStoreAsync()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Custom named X509 stores are not supported on macOS.");
            }

            using var store = new X509CertificateStore(m_telemetry);
            store.Open(kCustomStorePath);

            using Certificate cert1 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -2);
            using Certificate cert2 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -1);

            // Add 2 certs, limit = 5: count (2) < limit (5), no trim expected.
            using CertificateCollection initialCollection = new() { cert1, cert2 };
            await store.AddRejectedAsync(initialCollection, maxCertificates: 5).ConfigureAwait(false);

            using CertificateCollection allCerts = await store.EnumerateAsync().ConfigureAwait(false);

            Assert.That(allCerts, Has.Count.EqualTo(2),
                "Both certificates should remain; trim must not run when count is within the limit.");

            using CertificateCollection foundCert1 = await store.FindByThumbprintAsync(cert1.Thumbprint).ConfigureAwait(false);
            Assert.That(foundCert1, Has.Count.EqualTo(1), "cert1 should remain.");

            using CertificateCollection foundCert2 = await store.FindByThumbprintAsync(cert2.Thumbprint).ConfigureAwait(false);
            Assert.That(foundCert2, Has.Count.EqualTo(1), "cert2 should remain.");
        }

        /// <summary>
        /// Verify that when the store exceeds <c>maxCertificates</c>, the oldest certificates
        /// (by <see cref="Certificate.NotBefore"/>) are removed until the limit is met.
        /// </summary>
        [Test]
        public async Task AddRejectedAsyncTrimsOldestCertificatesWhenOverLimitAsync()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Custom named X509 stores are not supported on macOS.");
            }

            using var store = new X509CertificateStore(m_telemetry);
            store.Open(kCustomStorePath);

            // Controlled NotBefore values so oldest/newest order is deterministic.
            using Certificate cert1 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -40); // oldest
            using Certificate cert2 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -30);
            using Certificate cert3 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -20);
            using Certificate cert4 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -10);
            using Certificate cert5 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -1); // newest

            // Populate store with all 5 using unlimited mode.
            using CertificateCollection allFive = new() { cert1, cert2, cert3, cert4, cert5 };
            await store.AddRejectedAsync(allFive, maxCertificates: 0).ConfigureAwait(false);

            // Enforce limit of 3 via an empty input: only the trim code runs.
            await store.AddRejectedAsync(new CertificateCollection(), maxCertificates: 3).ConfigureAwait(false);

            // The 2 oldest must have been removed.
            using CertificateCollection foundCert1 = await store.FindByThumbprintAsync(cert1.Thumbprint).ConfigureAwait(false);
            Assert.That(foundCert1, Is.Empty, "cert1 (oldest, NotBefore -40d) must be trimmed.");

            using CertificateCollection foundCert2 = await store.FindByThumbprintAsync(cert2.Thumbprint).ConfigureAwait(false);
            Assert.That(foundCert2, Is.Empty, "cert2 (NotBefore -30d) must be trimmed.");

            // The 3 newest must survive.
            using CertificateCollection foundCert3 = await store.FindByThumbprintAsync(cert3.Thumbprint).ConfigureAwait(false);
            Assert.That(foundCert3, Has.Count.EqualTo(1), "cert3 should survive.");

            using CertificateCollection foundCert4 = await store.FindByThumbprintAsync(cert4.Thumbprint).ConfigureAwait(false);
            Assert.That(foundCert4, Has.Count.EqualTo(1), "cert4 should survive.");

            using CertificateCollection foundCert5 = await store.FindByThumbprintAsync(cert5.Thumbprint).ConfigureAwait(false);
            Assert.That(foundCert5, Has.Count.EqualTo(1), "cert5 (newest, NotBefore -1d) should survive.");

            using CertificateCollection remaining = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(remaining, Has.Count.EqualTo(3),
                "Store must be trimmed to exactly maxCertificates=3.");
        }

        /// <summary>
        /// Verify that when a chain is stored, the oldest chain members (root, then intermediate)
        /// are trimmed first when a subsequent call enforces a lower <c>maxCertificates</c> limit.
        /// The root CA has the earliest <see cref="Certificate.NotBefore"/>, so it is
        /// the first to be trimmed.
        /// </summary>
        [Test]
        public async Task AddRejectedAsyncTrimsOldestChainMembersWhenOverLimitAsync()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Custom named X509 stores are not supported on macOS.");
            }

            using var store = new X509CertificateStore(m_telemetry);
            store.Open(kCustomStorePath);

            // Root CA is oldest (-30d), intermediate is middle (-20d), leaf is newest (-5d).
            ChainFixture chain = BuildChain(
                rootNotBeforeOffsetDays: -30,
                intermediateNotBeforeOffsetDays: -20,
                leafNotBeforeOffsetDays: -5);
            using CertificateCollection chainCollection = chain.ToCollection();

            // Store all 3 chain members with no limit.
            await store.AddRejectedAsync(chainCollection, maxCertificates: 0).ConfigureAwait(false);

            // Enforce limit of 1: only the newest (leaf, NotBefore -5d) should survive.
            await store.AddRejectedAsync(new CertificateCollection(), maxCertificates: 1).ConfigureAwait(false);

            using CertificateCollection foundRoot = await store.FindByThumbprintAsync(chain.Root.Thumbprint).ConfigureAwait(false);
            Assert.That(foundRoot, Is.Empty,
                "Root CA (oldest NotBefore -30d) must be trimmed first.");

            using CertificateCollection foundIntermediate = await store.FindByThumbprintAsync(chain.Intermediate.Thumbprint).ConfigureAwait(false);
            Assert.That(foundIntermediate, Is.Empty,
                "Intermediate CA (NotBefore -20d) must also be trimmed.");

            using CertificateCollection foundLeaf = await store.FindByThumbprintAsync(chain.Leaf.Thumbprint).ConfigureAwait(false);
            Assert.That(foundLeaf, Has.Count.EqualTo(1),
                "Leaf (newest, NotBefore -5d) must survive the trim.");

            using CertificateCollection remaining = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(remaining, Has.Count.EqualTo(1),
                "Store must be trimmed to exactly maxCertificates=1.");

            chain.Dispose();
        }

        /// <summary>
        /// Verify that a subsequent call with a lower <c>maxCertificates</c> correctly adds
        /// the new certificate and trims the store to the new limit in a single operation.
        /// </summary>
        [Test]
        public async Task AddRejectedAsyncSubsequentCallTrimsStoreToNewLimitAsync()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("Custom named X509 stores are not supported on macOS.");
            }

            using var store = new X509CertificateStore(m_telemetry);
            store.Open(kCustomStorePath);

            using Certificate cert1 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -30); // oldest
            using Certificate cert2 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -20);
            using Certificate cert3 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -10);

            // Populate store (3 certs, no limit).
            using CertificateCollection firstThree = new() { cert1, cert2, cert3 };
            await store.AddRejectedAsync(firstThree, maxCertificates: 0).ConfigureAwait(false);

            // Add a 4th cert with maxCertificates=2:
            //   - cert4 is processed (processed loop)
            //   - store now holds 4 certs; excess = 4-2 = 2
            //   - trim removes cert1 and cert2 (the 2 oldest by NotBefore)
            using Certificate cert4 = CreatePublicOnlyCertificate(kLeafSubject, notBeforeOffsetDays: -1); // newest
            using CertificateCollection fourth = new() { cert4 };
            await store.AddRejectedAsync(fourth, maxCertificates: 2).ConfigureAwait(false);

            using CertificateCollection foundCert1 = await store.FindByThumbprintAsync(cert1.Thumbprint).ConfigureAwait(false);
            Assert.That(foundCert1, Is.Empty, "cert1 (oldest, NotBefore -30d) must be trimmed.");

            using CertificateCollection foundCert2 = await store.FindByThumbprintAsync(cert2.Thumbprint).ConfigureAwait(false);
            Assert.That(foundCert2, Is.Empty, "cert2 (NotBefore -20d) must be trimmed.");

            using CertificateCollection foundCert3 = await store.FindByThumbprintAsync(cert3.Thumbprint).ConfigureAwait(false);
            Assert.That(foundCert3, Has.Count.EqualTo(1), "cert3 (NotBefore -10d) should survive.");

            using CertificateCollection foundCert4 = await store.FindByThumbprintAsync(cert4.Thumbprint).ConfigureAwait(false);
            Assert.That(foundCert4, Has.Count.EqualTo(1), "cert4 (newest, just added) should survive.");

            using CertificateCollection remaining = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(remaining, Has.Count.EqualTo(2),
                "Store must be trimmed to exactly maxCertificates=2.");
        }

        /// <summary>
        /// Represents a three-certificate chain: leaf → intermediate CA → root CA.
        /// All certificates are public-key only (no private key), matching what
        /// <see cref="CertificateValidator"/> stores in the rejected store.
        /// </summary>
        private sealed class ChainFixture : IDisposable
        {
            /// <summary>Leaf certificate (public key only), signed by Intermediate.</summary>
            public Certificate Leaf { get; init; }

            /// <summary>Intermediate CA certificate (public key only), signed by Root.</summary>
            public Certificate Intermediate { get; init; }

            /// <summary>Root CA certificate (public key only, self-signed).</summary>
            public Certificate Root { get; init; }

            /// <summary>
            /// Returns the collection in validator order: leaf first, then issuers.
            /// The caller owns the returned <see cref="CertificateCollection"/> and must dispose it.
            /// </summary>
            public CertificateCollection ToCollection()
            {
                return new CertificateCollection { Leaf, Intermediate, Root };
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                Leaf?.Dispose();
                Intermediate?.Dispose();
                Root?.Dispose();
            }
        }

        /// <summary>
        /// Builds a three-certificate chain with controllable <c>NotBefore</c> offsets.
        /// All returned certificates are public-key only.
        /// </summary>
        private static ChainFixture BuildChain(
            int notBeforeOffsetDays = -5,
            int? rootNotBeforeOffsetDays = null,
            int? intermediateNotBeforeOffsetDays = null,
            int? leafNotBeforeOffsetDays = null)
        {
            int rootOffset = rootNotBeforeOffsetDays ?? notBeforeOffsetDays - 10;
            int intermediateOffset = intermediateNotBeforeOffsetDays ?? notBeforeOffsetDays - 5;
            int leafOffset = leafNotBeforeOffsetDays ?? notBeforeOffsetDays;

            DateTime rootNotBefore = DateTime.UtcNow.AddDays(rootOffset);
            DateTime intermediateNotBefore = DateTime.UtcNow.AddDays(intermediateOffset);
            DateTime leafNotBefore = DateTime.UtcNow.AddDays(leafOffset);

            // Root CA: self-signed, longest validity so it is never in-flight expired.
            using Certificate rootWithKey = s_factory
                .CreateCertificate(kRootCaSubject)
                .SetNotBefore(rootNotBefore)
                .SetNotAfter(rootNotBefore.AddYears(10))
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .SetCAConstraint()
                .CreateForRSA();

            // Intermediate CA: signed by root, path length 0 (signs leaf only).
            using Certificate intermediateWithKey = s_factory
                .CreateCertificate(kIntermediateCaSubject)
                .SetNotBefore(intermediateNotBefore)
                .SetNotAfter(intermediateNotBefore.AddYears(5))
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .SetCAConstraint(pathLengthConstraint: 0)
                .SetIssuer(rootWithKey)
                .CreateForRSA();

            // Leaf: signed by the intermediate CA.
            using Certificate leafWithKey = s_factory
                .CreateCertificate(kLeafSubject)
                .SetNotBefore(leafNotBefore)
                .SetNotAfter(leafNotBefore.AddMonths(12))
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .SetIssuer(intermediateWithKey)
                .CreateForRSA();

            // Strip private keys: the validator stores public-key-only chain members.
            return new ChainFixture
            {
                Leaf = Certificate.FromRawData(leafWithKey.RawData),
                Intermediate = Certificate.FromRawData(intermediateWithKey.RawData),
                Root = Certificate.FromRawData(rootWithKey.RawData)
            };
        }

        /// <summary>
        /// Builds a root CA certificate retaining its private key (needed to sign children).
        /// </summary>
        private static Certificate BuildRootCa(int notBeforeOffsetDays)
        {
            DateTime notBefore = DateTime.UtcNow.AddDays(notBeforeOffsetDays);
            return s_factory
                .CreateCertificate(kRootCaSubject)
                .SetNotBefore(notBefore)
                .SetNotAfter(notBefore.AddYears(10))
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .SetCAConstraint()
                .CreateForRSA();
        }

        /// <summary>
        /// Builds a leaf certificate signed by <paramref name="issuer"/>, retaining the
        /// private key (e.g. to validate the <c>NoPrivateKeys</c> stripping behaviour).
        /// </summary>
        private static Certificate BuildLeafSignedBy(
            Certificate issuer,
            int notBeforeOffsetDays)
        {
            DateTime notBefore = DateTime.UtcNow.AddDays(notBeforeOffsetDays);
            return s_factory
                .CreateCertificate(kLeafSubject)
                .SetNotBefore(notBefore)
                .SetNotAfter(notBefore.AddMonths(12))
                .SetHashAlgorithm(HashAlgorithmName.SHA256)
                .SetIssuer(issuer)
                .CreateForRSA();
        }

        /// <summary>
        /// Creates a public-key-only certificate with a controlled
        /// <see cref="Certificate.NotBefore"/> so that ordering in trim tests is
        /// fully deterministic.
        /// </summary>
        private static Certificate CreatePublicOnlyCertificate(
            string subject,
            int notBeforeOffsetDays)
        {
            DateTime notBefore = DateTime.UtcNow.AddDays(notBeforeOffsetDays);
            using Certificate cert = s_factory
                .CreateCertificate(subject)
                .SetNotBefore(notBefore)
                .SetNotAfter(notBefore.AddMonths(12))
                .CreateForRSA();

            return Certificate.FromRawData(cert.RawData);
        }
    }
}
