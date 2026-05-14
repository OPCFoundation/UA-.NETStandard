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
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Integration tests for the <see cref="CertificateManager"/> class.
    /// </summary>
    [TestFixture]
    [Category("CertificateManager")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class CertificateManagerTests
    {
        private ITelemetryContext m_telemetry;
        private readonly List<string> m_tempDirs = [];

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

        [TearDown]
        public void TearDown()
        {
            foreach (string dir in m_tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, true);
                    }
                }
                catch (IOException)
                {
                    // best effort cleanup
                }
            }

            m_tempDirs.Clear();
        }

        #region Trust-List Registry

        [Test]
        public void RegisterTrustListAddsEntry()
        {
            using var manager = new CertificateManager(m_telemetry);
            string trustedPath = CreateTempDir();

            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            Assert.That(manager.TrustLists, Has.Count.EqualTo(1));
            Assert.That(manager.TrustLists, Does.Contain(TrustListIdentifier.Peers));
        }

        [Test]
        public void RegisterTrustListDuplicateIsNoOp()
        {
            using var manager = new CertificateManager(m_telemetry);
            string trustedPath = CreateTempDir();

            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            Assert.That(manager.TrustLists, Has.Count.EqualTo(1));
        }

        [Test]
        public void OpenTrustedStoreReturnsStore()
        {
            using var manager = new CertificateManager(m_telemetry);
            string trustedPath = CreateTempDir();
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers);

            Assert.That(store, Is.Not.Null);
        }

        [Test]
        public void OpenIssuerStoreReturnsNullWhenNoIssuerPath()
        {
            using var manager = new CertificateManager(m_telemetry);
            string trustedPath = CreateTempDir();
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            ICertificateStore store = manager.OpenIssuerStore(TrustListIdentifier.Peers);

            Assert.That(store, Is.Null);
        }

        [Test]
        public void OpenUnregisteredTrustListThrows()
        {
            using var manager = new CertificateManager(m_telemetry);

            Assert.Throws<KeyNotFoundException>(
                () => manager.OpenTrustedStore(TrustListIdentifier.Peers));
        }

        #endregion Trust-List Registry

        #region Certificate Registry

        [Test]
        public async Task LoadApplicationCertificatesLoadsFromConfig()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=TestApp, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // Persist the cert into a temp directory store so the manager
            // can load it via the resolver (no more in-memory cert cache
            // on CertificateIdentifier).
            string storePath = CreateTempDir();
            await cert.AddToStoreAsync(
                CertificateStoreType.Directory,
                storePath,
                password: null,
                m_telemetry).ConfigureAwait(false);

            var certId = new CertificateIdentifier
            {
                Thumbprint = cert.Thumbprint,
                SubjectName = cert.Subject,
                StoreType = CertificateStoreType.Directory,
                StorePath = storePath,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };

            var secConfig = new SecurityConfiguration
            {
                ApplicationCertificates = [certId]
            };

            using var manager = new CertificateManager(m_telemetry);
            await manager.LoadApplicationCertificatesAsync(secConfig).ConfigureAwait(false);

            Assert.That(manager.ApplicationCertificates, Has.Count.EqualTo(1));
            Assert.That(
                manager.ApplicationCertificates[0].CertificateType,
                Is.EqualTo(ObjectTypeIds.RsaSha256ApplicationCertificateType));
        }

        [Test]
        public async Task GetInstanceCertificateReturnsCertForPolicy()
        {
            using var manager = new CertificateManager(m_telemetry);
            using Certificate cert = CertificateBuilder
                .Create("CN=PolicyTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            await manager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                cert).ConfigureAwait(false);

            CertificateEntry entry = manager.GetInstanceCertificate(
                SecurityPolicies.Basic256Sha256);

            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.Certificate.Thumbprint, Is.EqualTo(cert.Thumbprint));
        }

        #endregion Certificate Registry

        #region Validation

        [Test]
        public async Task ValidateUntrustedCertReturnsFailure()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=Untrusted")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var certCollection = new CertificateCollection { cert };
            CertificateValidationResult result = await manager.ValidateAsync(
                certCollection,
                TrustListIdentifier.Peers).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
        }

        [Test]
        public async Task ValidateTrustedCertReturnsSuccess()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=Trusted")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using (ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(cert).ConfigureAwait(false);
            }

            using var trustedCollection = new CertificateCollection { cert };
            CertificateValidationResult result = await manager.ValidateAsync(
                trustedCollection,
                TrustListIdentifier.Peers).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.True);
        }

        #endregion Validation

        #region Lifecycle

        [Test]
        public async Task CertificateChangesNotifiesOnUpdate()
        {
            using var manager = new CertificateManager(m_telemetry);
            CertificateChangeEvent received = null;

            using IDisposable subscription = manager.CertificateChanges.Subscribe(
                new TestObserver<CertificateChangeEvent>(evt => received = evt));

            using Certificate cert = CertificateBuilder
                .Create("CN=Updated")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            await manager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                cert).ConfigureAwait(false);

            Assert.That(received, Is.Not.Null);
            Assert.That(
                received.Kind,
                Is.EqualTo(CertificateChangeKind.ApplicationCertificateUpdated));
            Assert.That(received.NewCertificate, Is.Not.Null);
        }

        [Test]
        public Task RejectCertificateAsyncEnqueuesSuccessfully()
        {
            string rejectedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Rejected, rejectedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=Rejected")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var rejectedCollection = new CertificateCollection { cert };
            Assert.DoesNotThrowAsync(async () =>
                await manager.RejectCertificateAsync(
                    rejectedCollection).ConfigureAwait(false));
            return Task.CompletedTask;
        }

        #endregion Lifecycle

        #region Factory Creation

        [Test]
        public void FactoryCreateFromSecurityConfiguration()
        {
            string peersPath = CreateTempDir();
            string usersPath = CreateTempDir();
            string rejectedPath = CreateTempDir();

            var secConfig = new SecurityConfiguration
            {
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StorePath = peersPath
                },
                TrustedUserCertificates = new CertificateTrustList
                {
                    StorePath = usersPath
                },
                RejectedCertificateStore = new CertificateStoreIdentifier(rejectedPath)
            };

            using CertificateManager manager = CertificateManagerFactory.Create(
                secConfig, m_telemetry);

            Assert.That(manager.TrustLists, Does.Contain(TrustListIdentifier.Peers));
            Assert.That(manager.TrustLists, Does.Contain(TrustListIdentifier.Users));
            Assert.That(manager.TrustLists, Does.Contain(TrustListIdentifier.Rejected));
        }

        #endregion Factory Creation

        #region Issuer Resolution and Chain Blob

        [Test]
        public async Task GetIssuersAsyncReturnsEmptyForSelfSignedCertificate()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=SelfSigned, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            CertificateManager registry = manager;
            var issuers = new List<CertificateIssuerReference>();
            bool isTrusted = await registry.GetIssuersAsync(cert, issuers).ConfigureAwait(false);

            Assert.That(issuers, Is.Empty);
            Assert.That(isTrusted, Is.False);
        }

        /// <summary>
        /// <see cref="ICertificateRegistry.GetIssuersAsync"/> appends to —
        /// rather than replaces — the caller's <c>issuers</c> list. When
        /// no new issuers are discovered (self-signed leaf), pre-populated
        /// entries must remain untouched.
        /// </summary>
        [Test]
        public async Task GetIssuersAsyncPreservesPrePopulatedListWhenNoIssuersFound()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate sentinel = CertificateBuilder
                .Create("CN=Sentinel, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using var sentinelClone = Certificate.FromRawData(sentinel.RawData);

            using Certificate selfSigned = CertificateBuilder
                .Create("CN=SelfSignedAppendCheck, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            CertificateManager registry = manager;

            // Pre-populate the list with a sentinel reference.
            var issuers = new List<CertificateIssuerReference>
            {
                new(sentinelClone, new CertificateValidationOptions())
            };

            bool isTrusted = await registry
                .GetIssuersAsync(selfSigned, issuers)
                .ConfigureAwait(false);

            Assert.That(isTrusted, Is.False);
            Assert.That(issuers, Has.Count.EqualTo(1),
                "Self-signed leaf has no issuers; the sentinel must remain.");
            Assert.That(
                issuers[0].Certificate.Thumbprint,
                Is.EqualTo(sentinel.Thumbprint),
                "Pre-populated entries must be preserved verbatim.");
        }

        /// <summary>
        /// <see cref="ICertificateRegistry.GetIssuersAsync"/> appends newly
        /// resolved issuers to the supplied list and reports the chain as
        /// trusted when an issuer is found in a registered trust list.
        /// </summary>
        [Test]
        public async Task GetIssuersAsyncAppendsResolvedIssuersToPrePopulatedList()
        {
            // Build a 2-level chain: trusted root CA + leaf signed by root.
            using Certificate rootCa = CertificateBuilder
                .Create("CN=AppendChainRoot, O=OPC Foundation")
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=AppendChainLeaf, O=OPC Foundation")
                .SetIssuer(rootCa)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // Persist the root CA into the Peers trusted store so the
            // chain walk in CertificateValidationCore.GetIssuersAsync can
            // resolve and trust it.
            string trustedPath = CreateTempDir();
            using var rootForStore = Certificate.FromRawData(rootCa.RawData);
            await rootForStore.AddToStoreAsync(
                CertificateStoreType.Directory,
                trustedPath,
                password: null,
                m_telemetry).ConfigureAwait(false);

            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate sentinel = CertificateBuilder
                .Create("CN=AppendChainSentinel, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using var sentinelClone = Certificate.FromRawData(sentinel.RawData);

            CertificateManager registry = manager;

            var issuers = new List<CertificateIssuerReference>
            {
                new(sentinelClone, new CertificateValidationOptions())
            };

            bool isTrusted = await registry
                .GetIssuersAsync(leaf, issuers)
                .ConfigureAwait(false);

            try
            {
                Assert.That(isTrusted, Is.True,
                    "Issuer is in the Peers trust list; result must be trusted.");
                Assert.That(issuers, Has.Count.EqualTo(2),
                    "Sentinel preserved + root CA appended.");
                Assert.That(issuers[0].Certificate.Thumbprint,
                    Is.EqualTo(sentinel.Thumbprint),
                    "Pre-populated sentinel must remain at index 0.");
                Assert.That(issuers[1].Certificate.Thumbprint,
                    Is.EqualTo(rootCa.Thumbprint),
                    "Newly resolved issuer must be appended at the tail.");
            }
            finally
            {
                // The new issuer reference at index 1 is caller-owned per
                // the CertificateIssuerReference contract; dispose it (the
                // sentinel at index 0 is owned by the test via the using).
                if (issuers.Count > 1)
                {
                    issuers[1].Certificate.Dispose();
                }
            }
        }

        /// <summary>
        /// Verifies that <see cref="CertificateIssuerReference.Certificate"/>
        /// instances returned by <see cref="ICertificateRegistry.GetIssuersAsync"/>
        /// follow the documented caller-owned lifetime: the test snapshots
        /// the global Certificate refcount before and after, then asserts
        /// that every certificate created during the call is disposed once
        /// the issuer references are released.
        /// </summary>
        [Test]
        [NonParallelizable]
        public async Task GetIssuersAsyncReturnedReferencesAreCallerOwnedAndDisposable()
        {
            using Certificate rootCa = CertificateBuilder
                .Create("CN=RefcountRoot, O=OPC Foundation")
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=RefcountLeaf, O=OPC Foundation")
                .SetIssuer(rootCa)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            string trustedPath = CreateTempDir();
            using var rootForStore = Certificate.FromRawData(rootCa.RawData);
            await rootForStore.AddToStoreAsync(
                CertificateStoreType.Directory,
                trustedPath,
                password: null,
                m_telemetry).ConfigureAwait(false);

            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            CertificateManager registry = manager;

            long createdBefore = Certificate.InstancesCreated;
            long disposedBefore = Certificate.InstancesDisposed;

            var issuers = new List<CertificateIssuerReference>();
            bool isTrusted = await registry
                .GetIssuersAsync(leaf, issuers)
                .ConfigureAwait(false);

            Assert.That(isTrusted, Is.True);
            Assert.That(issuers, Has.Count.EqualTo(1));

            // Dispose every reference returned by the call. The contract
            // is that the caller owns these and is responsible for disposal.
            foreach (CertificateIssuerReference reference in issuers)
            {
                reference.Certificate.Dispose();
            }

            long createdDelta = Certificate.InstancesCreated - createdBefore;
            long disposedDelta = Certificate.InstancesDisposed - disposedBefore;
            Assert.That(disposedDelta, Is.EqualTo(createdDelta),
                "Every Certificate instance materialised during GetIssuersAsync " +
                "must be disposable by the caller (no orphaned refcount).");
        }

        [Test]
        public async Task SendCertificateChainBlobMatchesLeafOrFullChain()
        {
            // Build a 2-level chain: root CA + leaf signed by root.
            using Certificate rootCa = CertificateBuilder
                .Create("CN=ChainBlobRoot, O=OPC Foundation")
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=ChainBlobLeaf, O=OPC Foundation")
                .SetIssuer(rootCa)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // Persist the leaf cert into a temp directory store so the
            // resolver can find it during LoadApplicationCertificatesAsync.
            string leafStorePath = CreateTempDir();
            await leaf.AddToStoreAsync(
                CertificateStoreType.Directory,
                leafStorePath,
                password: null,
                m_telemetry).ConfigureAwait(false);

            var leafCertId = new CertificateIdentifier
            {
                Thumbprint = leaf.Thumbprint,
                SubjectName = leaf.Subject,
                StoreType = CertificateStoreType.Directory,
                StorePath = leafStorePath,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };

            // Configure SendCertificateChain = true and load the cert with its issuer chain.
            var secConfig = new SecurityConfiguration
            {
                ApplicationCertificates = [leafCertId],
                SendCertificateChain = true
            };

            using var manager = new CertificateManager(m_telemetry);
            manager.MapFromSecurityConfiguration(secConfig);
            await manager.LoadApplicationCertificatesAsync(secConfig).ConfigureAwait(false);

            // Inject the issuer into the entry's pre-loaded chain so the registry
            // knows about it (mirrors what CheckApplicationInstanceCertificatesAsync
            // does in production).
            CertificateEntry entry = manager.GetInstanceCertificate(SecurityPolicies.Basic256Sha256);
            Assert.That(entry, Is.Not.Null);
            entry.IssuerChain.Add(rootCa);

            // Full chain: blob == leaf raw bytes followed by root raw bytes.
            byte[] fullChain = manager.LoadCertificateChainRaw(leaf);
            Assert.That(fullChain, Is.Not.Null);
            byte[] expectedFull = new byte[leaf.RawData.Length + rootCa.RawData.Length];
            Buffer.BlockCopy(leaf.RawData, 0, expectedFull, 0, leaf.RawData.Length);
            Buffer.BlockCopy(rootCa.RawData, 0, expectedFull, leaf.RawData.Length, rootCa.RawData.Length);
            Assert.That(fullChain, Is.EqualTo(expectedFull),
                "CertificateManager.LoadCertificateChainRaw must produce the legacy " +
                "DER-encoded chain blob (leaf || issuers) byte-for-byte.");

            // Leaf-only mode: blob is just the leaf's raw bytes.
            var leafOnlyConfig = new SecurityConfiguration
            {
                ApplicationCertificates = [leafCertId],
                SendCertificateChain = false
            };
            using var leafOnlyManager = new CertificateManager(m_telemetry);
            leafOnlyManager.MapFromSecurityConfiguration(leafOnlyConfig);
            Assert.That(leafOnlyManager.SendCertificateChain, Is.False);
        }

        [Test]
        public async Task UpdateAsyncReplacesTrustListPaths()
        {
            // Initial config with one trusted path.
            string oldPath = CreateTempDir();
            string newPath = CreateTempDir();

            var initial = new SecurityConfiguration
            {
                TrustedPeerCertificates = new CertificateTrustList { StorePath = oldPath }
            };

            using CertificateManager manager = CertificateManagerFactory.Create(initial, m_telemetry);

            using (ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                Assert.That(store, Is.Not.Null);
            }

            // Switch to new path via UpdateAsync.
            var updated = new SecurityConfiguration
            {
                TrustedPeerCertificates = new CertificateTrustList { StorePath = newPath }
            };

            await manager.UpdateAsync(updated).ConfigureAwait(false);

            // Manager must now serve from the new path.
            using ICertificateStore newStore = manager.OpenTrustedStore(TrustListIdentifier.Peers);
            Assert.That(newStore.StorePath, Is.EqualTo(newPath));
        }

        #endregion Issuer Resolution and Chain Blob

        #region Helpers

        private string CreateTempDir()
        {
            string dir = Path.Combine(
                Path.GetTempPath(),
                "opcua-cm-test-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(dir);
            m_tempDirs.Add(dir);
            return dir;
        }

        /// <summary>
        /// Simple observer for testing <see cref="IObservable{T}"/> subscriptions.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private sealed class TestObserver<T>(Action<T> onNext) : IObserver<T>
        {
            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
            }

            public void OnNext(T value)
            {
                onNext(value);
            }
        }

        #endregion Helpers
    }
}
