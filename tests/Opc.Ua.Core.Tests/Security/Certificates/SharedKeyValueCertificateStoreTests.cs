// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2000: test code; decoded certificates are ownership-transferred to the returned collections or are
// short-lived, making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    /// <summary>
    /// Tests for <see cref="SharedKeyValueCertificateStore"/> and
    /// <see cref="SharedKeyValueCertificateStoreProvider"/>.
    /// </summary>
    [TestFixture]
    [Category("SharedKeyValueCertificateStore")]
    [Parallelizable(ParallelScope.All)]
    public class SharedKeyValueCertificateStoreTests
    {
        private const string StorePath = "kv:pki/trusted";
        private const string IssuerStorePath = "kv:pki/issuer";

        private static ITelemetryContext Telemetry => NUnitTelemetryContext.Create();

        private static Certificate CreateCertificate(string subject)
        {
            using Certificate cert = CertificateBuilder
                .Create(subject)
                .SetLifeTime(365)
                .CreateForRSA();
            return Certificate.FromRawData(cert.RawData);
        }

        private static SharedKeyValueCertificateStore CreateStore(
            ISharedKeyValueStore backend,
            IRecordProtector? protector = null,
            string path = StorePath)
        {
            var store = new SharedKeyValueCertificateStore(backend, protector, Telemetry);
            store.Open(path);
            return store;
        }

        private static async Task<int> CountEntriesAsync(InMemorySharedKeyValueStore backend, string keyPrefix)
        {
            int count = 0;
            await foreach (KeyValuePair<string, ByteString> _ in backend.ScanAsync(keyPrefix).ConfigureAwait(false))
            {
                count++;
            }

            return count;
        }

        [Test]
        public void PropertiesReportPublicOnlyStore()
        {
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateStore(backend);

            Assert.That(store.StoreType, Is.EqualTo(CertificateStoreType.SharedKeyValue));
            Assert.That(store.StorePath, Is.EqualTo(StorePath));
            Assert.That(store.NoPrivateKeys, Is.True);
            Assert.That(store.SupportsLoadPrivateKey, Is.False);
            Assert.That(store.SupportsCRLs, Is.True);
        }

        [Test]
        public async Task AddAndEnumerateRoundTripAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateStore(backend);
            using Certificate certificate = CreateCertificate("CN=KvRoundTrip");

            await store.AddAsync(certificate).ConfigureAwait(false);

            using CertificateCollection certificates = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(certificates, Has.Count.EqualTo(1));
            Assert.That(certificates[0].Thumbprint, Is.EqualTo(certificate.Thumbprint));
            Assert.That(certificates[0].RawData, Is.EqualTo(certificate.RawData));
        }

        [Test]
        public async Task FindByThumbprintReturnsMatchAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateStore(backend);
            using Certificate certificate = CreateCertificate("CN=KvFind");
            await store.AddAsync(certificate).ConfigureAwait(false);

            using CertificateCollection found = await store
                .FindByThumbprintAsync(certificate.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(found, Has.Count.EqualTo(1));
            Assert.That(found[0].Thumbprint, Is.EqualTo(certificate.Thumbprint));

            using CertificateCollection missing = await store
                .FindByThumbprintAsync("0000000000000000000000000000000000000000")
                .ConfigureAwait(false);
            Assert.That(missing, Has.Count.Zero);
        }

        [Test]
        public async Task DeleteRemovesCertificateAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateStore(backend);
            using Certificate certificate = CreateCertificate("CN=KvDelete");
            await store.AddAsync(certificate).ConfigureAwait(false);

            Assert.That(await store.DeleteAsync(certificate.Thumbprint).ConfigureAwait(false), Is.True);
            using CertificateCollection remaining = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(remaining, Has.Count.Zero);
            Assert.That(await store.DeleteAsync(certificate.Thumbprint).ConfigureAwait(false), Is.False);
        }

        [Test]
        public async Task AddDuplicateThrowsAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateStore(backend);
            using Certificate certificate = CreateCertificate("CN=KvDuplicate");
            await store.AddAsync(certificate).ConfigureAwait(false);

            Assert.That(
                async () => await store.AddAsync(certificate).ConfigureAwait(false),
                Throws.ArgumentException);
        }

        [Test]
        public async Task CertificateAddedOnOneReplicaIsVisibleOnAnotherAsync()
        {
            // Two stores over ONE shared backend model two replicas sharing a
            // trust list: a certificate trusted on replica A is trusted on B.
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore replicaA = CreateStore(backend);
            using SharedKeyValueCertificateStore replicaB = CreateStore(backend);
            using Certificate certificate = CreateCertificate("CN=KvShared");

            await replicaA.AddAsync(certificate).ConfigureAwait(false);

            using CertificateCollection onB = await replicaB.EnumerateAsync().ConfigureAwait(false);
            Assert.That(onB, Has.Count.EqualTo(1));
            Assert.That(onB[0].Thumbprint, Is.EqualTo(certificate.Thumbprint));

            await replicaA.DeleteAsync(certificate.Thumbprint).ConfigureAwait(false);
            using CertificateCollection afterDelete = await replicaB.EnumerateAsync().ConfigureAwait(false);
            Assert.That(afterDelete, Has.Count.Zero);
        }

        /// <summary>
        /// An authenticated freshness view is required even if a raw backend has a protector.
        /// </summary>
        [Test]
        public void RawSharedBackendIsRejected()
        {
            ISharedKeyValueStore backend = new Mock<ISharedKeyValueStore>(MockBehavior.Strict).Object;
            using AesCbcHmacRecordProtector protector = CreateProtector();

            Assert.That(() => new SharedKeyValueCertificateStore(backend, protector, Telemetry),
                Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("store"));
            Assert.That(() => new SharedKeyValueCertificateStoreProvider(backend, protector),
                Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("store"));
        }

        /// <summary>
        /// Neither an omitted nor a no-op protector is accepted for distributed certificate records.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FreshnessViewRequiresAuthenticatingCertificateProtector(bool explicitNullProtector)
        {
            using var stores = new ProtectedStores();
            IRecordProtector? protector = explicitNullProtector ? NullRecordProtector.Instance : null;

            Assert.That(() => new SharedKeyValueCertificateStore(stores.View, protector, Telemetry),
                Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("protector"));
            Assert.That(() => new SharedKeyValueCertificateStoreProvider(stores.View, protector),
                Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("protector"));
        }

        /// <summary>
        /// The distributed provider uses the configured freshness view, without enrolling it automatically.
        /// </summary>
        [Test]
        public async Task InjectedProviderRequiresEnrollmentAndDoesNotReadLegacyRecordsAsync()
        {
            using var stores = new ProtectedStores();
            using SharedKeyValueCertificateStore legacy = CreateStore(stores.Payload, stores.Protector);
            using Certificate certificate = CertificateBuilder.Create("CN=KvLegacy").SetCAConstraint().CreateForRSA();
            await legacy.AddAsync(certificate).ConfigureAwait(false);
            var crl = new X509CRL(CrlBuilder.Create(certificate.SubjectName).CreateForRSA(certificate));
            await legacy.AddCRLAsync(crl).ConfigureAwait(false);

            var services = new ServiceCollection();
            stores.RegisterFreshness(services);
            services.AddOpcUa().AddSharedKeyValueCertificateStore();
            using ServiceProvider provider = services.BuildServiceProvider();
            ICertificateStoreProvider storeProvider = provider.GetRequiredService<ICertificateStoreProvider>();
            using ICertificateStore store = storeProvider.CreateStore(Telemetry);
            store.Open(StorePath);

            Assert.That(() => store.FindByThumbprintAsync(certificate.Thumbprint),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.Code))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));

            await provider.GetRequiredService<FreshnessProtectedKeyValueStore>().EnrollAsync().ConfigureAwait(false);
            using CertificateCollection absent = await store.FindByThumbprintAsync(certificate.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(absent, Is.Empty);
            Assert.That(await store.EnumerateCRLsAsync().ConfigureAwait(false), Is.Empty);

            await store.AddAsync(certificate).ConfigureAwait(false);
            using CertificateCollection found = await store.FindByThumbprintAsync(certificate.Thumbprint)
                .ConfigureAwait(false);
            Assert.That(found.Single().Thumbprint, Is.EqualTo(certificate.Thumbprint));
        }

        /// <summary>
        /// A committed but unavailable certificate is not an absent certificate or a partial list.
        /// </summary>
        [Test]
        public async Task UnavailableCommittedCertificateFailsLookupEnumerationAndValidationAsync()
        {
            using var stores = new ProtectedStores();
            await stores.View.EnrollAsync().ConfigureAwait(false);
            using SharedKeyValueCertificateStore writer = CreateStore(stores.View, stores.Protector);
            using Certificate certificate = CreateCertificate("CN=KvMissingCertificate");
            using Certificate other = CreateCertificate("CN=KvAvailableCertificate");
            await writer.AddAsync(other).ConfigureAwait(false);
            string payloadKey = await stores.CommitAndGetPayloadKeyAsync(() => writer.AddAsync(certificate))
                .ConfigureAwait(false);
            await using CertificateManager manager = CreateManager(stores, autoAccept: true);
            using var chain = new CertificateCollection { certificate };
            var options = new ValidationOptions { RecordRejectedCertificates = false };
            Assert.That((await manager.ValidateAsync(chain, options: options).ConfigureAwait(false)).IsValid, Is.True);

            stores.MarkPayloadUnavailable(payloadKey);

            using SharedKeyValueCertificateStore reader = CreateStore(stores.View, stores.Protector);
            Assert.That(() => reader.FindByThumbprintAsync(certificate.Thumbprint),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.Code))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(() => reader.EnumerateAsync(),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.Code))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
            CertificateValidationResult result = await manager.ValidateAsync(chain, options: options)
                .ConfigureAwait(false);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.IsSuppressible, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        /// <summary>
        /// An authority outage is not ordinary untrusted status, even when auto-acceptance is enabled.
        /// </summary>
        [Test]
        public async Task AuthorityOutageInvalidatesCertificateValidationAsync()
        {
            using var stores = new ProtectedStores();
            await stores.View.EnrollAsync().ConfigureAwait(false);
            using Certificate certificate = CreateCertificate("CN=KvAuthorityUnavailable");
            using SharedKeyValueCertificateStore writer = CreateStore(stores.View, stores.Protector);
            await writer.AddAsync(certificate).ConfigureAwait(false);
            await using CertificateManager manager = CreateManager(stores, autoAccept: true);
            using var chain = new CertificateCollection { certificate };
            var options = new ValidationOptions { RecordRejectedCertificates = false };
            Assert.That((await manager.ValidateAsync(chain, options: options).ConfigureAwait(false)).IsValid, Is.True);

            stores.MarkAuthorityUnavailable();

            CertificateValidationResult result = await manager.ValidateAsync(chain, options: options)
                .ConfigureAwait(false);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.IsSuppressible, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        /// <summary>
        /// A previous successful validation must not override a committed removal from the trust list.
        /// </summary>
        [Test]
        public async Task CommittedCertificateDeletionInvalidatesCachedTrustAsync()
        {
            using var stores = new ProtectedStores();
            await stores.View.EnrollAsync().ConfigureAwait(false);
            using SharedKeyValueCertificateStore writer = CreateStore(stores.View, stores.Protector);
            using Certificate certificate = CreateCertificate("CN=KvRemoved");
            await writer.AddAsync(certificate).ConfigureAwait(false);
            await using CertificateManager manager = CreateManager(stores);
            using var chain = new CertificateCollection { certificate };
            var options = new ValidationOptions { RecordRejectedCertificates = false };
            Assert.That((await manager.ValidateAsync(chain, options: options).ConfigureAwait(false)).IsValid, Is.True);

            Assert.That(await writer.DeleteAsync(certificate.Thumbprint).ConfigureAwait(false), Is.True);

            CertificateValidationResult removed = await manager.ValidateAsync(chain, options: options)
                .ConfigureAwait(false);
            Assert.That(removed.IsValid, Is.False);
            Assert.That(removed.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
        }

        /// <summary>
        /// Data that is readable at the freshness layer but not authenticated as certificates or CRLs is rejected.
        /// </summary>
        [Test]
        public async Task UnverifiableCommittedRecordsAreRejectedAsync()
        {
            using var stores = new ProtectedStores();
            await stores.View.EnrollAsync().ConfigureAwait(false);
            using SharedKeyValueCertificateStore writer = CreateStore(stores.View, stores.Protector);
            using Certificate issuer = CertificateBuilder.Create("CN=KvProtectedRecord")
                .SetCAConstraint()
                .CreateForRSA();
            await writer.AddAsync(issuer).ConfigureAwait(false);
            var crl = new X509CRL(CrlBuilder.Create(issuer.SubjectName).CreateForRSA(issuer));
            await writer.AddCRLAsync(crl).ConfigureAwait(false);

            using AesCbcHmacRecordProtector otherProtector = CreateProtector();
            using SharedKeyValueCertificateStore reader = CreateStore(stores.View, otherProtector);

            Assert.That(() => reader.FindByThumbprintAsync(issuer.Thumbprint),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.Code))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(() => reader.EnumerateCRLsAsync(),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.Code))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        /// <summary>
        /// A complete CRL in one store cannot make revocation succeed when the other store is unavailable.
        /// </summary>
        [Test]
        public async Task MissingCommittedCrlBlocksRevocationAcrossTrustStoresAsync()
        {
            using var stores = new ProtectedStores();
            await stores.View.EnrollAsync().ConfigureAwait(false);
            using SharedKeyValueCertificateStore trusted = CreateStore(stores.View, stores.Protector);
            using SharedKeyValueCertificateStore issuers = CreateStore(stores.View, stores.Protector, IssuerStorePath);
            using Certificate issuer = CertificateBuilder.Create("CN=KvCrlStores").SetCAConstraint().CreateForRSA();
            using Certificate leaf = CertificateBuilder.Create("CN=KvCrlStoresLeaf").SetIssuer(issuer).CreateForRSA();
            await trusted.AddAsync(issuer).ConfigureAwait(false);
            await issuers.AddAsync(issuer).ConfigureAwait(false);
            var crl = new X509CRL(CrlBuilder.Create(issuer.SubjectName).CreateForRSA(issuer));
            await trusted.AddCRLAsync(crl).ConfigureAwait(false);
            string payloadKey = await stores.CommitAndGetPayloadKeyAsync(() => issuers.AddCRLAsync(crl))
                .ConfigureAwait(false);
            await using CertificateManager manager = CreateManager(stores, includeIssuerStore: true);
            using var chain = new CertificateCollection { leaf };
            var options = new ValidationOptions { RecordRejectedCertificates = false };
            Assert.That((await manager.ValidateAsync(chain, options: options).ConfigureAwait(false)).IsValid, Is.True);

            stores.MarkPayloadUnavailable(payloadKey);

            Assert.That(() => issuers.IsRevokedAsync(issuer, leaf),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.Code))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
            CertificateValidationResult result = await manager.ValidateAsync(chain, options: options)
                .ConfigureAwait(false);
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.IsSuppressible, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task AddRejectedTrimsToMaximumAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateStore(backend);

            for (int i = 0; i < 5; i++)
            {
                using Certificate certificate = CreateCertificate($"CN=KvRejected{i}");
                using var collection = new CertificateCollection { certificate };
                await store.AddRejectedAsync(collection, maxCertificates: 3).ConfigureAwait(false);
            }

            using CertificateCollection rejected = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(rejected, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task AddRejectedWithNegativeMaximumKeepsNothingAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateStore(backend);
            using Certificate certificate = CreateCertificate("CN=KvRejectedNone");
            using var collection = new CertificateCollection { certificate };

            await store.AddRejectedAsync(collection, maxCertificates: -1).ConfigureAwait(false);

            using CertificateCollection rejected = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(rejected, Has.Count.Zero);
        }

        [Test]
        public async Task AddRejectedTrimRemovesCorruptEntriesAsync()
        {
            var protector = new MarkerRecordProtector();
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateStore(backend, protector);

            for (int i = 0; i < 3; i++)
            {
                using Certificate certificate = CreateCertificate($"CN=KvRejectedValid{i}");
                using var collection = new CertificateCollection { certificate };
                await store.AddRejectedAsync(collection, maxCertificates: 3).ConfigureAwait(false);
            }

            const string corruptKey = StorePath + "/cert/corrupt";
            await backend.SetAsync(corruptKey, new ByteString(new byte[] { 1, 2, 3, 4, 5 })).ConfigureAwait(false);

            using (Certificate certificate = CreateCertificate("CN=KvRejectedValid3"))
            using (var collection = new CertificateCollection { certificate })
            {
                await store.AddRejectedAsync(collection, maxCertificates: 3).ConfigureAwait(false);
            }

            Assert.That(await CountEntriesAsync(backend, StorePath + "/cert/").ConfigureAwait(false), Is.EqualTo(3));
            (bool corruptFound, ByteString _) = await backend.TryGetAsync(corruptKey).ConfigureAwait(false);
            Assert.That(corruptFound, Is.False);
            using CertificateCollection rejected = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(rejected, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task CrlAddEnumerateDeleteRoundTripAsync()
        {
            using var stores = new ProtectedStores();
            await stores.View.EnrollAsync().ConfigureAwait(false);
            using SharedKeyValueCertificateStore store = CreateStore(stores.View, stores.Protector);
            using Certificate issuer = CertificateBuilder
                .Create("CN=KvCrlIssuer")
                .SetCAConstraint()
                .SetRSAKeySize(2048)
                .CreateForRSA();
            using Certificate leaf = CertificateBuilder
                .Create("CN=KvCrlLeaf")
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();
            await store.AddAsync(issuer).ConfigureAwait(false);

            var crl = new X509CRL(CrlBuilder
                .Create(issuer.SubjectName)
                .AddRevokedCertificate(leaf)
                .CreateForRSA(issuer));
            await store.AddCRLAsync(crl).ConfigureAwait(false);

            X509CRLCollection crls = await store.EnumerateCRLsAsync().ConfigureAwait(false);
            Assert.That(crls, Has.Count.EqualTo(1));

            X509CRLCollection issuerCrls = await store
                .EnumerateCRLsAsync(issuer, validateUpdateTime: false)
                .ConfigureAwait(false);
            Assert.That(issuerCrls, Has.Count.EqualTo(1));

            // A CRL shared through the store revokes the leaf on every replica.
            StatusCode revoked = await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false);
            Assert.That(revoked.Code, Is.EqualTo(StatusCodes.BadCertificateRevoked));

            Assert.That(await store.DeleteCRLAsync(crl).ConfigureAwait(false), Is.True);
            X509CRLCollection empty = await store.EnumerateCRLsAsync().ConfigureAwait(false);
            Assert.That(empty, Has.Count.Zero);
            Assert.That(await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false),
                Is.EqualTo(StatusCodes.BadCertificateRevocationUnknown));
        }

        /// <summary>
        /// A same-name CRL with an unverifiable signature only belongs to another issuer if its key identifies one.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SameNameCrlRequiresAnIdentifiableIssuerAsync(bool identifyOtherIssuer)
        {
            using var stores = new ProtectedStores();
            await stores.View.EnrollAsync().ConfigureAwait(false);
            using SharedKeyValueCertificateStore store = CreateStore(stores.View, stores.Protector);
            using Certificate issuer = CertificateBuilder.Create("CN=KvSameName").SetCAConstraint().CreateForRSA();
            using Certificate other = CertificateBuilder.Create("CN=KvSameName").SetCAConstraint().CreateForRSA();
            using Certificate leaf = CertificateBuilder.Create("CN=KvSameNameLeaf").SetIssuer(issuer).CreateForRSA();
            await store.AddAsync(issuer).ConfigureAwait(false);
            await store.AddAsync(other).ConfigureAwait(false);
            var crl = new X509CRL(CrlBuilder.Create(issuer.SubjectName).CreateForRSA(issuer));
            await store.AddCRLAsync(crl).ConfigureAwait(false);
            CrlBuilder otherCrl = CrlBuilder.Create(other.SubjectName);
            if (identifyOtherIssuer)
            {
                otherCrl.AddCRLExtension(other.BuildAuthorityKeyIdentifier());
            }
            await store.AddCRLAsync(new X509CRL(otherCrl.CreateForRSA(other))).ConfigureAwait(false);

            if (identifyOtherIssuer)
            {
                Assert.That(await store.IsRevokedAsync(issuer, leaf).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.Good));
            }
            else
            {
                Assert.That(() => store.IsRevokedAsync(issuer, leaf),
                    Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadSecurityChecksFailed));
            }
        }

        [Test]
        public async Task LoadPrivateKeyReturnsNullAsync()
        {
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateStore(backend);
            using Certificate certificate = CreateCertificate("CN=KvNoKey");
            await store.AddAsync(certificate).ConfigureAwait(false);

            Certificate? key = await store
                .LoadPrivateKeyAsync(
                    certificate.Thumbprint, null, null, NodeId.Null, null)
                .ConfigureAwait(false);
            Assert.That(key, Is.Null);
        }

        [Test]
        public void ProviderResolvesStoreForSchemePath()
        {
            using var backend = new InMemorySharedKeyValueStore();
            var provider = new SharedKeyValueCertificateStoreProvider(backend);

            Assert.That(provider.StoreTypeName, Is.EqualTo(CertificateStoreType.SharedKeyValue));
            Assert.That(provider.SupportsStorePath("kv:pki/trusted"), Is.True);
            Assert.That(provider.SupportsStorePath("/tmp/pki/trusted"), Is.False);

            using ICertificateStore store = provider.CreateStore(Telemetry);
            Assert.That(store, Is.InstanceOf<SharedKeyValueCertificateStore>());
        }

        [Test]
        public void ConstructorThrowsOnNullStore()
        {
            Assert.That(
                () => new SharedKeyValueCertificateStore(null!, null, Telemetry),
                Throws.ArgumentNullException);
            Assert.That(
                () => new SharedKeyValueCertificateStoreProvider(null!),
                Throws.ArgumentNullException);
        }

        private static CertificateManager CreateManager(
            ProtectedStores stores,
            bool autoAccept = false,
            bool includeIssuerStore = false)
        {
            var configuration = new SecurityConfiguration
            {
                UseValidatedCertificates = true,
                AutoAcceptUntrustedCertificates = autoAccept,
                RejectUnknownRevocationStatus = false
            };
            return CertificateManagerFactory.Create(configuration, Telemetry, options =>
            {
                options.AddStoreProvider(new SharedKeyValueCertificateStoreProvider(stores.View, stores.Protector));
                options.AddTrustList(TrustListIdentifier.Peers.Name, StorePath,
                    includeIssuerStore ? IssuerStorePath : null);
            });
        }

        private static AesCbcHmacRecordProtector CreateProtector()
        {
            using var random = RandomNumberGenerator.Create();
            var key = new byte[32];
            random.GetBytes(key);
            var protector = new AesCbcHmacRecordProtector(key);
            Array.Clear(key, 0, key.Length);
            return protector;
        }

        private sealed class ProtectedStores : IDisposable
        {
            public ProtectedStores()
            {
                m_payload.Setup(store => store.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string key, CancellationToken ct) => Payload.TryGetAsync(key, ct));
                m_payload.Setup(store => store.SetAsync(
                        It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Returns((string key, ByteString value, CancellationToken ct) => Payload.SetAsync(key, value, ct));

                Mock<ISharedKeyValueStoreConsistency> consistency = m_authority.As<ISharedKeyValueStoreConsistency>();
                consistency.Setup(store => store.IsLinearizable(It.IsAny<string>())).Returns(true);
                consistency.Setup(store => store.IsProcessLocal(It.IsAny<string>())).Returns(false);
                m_authority.Setup(store => store.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string key, CancellationToken ct) => m_authorityData.TryGetAsync(key, ct));
                m_authority.Setup(store => store.CompareAndSwapAsync(
                        It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                    .Returns((string key, ByteString expected, ByteString value, CancellationToken ct) =>
                        m_authorityData.CompareAndSwapAsync(key, expected, value, ct));
                m_authority.Setup(store => store.ScanAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string prefix, CancellationToken ct) => m_authorityData.ScanAsync(prefix, ct));

                View = new FreshnessProtectedKeyValueStore(
                    m_payload.Object, m_authority.Object, Protector, m_deployment, m_epoch);
            }

            public InMemorySharedKeyValueStore Payload { get; } = new();

            public AesCbcHmacRecordProtector Protector { get; } = CreateProtector();

            public FreshnessProtectedKeyValueStore View { get; }

            public void RegisterFreshness(IServiceCollection services)
            {
                services.AddSingleton(m_payload.Object);
                services.AddSingleton<IRecordProtector>(Protector);
                services.AddSharedRecordFreshness(_ => m_authority.Object, m_deployment, m_epoch);
            }

            public async Task<string> CommitAndGetPayloadKeyAsync(Func<Task> commit)
            {
                List<string> before = await ReadPayloadKeysAsync().ConfigureAwait(false);
                await commit().ConfigureAwait(false);
                List<string> after = await ReadPayloadKeysAsync().ConfigureAwait(false);
                return after.Except(before, StringComparer.Ordinal).Single();
            }

            public void MarkPayloadUnavailable(string key)
            {
                m_payload.Setup(store => store.TryGetAsync(key, It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<(bool Found, ByteString Value)>((false, default)));
            }

            public void MarkAuthorityUnavailable()
            {
                m_authority.Setup(store => store.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Throws(new TimeoutException("Certificate store test authority is unavailable."));
            }

            public void Dispose()
            {
                Protector.Dispose();
                m_authorityData.Dispose();
                Payload.Dispose();
            }

            private async Task<List<string>> ReadPayloadKeysAsync()
            {
                var keys = new List<string>();
                await foreach (KeyValuePair<string, ByteString> entry in Payload
                    .ScanAsync(string.Empty)
                    .ConfigureAwait(false))
                {
                    keys.Add(entry.Key);
                }
                return keys;
            }

            private readonly Mock<ISharedKeyValueStore> m_payload = new(MockBehavior.Strict);
            private readonly Mock<ISharedKeyValueStore> m_authority = new(MockBehavior.Strict);
            private readonly InMemorySharedKeyValueStore m_authorityData = new();
            private readonly Guid m_deployment = Guid.NewGuid();
            private readonly Guid m_epoch = Guid.NewGuid();
        }

        /// <summary>
        /// A minimal authenticating record protector: it prefixes plaintext with
        /// a fixed marker and rejects any record that does not carry the marker,
        /// so a forged record fails closed. Used to exercise the store's
        /// integrity path without a full cipher.
        /// </summary>
        private sealed class MarkerRecordProtector : IRecordProtector
        {
            public ByteString Protect(ByteString context, ByteString plaintext)
            {
                ReadOnlySpan<byte> source = plaintext.Span;
                int headerLength = s_marker.Length + sizeof(int) + context.Length;
                byte[] result = new byte[headerLength + source.Length];
                s_marker.CopyTo(result.AsSpan());
                BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(s_marker.Length), context.Length);
                context.Span.CopyTo(result.AsSpan(s_marker.Length + sizeof(int)));
                source.CopyTo(result.AsSpan(headerLength));
                return new ByteString(result);
            }

            public bool TryUnprotect(ByteString context, ByteString protectedRecord, out ByteString plaintext)
            {
                ReadOnlySpan<byte> span = protectedRecord.Span;
                int headerLength = s_marker.Length + sizeof(int) + context.Length;
                if (span.Length < headerLength ||
                    !span[..s_marker.Length].SequenceEqual(s_marker) ||
                    BinaryPrimitives.ReadInt32LittleEndian(span[s_marker.Length..]) != context.Length ||
                    !span.Slice(s_marker.Length + sizeof(int), context.Length).SequenceEqual(context.Span))
                {
                    plaintext = default;
                    return false;
                }
                plaintext = new ByteString(span[headerLength..].ToArray());
                return true;
            }

            private static readonly byte[] s_marker = [0xA5, 0x5A, 0xC3, 0x3C];
        }
    }
}
