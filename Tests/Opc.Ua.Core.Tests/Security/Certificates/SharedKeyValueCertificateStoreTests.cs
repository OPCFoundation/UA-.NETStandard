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

// CA2000: test code; decoded certificates are ownership-transferred to the returned collections or are
// short-lived, making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

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
            IRecordProtector? protector = null)
        {
            var store = new SharedKeyValueCertificateStore(backend, protector, Telemetry);
            store.Open(StorePath);
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

        [Test]
        public async Task TamperedRecordIsRejectedFailClosedAsync()
        {
            var protector = new MarkerRecordProtector();
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateStore(backend, protector);
            using Certificate kept = CreateCertificate("CN=KvKept");
            using Certificate tampered = CreateCertificate("CN=KvTampered");
            await store.AddAsync(kept).ConfigureAwait(false);
            await store.AddAsync(tampered).ConfigureAwait(false);

            // Forge the stored record for the tampered certificate by writing
            // bytes that do not carry the protector's authentication marker.
            string tamperedKey = StorePath + "/cert/" + tampered.Thumbprint;
            await backend
                .SetAsync(tamperedKey, new ByteString(new byte[] { 1, 2, 3, 4, 5 }))
                .ConfigureAwait(false);

            using CertificateCollection certificates = await store.EnumerateAsync().ConfigureAwait(false);
            Assert.That(certificates, Has.Count.EqualTo(1), "the forged record must fail closed");
            Assert.That(certificates[0].Thumbprint, Is.EqualTo(kept.Thumbprint));
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
            using var backend = new InMemorySharedKeyValueStore();
            using SharedKeyValueCertificateStore store = CreateStore(backend);
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

        /// <summary>
        /// A minimal authenticating record protector: it prefixes plaintext with
        /// a fixed marker and rejects any record that does not carry the marker,
        /// so a forged record fails closed. Used to exercise the store's
        /// integrity path without a full cipher.
        /// </summary>
        private sealed class MarkerRecordProtector : IRecordProtector
        {
            private static readonly byte[] s_marker = [0xA5, 0x5A, 0xC3, 0x3C];

            public ByteString Protect(ByteString plaintext)
            {
                ReadOnlySpan<byte> source = plaintext.Span;
                byte[] result = new byte[s_marker.Length + source.Length];
                s_marker.CopyTo(result.AsSpan());
                source.CopyTo(result.AsSpan(s_marker.Length));
                return new ByteString(result);
            }

            public bool TryUnprotect(ByteString protectedRecord, out ByteString plaintext)
            {
                ReadOnlySpan<byte> span = protectedRecord.Span;
                if (span.Length < s_marker.Length ||
                    !span[..s_marker.Length].SequenceEqual(s_marker))
                {
                    plaintext = ByteString.Empty;
                    return false;
                }
                plaintext = new ByteString(span[s_marker.Length..].ToArray());
                return true;
            }
        }
    }
}
