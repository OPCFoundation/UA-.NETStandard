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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Redundancy.Server.Tests.Historian
{
    [TestFixture]
    [Category("Distributed")]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class SharedKeyValueHistoryContinuationStoreTests
    {
        [Test]
        public void ProcessLocalStoreIsRejected()
        {
            using var keyValueStore = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());

            Assert.That(
                () => new SharedKeyValueHistoryContinuationStore(
                    keyValueStore,
                    context,
                    protector),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void UnprotectedStoreIsRejected()
        {
            using var keyValueStore = new StrongTestStore();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());

            Assert.That(
                () => new SharedKeyValueHistoryContinuationStore(
                    keyValueStore,
                    context,
                    NullRecordProtector.Instance),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task StoredEnvelopeLoadsAndCanBeTakenOnlyOnceAsync()
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            await using var primary = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            await using var backup = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            var id = Guid.NewGuid();
            var expected = new HistoryContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = sessionId,
                CodecId = "test",
                CodecVersion = 3,
                Payload = ByteString.From([1, 2, 3])
            };

            await primary.StoreAsync(expected).ConfigureAwait(false);

            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await backup.LoadAsync(sessionId).ConfigureAwait(false);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo(expected));
            Assert.That(
                await backup.TryTakeAsync(sessionId, id).ConfigureAwait(false),
                Is.True);
            Assert.That(
                await primary.TryTakeAsync(sessionId, id).ConfigureAwait(false),
                Is.False);
            Assert.That(
                await primary.LoadAsync(sessionId).ConfigureAwait(false),
                Is.Empty);
        }

        [Test]
        public async Task DuplicateContinuationIdentifierIsRejectedAsync()
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            await using var store = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            var envelope = new HistoryContinuationPointEnvelope
            {
                Id = Guid.NewGuid(),
                OwnerSessionId = new NodeId(Guid.NewGuid(), 1),
                CodecId = "test",
                CodecVersion = 1,
                Payload = ByteString.From([1])
            };
            await store.StoreAsync(envelope).ConfigureAwait(false);

            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await store.StoreAsync(envelope).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadEntryExists));
        }

        [Test]
        public async Task TamperedEnvelopeIsNotLoadedAsync()
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            await using var store = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            var id = Guid.NewGuid();
            await store.StoreAsync(new HistoryContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = sessionId,
                CodecId = "test",
                CodecVersion = 1,
                Payload = ByteString.From([1, 2, 3])
            }).ConfigureAwait(false);
            await keyValueStore.SetAsync(
                SharedKeyValueHistoryContinuationStore.KeyFor(sessionId, id),
                ByteString.From([0xFF, 0x00, 0x55])).ConfigureAwait(false);

            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await store.LoadAsync(sessionId).ConfigureAwait(false);

            Assert.That(loaded, Is.Empty);
            await AssertEventuallyAsync(
                async () => !await keyValueStore.ContainsAsync(
                    SharedKeyValueHistoryContinuationStore.KeyFor(
                        sessionId,
                        id)).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task LoadRejectsSessionQuotaOverflowAsync()
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            await using var writer = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            await using var reader = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector,
                maxEnvelopesPerSession: 1);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            await writer.StoreAsync(CreateEnvelope(sessionId)).ConfigureAwait(false);
            await writer.StoreAsync(CreateEnvelope(sessionId)).ConfigureAwait(false);

            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await reader.LoadAsync(sessionId).ConfigureAwait(false))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadTooManyOperations));
        }

        [Test]
        public async Task ExpiredEnvelopeCannotBeLoadedOrTakenAsync()
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var timeProvider = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            await using var store = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector,
                retentionTime: TimeSpan.FromMinutes(1),
                timeProvider: timeProvider);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            HistoryContinuationPointEnvelope envelope = CreateEnvelope(sessionId);
            await store.StoreAsync(envelope).ConfigureAwait(false);

            timeProvider.Advance(TimeSpan.FromMinutes(2));

            Assert.That(
                await store.LoadAsync(sessionId).ConfigureAwait(false),
                Is.Empty);
            Assert.That(
                await store.TryTakeAsync(
                    sessionId,
                    envelope.Id).ConfigureAwait(false),
                Is.False);
            await AssertEventuallyAsync(
                async () => !await keyValueStore.ContainsAsync(
                    SharedKeyValueHistoryContinuationStore.KeyFor(
                        sessionId,
                        envelope.Id)).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ScheduledRemovalDeletesEnvelopeAsync()
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            await using var store = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            var id = Guid.NewGuid();
            await store.StoreAsync(new HistoryContinuationPointEnvelope
            {
                Id = id,
                OwnerSessionId = sessionId,
                CodecId = "test",
                CodecVersion = 1,
                Payload = ByteString.From([4])
            }).ConfigureAwait(false);

            store.ScheduleRemove(sessionId, id);

            await AssertEventuallyAsync(
                async () => (await store.LoadAsync(sessionId).ConfigureAwait(false))
                    .IsEmpty).ConfigureAwait(false);
        }

        [Test]
        public async Task PostClaimDeleteFailureDoesNotUndoClaimAsync()
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            await using var store = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            HistoryContinuationPointEnvelope envelope =
                CreateEnvelope(sessionId);
            await store.StoreAsync(envelope).ConfigureAwait(false);
            keyValueStore.NonRetryableDeleteFailuresRemaining = 1;

            bool claimed = await store.TryTakeAsync(
                sessionId,
                envelope.Id).ConfigureAwait(false);

            Assert.That(claimed, Is.True);
            await AssertEventuallyAsync(
                async () => !await keyValueStore.ContainsAsync(
                    SharedKeyValueHistoryContinuationStore.KeyFor(
                        sessionId,
                        envelope.Id)).ConfigureAwait(false))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task CommitThenThrowCasIsResolvedForStoreAndClaimAsync()
        {
            using var keyValueStore = new StrongTestStore
            {
                CommitThenThrowNextCompareAndSwap = true
            };
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            await using var store = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            HistoryContinuationPointEnvelope envelope =
                CreateEnvelope(sessionId);
            using var cts = new CancellationTokenSource();

            await store.StoreAsync(
                envelope,
                cts.Token).ConfigureAwait(false);
            Assert.That(
                keyValueStore.ResolutionReadUsedCancelableToken,
                Is.False);
            Assert.That(
                await store.LoadAsync(sessionId).ConfigureAwait(false),
                Has.Count.EqualTo(1));
            keyValueStore.CommitThenThrowNextCompareAndSwap = true;
            keyValueStore.ResolutionReadUsedCancelableToken = null;
            Assert.That(
                await store.TryTakeAsync(
                    sessionId,
                    envelope.Id,
                    cts.Token).ConfigureAwait(false),
                Is.True);
            Assert.That(
                keyValueStore.ResolutionReadUsedCancelableToken,
                Is.False);
            Assert.That(
                await store.TryTakeAsync(
                    sessionId,
                    envelope.Id).ConfigureAwait(false),
                Is.False);
        }

        [Test]
        public async Task MissingRecordAfterClaimCasFailureIsNotClaimedAsync()
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            await using var store = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            HistoryContinuationPointEnvelope envelope =
                CreateEnvelope(sessionId);
            await store.StoreAsync(envelope).ConfigureAwait(false);
            keyValueStore.DeleteThenThrowNextCompareAndSwap = true;

            bool claimed = await store.TryTakeAsync(
                sessionId,
                envelope.Id).ConfigureAwait(false);

            Assert.That(claimed, Is.False);
        }

        [Test]
        public async Task DisposeCancelsIndefiniteDeleteRetriesAsync()
        {
            using var keyValueStore = new StrongTestStore
            {
                DeleteFailuresRemaining = int.MaxValue
            };
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            var store = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            HistoryContinuationPointEnvelope envelope =
                CreateEnvelope(sessionId);
            await store.StoreAsync(envelope).ConfigureAwait(false);
            store.ScheduleRemove(sessionId, envelope.Id);

            await store.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            Assert.DoesNotThrow(
                () => store.ScheduleRemove(sessionId, envelope.Id));
        }

        private static async Task AssertEventuallyAsync(Func<Task<bool>> predicate)
        {
            for (int i = 0; i < 300; i++)
            {
                if (await predicate().ConfigureAwait(false))
                {
                    return;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail("Condition was not observed before the timeout.");
        }

        private static HistoryContinuationPointEnvelope CreateEnvelope(
            NodeId sessionId)
        {
            return new HistoryContinuationPointEnvelope
            {
                Id = Guid.NewGuid(),
                OwnerSessionId = sessionId,
                CodecId = "test",
                CodecVersion = 1,
                Payload = ByteString.From([1])
            };
        }

        private static AesCbcHmacRecordProtector CreateProtector()
        {
            return new AesCbcHmacRecordProtector(
            [
                0, 1, 2, 3, 4, 5, 6, 7,
                8, 9, 10, 11, 12, 13, 14, 15,
                16, 17, 18, 19, 20, 21, 22, 23,
                24, 25, 26, 27, 28, 29, 30, 31
            ]);
        }

        private sealed class StrongTestStore :
            ISharedKeyValueStore,
            ISharedKeyValueStoreConsistency,
            IDisposable
        {
            public int DeleteFailuresRemaining { get; set; }

            public int NonRetryableDeleteFailuresRemaining { get; set; }

            public bool CommitThenThrowNextCompareAndSwap { get; set; }

            public bool DeleteThenThrowNextCompareAndSwap { get; set; }

            public bool? ResolutionReadUsedCancelableToken { get; set; }

            public bool IsLinearizable(string key)
            {
                return true;
            }

            public bool IsProcessLocal(string key)
            {
                return false;
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(
                string key,
                CancellationToken ct = default)
            {
                if (m_expectResolutionRead)
                {
                    m_expectResolutionRead = false;
                    ResolutionReadUsedCancelableToken = ct.CanBeCanceled;
                }
                return m_inner.TryGetAsync(key, ct);
            }

            public ValueTask SetAsync(
                string key,
                ByteString value,
                CancellationToken ct = default)
            {
                return m_inner.SetAsync(key, value, ct);
            }

            public async ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                if (DeleteThenThrowNextCompareAndSwap)
                {
                    DeleteThenThrowNextCompareAndSwap = false;
                    _ = await m_inner.DeleteAsync(key, ct)
                        .ConfigureAwait(false);
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "Another claimant removed the record before the simulated transport failure.");
                }
                bool result = await m_inner.CompareAndSwapAsync(
                    key,
                    expected,
                    value,
                    ct).ConfigureAwait(false);
                if (result && CommitThenThrowNextCompareAndSwap)
                {
                    CommitThenThrowNextCompareAndSwap = false;
                    m_expectResolutionRead = true;
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "Committed before the simulated transport failure.");
                }
                return result;
            }

            public ValueTask<bool> DeleteAsync(
                string key,
                CancellationToken ct = default)
            {
                if (NonRetryableDeleteFailuresRemaining > 0)
                {
                    NonRetryableDeleteFailuresRemaining--;
                    throw new NotSupportedException(
                        "Injected non-retryable delete failure.");
                }
                if (DeleteFailuresRemaining > 0)
                {
                    DeleteFailuresRemaining--;
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "Simulated transient delete failure.");
                }
                return m_inner.DeleteAsync(key, ct);
            }

            public async ValueTask<bool> ContainsAsync(string key)
            {
                (bool found, _) = await m_inner.TryGetAsync(
                    key,
                    CancellationToken.None).ConfigureAwait(false);
                return found;
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

            public void Dispose()
            {
                m_inner.Dispose();
            }

            private readonly InMemorySharedKeyValueStore m_inner = new();
            private bool m_expectResolutionRead;
        }
    }
}
