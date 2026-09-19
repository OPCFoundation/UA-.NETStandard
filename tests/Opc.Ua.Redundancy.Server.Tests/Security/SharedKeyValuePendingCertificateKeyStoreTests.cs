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
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Security.Certificates;
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

            bool saved = await store.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false);
            Assert.That(saved, Is.True);

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
            Assert.That(taken, Is.Not.Null);
            Assert.That(taken!.Thumbprint, Is.EqualTo(original.Thumbprint));
            Assert.That(taken.HasPrivateKey, Is.True);

            using Certificate? again = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
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

            Assert.That(await store.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false), Is.True);

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
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

            Assert.That(await onA.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false), Is.True);

            using Certificate? takenOnB = await onB.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
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

            Assert.That(await store.SaveAsync(context, first, CancellationToken.None).ConfigureAwait(false), Is.True);
            Assert.That(await store.SaveAsync(context, second, CancellationToken.None).ConfigureAwait(false), Is.True);

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
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

            Assert.That(await store.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false), Is.True);
            await store.RemoveAsync(context, CancellationToken.None).ConfigureAwait(false);

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
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

            Assert.That(await store.SaveAsync(contextA, certA, CancellationToken.None).ConfigureAwait(false), Is.True);

            using Certificate? fromB = await store.TryTakeAsync(contextB, CancellationToken.None).ConfigureAwait(false);
            Assert.That(fromB, Is.Null, "a different (group, type) scope must not see another scope's pending key");

            using Certificate? fromA = await store.TryTakeAsync(contextA, CancellationToken.None).ConfigureAwait(false);
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

            Assert.That(await saveStore.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false), Is.True);

            using Certificate? taken = await takeStore.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
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

            Assert.That(await store.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false), Is.True);
            await kv.SetAsync(store.KeyFor(context), ByteString.From(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }))
                .ConfigureAwait(false);

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
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
            Assert.That(await onA.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false), Is.True);

            Task<Certificate?> takeA = onA.TryTakeAsync(context, CancellationToken.None).AsTask();
            Task<Certificate?> takeB = onB.TryTakeAsync(context, CancellationToken.None).AsTask();
            Certificate?[] results = await Task.WhenAll(takeA, takeB).ConfigureAwait(false);

            int winners = (results[0] != null ? 1 : 0) + (results[1] != null ? 1 : 0);
            results[0]?.Dispose();
            results[1]?.Dispose();
            Assert.That(winners, Is.EqualTo(1), "exactly one replica may consume the pending key");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TryTakeWipesDecryptedBackingBufferAsync(bool useKeyRing)
        {
            using var kv = new InMemorySharedKeyValueStore();
            var protector = new InstrumentedRecordProtector();
            using var ring = new KeyRingRecordProtector(protector);
            var store = new SharedKeyValuePendingCertificateKeyStore(
                kv, NewOptions(), useKeyRing ? ring : protector);
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewCertificateWithKey();

            Assert.That(await store.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false), Is.True);

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
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
            Assert.That(protector.ImmutableReads, Is.Zero);
            Assert.That(protector.LastProtectedPlaintext.ToArray(), Is.All.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PendingKeyRejectsWrongOrEmptyContextAsync(bool emptyContext)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(19));
            var store = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions(), protector);
            PendingCertificateKeyContext source = NewContext();
            PendingCertificateKeyContext target = emptyContext ? source : NewContext();
            using Certificate original = NewCertificateWithKey();
            Assert.That(await store.SaveAsync(source, original).ConfigureAwait(false), Is.True);
            string sourceKey = store.KeyFor(source);
            string targetKey = store.KeyFor(target);
            (bool found, ByteString record) = await kv.TryGetAsync(sourceKey).ConfigureAwait(false);
            Assert.That(found, Is.True);
            ByteString transplanted = record;
            if (emptyContext)
            {
                Assert.That(protector.TryUnprotectOwned(
                    RecordProtectionContext.Create("pending-certificate-key", sourceKey), record, out byte[] owned),
                    Is.True);
                try
                {
                    transplanted = protector.Protect(default, ByteString.From(owned));
                }
                finally
                {
                    CryptoUtils.ZeroMemory(owned);
                }
            }
            await kv.SetAsync(targetKey, transplanted).ConfigureAwait(false);

            using Certificate? rejected = await store.TryTakeAsync(target).ConfigureAwait(false);

            Assert.That(rejected, Is.Null);
            (found, ByteString retained) = await kv.TryGetAsync(targetKey).ConfigureAwait(false);
            Assert.That(found, Is.True, "Authentication failure must not consume the stored record.");
            Assert.That(retained, Is.EqualTo(transplanted));
            await kv.SetAsync(sourceKey, record).ConfigureAwait(false);
            using Certificate? accepted = await store.TryTakeAsync(source).ConfigureAwait(false);
            Assert.That(accepted, Is.Not.Null);
            Assert.That(accepted!.Thumbprint, Is.EqualTo(original.Thumbprint));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidPendingKeyWipesOriginalOwnedBufferAsync(bool invalidCertificate)
        {
            using var kv = new InMemorySharedKeyValueStore();
            var protector = new InstrumentedRecordProtector();
            var store = new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions(), protector);
            PendingCertificateKeyContext context = NewContext();
            string key = store.KeyFor(context);
            byte[] malformed = invalidCertificate ? [0, 0, 0, 0, 1, 0, 0, 0, 0xFF] : [1, 2, 3];
            ByteString record = protector.Protect(
                RecordProtectionContext.Create("pending-certificate-key", key), ByteString.From(malformed));
            await kv.SetAsync(key, record).ConfigureAwait(false);

            if (invalidCertificate)
            {
                Assert.That(
                    async () => await store.TryTakeAsync(context).ConfigureAwait(false),
                    Throws.InstanceOf<CryptographicException>());
            }
            else
            {
                using Certificate? taken = await store.TryTakeAsync(context).ConfigureAwait(false);
                Assert.That(taken, Is.Null);
            }

            Assert.That(protector.LastOwnedPlaintext, Is.Not.Null);
            Assert.That(protector.LastOwnedPlaintext, Has.Length.EqualTo(malformed.Length));
            Assert.That(protector.LastOwnedPlaintext, Is.All.Zero);
            Assert.That(protector.ImmutableReads, Is.Zero);
            (_, ByteString retained) = await kv.TryGetAsync(key).ConfigureAwait(false);
            Assert.That(retained, Is.EqualTo(record));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedPendingKeyClaimWipesOriginalOwnedBufferAsync(bool canceled)
        {
            using var backend = new InMemorySharedKeyValueStore();
            var protector = new InstrumentedRecordProtector();
            var writer = new SharedKeyValuePendingCertificateKeyStore(backend, NewOptions(), protector);
            PendingCertificateKeyContext context = NewContext();
            using Certificate original = NewCertificateWithKey();
            Assert.That(await writer.SaveAsync(context, original).ConfigureAwait(false), Is.True);
            (_, ByteString record) = await backend.TryGetAsync(writer.KeyFor(context)).ConfigureAwait(false);
            var kv = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
            kv.Setup(value => value.TryGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => backend.TryGetAsync(key, ct));
            kv.Setup(value => value.CompareAndSwapAsync(
                    It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(() => canceled
                    ? throw new OperationCanceledException()
                    : new ValueTask<bool>(false));
            var reader = new SharedKeyValuePendingCertificateKeyStore(kv.Object, NewOptions(), protector);

            if (canceled)
            {
                Assert.That(
                    async () => await reader.TryTakeAsync(context).ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>());
            }
            else
            {
                using Certificate? taken = await reader.TryTakeAsync(context).ConfigureAwait(false);
                Assert.That(taken, Is.Null);
            }

            Assert.That(protector.LastOwnedPlaintext, Is.Not.Null.And.Not.Empty);
            Assert.That(protector.LastOwnedPlaintext, Is.All.Zero);
            Assert.That(protector.ImmutableReads, Is.Zero);
            (_, ByteString retained) = await backend.TryGetAsync(writer.KeyFor(context)).ConfigureAwait(false);
            Assert.That(retained, Is.EqualTo(record));
        }

        [TestCase(false)]
        [TestCase(true)]
        public Task SaveWipesWorkingRecordWhenProtectionOrStoreFailsAsync(bool failProtection)
        {
            var kv = new Mock<ISharedKeyValueStore>(MockBehavior.Strict);
            kv.Setup(value => value.SetAsync(
                    It.IsAny<string>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Store write failed."));
            var protector = new InstrumentedRecordProtector { RejectProtection = failProtection };
            var store = new SharedKeyValuePendingCertificateKeyStore(kv.Object, NewOptions(), protector);
            using Certificate original = NewCertificateWithKey();

            Assert.That(
                async () => await store.SaveAsync(NewContext(), original).ConfigureAwait(false),
                Throws.InvalidOperationException);

            Assert.That(protector.LastProtectedPlaintext.IsEmpty, Is.False);
            Assert.That(protector.LastProtectedPlaintext.ToArray(), Is.All.Zero);
            return Task.CompletedTask;
        }

        [Test]
        public void PendingKeyStoreRejectsProtectorWithoutOwnedBuffers()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var protector = new Mock<IRecordProtector>(MockBehavior.Strict);

            Assert.That(
                () => new SharedKeyValuePendingCertificateKeyStore(kv, NewOptions(), protector.Object),
                Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("protector"));
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

            Assert.That(await store.SaveAsync(context, original, CancellationToken.None).ConfigureAwait(false), Is.True);

            (bool found, ByteString stored) = await kv.TryGetAsync(store.KeyFor(context)).ConfigureAwait(false);
            Assert.That(found, Is.True);
            byte[] before = stored.ToArray();
            Assert.That(Array.TrueForAll(before, b => b == 0), Is.False,
                "precondition: the stored pass-through record is non-zero");

            using Certificate? taken = await store.TryTakeAsync(context, CancellationToken.None).ConfigureAwait(false);
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
                await store.SaveAsync(null!, cert, CancellationToken.None).ConfigureAwait(false));
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await store.SaveAsync(NewContext(), null!, CancellationToken.None).ConfigureAwait(false));
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
            public byte[]? LastOwnedPlaintext { get; private set; }

            public ByteString LastProtectedPlaintext { get; private set; }

            public int ImmutableReads { get; private set; }

            public bool RejectProtection { get; set; }

            public ByteString Protect(ByteString context, ByteString plaintext)
            {
                LastProtectedPlaintext = plaintext;
                if (RejectProtection)
                {
                    throw new InvalidOperationException("Protection failed.");
                }
                ReadOnlySpan<byte> source = plaintext.Span;
                int headerLength = 1 + sizeof(int) + context.Length;
                byte[] envelope = new byte[headerLength + source.Length];
                envelope[0] = Marker;
                BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(1), context.Length);
                context.Span.CopyTo(envelope.AsSpan(1 + sizeof(int)));
                source.CopyTo(envelope.AsSpan(headerLength));
                return new ByteString(envelope);
            }

            public bool TryUnprotect(ByteString context, ByteString protectedRecord, out ByteString plaintext)
            {
                ImmutableReads++;
                if (!TryDecode(context, protectedRecord, out byte[] data))
                {
                    plaintext = default;
                    return false;
                }
                plaintext = new ByteString(data);
                return true;
            }

            public bool TryUnprotectOwned(ByteString context, ByteString protectedRecord, out byte[] plaintext)
            {
                if (!TryDecode(context, protectedRecord, out byte[] data))
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

            private static bool TryDecode(ByteString context, ByteString protectedRecord, out byte[] data)
            {
                ReadOnlySpan<byte> span = protectedRecord.Span;
                int headerLength = 1 + sizeof(int) + context.Length;
                if (span.Length < headerLength ||
                    span[0] != Marker ||
                    BinaryPrimitives.ReadInt32LittleEndian(span[1..]) != context.Length ||
                    !span.Slice(1 + sizeof(int), context.Length).SequenceEqual(context.Span))
                {
                    data = [];
                    return false;
                }
                data = span[headerLength..].ToArray();
                return true;
            }

            private const byte Marker = 0xC3;
        }
    }
}
