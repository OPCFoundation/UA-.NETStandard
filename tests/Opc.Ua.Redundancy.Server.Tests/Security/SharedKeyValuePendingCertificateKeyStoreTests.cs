/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

// IDE0230: byte-array literals below are opaque binary test vectors, not text.
#pragma warning disable IDE0230 // Use UTF-8 string literal

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the distributed, shared-store-backed
    /// <see cref="SharedKeyValuePendingCertificateKeyStore"/>: cross-replica
    /// persistence of a regenerated private key, atomic consumption, scoping by
    /// certificate group/type and fail-closed record protection.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public sealed class SharedKeyValuePendingCertificateKeyStoreTests
    {
        private ITelemetryContext m_telemetry = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public async Task SaveThenTryTakeRoundTripsTheCertificateAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions());
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewCertificateWithKey();

            bool saved = await store.SaveAsync(context, original, CancellationToken.None);
            Assert.That(saved, Is.True);

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None);
            Assert.That(taken, Is.Not.Null);
            Assert.That(taken!.Thumbprint, Is.EqualTo(original.Thumbprint));
            Assert.That(taken.HasPrivateKey, Is.True);

            using Certificate? again = await store.TryTakeAsync(context, CancellationToken.None);
            Assert.That(again, Is.Null, "TryTakeAsync consumes the entry");
        }

        [Test]
        public async Task SaveThenTryTakeRoundTripsWithProtectorAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(3));
            var store = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions(), protector);
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewCertificateWithKey();

            Assert.That(await store.SaveAsync(context, original, CancellationToken.None), Is.True);

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None);
            Assert.That(taken, Is.Not.Null);
            Assert.That(taken!.Thumbprint, Is.EqualTo(original.Thumbprint));
            Assert.That(taken.HasPrivateKey, Is.True);
        }

        [Test]
        public async Task PendingKeyVisibleToAnotherReplicaSharingStoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(4));
            var onA = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions(), protector);
            var onB = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions(), protector);
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewCertificateWithKey();

            Assert.That(await onA.SaveAsync(context, original, CancellationToken.None), Is.True);

            using Certificate? takenOnB = await onB.TryTakeAsync(context, CancellationToken.None);
            Assert.That(takenOnB, Is.Not.Null, "a replica that did not run CreateSigningRequest can still consume the key");
            Assert.That(takenOnB!.Thumbprint, Is.EqualTo(original.Thumbprint));
            Assert.That(takenOnB.HasPrivateKey, Is.True);
        }

        [Test]
        public async Task SaveTwiceReplacesPreviousEntryAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions());
            PendingCertificateKeyContext context = NewContext();
            using Certificate first = NewCertificateWithKey();
            using Certificate second = NewCertificateWithKey();

            Assert.That(await store.SaveAsync(context, first, CancellationToken.None), Is.True);
            Assert.That(await store.SaveAsync(context, second, CancellationToken.None), Is.True);

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None);
            Assert.That(taken, Is.Not.Null);
            Assert.That(taken!.Thumbprint, Is.EqualTo(second.Thumbprint));
        }

        [Test]
        public async Task RemoveDiscardsPendingKeyAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions());
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewCertificateWithKey();

            Assert.That(await store.SaveAsync(context, original, CancellationToken.None), Is.True);
            await store.RemoveAsync(context, CancellationToken.None);

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None);
            Assert.That(taken, Is.Null);
        }

        [Test]
        public async Task EntriesScopedByGroupAndTypeAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions());
            PendingCertificateKeyContext contextA = NewContext();
            PendingCertificateKeyContext contextB = NewContext();
            using Certificate certA = NewCertificateWithKey();

            Assert.That(await store.SaveAsync(contextA, certA, CancellationToken.None), Is.True);

            using Certificate? fromB = await store.TryTakeAsync(contextB, CancellationToken.None);
            Assert.That(fromB, Is.Null, "a different (group, type) scope must not see another scope's pending key");

            using Certificate? fromA = await store.TryTakeAsync(contextA, CancellationToken.None);
            Assert.That(fromA, Is.Not.Null);
        }

        [Test]
        public async Task WrongProtectorKeyFailsClosedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var writer = new AesCbcHmacRecordProtector(MakeKey(7));
            using var reader = new AesCbcHmacRecordProtector(MakeKey(8));
            var saveStore = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions(), writer);
            var takeStore = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions(), reader);
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewCertificateWithKey();

            Assert.That(await saveStore.SaveAsync(context, original, CancellationToken.None), Is.True);

            using Certificate? taken = await takeStore.TryTakeAsync(context, CancellationToken.None);
            Assert.That(taken, Is.Null, "a record produced under a different key must not decrypt (fail-closed)");
        }

        [Test]
        public async Task TamperedRecordFailsClosedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(9));
            var store = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions(), protector);
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewCertificateWithKey();

            Assert.That(await store.SaveAsync(context, original, CancellationToken.None), Is.True);
            await kv.SetAsync(store.KeyFor(context), ByteString.From(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }))
                .ConfigureAwait(false);

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None);
            Assert.That(taken, Is.Null);
        }

        [Test]
        public async Task ConcurrentTryTakeConsumedByExactlyOneReplicaAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(11));
            var onA = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions(), protector);
            var onB = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions(), protector);
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewCertificateWithKey();
            Assert.That(await onA.SaveAsync(context, original, CancellationToken.None), Is.True);

            Task<Certificate?> takeA = onA.TryTakeAsync(context, CancellationToken.None).AsTask();
            Task<Certificate?> takeB = onB.TryTakeAsync(context, CancellationToken.None).AsTask();
            Certificate?[] results = await Task.WhenAll(takeA, takeB);

            int winners = (results[0] != null ? 1 : 0) + (results[1] != null ? 1 : 0);
            results[0]?.Dispose();
            results[1]?.Dispose();
            Assert.That(winners, Is.EqualTo(1), "exactly one replica may consume the pending key");
        }

        [Test]
        public async Task TryTakeWipesDecryptedBackingBufferAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var protector = new InstrumentedRecordProtector();
            var store = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions(), protector);
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewCertificateWithKey();

            Assert.That(await store.SaveAsync(context, original, CancellationToken.None), Is.True);

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None);
            Assert.That(taken, Is.Not.Null);
            Assert.That(taken!.Thumbprint, Is.EqualTo(original.Thumbprint));

            // The instrumented protector decrypts into a fresh buffer distinct
            // from the shared-store input and hands it back as the caller-owned
            // plaintext. TryTakeAsync must wipe that exact buffer.
            byte[]? decrypted = protector.LastOwnedPlaintext;
            Assert.That(decrypted, Is.Not.Null, "the protector must have decrypted the record");
            Assert.That(decrypted!, Has.Length.GreaterThan(0));
            Assert.That(Array.TrueForAll(decrypted, b => b == 0), Is.True,
                "TryTakeAsync must wipe the real protector's distinct decrypted backing buffer");
        }

        [Test]
        public async Task TryTakePreservesSharedStoreInputForPassThroughAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            // No protector => NullRecordProtector pass-through: the stored record
            // and the recovered plaintext share the same backing buffer, so
            // TryTakeAsync must never wipe (mutate) the shared-store input.
            var store = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions());
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewCertificateWithKey();

            Assert.That(await store.SaveAsync(context, original, CancellationToken.None), Is.True);

            (bool found, ByteString stored) = await kv.TryGetAsync(store.KeyFor(context));
            Assert.That(found, Is.True);
            byte[] before = stored.ToArray();
            Assert.That(Array.TrueForAll(before, b => b == 0), Is.False,
                "precondition: the stored pass-through record is non-zero");

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None);
            Assert.That(taken, Is.Not.Null);

            // The pass-through input buffer must be byte-for-byte intact.
            Assert.That(stored.ToArray(), Is.EqualTo(before),
                "TryTakeAsync must never mutate the shared-store input buffer for the pass-through protector");
        }

        [Test]
        public void KeyForScopesUnderConfiguredPrefix()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var options = new DistributedPushConfigurationOptions { KeyPrefix = "custom/" };
            var store = new SharedKeyValuePendingCertificateKeyStore(kv, options);

            string key = store.KeyFor(NewContext());

            Assert.That(key, Does.StartWith("custom/pendingkey/"));
        }

        [Test]
        public void SaveWithNullArgumentsThrows()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions());
            using Certificate cert = NewCertificateWithKey();

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await store.SaveAsync(null!, cert, CancellationToken.None));
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await store.SaveAsync(NewContext(), null!, CancellationToken.None));
        }

        [Test]
        public void ConstructorNullArgumentsThrow()
        {
            using var kv = new InMemorySharedKeyValueStore();

            Assert.That(
                () => new SharedKeyValuePendingCertificateKeyStore(null!, NewOptions()),
                Throws.ArgumentNullException);
            Assert.That(
                () => new SharedKeyValuePendingCertificateKeyStore(kv, null!),
                Throws.ArgumentNullException);
        }

        private static DistributedPushConfigurationOptions NewOptions()
        {
            return new DistributedPushConfigurationOptions { ReplicaId = "test" };
        }

        private PendingCertificateKeyContext NewContext()
        {
            return new PendingCertificateKeyContext(
                new CertificateStoreIdentifier("unused", CertificateStoreType.Directory),
                new NodeId(Guid.NewGuid(), 1),
                new NodeId(Guid.NewGuid(), 1),
                null,
                m_telemetry);
        }

        private static Certificate NewCertificateWithKey()
        {
            return CertificateBuilder
                .Create("CN=PendingKey " + Guid.NewGuid().ToString("N")[..8])
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static byte[] MakeKey(byte seed)
        {
            byte[] key = new byte[32];
            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)(seed + i);
            }
            return key;
        }

        /// <summary>
        /// A round-tripping <see cref="IRecordProtector"/> that always decrypts
        /// into a fresh buffer distinct from the protected input and retains a
        /// reference to it, so a test can assert the store wiped that exact
        /// decrypted backing buffer.
        /// </summary>
        private sealed class InstrumentedRecordProtector : IOwnedRecordProtector
        {
            private const byte Marker = 0xC3;

            public byte[]? LastOwnedPlaintext { get; private set; }

            public ByteString Protect(ByteString plaintext)
            {
                ReadOnlySpan<byte> source = plaintext.Span;
                byte[] envelope = new byte[1 + source.Length];
                envelope[0] = Marker;
                source.CopyTo(envelope.AsSpan(1));
                return new ByteString(envelope);
            }

            public bool TryUnprotect(ByteString protectedRecord, out ByteString plaintext)
            {
                if (!TryDecode(protectedRecord, out byte[] data))
                {
                    plaintext = ByteString.Empty;
                    return false;
                }
                plaintext = new ByteString(data);
                return true;
            }

            public bool TryUnprotectOwned(ByteString protectedRecord, out byte[] plaintext)
            {
                if (!TryDecode(protectedRecord, out byte[] data))
                {
                    plaintext = [];
                    return false;
                }
                // Retain the fresh decrypted buffer so the test can prove the
                // store wiped it (rather than only a downstream copy).
                LastOwnedPlaintext = data;
                plaintext = data;
                return true;
            }

            private static bool TryDecode(ByteString protectedRecord, out byte[] data)
            {
                ReadOnlySpan<byte> span = protectedRecord.Span;
                if (span.Length < 1 || span[0] != Marker)
                {
                    data = [];
                    return false;
                }
                data = span[1..].ToArray();
                return true;
            }
        }
    }
}
