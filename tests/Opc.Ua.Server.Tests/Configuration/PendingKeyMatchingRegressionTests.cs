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
    [TestFixture]
    public sealed class PendingKeyMatchingRegressionTests
    {
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

        [Test]
        public async Task RestoringFailedClaimNeverOverwritesANewerSigningKeyAsync(
            [Values("memory", "directory", "hardware")] string kind,
            [Values(false, true)] bool replace)
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

        private static Certificate CreateUploadCertificate(Certificate pending)
        {
            using RSA publicKey = pending.GetRSAPublicKey();
            using RSA privateKey = pending.GetRSAPrivateKey();
            var signature = X509SignatureGenerator.CreateForRSA(privateKey, RSASignaturePadding.Pkcs1);
            return CertificateBuilder.Create(pending.Subject).SetRSAPublicKey(publicKey).CreateForRSA(signature);
        }

        private static void AssertKeyWorks(Certificate certificate)
        {
            using RSA privateKey = certificate.GetRSAPrivateKey();
            using RSA publicKey = certificate.GetRSAPublicKey();
            byte[] hash = new byte[32];
            byte[] signature = privateKey.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.That(publicKey.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);
        }

        private sealed class StoreHarness : IAsyncDisposable
        {
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

            public PendingCertificateKeyContext Context { get; }
            public IMatchingPendingCertificateKeyStore Store { get; }

            public IMatchingPendingCertificateKeyStore CreateReplica()
            {
                return m_kind switch
                {
                    "hardware" => new HardwarePendingCertificateKeyStore(m_hardware),
                    "directory" => new DirectoryPendingCertificateKeyStore(),
                    _ => Store
                };
            }

            public Certificate CreateKey(string name)
            {
                return m_hardware != null
                    ? m_hardware.GetStore(Context.BaseStore.StorePath).CreateRsaCertificate("CN=" + name)
                    : DefaultCertificateFactory.Instance.CreateCertificate("CN=" + name).CreateForRSA();
            }

            public async ValueTask DisposeAsync()
            {
                await Store.RemoveAsync(Context).ConfigureAwait(false);
                m_hardware?.Dispose();
                if (Directory.Exists(m_directory))
                {
                    Directory.Delete(m_directory, true);
                }
            }

            private readonly string m_kind;
            private readonly string m_directory;
            private readonly SimulatedHardwareCertificateStoreProvider m_hardware;
        }
    }
}
