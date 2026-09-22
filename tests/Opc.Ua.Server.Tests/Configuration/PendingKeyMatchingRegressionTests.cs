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
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Core.TestFramework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Configuration
{
    /// <summary>
    /// Verifies matching, exclusive claims, and recovery of pending keys across certificate store implementations.
    /// </summary>
    [TestFixture]
    public sealed class PendingKeyMatchingRegressionTests
    {
        [Test]
        public async Task MatchingPeekRetainsAnIndependentlyOwnedPendingKeyAsync(
            [Values("memory", "directory", "hardware")] string kind)
        {
            await using var harness = new StoreHarness(kind);
            using Certificate pending = harness.CreateKey("peek");
            using Certificate wrong = harness.CreateKey("wrong");
            using Certificate matchingPublic = CreateUploadCertificate(pending);
            using Certificate wrongPublic = CreateUploadCertificate(wrong);
            using Certificate empty = await harness.Store.TryPeekMatchingAsync(harness.Context, matchingPublic)
                .ConfigureAwait(false);
            Assert.That(empty, Is.Null);
            Assert.That(await harness.Store.SaveAsync(harness.Context, pending).ConfigureAwait(false), Is.True);
            using Certificate rejected = await harness.Store.TryPeekMatchingAsync(harness.Context, wrongPublic)
                .ConfigureAwait(false);
            Assert.That(rejected, Is.Null);
            using (Certificate peeked = await harness.Store.TryPeekMatchingAsync(harness.Context, matchingPublic)
                .ConfigureAwait(false))
            {
                Assert.That(peeked.Thumbprint, Is.EqualTo(pending.Thumbprint));
                AssertKeyWorks(peeked);
            }
            using Certificate taken = await harness.CreateReplica()
                .TryTakeMatchingAsync(harness.Context, matchingPublic).ConfigureAwait(false);
            Assert.That(taken.Thumbprint, Is.EqualTo(pending.Thumbprint));
            AssertKeyWorks(taken);
            using Certificate consumed = await harness.Store.TryPeekMatchingAsync(harness.Context, matchingPublic)
                .ConfigureAwait(false);
            Assert.That(consumed, Is.Null);
        }

        [Test]
        public async Task CancelledPeekDoesNotConsumeThePendingKeyAsync(
            [Values("memory", "directory", "hardware")] string kind)
        {
            await using var harness = new StoreHarness(kind);
            using Certificate pending = harness.CreateKey("cancel-peek");
            using Certificate matchingPublic = CreateUploadCertificate(pending);
            Assert.That(await harness.Store.SaveAsync(harness.Context, pending).ConfigureAwait(false), Is.True);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.That(() => harness.Store.TryPeekMatchingAsync(
                harness.Context, matchingPublic, cancelled.Token).AsTask(),
                Throws.InstanceOf<OperationCanceledException>());
            using Certificate retained = await harness.Store.TryTakeMatchingAsync(harness.Context, matchingPublic)
                .ConfigureAwait(false);
            Assert.That(retained, Is.Not.Null);
            AssertKeyWorks(retained);
        }

        [Test]
        public async Task DirectoryPeekDeclinesAnUnsupportedBaseStoreWithoutOpeningItAsync()
        {
            var context = new PendingCertificateKeyContext(
                new CertificateStoreIdentifier("unused-platform-store", CertificateStoreType.X509Store, false),
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                ObjectTypeIds.RsaSha256ApplicationCertificateType, null, NUnitTelemetryContext.Create());
            using Certificate certificate = DefaultCertificateFactory.Instance.CreateCertificate("CN=No Pending Store")
                .CreateForRSA();
            using Certificate pending = await new DirectoryPendingCertificateKeyStore()
                .TryPeekMatchingAsync(context, certificate).ConfigureAwait(false);
            Assert.That(pending, Is.Null);
        }

        /// <summary>
        /// Verifies that a rejected certificate leaves the pending key available for one matching claim by a replica.
        /// </summary>
        [Test]
        public async Task RejectedMatchSurvivesAnotherStoreInstanceAndIsConsumedOnceAsync(
            [Values("memory", "directory", "hardware")] string kind)
        {
            await using var harness = new StoreHarness(kind);
            using Certificate pending = harness.CreateKey("pending");
            using Certificate wrong = harness.CreateKey("wrong");
            using Certificate matchingPublic = CreateUploadCertificate(pending);
            using Certificate wrongPublic = CreateUploadCertificate(wrong);
            Assert.That(await harness.Store.SaveAsync(harness.Context, pending).ConfigureAwait(false), Is.True);
            using Certificate rejected = await harness.Store.TryTakeMatchingAsync(harness.Context, wrongPublic)
                .ConfigureAwait(false);
            Assert.That(rejected, Is.Null);
            IMatchingPendingCertificateKeyStore replica = harness.CreateReplica();
            using Certificate taken = await replica.TryTakeMatchingAsync(harness.Context, matchingPublic).ConfigureAwait(false);
            Assert.That(taken, Is.Not.Null);
            Assert.That(taken.Thumbprint, Is.EqualTo(pending.Thumbprint));
            AssertKeyWorks(taken);
            using Certificate again = await replica.TryTakeMatchingAsync(harness.Context, matchingPublic).ConfigureAwait(false);
            Assert.That(again, Is.Null);
        }

        /// <summary>
        /// Verifies that restoring a failed claim succeeds only when no newer pending key has replaced it.
        /// </summary>
        [Test]
        public async Task RestoringFailedClaimNeverOverwritesANewerSigningKeyAsync(
            [Values("memory", "directory", "hardware")] string kind,
            [Values] bool replace)
        {
            await using var harness = new StoreHarness(kind);
            using Certificate original = harness.CreateKey("original");
            using Certificate newer = harness.CreateKey("newer");
            using Certificate matchingPublic = CreateUploadCertificate(original);
            Assert.That(await harness.Store.SaveAsync(harness.Context, original).ConfigureAwait(false), Is.True);
            using Certificate taken = await harness.Store.TryTakeMatchingAsync(harness.Context, matchingPublic)
                .ConfigureAwait(false);
            Assert.That(taken, Is.Not.Null);
            if (replace)
            {
                Assert.That(await harness.CreateReplica().SaveAsync(harness.Context, newer).ConfigureAwait(false), Is.True);
            }
            bool restored = await harness.Store.TryRestoreAsync(harness.Context, taken).ConfigureAwait(false);
            Assert.That(restored, Is.EqualTo(!replace));
            using Certificate final = await harness.Store.TryTakeAsync(harness.Context).ConfigureAwait(false);
            Assert.That(final.Thumbprint, Is.EqualTo(replace ? newer.Thumbprint : original.Thumbprint));
            AssertKeyWorks(final);
        }

        /// <summary>
        /// Verifies that concurrent matching claims transfer the usable private key to exactly one caller.
        /// </summary>
        [Test]
        public async Task ConcurrentMatchingClaimsHaveExactlyOneOwnerAsync(
            [Values("memory", "directory", "hardware")] string kind)
        {
            await using var harness = new StoreHarness(kind);
            using Certificate pending = harness.CreateKey("race");
            using Certificate matchingPublic = CreateUploadCertificate(pending);
            Assert.That(await harness.Store.SaveAsync(harness.Context, pending).ConfigureAwait(false), Is.True);
            Task<Certificate> first = harness.Store.TryTakeMatchingAsync(harness.Context, matchingPublic).AsTask();
            Task<Certificate> second = harness.CreateReplica().TryTakeMatchingAsync(harness.Context, matchingPublic).AsTask();
            Certificate[] claimed = await Task.WhenAll(first, second).ConfigureAwait(false);
            try
            {
                Assert.That(claimed.Count(value => value != null), Is.EqualTo(1));
                AssertKeyWorks(claimed.Single(value => value != null));
            }
            finally
            {
                foreach (Certificate certificate in claimed)
                {
                    certificate?.Dispose();
                }
            }
        }

        /// <summary>
        /// Verifies that cancellation before a claim does not consume or damage the pending private key.
        /// </summary>
        [Test]
        public async Task CancelledClaimLeavesThePendingKeyUntouchedAsync(
            [Values("memory", "directory", "hardware")] string kind)
        {
            await using var harness = new StoreHarness(kind);
            using Certificate pending = harness.CreateKey("cancel");
            using Certificate matchingPublic = CreateUploadCertificate(pending);
            Assert.That(await harness.Store.SaveAsync(harness.Context, pending).ConfigureAwait(false), Is.True);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.That(() => harness.Store.TryTakeMatchingAsync(harness.Context, matchingPublic, cancellation.Token).AsTask(),
                Throws.InstanceOf<OperationCanceledException>());
            using Certificate final = await harness.Store.TryTakeMatchingAsync(harness.Context, matchingPublic).ConfigureAwait(false);
            Assert.That(final, Is.Not.Null);
            AssertKeyWorks(final);
        }

        /// <summary>
        /// Creates a certificate containing the pending key's public key without attaching its private key.
        /// </summary>
        private static Certificate CreateUploadCertificate(Certificate pending)
        {
            using RSA publicKey = pending.GetRSAPublicKey();
            using RSA privateKey = pending.GetRSAPrivateKey();
            var signature = X509SignatureGenerator.CreateForRSA(privateKey, RSASignaturePadding.Pkcs1);
            return CertificateBuilder.Create(pending.Subject).SetRSAPublicKey(publicKey).CreateForRSA(signature);
        }

        /// <summary>
        /// Verifies that a claimed certificate still has a private key capable of producing a valid signature.
        /// </summary>
        private static void AssertKeyWorks(Certificate certificate)
        {
            using RSA privateKey = certificate.GetRSAPrivateKey();
            using RSA publicKey = certificate.GetRSAPublicKey();
            byte[] hash = new byte[32];
            byte[] signature = privateKey.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.That(publicKey.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);
        }

        /// <summary>
        /// Owns an isolated pending-key store and supplies replicas that share its backing storage.
        /// </summary>
        private sealed class StoreHarness : IAsyncDisposable
        {
            /// <summary>
            /// Creates an isolated in-memory, directory, or simulated hardware store for matching-key tests.
            /// </summary>
            public StoreHarness(string kind)
            {
                m_kind = kind;
                m_directory = Path.Combine(Path.GetTempPath(), "pending-match-" + Guid.NewGuid().ToString("N"));
                string path = m_directory;
                string storeType = CertificateStoreType.Directory;
                if (kind == "hardware")
                {
                    m_hardware = new SimulatedHardwareCertificateStoreProvider();
                    path = SimulatedHardwareCertificateStore.StoreScheme + "token/" + Guid.NewGuid().ToString("N");
                    storeType = SimulatedHardwareCertificateStore.StoreTypeName;
                }
                Context = new PendingCertificateKeyContext(
                    new CertificateStoreIdentifier(path, storeType, false),
                    ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType, null, NUnitTelemetryContext.Create());
                Store = kind switch
                {
                    "hardware" => new HardwarePendingCertificateKeyStore(m_hardware),
                    "directory" => new DirectoryPendingCertificateKeyStore(),
                    _ => new InMemoryPendingCertificateKeyStore()
                };
            }

            /// <summary>
            /// Gets the certificate group, type, and backing-store identity used by all claims.
            /// </summary>
            public PendingCertificateKeyContext Context { get; }

            /// <summary>
            /// Gets the store under test for saving and atomically claiming pending keys.
            /// </summary>
            public IPeekablePendingCertificateKeyStore Store { get; }

            /// <summary>
            /// Creates another store over the same backing storage, or reuses the shared in-memory store.
            /// </summary>
            public IPeekablePendingCertificateKeyStore CreateReplica()
            {
                return m_kind switch
                {
                    "hardware" => new HardwarePendingCertificateKeyStore(m_hardware),
                    "directory" => new DirectoryPendingCertificateKeyStore(),
                    _ => Store
                };
            }

            /// <summary>
            /// Creates an RSA certificate whose private key is owned by the selected storage provider.
            /// </summary>
            public Certificate CreateKey(string name)
            {
                return m_hardware != null
                    ? m_hardware.GetStore(Context.BaseStore.StorePath).CreateRsaCertificate("CN=" + name)
                    : DefaultCertificateFactory.Instance.CreateCertificate("CN=" + name).CreateForRSA();
            }

            /// <summary>
            /// Removes the pending key and releases the hardware provider and temporary directory.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                await Store.RemoveAsync(Context).ConfigureAwait(false);
                m_hardware?.Dispose();
                if (Directory.Exists(m_directory))
                {
                    Directory.Delete(m_directory, true);
                }
            }

            /// <summary>
            /// Selects the in-memory, directory, or hardware-backed pending-key implementation.
            /// </summary>
            private readonly string m_kind;

            /// <summary>
            /// Locates the isolated backing store shared by replicas in a test.
            /// </summary>
            private readonly string m_directory;

            /// <summary>
            /// Retains the simulated hardware key provider until test cleanup completes.
            /// </summary>
            private readonly SimulatedHardwareCertificateStoreProvider m_hardware;
        }
    }
}
