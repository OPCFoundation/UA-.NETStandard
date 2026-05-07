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

        #endregion

        #region Certificate Registry

        [Test]
        public async Task LoadApplicationCertificatesLoadsFromConfig()
        {
            using Certificate cert = CertificateBuilder
                .Create("CN=TestApp, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var certId = new CertificateIdentifier(cert)
            {
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

        #endregion

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

        #endregion

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
        public async Task RejectCertificateAsyncEnqueuesSuccessfully()
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
        }

        #endregion

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

        #endregion

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

            ICertificateRegistry registry = manager;
            var issuers = new List<CertificateIssuerReference>();
            bool isTrusted = await registry.GetIssuersAsync(cert, issuers).ConfigureAwait(false);

            Assert.That(issuers, Is.Empty);
            Assert.That(isTrusted, Is.False);
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

            using var leafCertId = new CertificateIdentifier(leaf)
            {
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

        #endregion

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

        #endregion
    }
}
