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

// CA2000: system-under-test disposables are created per test and released at teardown;
//   there is no cross-test resource leak. Suppressed file-level for the suite.
#pragma warning disable CA2000 // Dispose objects before losing scope

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for <see cref="SharedKeyValueSubscriptionStore"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Category("Subscription")]
    [Parallelizable(ParallelScope.All)]
    public class SharedKeyValueSubscriptionStoreTests
    {
        private const ushort NamespaceIndex = 2;

        [Test]
        public async Task StoreAndRestoreRoundTripsDefinitionAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            StoredSubscription expected = NewSubscription(100, 10);

            bool stored = await store.StoreSubscriptionsAsync([expected]).ConfigureAwait(false);
            RestoreSubscriptionResult result = await store.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(stored, Is.True);
            Assert.That(result.Success, Is.True);
            var actual = (StoredSubscription)result.Subscriptions!.Single();
            AssertSubscription(actual, expected);
            AssertMonitoredItem((StoredMonitoredItem)actual.MonitoredItems.Single(), NewItem(100, 10));
        }

        [Test]
        public async Task StoreIsVisibleToSecondReplicaAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore active = CreateStore(kv);
            SharedKeyValueSubscriptionStore backup = CreateStore(kv);
            StoredSubscription expected = NewSubscription(200, 20);

            await active.StoreSubscriptionsAsync([expected]).ConfigureAwait(false);
            RestoreSubscriptionResult result = await backup.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            var actual = (StoredSubscription)result.Subscriptions!.Single();
            AssertSubscription(actual, expected);
        }

        [Test]
        public async Task RestoreUsesOnlyCommittedGenerationDuringConcurrentSnapshotsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var blockedBackend = new BlockingManifestCommitStore(kv);
            var firstWriter = new SharedKeyValueSubscriptionStore(blockedBackend, CreateContext());
            Task firstCommit = firstWriter
                .StoreSubscriptionsAsync([NewSubscription(225, 22)])
                .AsTask();
            await blockedBackend.WaitForBlockedManifestAsync().ConfigureAwait(false);

            var secondBackend = new AsyncSharedKeyValueStore(kv);
            var secondWriter = new SharedKeyValueSubscriptionStore(secondBackend, CreateContext());
            await secondWriter
                .StoreSubscriptionsAsync([NewSubscription(226, 23)])
                .ConfigureAwait(false);
            var readerBackend = new AsyncSharedKeyValueStore(kv);
            var reader = new SharedKeyValueSubscriptionStore(readerBackend, CreateContext());

            RestoreSubscriptionResult secondResult = await reader.RestoreSubscriptionsAsync().ConfigureAwait(false);
            Assert.That(secondResult.Subscriptions!.Select(subscription => subscription.Id), Is.EqualTo(new uint[] { 226 }));

            blockedBackend.ReleaseManifest();
            await firstCommit.ConfigureAwait(false);
            RestoreSubscriptionResult firstResult = await reader.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(firstResult.Subscriptions!.Select(subscription => subscription.Id), Is.EqualTo(new uint[] { 225 }));
        }

        [Test]
        public async Task RestoreLoadsPersistedDefinitionsAfterProcessRestartAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore active = CreateStore(kv);
            StoredSubscription expected = NewSubscription(250, 25);
            await active.StoreSubscriptionsAsync([expected]).ConfigureAwait(false);

            var restartedBackend = new AsyncSharedKeyValueStore(kv);
            var restarted = new SharedKeyValueSubscriptionStore(restartedBackend, CreateContext());
            RestoreSubscriptionResult result = await restarted.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            var actual = (StoredSubscription)result.Subscriptions!.Single();
            AssertSubscription(actual, expected);
        }

        [Test]
        public async Task EmptyPersistedSnapshotClearsCachedDefinitionsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore legacyEncoder = CreateStore(kv);
            StoredSubscription stale = NewSubscription(275, 27);
            await kv
                .SetAsync(
                    SharedKeyValueSubscriptionStore.KeyFor(stale.Id),
                    EncodeDefinition(legacyEncoder, stale))
                .ConfigureAwait(false);
            var restartedBackend = new AsyncSharedKeyValueStore(kv);
            var restarted = new SharedKeyValueSubscriptionStore(restartedBackend, CreateContext());
            await restarted.StoreSubscriptionsAsync([]).ConfigureAwait(false);

            RestoreSubscriptionResult result = await restarted.RestoreSubscriptionsAsync().ConfigureAwait(false);
            (bool foundLegacy, _) = await kv
                .TryGetAsync(SharedKeyValueSubscriptionStore.KeyFor(stale.Id))
                .ConfigureAwait(false);
            (bool foundManifest, _) = await kv
                .TryGetAsync(SharedKeyValueSubscriptionStore.SnapshotManifestKey())
                .ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Subscriptions, Is.Empty);
            Assert.That(foundLegacy, Is.True);
            Assert.That(foundManifest, Is.True);
            Assert.That(GetCachedSubscriptionIds(restarted), Is.Empty);
        }

        [Test]
        public async Task RestoreReadsLegacyDefinitionsWhenManifestIsAbsentAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore legacyEncoder = CreateStore(kv);
            StoredSubscription expected = NewSubscription(276, 28);
            await kv
                .SetAsync(
                    SharedKeyValueSubscriptionStore.KeyFor(expected.Id),
                    EncodeDefinition(legacyEncoder, expected))
                .ConfigureAwait(false);

            var restartedBackend = new AsyncSharedKeyValueStore(kv);
            var restarted = new SharedKeyValueSubscriptionStore(restartedBackend, CreateContext());
            RestoreSubscriptionResult result = await restarted.RestoreSubscriptionsAsync().ConfigureAwait(false);

            var actual = (StoredSubscription)result.Subscriptions!.Single();
            AssertSubscription(actual, expected);
        }

        [Test]
        public async Task ProtectedDefinitionRestoreFailsWhenBackendRecordIsTamperedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(11));
            var store = new SharedKeyValueSubscriptionStore(kv, CreateContext(), protector);
            StoredSubscription subscription = NewSubscription(300, 30);
            await store.StoreSubscriptionsAsync([subscription]).ConfigureAwait(false);

            KeyValuePair<string, ByteString> record = await GetSingleGenerationRecordAsync(kv).ConfigureAwait(false);
            await kv
                .SetAsync(record.Key, ByteString.From(new byte[] { 1, 2, 3, 4, 5 }))
                .ConfigureAwait(false);

            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                () => store.RestoreSubscriptionsAsync().AsTask());

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task ProtectedStoreDoesNotPersistDefinitionInClearTextAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(12));
            var store = new SharedKeyValueSubscriptionStore(kv, CreateContext(), protector);
            StoredSubscription subscription = NewSubscription(400, 40);

            await store.StoreSubscriptionsAsync([subscription]).ConfigureAwait(false);
            KeyValuePair<string, ByteString> record = await GetSingleGenerationRecordAsync(kv).ConfigureAwait(false);

            Assert.That(Contains(record.Value.ToArray(), BitConverter.GetBytes(subscription.Id)), Is.False);
            Assert.That((await store.RestoreSubscriptionsAsync().ConfigureAwait(false)).Subscriptions!.Single().Id, Is.EqualTo(subscription.Id));
        }

        [Test]
        public async Task CorruptSnapshotFailsWithoutPartiallyReplacingCacheAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore reader = CreateStore(kv);
            await reader.StoreSubscriptionsAsync([NewSubscription(450, 45)]).ConfigureAwait(false);

            var writerBackend = new AsyncSharedKeyValueStore(kv);
            var writer = new SharedKeyValueSubscriptionStore(writerBackend, CreateContext());
            await writer
                .StoreSubscriptionsAsync([NewSubscription(451, 46), NewSubscription(452, 47)])
                .ConfigureAwait(false);
            KeyValuePair<string, ByteString> currentRecord = await FindGenerationRecordAsync(kv, 452)
                .ConfigureAwait(false);
            ByteString validRecord = currentRecord.Value;
            await kv
                .SetAsync(
                    currentRecord.Key,
                    ByteString.From(new byte[] { 1, 2, 3, 4, 5 }))
                .ConfigureAwait(false);

            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                () => reader.RestoreSubscriptionsAsync().AsTask());
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(GetCachedSubscriptionIds(reader), Is.EqualTo(new uint[] { 450 }));

            await kv
                .SetAsync(currentRecord.Key, validRecord)
                .ConfigureAwait(false);
            RestoreSubscriptionResult result = await reader.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(result.Subscriptions!.Select(subscription => subscription.Id), Is.EqualTo(new uint[] { 451, 452 }));
            Assert.That(GetCachedSubscriptionIds(reader), Is.EqualTo(new uint[] { 451, 452 }));
        }

        [Test]
        public async Task RestoreRejectsSubscriptionIdThatDoesNotMatchPersistedKeyAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore writer = CreateStore(kv);
            await writer.StoreSubscriptionsAsync([NewSubscription(475, 48)]).ConfigureAwait(false);
            KeyValuePair<string, ByteString> generationRecord = await GetSingleGenerationRecordAsync(kv)
                .ConfigureAwait(false);
            await kv.DeleteAsync(SharedKeyValueSubscriptionStore.SnapshotManifestKey()).ConfigureAwait(false);
            await kv
                .SetAsync(SharedKeyValueSubscriptionStore.KeyFor(476), generationRecord.Value)
                .ConfigureAwait(false);

            var restartedBackend = new AsyncSharedKeyValueStore(kv);
            var restarted = new SharedKeyValueSubscriptionStore(restartedBackend, CreateContext());
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                () => restarted.RestoreSubscriptionsAsync().AsTask());

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
        }

        [Test]
        public async Task StoreRejectsInvalidMonitoredItemIdentityAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedKeyValueSubscriptionStore store = CreateStore(kv);
            StoredSubscription subscription = NewSubscription(490, 49);
            ((StoredMonitoredItem)subscription.MonitoredItems.Single()).SubscriptionId = 491;

            ArgumentException? exception = Assert.ThrowsAsync<ArgumentException>(
                () => store.StoreSubscriptionsAsync([subscription]).AsTask());
            (bool foundManifest, _) = await kv
                .TryGetAsync(SharedKeyValueSubscriptionStore.SnapshotManifestKey())
                .ConfigureAwait(false);

            Assert.That(exception!.ParamName, Is.EqualTo("subscriptions"));
            Assert.That(foundManifest, Is.False);
        }

        [Test]
        public async Task StoreReplacesRemovedSubscriptionsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            await store.StoreSubscriptionsAsync([NewSubscription(500, 50), NewSubscription(501, 51)]).ConfigureAwait(false);
            await store.StoreSubscriptionsAsync([NewSubscription(501, 51)]).ConfigureAwait(false);
            RestoreSubscriptionResult result = await store.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(result.Subscriptions!.Select(s => s.Id), Is.EqualTo(new uint[] { 501 }));
        }

        [Test]
        public async Task OnSubscriptionRestoreCompleteCleansStaleDefinitionsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            await store.StoreSubscriptionsAsync([NewSubscription(600, 60), NewSubscription(601, 61)]).ConfigureAwait(false);

            await store.OnSubscriptionRestoreCompleteAsync(new Dictionary<uint, ArrayOf<uint>>
            {
                [601] = new ArrayOf<uint>(new uint[] { 61 })
            }).ConfigureAwait(false);
            RestoreSubscriptionResult result = await store.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(result.Subscriptions!.Select(s => s.Id), Is.EqualTo(new uint[] { 601 }));
        }

        [Test]
        public async Task StoreSubscriptionsRejectsDuplicateSubscriptionIdsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedKeyValueSubscriptionStore store = CreateStore(kv);
            StoredSubscription first = NewSubscription(950, 95);
            StoredSubscription duplicate = NewSubscription(950, 96);

            ArgumentException? exception = Assert.ThrowsAsync<ArgumentException>(
                () => store.StoreSubscriptionsAsync([first, duplicate]).AsTask());
            (bool foundManifest, _) = await kv
                .TryGetAsync(SharedKeyValueSubscriptionStore.SnapshotManifestKey())
                .ConfigureAwait(false);

            Assert.That(exception!.ParamName, Is.EqualTo("subscriptions"));
            Assert.That(exception.Message, Does.Contain("duplicate subscription ids"));
            Assert.That(foundManifest, Is.False);
        }

        [Test]
        public void StoreSubscriptionsHonorsCancellationBeforeCommit()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await store
                    .StoreSubscriptionsAsync([NewSubscription(951, 96)], cts.Token)
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void RestoreSubscriptionsHonorsCancellationBeforeManifestLookup()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var cancelling = new CancellationCheckingStore(kv);
            var store = new SharedKeyValueSubscriptionStore(cancelling, CreateContext());
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.That(
                async () => await store.RestoreSubscriptionsAsync(cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task StoreSubscriptionsReleasesCommitLockAndPreservesCacheOnMidWriteFailureAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var failingBackend = new ThrowOnNthGenerationWriteStore(kv, failOnCallNumber: 3);
            await using var store = new SharedKeyValueSubscriptionStore(failingBackend, CreateContext());
            await store.StoreSubscriptionsAsync([NewSubscription(990, 103)]).ConfigureAwait(false);

            InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
                () => store.StoreSubscriptionsAsync(
                    [NewSubscription(990, 103), NewSubscription(991, 104)]).AsTask());

            Assert.That(exception, Is.Not.Null);
            RestoreSubscriptionResult resultAfterFailure = await store.RestoreSubscriptionsAsync().ConfigureAwait(false);
            Assert.That(resultAfterFailure.Subscriptions!.Select(s => s.Id), Is.EqualTo(new uint[] { 990 }));
            Assert.That(GetCachedSubscriptionIds(store), Is.EqualTo(new uint[] { 990 }));

            // The commit semaphore must have been released on the failed attempt so a later commit still succeeds.
            bool stored = await store.StoreSubscriptionsAsync([NewSubscription(992, 105)]).ConfigureAwait(false);
            RestoreSubscriptionResult resultAfterRecovery = await store.RestoreSubscriptionsAsync().ConfigureAwait(false);

            Assert.That(stored, Is.True);
            Assert.That(resultAfterRecovery.Subscriptions!.Select(s => s.Id), Is.EqualTo(new uint[] { 992 }));
        }

        [Test]
        public async Task SequentialCommitsRetainPriorGenerationRecordsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            await store.StoreSubscriptionsAsync([NewSubscription(995, 106)]).ConfigureAwait(false);
            KeyValuePair<string, ByteString> firstGenerationRecord =
                await GetSingleGenerationRecordAsync(kv).ConfigureAwait(false);

            await store.StoreSubscriptionsAsync([NewSubscription(996, 107)]).ConfigureAwait(false);
            (bool stillPresent, _) = await kv.TryGetAsync(firstGenerationRecord.Key).ConfigureAwait(false);

            Assert.That(stillPresent, Is.True);
        }

        [Test]
        public async Task RestoreRejectsTamperedManifestAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(14));
            await using var store = new SharedKeyValueSubscriptionStore(kv, CreateContext(), protector);
            await store.StoreSubscriptionsAsync([NewSubscription(955, 96)]).ConfigureAwait(false);

            await kv
                .SetAsync(
                    SharedKeyValueSubscriptionStore.SnapshotManifestKey(),
                    ByteString.From(new byte[] { 9, 9, 9 }))
                .ConfigureAwait(false);

            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                () => store.RestoreSubscriptionsAsync().AsTask());

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task RestoreRejectsMalformedManifestVersionAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var encoder = new BinaryEncoder(CreateContext());
            encoder.WriteInt32(null, 999);
            encoder.WriteByteString(null, ByteString.From(new byte[16]));
            encoder.WriteUInt32(null, 0);
            await kv
                .SetAsync(
                    SharedKeyValueSubscriptionStore.SnapshotManifestKey(),
                    ByteString.From(encoder.CloseAndReturnBuffer()))
                .ConfigureAwait(false);

            await using SharedKeyValueSubscriptionStore store = CreateStore(kv);
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                () => store.RestoreSubscriptionsAsync().AsTask());

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(exception.Message, Does.Contain("manifest is malformed"));
        }

        [Test]
        public async Task RestoreRejectsGenerationKeyWithoutSubscriptionIdAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore writer = CreateStore(kv);
            await writer.StoreSubscriptionsAsync([NewSubscription(960, 96)]).ConfigureAwait(false);
            KeyValuePair<string, ByteString> record = await GetSingleGenerationRecordAsync(kv).ConfigureAwait(false);
            await kv
                .SetAsync(GenerationPrefixOf(record.Key), ByteString.From(new byte[] { 1 }))
                .ConfigureAwait(false);

            var restartedBackend = new AsyncSharedKeyValueStore(kv);
            var restarted = new SharedKeyValueSubscriptionStore(restartedBackend, CreateContext());
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                () => restarted.RestoreSubscriptionsAsync().AsTask());

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(exception.Message, Does.Contain("key is malformed"));
        }

        [Test]
        public async Task RestoreRejectsGenerationKeyWithNonNumericSuffixAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore writer = CreateStore(kv);
            await writer.StoreSubscriptionsAsync([NewSubscription(961, 96)]).ConfigureAwait(false);
            KeyValuePair<string, ByteString> record = await GetSingleGenerationRecordAsync(kv).ConfigureAwait(false);
            await kv
                .SetAsync(GenerationPrefixOf(record.Key) + "12a", ByteString.From(new byte[] { 1 }))
                .ConfigureAwait(false);

            var restartedBackend = new AsyncSharedKeyValueStore(kv);
            var restarted = new SharedKeyValueSubscriptionStore(restartedBackend, CreateContext());
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                () => restarted.RestoreSubscriptionsAsync().AsTask());

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(exception.Message, Does.Contain("key is malformed"));
        }

        [Test]
        public async Task RestoreRejectsGenerationKeyWithOverflowingSuffixAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore writer = CreateStore(kv);
            await writer.StoreSubscriptionsAsync([NewSubscription(962, 96)]).ConfigureAwait(false);
            KeyValuePair<string, ByteString> record = await GetSingleGenerationRecordAsync(kv).ConfigureAwait(false);
            await kv
                .SetAsync(
                    GenerationPrefixOf(record.Key) + "99999999999999999999",
                    ByteString.From(new byte[] { 1 }))
                .ConfigureAwait(false);

            var restartedBackend = new AsyncSharedKeyValueStore(kv);
            var restarted = new SharedKeyValueSubscriptionStore(restartedBackend, CreateContext());
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                () => restarted.RestoreSubscriptionsAsync().AsTask());

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(exception.Message, Does.Contain("key is malformed"));
        }

        [Test]
        public async Task RestoreRejectsDuplicateGenerationRecordsFromScanAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using var writer = new SharedKeyValueSubscriptionStore(kv, CreateContext());
            await writer.StoreSubscriptionsAsync([NewSubscription(965, 97)]).ConfigureAwait(false);

            var duplicating = new DuplicatingScanStore(kv);
            var reader = new SharedKeyValueSubscriptionStore(duplicating, CreateContext());
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                () => reader.RestoreSubscriptionsAsync().AsTask());

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(exception.Message, Does.Contain("duplicate records"));
        }

        [Test]
        public async Task RestoreRejectsIncompleteGenerationAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore writer = CreateStore(kv);
            await writer
                .StoreSubscriptionsAsync([NewSubscription(970, 98), NewSubscription(971, 99)])
                .ConfigureAwait(false);
            KeyValuePair<string, ByteString> record = await FindGenerationRecordAsync(kv, 971).ConfigureAwait(false);
            await kv.DeleteAsync(record.Key).ConfigureAwait(false);

            var restartedBackend = new AsyncSharedKeyValueStore(kv);
            var restarted = new SharedKeyValueSubscriptionStore(restartedBackend, CreateContext());
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                () => restarted.RestoreSubscriptionsAsync().AsTask());

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(exception.Message, Does.Contain("incomplete"));
        }

        [Test]
        public async Task RestoreRejectsPersistedRecordWithInvalidMonitoredItemIdentityAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore encoder = CreateStore(kv);
            StoredSubscription malformed = NewSubscription(975, 100);
            ((StoredMonitoredItem)malformed.MonitoredItems.Single()).SubscriptionId = 976;
            await kv
                .SetAsync(SharedKeyValueSubscriptionStore.KeyFor(malformed.Id), EncodeDefinition(encoder, malformed))
                .ConfigureAwait(false);

            var restartedBackend = new AsyncSharedKeyValueStore(kv);
            var restarted = new SharedKeyValueSubscriptionStore(restartedBackend, CreateContext());
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                () => restarted.RestoreSubscriptionsAsync().AsTask());

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(exception.Message, Does.Contain("monitored-item"));
        }

        [Test]
        public async Task RestoreRejectsRecordWithTrailingDataAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore encoder = CreateStore(kv);
            StoredSubscription subscription = NewSubscription(980, 101);
            ByteString encoded = EncodeDefinition(encoder, subscription);
            byte[] withTrailingGarbage = [.. encoded.ToArray(), 1, 2, 3];
            await kv
                .SetAsync(
                    SharedKeyValueSubscriptionStore.KeyFor(subscription.Id),
                    ByteString.From(withTrailingGarbage))
                .ConfigureAwait(false);

            var restartedBackend = new AsyncSharedKeyValueStore(kv);
            var restarted = new SharedKeyValueSubscriptionStore(restartedBackend, CreateContext());
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(
                () => restarted.RestoreSubscriptionsAsync().AsTask());

            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadDecodingError));
            Assert.That(exception.Message, Does.Contain("trailing data"));
        }

        [Test]
        public async Task RetransmissionStateIsVisibleToSecondReplicaAndKeepsSequenceContinuityAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore primary = CreateStore(kv);
            SharedKeyValueSubscriptionStore backup = CreateStore(kv);

            primary.StoreRetransmissionState(
                700,
                4,
                [NewNotification(1), NewNotification(2), NewNotification(3)]);
            await primary.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? state = await backup.LoadRetransmissionStateAsync(700).ConfigureAwait(false);

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.NextSequenceNumber, Is.EqualTo(4));
            Assert.That(
                state.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 1, 2, 3 }));

            NotificationMessage republished = state.SentMessages.Memory.ToArray().Single(m => m.SequenceNumber == 2);
            Assert.That(republished.NotificationData, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RetransmissionAcknowledgeEvictsMirroredNotificationAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore primary = CreateStore(kv);
            SharedKeyValueSubscriptionStore backup = CreateStore(kv);
            primary.StoreRetransmissionState(
                701,
                4,
                [NewNotification(1), NewNotification(2), NewNotification(3)]);
            await primary.FlushAsync().ConfigureAwait(false);

            primary.AcknowledgeNotification(701, 2);
            await primary.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? state = await backup.LoadRetransmissionStateAsync(701).ConfigureAwait(false);

            Assert.That(state, Is.Not.Null);
            Assert.That(
                state!.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 1, 3 }));
        }

        [Test]
        public async Task RetransmissionSnapshotRemovesStaleSequenceKeysAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            store.StoreRetransmissionState(
                702,
                4,
                [NewNotification(1), NewNotification(2), NewNotification(3)]);
            await store.FlushAsync().ConfigureAwait(false);

            store.StoreRetransmissionState(702, 5, [NewNotification(3), NewNotification(4)]);
            await store.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? state = await store.LoadRetransmissionStateAsync(702).ConfigureAwait(false);

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.NextSequenceNumber, Is.EqualTo(5));
            Assert.That(
                state.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 3, 4 }));
        }

        [Test]
        public async Task RetransmissionDeltaAddsAndRemovesOnlyChangedMessagesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            SharedKeyValueSubscriptionStore backup = CreateStore(kv);

            store.StoreRetransmissionStateDelta(708, 4, [NewNotification(1), NewNotification(2)], []);
            await store.FlushAsync().ConfigureAwait(false);
            store.StoreRetransmissionStateDelta(708, 5, [NewNotification(3)], [1]);
            await store.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? state = await backup.LoadRetransmissionStateAsync(708).ConfigureAwait(false);

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.NextSequenceNumber, Is.EqualTo(5));
            Assert.That(
                state.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 2, 3 }));
        }

        [Test]
        public async Task RetransmissionMessagesDoNotRepeatNamespaceTablesAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            store.StoreRetransmissionStateDelta(709, 2, [NewNotification(1)], []);
            await store.FlushAsync().ConfigureAwait(false);
            (bool stateFound, ByteString stateRaw) = await kv.TryGetAsync(
                SharedKeyValueSubscriptionStore.RetransmissionStateKeyFor(709)).ConfigureAwait(false);
            (bool messageFound, ByteString messageRaw) = await kv.TryGetAsync(
                SharedKeyValueSubscriptionStore.RetransmissionMessageKeyFor(709, 1)).ConfigureAwait(false);

            byte[] namespaceBytes = Encoding.UTF8.GetBytes("urn:test:subscriptions");
            Assert.That(stateFound, Is.True);
            Assert.That(messageFound, Is.True);
            Assert.That(Contains(stateRaw.ToArray(), namespaceBytes), Is.True);
            Assert.That(Contains(messageRaw.ToArray(), namespaceBytes), Is.False);
        }

        [Test]
        public async Task RetransmissionTamperFailsClosedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(13));
            var store = new SharedKeyValueSubscriptionStore(kv, CreateContext(), protector);
            store.StoreRetransmissionState(703, 2, [NewNotification(1)]);
            await store.FlushAsync().ConfigureAwait(false);

            await kv.SetAsync(
                SharedKeyValueSubscriptionStore.RetransmissionStateKeyFor(703),
                ByteString.From(new byte[] { 9, 8, 7 })).ConfigureAwait(false);
            SubscriptionRetransmissionState? state = await store.LoadRetransmissionStateAsync(703).ConfigureAwait(false);

            Assert.That(state, Is.Null);
        }

        [Test]
        public async Task MissingRetransmissionStateReturnsNullAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            SubscriptionRetransmissionState? state = await store.LoadRetransmissionStateAsync(704).ConfigureAwait(false);

            Assert.That(state, Is.Null);
        }

        [Test]
        public async Task RetransmissionMirrorUsesAsyncBackendWithoutBlockingAsync()
        {
            using var inner = new InMemorySharedKeyValueStore();
            var kv = new AsyncSharedKeyValueStore(inner);
            var primary = new SharedKeyValueSubscriptionStore(kv, CreateContext());
            var backup = new SharedKeyValueSubscriptionStore(kv, CreateContext());

            primary.StoreRetransmissionState(705, 3, [NewNotification(1), NewNotification(2)]);
            await primary.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? state = await backup.LoadRetransmissionStateAsync(705).ConfigureAwait(false);

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.NextSequenceNumber, Is.EqualTo(3));
            Assert.That(
                state.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 1, 2 }));
        }

        [Test]
        public async Task RetransmissionDeleteIsOrderedAfterInflightMirrorWritesAsync()
        {
            const uint subscriptionId = 706;
            using var inner = new InMemorySharedKeyValueStore();
            var kv = new BlockingRetransmissionSetStore(inner, subscriptionId);
            await using var store = new SharedKeyValueSubscriptionStore(kv, CreateContext());
            await store.StoreSubscriptionsAsync([NewSubscription(subscriptionId, 76)]).ConfigureAwait(false);

            store.StoreRetransmissionState(subscriptionId, 3, [NewNotification(1), NewNotification(2)]);
            await kv.WaitForBlockedSetAsync().ConfigureAwait(false);
            await store.StoreSubscriptionsAsync([]).ConfigureAwait(false);
            kv.ReleaseBlockedSets();
            await store.FlushAsync().ConfigureAwait(false);

            SubscriptionRetransmissionState? state = await store.LoadRetransmissionStateAsync(subscriptionId).ConfigureAwait(false);

            Assert.That(state, Is.Null);
            Assert.That(await CountKeysAsync(inner, RetransmissionPrefixFor(subscriptionId)).ConfigureAwait(false), Is.Zero);
        }

        [Test]
        public async Task ReusedSubscriptionIdDoesNotLoadDeletedRetransmissionStateAsync()
        {
            const uint subscriptionId = 707;
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedKeyValueSubscriptionStore store = CreateStore(kv);
            await using SharedKeyValueSubscriptionStore backup = CreateStore(kv);

            await store.StoreSubscriptionsAsync([NewSubscription(subscriptionId, 77)]).ConfigureAwait(false);
            store.StoreRetransmissionState(subscriptionId, 4, [NewNotification(1), NewNotification(2), NewNotification(3)]);
            await store.FlushAsync().ConfigureAwait(false);
            await store.StoreSubscriptionsAsync([]).ConfigureAwait(false);
            await store.FlushAsync().ConfigureAwait(false);
            await store.StoreSubscriptionsAsync([NewSubscription(subscriptionId, 78)]).ConfigureAwait(false);

            SubscriptionRetransmissionState? deletedState = await backup.LoadRetransmissionStateAsync(subscriptionId).ConfigureAwait(false);
            store.StoreRetransmissionState(subscriptionId, 10, [NewNotification(9)]);
            await store.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? newState = await backup.LoadRetransmissionStateAsync(subscriptionId).ConfigureAwait(false);

            Assert.That(deletedState, Is.Null);
            Assert.That(newState, Is.Not.Null);
            Assert.That(newState!.NextSequenceNumber, Is.EqualTo(10));
            Assert.That(
                newState.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 9 }));
        }

        [Test]
        public void ConstructorValidatesArguments()
        {
            IServiceMessageContext context = CreateContext();
            var kvMock = new Mock<ISharedKeyValueStore>();

            Assert.That(
                () => new SharedKeyValueSubscriptionStore(null!, context),
                Throws.ArgumentNullException);
            Assert.That(
                () => new SharedKeyValueSubscriptionStore(kvMock.Object, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void RestoreQueueFallbacksReturnNull()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            Assert.That(store.RestoreDataChangeMonitoredItemQueue(1), Is.Null);
            Assert.That(store.RestoreEventMonitoredItemQueue(1), Is.Null);
        }

        [Test]
        public void OnSubscriptionRestoreCompleteValidatesArguments()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            Assert.That(
                async () => await store.OnSubscriptionRestoreCompleteAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task DisposeAsyncCancelsBlockedSharedStoreWritesAsync()
        {
            await using var kv = new HangingWriteSharedKeyValueStore();
            var store = new SharedKeyValueSubscriptionStore(kv, CreateContext());
            store.StoreRetransmissionState(800, 2, [NewNotification(1)]);

            await kv.WaitForWriteAsync().ConfigureAwait(false);
            Task disposeTask = store.DisposeAsync().AsTask();
            Task completed = await Task.WhenAny(disposeTask, Task.Delay(2000)).ConfigureAwait(false);

            Assert.That(completed, Is.SameAs(disposeTask));
            await disposeTask.ConfigureAwait(false);
        }

        [Test]
        public void DefinitionCodecRoundTripsAllSupportedFilterShapes()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            StoredSubscription subscription = NewSubscription(900, 90);
            subscription.MonitoredItems =
            [
                NewItemWithFilter(900, 90, null),
                NewItemWithFilter(900, 91, new EventFilter()),
                NewItemWithFilter(900, 92, new AggregateFilter
                {
                    AggregateType = ObjectIds.AggregateFunction_Average,
                    ProcessingInterval = 1000,
                    StartTime = new DateTimeUtc(638000000000000000)
                })
            ];

            MethodInfo encode = GetPrivateMethod("Encode", typeof(StoredSubscription));
            MethodInfo decode = GetPrivateMethod("Decode", typeof(ByteString));
            var payload = (ByteString)encode.Invoke(store, [subscription])!;
            var decoded = (StoredSubscription)decode.Invoke(store, [payload])!;
            var decodedItems = decoded.MonitoredItems.ToList();

            Assert.That(decodedItems, Has.Count.EqualTo(3));
            Assert.That(decodedItems[0].FilterToUse, Is.Null);
            Assert.That(decodedItems[1].FilterToUse, Is.TypeOf<EventFilter>());
            Assert.That(decodedItems[2].FilterToUse, Is.TypeOf<AggregateFilter>());
        }

        [Test]
        public void DefinitionDecodeRejectsUnsupportedVersion()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);
            using var encoder = new BinaryEncoder(CreateContext());
            encoder.WriteInt32(null, 999);
            var payload = ByteString.From(encoder.CloseAndReturnBuffer());
            MethodInfo decode = GetPrivateMethod("Decode", typeof(ByteString));

            TargetInvocationException? ex = Assert.Throws<TargetInvocationException>(
                () => decode.Invoke(store, [payload]));

            Assert.That(ex!.InnerException, Is.TypeOf<ServiceResultException>());
        }

        [Test]
        public async Task ContinuationPointLoadIgnoresUnsupportedEnvelopeVersionAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedKeyValueSubscriptionStore store = CreateStore(kv);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            var continuationPointId = Guid.NewGuid();
            using var encoder = new BinaryEncoder(CreateContext());
            encoder.WriteInt32(null, 999);
            await kv.SetAsync(
                SharedKeyValueSubscriptionStore.ContinuationPointKeyFor(
                    sessionId,
                    ContinuationPointKind.Browse,
                    continuationPointId),
                ByteString.From(encoder.CloseAndReturnBuffer())).ConfigureAwait(false);

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await store.LoadContinuationPointsAsync(sessionId).ConfigureAwait(false);

            Assert.That(envelopes, Is.Empty);
        }

        [Test]
        public async Task ContinuationPointLoadIgnoresInvalidEnvelopeIdAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedKeyValueSubscriptionStore store = CreateStore(kv);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            var continuationPointId = Guid.NewGuid();
            using var encoder = new BinaryEncoder(CreateContext());
            encoder.WriteInt32(null, 1);
            encoder.WriteByteString(null, ByteString.From(new byte[] { 1, 2, 3 }));
            await kv.SetAsync(
                SharedKeyValueSubscriptionStore.ContinuationPointKeyFor(
                    sessionId,
                    ContinuationPointKind.Browse,
                    continuationPointId),
                ByteString.From(encoder.CloseAndReturnBuffer())).ConfigureAwait(false);

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await store.LoadContinuationPointsAsync(sessionId).ConfigureAwait(false);

            Assert.That(envelopes, Is.Empty);
        }

        [Test]
        public void StoreSubscriptionsRejectsNullArgument()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            Assert.That(
                async () => await store.StoreSubscriptionsAsync(null!).ConfigureAwait(false),
                Throws.ArgumentNullException);
        }

        [Test]
        public void StoreContinuationPointRejectsNullArgument()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            Assert.That(() => store.StoreContinuationPoint(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void RemoveContinuationPointIgnoresNullSession()
        {
            using var kv = new InMemorySharedKeyValueStore();
            SharedKeyValueSubscriptionStore store = CreateStore(kv);

            Assert.That(
                () => store.RemoveContinuationPoint(NodeId.Null, ContinuationPointKind.Browse, Guid.NewGuid()),
                Throws.Nothing);
        }

        [Test]
        public async Task LoadContinuationPointsReturnsEmptyForNullSessionAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedKeyValueSubscriptionStore store = CreateStore(kv);

            ArrayOf<ContinuationPointEnvelope> envelopes =
                await store.LoadContinuationPointsAsync(NodeId.Null).ConfigureAwait(false);

            Assert.That(envelopes, Is.Empty);
        }

        [Test]
        public async Task LoadRetransmissionStateReturnsNullForUnsupportedVersionAsync()
        {
            const uint subscriptionId = 720;
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedKeyValueSubscriptionStore store = CreateStore(kv);
            using var encoder = new BinaryEncoder(CreateContext());
            encoder.WriteInt32(null, 999);
            encoder.WriteUInt32(null, 5);
            await kv.SetAsync(
                SharedKeyValueSubscriptionStore.RetransmissionStateKeyFor(subscriptionId),
                ByteString.From(encoder.CloseAndReturnBuffer())).ConfigureAwait(false);

            SubscriptionRetransmissionState? state =
                await store.LoadRetransmissionStateAsync(subscriptionId).ConfigureAwait(false);

            Assert.That(state, Is.Null);
        }

        [Test]
        public async Task RetransmissionMirrorRequeuesBatchAfterTransientFailureAsync()
        {
            const uint subscriptionId = 721;
            using var inner = new InMemorySharedKeyValueStore();
            var kv = new ThrowOnceOnPrefixStore(inner, "subscription-retransmission/");
            await using var store = new SharedKeyValueSubscriptionStore(kv, CreateContext());

            store.StoreRetransmissionState(subscriptionId, 3, [NewNotification(1)]);
            await store.FlushAsync().ConfigureAwait(false);
            SubscriptionRetransmissionState? state =
                await store.LoadRetransmissionStateAsync(subscriptionId).ConfigureAwait(false);

            Assert.That(kv.ThrowCount, Is.EqualTo(1));
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.NextSequenceNumber, Is.EqualTo(3));
            Assert.That(
                state.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 1 }));
        }

        [Test]
        public async Task ContinuationPointMirrorRequeuesBatchAfterTransientFailureAsync()
        {
            using var inner = new InMemorySharedKeyValueStore();
            var kv = new ThrowOnceOnPrefixStore(inner, "continuation-point/");
            await using var store = new SharedKeyValueSubscriptionStore(kv, CreateContext());
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            var continuationPointId = Guid.NewGuid();

            store.StoreContinuationPoint(new ContinuationPointEnvelope
            {
                Id = continuationPointId,
                OwnerSessionId = sessionId,
                Kind = ContinuationPointKind.Browse,
                Index = 4
            });
            await store.FlushAsync().ConfigureAwait(false);
            ArrayOf<ContinuationPointEnvelope> envelopes =
                await store.LoadContinuationPointsAsync(sessionId).ConfigureAwait(false);

            Assert.That(kv.ThrowCount, Is.EqualTo(1));
            Assert.That(envelopes, Has.Count.EqualTo(1));
            Assert.That(envelopes[0].Id, Is.EqualTo(continuationPointId));
            Assert.That(envelopes[0].Index, Is.EqualTo(4));
        }

        [Test]
        public async Task LegacyRetransmissionMessageDecodesViaFallbackAsync()
        {
            const uint subscriptionId = 722;
            using var kv = new InMemorySharedKeyValueStore();
            await using SharedKeyValueSubscriptionStore store = CreateStore(kv);

            using var stateEncoder = new BinaryEncoder(CreateContext());
            stateEncoder.WriteInt32(null, 1);
            stateEncoder.WriteUInt32(null, 7);
            await kv.SetAsync(
                SharedKeyValueSubscriptionStore.RetransmissionStateKeyFor(subscriptionId),
                ByteString.From(stateEncoder.CloseAndReturnBuffer())).ConfigureAwait(false);

            ServiceMessageContext messageContext = CreateContext();
            using var messageEncoder = new BinaryEncoder(messageContext);
            messageEncoder.WriteStringArray(null, messageContext.NamespaceUris.ToArrayOf());
            messageEncoder.WriteStringArray(null, messageContext.ServerUris.ToArrayOf());
            messageEncoder.WriteEncodeable(null, NewNotification(3));
            await kv.SetAsync(
                SharedKeyValueSubscriptionStore.RetransmissionMessageKeyFor(subscriptionId, 3),
                ByteString.From(messageEncoder.CloseAndReturnBuffer())).ConfigureAwait(false);

            SubscriptionRetransmissionState? state =
                await store.LoadRetransmissionStateAsync(subscriptionId).ConfigureAwait(false);

            Assert.That(state, Is.Not.Null);
            Assert.That(state!.NextSequenceNumber, Is.EqualTo(7));
            Assert.That(
                state.SentMessages.Memory.ToArray().Select(m => m.SequenceNumber),
                Is.EqualTo(new uint[] { 3 }));
        }

        private static SharedKeyValueSubscriptionStore CreateStore(InMemorySharedKeyValueStore kv)
        {
            return new SharedKeyValueSubscriptionStore(kv, CreateContext());
        }

        private static ServiceMessageContext CreateContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            context.NamespaceUris.GetIndexOrAppend("urn:test:subscriptions");
            return context;
        }

        private static StoredSubscription NewSubscription(uint subscriptionId, uint monitoredItemId)
        {
            return new StoredSubscription
            {
                Id = subscriptionId,
                IsDurable = true,
                LifetimeCounter = 2,
                MaxLifetimeCount = 120,
                MaxKeepaliveCount = 12,
                MaxMessageCount = 8,
                MaxNotificationsPerPublish = 99,
                PublishingInterval = 250,
                Priority = 7,
                LastSentMessage = 0,
                SequenceNumber = 0,
                SentMessages = [],
                UserIdentityToken = new AnonymousIdentityToken(),
                MonitoredItems = [NewItem(subscriptionId, monitoredItemId)]
            };
        }

        private static StoredMonitoredItem NewItem(uint subscriptionId, uint monitoredItemId)
        {
            var filter = new DataChangeFilter
            {
                Trigger = DataChangeTrigger.StatusValue,
                DeadbandType = (uint)DeadbandType.Absolute,
                DeadbandValue = 1.5
            };

            return new StoredMonitoredItem
            {
                IsRestored = false,
                AlwaysReportUpdates = true,
                AttributeId = Attributes.Value,
                ClientHandle = 987,
                DiagnosticsMasks = DiagnosticsMasks.OperationAll,
                DiscardOldest = true,
                Encoding = new QualifiedName(BrowseNames.DefaultXml, 0),
                Id = monitoredItemId,
                IndexRange = "1:3",
                ParsedIndexRange = NumericRange.Parse("1:3"),
                IsDurable = true,
                LastError = ServiceResult.Good,
                LastValue = new DataValue(new Variant(42)),
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = new NodeId("Temperature", NamespaceIndex),
                FilterToUse = filter,
                OriginalFilter = filter,
                QueueSize = 3,
                Range = 100,
                SamplingInterval = 50,
                SourceSamplingInterval = 25,
                SubscriptionId = subscriptionId,
                TimestampsToReturn = TimestampsToReturn.Both,
                TypeMask = 1
            };
        }

        private static StoredMonitoredItem NewItemWithFilter(
            uint subscriptionId,
            uint monitoredItemId,
            MonitoringFilter? filter)
        {
            StoredMonitoredItem item = NewItem(subscriptionId, monitoredItemId);
            item.FilterToUse = filter!;
            item.OriginalFilter = filter!;
            return item;
        }

        private static MethodInfo GetPrivateMethod(string name, params Type[] parameterTypes)
        {
            MethodInfo? method = typeof(SharedKeyValueSubscriptionStore).GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                parameterTypes,
                null);
            Assert.That(method, Is.Not.Null);
            return method!;
        }

        private static ByteString EncodeDefinition(
            SharedKeyValueSubscriptionStore store,
            StoredSubscription subscription)
        {
            MethodInfo encode = GetPrivateMethod("Encode", typeof(StoredSubscription));
            return (ByteString)encode.Invoke(store, [subscription])!;
        }

        private static async Task<KeyValuePair<string, ByteString>> GetSingleGenerationRecordAsync(
            InMemorySharedKeyValueStore store)
        {
            var records = new List<KeyValuePair<string, ByteString>>();
            await foreach (KeyValuePair<string, ByteString> pair in store
                .ScanAsync(SharedKeyValueSubscriptionStore.SnapshotGenerationRootPrefix()))
            {
                records.Add(pair);
            }

            Assert.That(records, Has.Count.EqualTo(1));
            return records[0];
        }

        private static string GenerationPrefixOf(string generationRecordKey)
        {
            int separator = generationRecordKey.LastIndexOf('/');
            Assert.That(separator, Is.GreaterThan(-1));
            return generationRecordKey[..(separator + 1)];
        }

        private static async Task<KeyValuePair<string, ByteString>> FindGenerationRecordAsync(
            InMemorySharedKeyValueStore store,
            uint subscriptionId)
        {
            string suffix = "/" + subscriptionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var records = new List<KeyValuePair<string, ByteString>>();
            await foreach (KeyValuePair<string, ByteString> pair in store
                .ScanAsync(SharedKeyValueSubscriptionStore.SnapshotGenerationRootPrefix()))
            {
                if (pair.Key.EndsWith(suffix, StringComparison.Ordinal))
                {
                    records.Add(pair);
                }
            }

            Assert.That(records, Has.Count.EqualTo(1));
            return records[0];
        }

        private static uint[] GetCachedSubscriptionIds(SharedKeyValueSubscriptionStore store)
        {
            FieldInfo? cacheField = typeof(SharedKeyValueSubscriptionStore).GetField(
                "m_definitionCache",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(cacheField, Is.Not.Null);
            object? cache = cacheField!.GetValue(store);
            Assert.That(cache, Is.Not.Null);
            PropertyInfo? subscriptionsProperty = cache!.GetType().GetProperty(
                "Subscriptions",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(subscriptionsProperty, Is.Not.Null);
            var subscriptions =
                (Dictionary<uint, StoredSubscription>)subscriptionsProperty!.GetValue(cache)!;
            uint[] ids = [.. subscriptions.Keys];
            Array.Sort(ids);
            return ids;
        }

        private static NotificationMessage NewNotification(uint sequenceNumber)
        {
            var notification = new DataChangeNotification
            {
                MonitoredItems =
                [
                    new MonitoredItemNotification
                    {
                        ClientHandle = sequenceNumber,
                        Value = new DataValue(new Variant((int)sequenceNumber))
                    }
                ]
            };

            return new NotificationMessage
            {
                SequenceNumber = sequenceNumber,
                PublishTime = DateTimeUtc.Now,
                NotificationData = [new ExtensionObject(notification)]
            };
        }

        private static string RetransmissionPrefixFor(uint subscriptionId)
        {
            string messageKey = SharedKeyValueSubscriptionStore.RetransmissionMessageKeyFor(subscriptionId, 0);
            return messageKey[..^10];
        }

        private static async Task<int> CountKeysAsync(InMemorySharedKeyValueStore store, string keyPrefix)
        {
            int count = 0;
            await foreach (KeyValuePair<string, ByteString> pair in store.ScanAsync(keyPrefix))
            {
                _ = pair;
                count++;
            }
            (bool found, _) = await store.TryGetAsync(
                SharedKeyValueSubscriptionStore.RetransmissionStateKeyFor(
                    uint.Parse(keyPrefix.Split('/')[1], System.Globalization.CultureInfo.InvariantCulture))).ConfigureAwait(false);
            return found ? count + 1 : count;
        }

        private static void AssertSubscription(StoredSubscription actual, StoredSubscription expected)
        {
            Assert.That(actual.Id, Is.EqualTo(expected.Id));
            Assert.That(actual.IsDurable, Is.EqualTo(expected.IsDurable));
            Assert.That(actual.MaxLifetimeCount, Is.EqualTo(expected.MaxLifetimeCount));
            Assert.That(actual.MaxKeepaliveCount, Is.EqualTo(expected.MaxKeepaliveCount));
            Assert.That(actual.MaxNotificationsPerPublish, Is.EqualTo(expected.MaxNotificationsPerPublish));
            Assert.That(actual.PublishingInterval, Is.EqualTo(expected.PublishingInterval));
            Assert.That(actual.Priority, Is.EqualTo(expected.Priority));
        }

        private static void AssertMonitoredItem(StoredMonitoredItem actual, StoredMonitoredItem expected)
        {
            Assert.That(actual.SubscriptionId, Is.EqualTo(expected.SubscriptionId));
            Assert.That(actual.Id, Is.EqualTo(expected.Id));
            Assert.That(actual.NodeId, Is.EqualTo(expected.NodeId));
            Assert.That(actual.AttributeId, Is.EqualTo(expected.AttributeId));
            Assert.That(actual.MonitoringMode, Is.EqualTo(expected.MonitoringMode));
            Assert.That(actual.SamplingInterval, Is.EqualTo(expected.SamplingInterval));
            Assert.That(actual.QueueSize, Is.EqualTo(expected.QueueSize));
            Assert.That(actual.DiscardOldest, Is.EqualTo(expected.DiscardOldest));
            Assert.That(actual.FilterToUse, Is.TypeOf<DataChangeFilter>());
            Assert.That(((DataChangeFilter)actual.FilterToUse).DeadbandValue, Is.EqualTo(1.5));
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int ii = 0; ii + needle.Length <= haystack.Length; ii++)
            {
                bool match = true;
                for (int jj = 0; jj < needle.Length; jj++)
                {
                    if (haystack[ii + jj] != needle[jj])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return true;
                }
            }
            return false;
        }

        private static byte[] MakeKey(byte seed)
        {
            byte[] key = new byte[32];
            for (int ii = 0; ii < key.Length; ii++)
            {
                key[ii] = (byte)(seed + ii);
            }
            return key;
        }

        private sealed class BlockingManifestCommitStore : ISharedKeyValueStore
        {
            public BlockingManifestCommitStore(ISharedKeyValueStore inner)
            {
                m_inner = inner;
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                return m_inner.TryGetAsync(key, ct);
            }

            public async ValueTask SetAsync(
                string key,
                ByteString value,
                CancellationToken ct = default)
            {
                if (string.Equals(
                    key,
                    SharedKeyValueSubscriptionStore.SnapshotManifestKey(),
                    StringComparison.Ordinal) &&
                    Interlocked.Exchange(ref m_manifestBlocked, 1) == 0)
                {
                    m_blocked.TrySetResult(true);
                    await m_release.Task.WaitAsync(ct).ConfigureAwait(false);
                }

                await m_inner.SetAsync(key, value, ct).ConfigureAwait(false);
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                return m_inner.CompareAndSwapAsync(key, expected, value, ct);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                return m_inner.DeleteAsync(key, ct);
            }

            public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_inner.ScanAsync(keyPrefix, ct);
            }

            public IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_inner.WatchAsync(keyPrefix, ct);
            }

            public async Task WaitForBlockedManifestAsync()
            {
                await m_blocked.Task
                    .WaitAsync(TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false);
            }

            public void ReleaseManifest()
            {
                m_release.TrySetResult(true);
            }

            private readonly ISharedKeyValueStore m_inner;
            private readonly TaskCompletionSource<bool> m_blocked =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_manifestBlocked;
        }

        private sealed class AsyncSharedKeyValueStore : ISharedKeyValueStore
        {
            public AsyncSharedKeyValueStore(ISharedKeyValueStore inner)
            {
                m_inner = inner;
            }

            public async ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                await Task.Yield();
                return await m_inner.TryGetAsync(key, ct).ConfigureAwait(false);
            }

            public async ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                await Task.Yield();
                await m_inner.SetAsync(key, value, ct).ConfigureAwait(false);
            }

            public async ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                await Task.Yield();
                return await m_inner.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
            }

            public async ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                await Task.Yield();
                return await m_inner.DeleteAsync(key, ct).ConfigureAwait(false);
            }

            public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                await foreach (KeyValuePair<string, ByteString> pair in m_inner.ScanAsync(keyPrefix, ct))
                {
                    yield return pair;
                }
            }

            public async IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                await foreach (KeyValueChange change in m_inner.WatchAsync(keyPrefix, ct))
                {
                    yield return change;
                }
            }

            private readonly ISharedKeyValueStore m_inner;
        }

        private sealed class HangingWriteSharedKeyValueStore : ISharedKeyValueStore, IAsyncDisposable
        {
            public Task<bool> WaitForWriteAsync()
            {
                return m_writeStarted.Task;
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                return new ValueTask<(bool Found, ByteString Value)>((false, default));
            }

            public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                m_writeStarted.TrySetResult(true);
                return new ValueTask(Task.Delay(Timeout.Infinite, ct));
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                return new ValueTask<bool>(true);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                m_writeStarted.TrySetResult(true);
                return WaitForDeleteCancellationAsync(ct);
            }

            public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                yield break;
            }

            public async IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                yield break;
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            private static async ValueTask<bool> WaitForDeleteCancellationAsync(CancellationToken ct)
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return false;
            }

            private readonly TaskCompletionSource<bool> m_writeStarted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class BlockingRetransmissionSetStore : ISharedKeyValueStore
        {
            public BlockingRetransmissionSetStore(ISharedKeyValueStore inner, uint subscriptionId)
            {
                m_inner = inner;
                m_keyPrefix = "subscription-retransmission/" +
                    subscriptionId.ToString("D", System.Globalization.CultureInfo.InvariantCulture) +
                    "/";
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                return m_inner.TryGetAsync(key, ct);
            }

            public async ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                if (key.StartsWith(m_keyPrefix, StringComparison.Ordinal) &&
                    Interlocked.Exchange(ref m_blockedSetCount, 1) == 0)
                {
                    m_blocked.SetResult(true);
                    await m_release.Task.ConfigureAwait(false);
                }

                await m_inner.SetAsync(key, value, ct).ConfigureAwait(false);
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                return m_inner.CompareAndSwapAsync(key, expected, value, ct);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                return m_inner.DeleteAsync(key, ct);
            }

            public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await foreach (KeyValuePair<string, ByteString> pair in m_inner.ScanAsync(keyPrefix, ct))
                {
                    yield return pair;
                }
            }

            public async IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await foreach (KeyValueChange change in m_inner.WatchAsync(keyPrefix, ct))
                {
                    yield return change;
                }
            }

            public async Task WaitForBlockedSetAsync()
            {
                Task completed = await Task.WhenAny(m_blocked.Task, Task.Delay(TimeSpan.FromSeconds(10)))
                    .ConfigureAwait(false);
                Assert.That(completed, Is.SameAs(m_blocked.Task));
            }

            public void ReleaseBlockedSets()
            {
                m_release.SetResult(true);
            }

            private readonly ISharedKeyValueStore m_inner;
            private readonly string m_keyPrefix;

            private readonly TaskCompletionSource<bool> m_blocked =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private int m_blockedSetCount;
        }

        private sealed class ThrowOnceOnPrefixStore : ISharedKeyValueStore
        {
            public ThrowOnceOnPrefixStore(ISharedKeyValueStore inner, string keyPrefix)
            {
                m_inner = inner;
                m_keyPrefix = keyPrefix;
            }

            public int ThrowCount => m_throwCount;

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                return m_inner.TryGetAsync(key, ct);
            }

            public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                if (key.StartsWith(m_keyPrefix, StringComparison.Ordinal) &&
                    Interlocked.Exchange(ref m_throwGate, 1) == 0)
                {
                    Interlocked.Increment(ref m_throwCount);
                    throw new InvalidOperationException("Injected transient mirror failure.");
                }

                return m_inner.SetAsync(key, value, ct);
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                return m_inner.CompareAndSwapAsync(key, expected, value, ct);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                return m_inner.DeleteAsync(key, ct);
            }

            public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_inner.ScanAsync(keyPrefix, ct);
            }

            public IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_inner.WatchAsync(keyPrefix, ct);
            }

            private readonly ISharedKeyValueStore m_inner;
            private readonly string m_keyPrefix;
            private int m_throwGate;
            private int m_throwCount;
        }

        /// <summary>
        /// Backend whose <see cref="ScanAsync"/> yields every matching record twice, simulating a
        /// non-idempotent enumeration from a misbehaving distributed backend.
        /// </summary>
        private sealed class DuplicatingScanStore : ISharedKeyValueStore
        {
            public DuplicatingScanStore(ISharedKeyValueStore inner)
            {
                m_inner = inner;
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                return m_inner.TryGetAsync(key, ct);
            }

            public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                return m_inner.SetAsync(key, value, ct);
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                return m_inner.CompareAndSwapAsync(key, expected, value, ct);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                return m_inner.DeleteAsync(key, ct);
            }

            public async IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                var snapshot = new List<KeyValuePair<string, ByteString>>();
                await foreach (KeyValuePair<string, ByteString> pair in m_inner.ScanAsync(keyPrefix, ct)
                    .ConfigureAwait(false))
                {
                    snapshot.Add(pair);
                }

                foreach (KeyValuePair<string, ByteString> pair in snapshot)
                {
                    yield return pair;
                }
                foreach (KeyValuePair<string, ByteString> pair in snapshot)
                {
                    yield return pair;
                }
            }

            public IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_inner.WatchAsync(keyPrefix, ct);
            }

            private readonly ISharedKeyValueStore m_inner;
        }

        /// <summary>
        /// Backend that throws <see cref="OperationCanceledException"/> up front on every call, matching a
        /// well-behaved network backend that observes cancellation before doing any work.
        /// </summary>
        private sealed class CancellationCheckingStore : ISharedKeyValueStore
        {
            public CancellationCheckingStore(ISharedKeyValueStore inner)
            {
                m_inner = inner;
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return m_inner.TryGetAsync(key, ct);
            }

            public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return m_inner.SetAsync(key, value, ct);
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return m_inner.CompareAndSwapAsync(key, expected, value, ct);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return m_inner.DeleteAsync(key, ct);
            }

            public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return m_inner.ScanAsync(keyPrefix, ct);
            }

            public IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                return m_inner.WatchAsync(keyPrefix, ct);
            }

            private readonly ISharedKeyValueStore m_inner;
        }

        /// <summary>
        /// Backend that throws once its <see cref="SetAsync"/> call counter (scoped to snapshot-generation
        /// keys) reaches <c>failOnCallNumber</c>, simulating a shared-store write failure partway through
        /// committing an immutable snapshot generation.
        /// </summary>
        private sealed class ThrowOnNthGenerationWriteStore : ISharedKeyValueStore
        {
            public ThrowOnNthGenerationWriteStore(ISharedKeyValueStore inner, int failOnCallNumber)
            {
                m_inner = inner;
                m_failOnCallNumber = failOnCallNumber;
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                return m_inner.TryGetAsync(key, ct);
            }

            public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                if (key.StartsWith(
                        SharedKeyValueSubscriptionStore.SnapshotGenerationRootPrefix(),
                        StringComparison.Ordinal) &&
                    Interlocked.Increment(ref m_callCount) == m_failOnCallNumber)
                {
                    throw new InvalidOperationException("Injected snapshot write failure.");
                }

                return m_inner.SetAsync(key, value, ct);
            }

            public ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                return m_inner.CompareAndSwapAsync(key, expected, value, ct);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                return m_inner.DeleteAsync(key, ct);
            }

            public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_inner.ScanAsync(keyPrefix, ct);
            }

            public IAsyncEnumerable<KeyValueChange> WatchAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_inner.WatchAsync(keyPrefix, ct);
            }

            private readonly ISharedKeyValueStore m_inner;
            private readonly int m_failOnCallNumber;
            private int m_callCount;
        }
    }
}
