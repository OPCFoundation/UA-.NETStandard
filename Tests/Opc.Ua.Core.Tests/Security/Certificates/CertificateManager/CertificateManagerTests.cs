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
using System.Runtime.InteropServices;
using System.Threading;
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

            using CertificateEntryCollection snapshot = manager.SnapshotApplicationCertificates();
            Assert.That(snapshot, Has.Count.EqualTo(1));
            Assert.That(
                snapshot[0].CertificateType,
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

            using CertificateEntry entry = manager.AcquireApplicationCertificateBySecurityPolicy(
                SecurityPolicies.Basic256Sha256);

            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.Certificate.Thumbprint, Is.EqualTo(cert.Thumbprint));
        }

        [Test]
        public async Task AcquireApplicationCertificateBySecurityPolicyReturnsCallerOwnedEntry()
        {
            using var manager = new CertificateManager(m_telemetry);
            using Certificate cert = CertificateBuilder
                .Create("CN=OwnershipTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            await manager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                cert).ConfigureAwait(false);

            // Disposing an acquired entry must not affect the manager's own
            // registered certificate.
            using (CertificateEntry first = manager.AcquireApplicationCertificateBySecurityPolicy(
                SecurityPolicies.Basic256Sha256))
            {
                Assert.That(first, Is.Not.Null);
            }

            // A subsequent acquire still returns a usable certificate.
            using CertificateEntry second = manager.AcquireApplicationCertificateBySecurityPolicy(
                SecurityPolicies.Basic256Sha256);
            Assert.That(second, Is.Not.Null);
            Assert.That(second.Certificate.Thumbprint, Is.EqualTo(cert.Thumbprint));
            Assert.That(second.Certificate.RawData, Is.EqualTo(cert.RawData));
        }

        [Test]
        public async Task AcquireApplicationCertificateByTypeReturnsEntryOrNull()
        {
            using var manager = new CertificateManager(m_telemetry);
            using Certificate cert = CertificateBuilder
                .Create("CN=ByTypeTest")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            await manager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                cert).ConfigureAwait(false);

            // A matching type returns a caller-owned entry.
            using (CertificateEntry found = manager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType))
            {
                Assert.That(found, Is.Not.Null);
                Assert.That(found.Certificate.Thumbprint, Is.EqualTo(cert.Thumbprint));
            }

            // A type that is not registered returns null.
            CertificateEntry missing = manager.AcquireApplicationCertificateByType(
                ObjectTypeIds.EccNistP256ApplicationCertificateType);
            Assert.That(missing, Is.Null);
        }

        [Test]
        public void AcquireAndSnapshotReturnNullOrEmptyWhenNoCertificates()
        {
            using var manager = new CertificateManager(m_telemetry);

            Assert.That(
                manager.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256),
                Is.Null);
            Assert.That(
                manager.AcquireApplicationCertificateByType(
                    ObjectTypeIds.RsaSha256ApplicationCertificateType),
                Is.Null);

            using CertificateEntryCollection snapshot = manager.SnapshotApplicationCertificates();
            Assert.That(snapshot, Has.Count.EqualTo(0));
        }

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
        public async Task WriteTrustListAsyncNotifiesTrustListUpdatedAsync()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            var received = new List<CertificateChangeEvent>();
            using IDisposable subscription = manager.CertificateChanges.Subscribe(
                new TestObserver<CertificateChangeEvent>(received.Add));

            using Certificate cert = CertificateBuilder
                .Create("CN=Trusted")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var data = new TrustListData
            {
                TrustedCertificates = [cert]
            };

            await manager.WriteTrustListAsync(
                TrustListIdentifier.Peers,
                data,
                TrustListMasks.TrustedCertificates).ConfigureAwait(false);

            Assert.That(received, Has.Some.Matches<CertificateChangeEvent>(
                evt => evt.Kind == CertificateChangeKind.TrustListUpdated &&
                    evt.TrustList == TrustListIdentifier.Peers));
            Assert.That(received, Has.None.Matches<CertificateChangeEvent>(
                evt => evt.Kind == CertificateChangeKind.CrlUpdated));
        }

        [Test]
        public async Task WriteTrustListAsyncDoesNotNotifyWhenMaskExcludesEverythingAsync()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            var received = new List<CertificateChangeEvent>();
            using IDisposable subscription = manager.CertificateChanges.Subscribe(
                new TestObserver<CertificateChangeEvent>(received.Add));

            using var data = new TrustListData();
            await manager.WriteTrustListAsync(
                TrustListIdentifier.Peers,
                data,
                TrustListMasks.None).ConfigureAwait(false);

            Assert.That(received, Is.Empty);
        }

        [Test]
        public async Task TrustListTransactionCommitNotifiesAsync()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            var received = new List<CertificateChangeEvent>();
            using IDisposable subscription = manager.CertificateChanges.Subscribe(
                new TestObserver<CertificateChangeEvent>(received.Add));

            using Certificate cert = CertificateBuilder
                .Create("CN=TxTrusted")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            ITrustListTransaction tx = await manager
                .BeginUpdateAsync(TrustListIdentifier.Peers)
                .ConfigureAwait(false);
            try
            {
                await tx.AddTrustedCertificateAsync(cert).ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            finally
            {
                await tx.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(received, Has.Some.Matches<CertificateChangeEvent>(
                evt => evt.Kind == CertificateChangeKind.TrustListUpdated &&
                    evt.TrustList == TrustListIdentifier.Peers));
        }

        [Test]
        public async Task TrustListTransactionAbandonedDoesNotNotifyAsync()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            var received = new List<CertificateChangeEvent>();
            using IDisposable subscription = manager.CertificateChanges.Subscribe(
                new TestObserver<CertificateChangeEvent>(received.Add));

            using Certificate cert = CertificateBuilder
                .Create("CN=Abandoned")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            ITrustListTransaction tx = await manager
                .BeginUpdateAsync(TrustListIdentifier.Peers)
                .ConfigureAwait(false);
            await tx.AddTrustedCertificateAsync(cert).ConfigureAwait(false);
            // Disposing without committing must NOT emit a change event.
            await tx.DisposeAsync().ConfigureAwait(false);

            Assert.That(received, Is.Empty);
        }

        [Test]
        public async Task TrustListTransactionRemoveTrustedEmitsTrustListUpdatedAsync()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=ToRemove")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using (ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(cert).ConfigureAwait(false);
            }

            var received = new List<CertificateChangeEvent>();
            using IDisposable subscription = manager.CertificateChanges.Subscribe(
                new TestObserver<CertificateChangeEvent>(received.Add));

            ITrustListTransaction tx = await manager
                .BeginUpdateAsync(TrustListIdentifier.Peers)
                .ConfigureAwait(false);
            try
            {
                await tx.RemoveTrustedCertificateAsync(cert.Thumbprint).ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            finally
            {
                await tx.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(received, Has.Some.Matches<CertificateChangeEvent>(
                evt => evt.Kind == CertificateChangeKind.TrustListUpdated));
        }

        [Test]
        public async Task TrustListTransactionAddIssuerEmitsTrustListUpdatedAsync()
        {
            string trustedPath = CreateTempDir();
            string issuerPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath, issuerPath);

            var received = new List<CertificateChangeEvent>();
            using IDisposable subscription = manager.CertificateChanges.Subscribe(
                new TestObserver<CertificateChangeEvent>(received.Add));

            using Certificate issuer = CertificateBuilder
                .Create("CN=Issuer")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();

            ITrustListTransaction tx = await manager
                .BeginUpdateAsync(TrustListIdentifier.Peers)
                .ConfigureAwait(false);
            try
            {
                await tx.AddIssuerCertificateAsync(issuer).ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            finally
            {
                await tx.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(received, Has.Some.Matches<CertificateChangeEvent>(
                evt => evt.Kind == CertificateChangeKind.TrustListUpdated));
        }

        [Test]
        public async Task TrustListTransactionAddCrlEmitsCrlUpdatedAsync()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate issuer = CertificateBuilder
                .Create("CN=CrlIssuer")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // AddCRLAsync requires the issuer cert to already be in the
            // store to validate the CRL signature.
            using (ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(issuer).ConfigureAwait(false);
            }

            var crlBuilder = CrlBuilder.Create(issuer.SubjectName);
            X509CRL crl = new(crlBuilder.CreateForRSA(issuer));

            var received = new List<CertificateChangeEvent>();
            using IDisposable subscription = manager.CertificateChanges.Subscribe(
                new TestObserver<CertificateChangeEvent>(received.Add));

            ITrustListTransaction tx = await manager
                .BeginUpdateAsync(TrustListIdentifier.Peers)
                .ConfigureAwait(false);
            try
            {
                await tx.AddCrlAsync(crl).ConfigureAwait(false);
                await tx.CommitAsync().ConfigureAwait(false);
            }
            finally
            {
                await tx.DisposeAsync().ConfigureAwait(false);
            }

            Assert.That(received, Has.Some.Matches<CertificateChangeEvent>(
                evt => evt.Kind == CertificateChangeKind.CrlUpdated));
        }

        [Test]
        public async Task WriteTrustListAsyncIssuerCertificatesMaskEmitsTrustListUpdatedAsync()
        {
            string trustedPath = CreateTempDir();
            string issuerPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath, issuerPath);

            var received = new List<CertificateChangeEvent>();
            using IDisposable subscription = manager.CertificateChanges.Subscribe(
                new TestObserver<CertificateChangeEvent>(received.Add));

            using Certificate issuer = CertificateBuilder
                .Create("CN=WriteIssuer")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var data = new TrustListData
            {
                IssuerCertificates = [issuer]
            };

            await manager.WriteTrustListAsync(
                TrustListIdentifier.Peers,
                data,
                TrustListMasks.IssuerCertificates).ConfigureAwait(false);

            Assert.That(received, Has.Some.Matches<CertificateChangeEvent>(
                evt => evt.Kind == CertificateChangeKind.TrustListUpdated));
        }

        [Test]
        public async Task WriteTrustListAsyncTrustedCrlsMaskEmitsCrlUpdatedAsync()
        {
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate issuer = CertificateBuilder
                .Create("CN=WriteCrlIssuer")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();

            // The CRL store also requires the issuer cert to be present
            // so the CRL signature can be verified on add.
            using (ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(issuer).ConfigureAwait(false);
            }

            var crlBuilder = CrlBuilder.Create(issuer.SubjectName);
            X509CRL crl = new(crlBuilder.CreateForRSA(issuer));

            var received = new List<CertificateChangeEvent>();
            using IDisposable subscription = manager.CertificateChanges.Subscribe(
                new TestObserver<CertificateChangeEvent>(received.Add));

            using var data = new TrustListData
            {
                TrustedCrls = [crl]
            };

            await manager.WriteTrustListAsync(
                TrustListIdentifier.Peers,
                data,
                TrustListMasks.TrustedCrls).ConfigureAwait(false);

            Assert.That(received, Has.Some.Matches<CertificateChangeEvent>(
                evt => evt.Kind == CertificateChangeKind.CrlUpdated));
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

            // The validator reuses (caches) the trust-list store across
            // validations, so the parsed trust material it materialises is
            // owned by the store cache and released when the manager (and the
            // underlying validation core) is disposed. Dispose the manager
            // before the leak assertion so the check still verifies that no
            // Certificate instance is orphaned once everything is torn down.
            manager.Dispose();

            long createdDelta = Certificate.InstancesCreated - createdBefore;
            long disposedDelta = Certificate.InstancesDisposed - disposedBefore;
            Assert.That(disposedDelta, Is.EqualTo(createdDelta),
                "Every Certificate instance materialised during GetIssuersAsync " +
                "must be disposable by the caller (no orphaned refcount).");
        }

        /// <summary>
        /// Regression test for issue #3896: a server whose CA is in the
        /// <c>TrustedIssuerCertificates</c> store (not the trusted-peer store)
        /// must still emit the full chain. <see cref="CertificateManager.LoadApplicationCertificatesAsync"/>
        /// has to resolve the issuer chain from the issuer store on its own —
        /// no manual injection — so that <see cref="CertificateEntry.GetEncodedChainBlob"/>
        /// returns the legacy <c>leaf || issuers</c> blob byte-for-byte.
        /// </summary>
        [Test]
        public async Task SendCertificateChainBlobResolvesFullChainFromIssuerStore()
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

            // Persist the leaf cert into an application-certificate store so the
            // resolver can find it during LoadApplicationCertificatesAsync.
            string leafStorePath = CreateTempDir();
            await leaf.AddToStoreAsync(
                CertificateStoreType.Directory,
                leafStorePath,
                password: null,
                m_telemetry).ConfigureAwait(false);

            // Place the CA in the *issuer* store only (NOT the trusted-peer
            // store) — the exact setup described in issue #3896.
            string trustedPeerPath = CreateTempDir();
            string issuerStorePath = CreateTempDir();
            using (var rootCaPublic = Certificate.FromRawData(rootCa.RawData))
            {
                await rootCaPublic.AddToStoreAsync(
                    CertificateStoreType.Directory,
                    issuerStorePath,
                    password: null,
                    m_telemetry).ConfigureAwait(false);
            }

            var leafCertId = new CertificateIdentifier
            {
                Thumbprint = leaf.Thumbprint,
                SubjectName = leaf.Subject,
                StoreType = CertificateStoreType.Directory,
                StorePath = leafStorePath,
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
            };

            var secConfig = new SecurityConfiguration
            {
                ApplicationCertificates = [leafCertId],
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = trustedPeerPath
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = issuerStorePath
                },
                SendCertificateChain = true
            };

            using var manager = new CertificateManager(m_telemetry);
            manager.MapFromSecurityConfiguration(secConfig);
            await manager.LoadApplicationCertificatesAsync(secConfig).ConfigureAwait(false);

            // The load path must resolve the issuer chain from the issuer store
            // on its own — previously the entry was built with an empty issuer
            // chain so the blob regressed to leaf-only.
            using CertificateEntry entry = manager.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256);
            Assert.That(entry, Is.Not.Null);
            Assert.That(
                entry.IssuerChain,
                Has.Count.EqualTo(1),
                "LoadApplicationCertificatesAsync must resolve the issuer chain " +
                "from the issuer store (CA is not in the trusted-peer store).");

            // Full chain: blob == leaf raw bytes followed by root raw bytes.
            byte[] fullChain = entry.GetEncodedChainBlob();
            Assert.That(fullChain, Is.Not.Null);
            byte[] expectedFull = new byte[leaf.RawData.Length + rootCa.RawData.Length];
            Buffer.BlockCopy(leaf.RawData, 0, expectedFull, 0, leaf.RawData.Length);
            Buffer.BlockCopy(rootCa.RawData, 0, expectedFull, leaf.RawData.Length, rootCa.RawData.Length);
            Assert.That(fullChain, Is.EqualTo(expectedFull),
                "CertificateEntry.GetEncodedChainBlob must produce the legacy " +
                "DER-encoded chain blob (leaf || issuers) byte-for-byte.");
        }

        /// <summary>
        /// When the issuing CA is present in neither the trusted-peer nor the
        /// issuer store, the chain cannot be resolved and
        /// <see cref="CertificateEntry.GetEncodedChainBlob"/> must fall
        /// back to a leaf-only blob without throwing.
        /// </summary>
        [Test]
        public async Task LoadApplicationCertificatesProducesLeafOnlyBlobWhenIssuerUnavailable()
        {
            using Certificate rootCa = CertificateBuilder
                .Create("CN=MissingIssuerRoot, O=OPC Foundation")
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=MissingIssuerLeaf, O=OPC Foundation")
                .SetIssuer(rootCa)
                .SetRSAKeySize(2048)
                .CreateForRSA();

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

            // Both trust stores are configured but empty: the CA lives in
            // neither, so issuer resolution finds nothing.
            var secConfig = new SecurityConfiguration
            {
                ApplicationCertificates = [leafCertId],
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = CreateTempDir()
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = CreateTempDir()
                },
                SendCertificateChain = true
            };

            using var manager = new CertificateManager(m_telemetry);
            manager.MapFromSecurityConfiguration(secConfig);
            await manager.LoadApplicationCertificatesAsync(secConfig).ConfigureAwait(false);

            using CertificateEntry entry = manager.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry.IssuerChain, Is.Empty);

            byte[] blob = entry.GetEncodedChainBlob();
            Assert.That(
                blob,
                Is.EqualTo(leaf.RawData),
                "When the issuer cannot be resolved the chain blob must be leaf-only.");
        }

        /// <summary>
        /// Issuer-chain resolution must not swallow caller-requested
        /// cancellation: a cancelled token has to surface as an
        /// <see cref="OperationCanceledException"/> so callers can abort (e.g.
        /// responsive shutdown) instead of silently registering a leaf-only
        /// chain.
        /// </summary>
        [Test]
        public void UpdateApplicationCertificatePropagatesCancellation()
        {
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, CreateTempDir());

            // A CA-signed (non-self-signed) leaf forces issuer resolution to
            // walk the chain, where the cancelled token is observed.
            using Certificate ca = CertificateBuilder
                .Create("CN=CancelRoot, O=OPC Foundation")
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=CancelLeaf, O=OPC Foundation")
                .SetIssuer(ca)
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await manager.UpdateApplicationCertificateAsync(
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    leaf,
                    issuerChain: null,
                    cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
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

        [Test]
        public async Task ConcurrentValidationsReturnConsistentResultsAsync()
        {
            // Part A regression: the validator no longer serializes validations
            // behind a shared lock, so many concurrent validations must still
            // produce results identical to serial validation.
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate trusted = CertificateBuilder
                .Create("CN=ConcurrentTrusted")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate untrusted = CertificateBuilder
                .Create("CN=ConcurrentUntrusted")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using (ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(trusted).ConfigureAwait(false);
            }

            const int parallelism = 64;
            var tasks = new Task[parallelism];
            int trustedValid = 0;
            int untrustedInvalid = 0;

            for (int i = 0; i < parallelism; i++)
            {
                bool useTrusted = (i % 2) == 0;
                tasks[i] = Task.Run(async () =>
                {
                    Certificate subject = useTrusted ? trusted : untrusted;
                    using var chain = new CertificateCollection { subject };
                    CertificateValidationResult result = await manager
                        .ValidateAsync(chain, TrustListIdentifier.Peers)
                        .ConfigureAwait(false);

                    if (useTrusted && result.IsValid)
                    {
                        Interlocked.Increment(ref trustedValid);
                    }
                    else if (!useTrusted && !result.IsValid)
                    {
                        Interlocked.Increment(ref untrustedInvalid);
                    }
                });
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            Assert.That(trustedValid, Is.EqualTo(parallelism / 2),
                "Every concurrent validation of the trusted certificate must succeed.");
            Assert.That(untrustedInvalid, Is.EqualTo(parallelism / 2),
                "Every concurrent validation of the untrusted certificate must fail.");
        }

        [Test]
        public async Task TrustedCertificateAddedAfterWarmupIsObservedAsync()
        {
            // Part B regression: the validator caches and reuses the trust-list
            // store across validations. A certificate added to the store after
            // the cache is warmed must still be observed on the next validation
            // (the store's freshness check detects the new file).
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=LateTrusted")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var chain = new CertificateCollection { cert };

            // Warm the cache while the certificate is NOT trusted.
            CertificateValidationResult before = await manager
                .ValidateAsync(chain, TrustListIdentifier.Peers)
                .ConfigureAwait(false);
            Assert.That(before.IsValid, Is.False);

            // Add the certificate to the trusted store out of band.
            using (ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers))
            {
                await store.AddAsync(cert).ConfigureAwait(false);
            }

            // The next validation must observe the newly trusted certificate.
            CertificateValidationResult after = await manager
                .ValidateAsync(chain, TrustListIdentifier.Peers)
                .ConfigureAwait(false);
            Assert.That(after.IsValid, Is.True,
                "A certificate added to the trusted store after the cache was " +
                "warmed must be observed on the next validation.");
        }

        [Test]
        public async Task TrustedCertificateInjectedViaIndependentStoreIsObservedAsync()
        {
            // Part B: a certificate injected into the trusted directory by a
            // store instance NOT associated with the CertificateManager (e.g.
            // another process writing the PKI) must be observed by the validator
            // once the cache has been warmed.
            string trustedPath = CreateTempDir();
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, trustedPath);

            using Certificate cert = CertificateBuilder
                .Create("CN=IndependentlyTrusted")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            using var chain = new CertificateCollection { cert };

            CertificateValidationResult before = await manager
                .ValidateAsync(chain, TrustListIdentifier.Peers)
                .ConfigureAwait(false);
            Assert.That(before.IsValid, Is.False);

            // Inject the certificate through a separate DirectoryCertificateStore
            // instance the manager knows nothing about.
            using (var independentStore = new DirectoryCertificateStore(m_telemetry))
            {
                independentStore.Open(trustedPath);
                await independentStore.AddAsync(cert).ConfigureAwait(false);
            }

            CertificateValidationResult after = await manager
                .ValidateAsync(chain, TrustListIdentifier.Peers)
                .ConfigureAwait(false);
            Assert.That(after.IsValid, Is.True,
                "A certificate injected via an independent store instance must " +
                "be observed by the validator.");
        }

        [Test]
        public async Task TrustedCertificateInjectedViaIndependentX509StoreIsObservedAsync()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Assert.Ignore("X509 store tests are not run on macOS.");
            }

            // Part B (X509 variant): a certificate injected into an X509 (OS)
            // trusted store out of band must be observed by the validator.
            // X509CertificateStore re-reads the OS store on every enumeration,
            // so reuse never serves a stale view.
            const string x509StorePath = "CurrentUser\\My";
            using var manager = new CertificateManager(m_telemetry);
            manager.RegisterTrustList(TrustListIdentifier.Peers, x509StorePath);

            using Certificate cert = CertificateBuilder
                .Create("CN=IndependentlyTrustedX509")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using var publicKey = Certificate.FromRawData(cert.RawData);

            using var chain = new CertificateCollection { publicKey };
            try
            {
                CertificateValidationResult before = await manager
                    .ValidateAsync(chain, TrustListIdentifier.Peers)
                    .ConfigureAwait(false);
                Assert.That(before.IsValid, Is.False);

                // Inject the certificate directly into the OS X509 store.
                await publicKey.AddToStoreAsync(
                    CertificateStoreType.X509Store,
                    x509StorePath,
                    telemetry: m_telemetry).ConfigureAwait(false);

                CertificateValidationResult after = await manager
                    .ValidateAsync(chain, TrustListIdentifier.Peers)
                    .ConfigureAwait(false);
                Assert.That(after.IsValid, Is.True,
                    "A certificate injected into the X509 store must be observed " +
                    "by the validator.");
            }
            finally
            {
                using var cleanup = new X509CertificateStore(m_telemetry);
                cleanup.Open(x509StorePath);
                await cleanup.DeleteAsync(publicKey.Thumbprint).ConfigureAwait(false);
            }
        }

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
    }
}
