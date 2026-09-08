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
    /// <summary>
    /// Verifies protected, single-use history continuations and recoverable cleanup in shared storage.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public class SharedKeyValueHistoryContinuationStoreTests
    {
        /// <summary>
        /// Verifies that shared history continuations reject a process-local key-value store.
        /// </summary>
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

        /// <summary>
        /// Verifies that shared history continuations reject storage without record protection.
        /// </summary>
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

        /// <summary>
        /// Verifies that a stored continuation envelope loads and can be claimed only once.
        /// </summary>
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

        /// <summary>
        /// Verifies that legacy continuation envelopes remain readable after a format upgrade.
        /// </summary>
        [Test]
        public async Task LegacyEnvelopeLoadsAfterFormatUpgradeAsync()
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
                timeProvider: timeProvider);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            HistoryContinuationPointEnvelope expected =
                CreateEnvelope(sessionId);
            await keyValueStore.SetAsync(
                SharedKeyValueHistoryContinuationStore.KeyFor(
                    sessionId,
                    expected.Id),
                EncodeLegacyEnvelope(
                    expected,
                    context,
                    protector,
                    timeProvider.GetUtcNow().AddHours(1).UtcDateTime))
                .ConfigureAwait(false);

            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await store.LoadAsync(sessionId).ConfigureAwait(false);

            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies that storing a duplicate continuation identifier is rejected.
        /// </summary>
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

        /// <summary>
        /// Verifies that tampered continuation envelopes are not loaded.
        /// </summary>
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
            HistoryContinuationPointEnvelope original =
                CreateEnvelope(sessionId);
            await store.StoreAsync(original).ConfigureAwait(false);
            await keyValueStore.SetAsync(
                SharedKeyValueHistoryContinuationStore.KeyFor(
                    sessionId,
                    original.Id),
                ByteString.From([0xFF, 0x00, 0x55])).ConfigureAwait(false);

            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await store.LoadAsync(sessionId).ConfigureAwait(false);

            Assert.That(loaded, Is.Empty);
            HistoryContinuationPointEnvelope replacement =
                CreateReplacement(original, 2);
            await AssertEventuallyStoredAsync(store, replacement)
                .ConfigureAwait(false);
            loaded = await store.LoadAsync(sessionId).ConfigureAwait(false);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo(replacement));
        }

        /// <summary>
        /// Verifies that loading continuations rejects a session quota overflow.
        /// </summary>
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

        /// <summary>
        /// Verifies that expired continuation envelopes can neither be loaded nor claimed.
        /// </summary>
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
            HistoryContinuationPointEnvelope replacement =
                CreateReplacement(envelope, 2);
            await AssertEventuallyStoredAsync(store, replacement)
                .ConfigureAwait(false);
            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await store.LoadAsync(sessionId).ConfigureAwait(false);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo(replacement));
        }

        /// <summary>
        /// Verifies that scheduled removal deletes the continuation envelope.
        /// </summary>
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
            HistoryContinuationPointEnvelope original =
                CreateEnvelope(sessionId);
            await store.StoreAsync(original).ConfigureAwait(false);

            store.ScheduleRemove(sessionId, original.Id);

            await AssertEventuallyAsync(
                async () => (await store.LoadAsync(sessionId).ConfigureAwait(false))
                    .IsEmpty).ConfigureAwait(false);
            HistoryContinuationPointEnvelope replacement =
                CreateReplacement(original, 4);
            await store.StoreAsync(replacement).ConfigureAwait(false);
            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await store.LoadAsync(sessionId).ConfigureAwait(false);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo(replacement));
        }

        /// <summary>
        /// Verifies that cleanup failure does not undo an already successful continuation claim.
        /// </summary>
        [Test]
        public async Task CleanupFailureDoesNotUndoClaimAsync()
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
            keyValueStore.FailNextCleanupAttempts(1);

            bool claimed = await store.TryTakeAsync(
                sessionId,
                envelope.Id).ConfigureAwait(false);

            Assert.That(claimed, Is.True);
            await keyValueStore.WaitForCleanupAttemptsAsync(1)
                .ConfigureAwait(false);
            Assert.That(
                await store.TryTakeAsync(
                    sessionId,
                    envelope.Id).ConfigureAwait(false),
                Is.False);
            HistoryContinuationPointEnvelope replacement =
                CreateReplacement(envelope, 2);
            await store.StoreAsync(replacement).ConfigureAwait(false);
            Assert.That(
                await store.LoadAsync(sessionId).ConfigureAwait(false),
                Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Verifies that delayed cleanup cannot delete a restored continuation.
        /// </summary>
        [Test]
        public async Task DelayedCleanupDoesNotDeleteRestoredContinuationAsync()
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            await using var claimant = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            await using var restorer = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            HistoryContinuationPointEnvelope original =
                CreateEnvelope(sessionId);
            await claimant.StoreAsync(original).ConfigureAwait(false);
            keyValueStore.BlockNextCleanup();

            Assert.That(
                await claimant.TryTakeAsync(
                    sessionId,
                    original.Id).ConfigureAwait(false),
                Is.True);
            await keyValueStore.WaitForCleanupBlockedAsync()
                .ConfigureAwait(false);
            HistoryContinuationPointEnvelope restored =
                CreateReplacement(original, 2);
            await restorer.StoreAsync(restored).ConfigureAwait(false);

            keyValueStore.ReleaseCleanup();
            await keyValueStore.WaitForCleanupCompletionAsync()
                .ConfigureAwait(false);

            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await restorer.LoadAsync(sessionId).ConfigureAwait(false);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo(restored));
            Assert.That(
                await restorer.TryTakeAsync(
                    sessionId,
                    restored.Id).ConfigureAwait(false),
                Is.True);
            Assert.That(
                await claimant.TryTakeAsync(
                    sessionId,
                    restored.Id).ConfigureAwait(false),
                Is.False);
        }

        /// <summary>
        /// Verifies that an abandoned claim marker is cleaned before the same identifier is reused.
        /// </summary>
        [Test]
        public async Task AbandonedClaimMarkerIsCleanedForSameIdentifierReuseAsync()
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
            HistoryContinuationPointEnvelope original =
                CreateEnvelope(sessionId);
            await store.StoreAsync(original).ConfigureAwait(false);
            keyValueStore.ObserveNextCleanup();

            Assert.That(
                await store.TryTakeAsync(
                    sessionId,
                    original.Id).ConfigureAwait(false),
                Is.True);
            await keyValueStore.WaitForCleanupCompletionAsync()
                .ConfigureAwait(false);
            Assert.That(
                await store.TryTakeAsync(
                    sessionId,
                    original.Id).ConfigureAwait(false),
                Is.False);
            HistoryContinuationPointEnvelope replacement =
                CreateReplacement(original, 3);
            await store.StoreAsync(replacement).ConfigureAwait(false);

            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await store.LoadAsync(sessionId).ConfigureAwait(false);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo(replacement));
        }

        /// <summary>
        /// Verifies that restart recovery can restore an identifier over its cleanup marker.
        /// </summary>
        [Test]
        public async Task RestartCanRestoreSameIdentifierOverCleanupMarkerAsync()
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            HistoryContinuationPointEnvelope original =
                CreateEnvelope(sessionId);
            var firstStore = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            await firstStore.StoreAsync(original).ConfigureAwait(false);
            keyValueStore.ObserveNextCleanup();
            Assert.That(
                await firstStore.TryTakeAsync(
                    sessionId,
                    original.Id).ConfigureAwait(false),
                Is.True);
            await keyValueStore.WaitForCleanupCompletionAsync()
                .ConfigureAwait(false);
            await firstStore.DisposeAsync().ConfigureAwait(false);
            await using var restartedStore =
                new SharedKeyValueHistoryContinuationStore(
                    keyValueStore,
                    context,
                    protector);
            HistoryContinuationPointEnvelope replacement =
                CreateReplacement(original, 4);

            await restartedStore.StoreAsync(replacement).ConfigureAwait(false);

            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await restartedStore.LoadAsync(sessionId).ConfigureAwait(false);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo(replacement));
        }

        /// <summary>
        /// Verifies that restart recovery can restore a claim marker after cleanup retries are exhausted.
        /// </summary>
        [Test]
        public async Task RetryExhaustedClaimMarkerCanBeRestoredAfterRestartAsync()
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            HistoryContinuationPointEnvelope original =
                CreateEnvelope(sessionId);
            var firstStore = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            await firstStore.StoreAsync(original).ConfigureAwait(false);
            keyValueStore.FailNextCleanupAttempts(5);

            Assert.That(
                await firstStore.TryTakeAsync(
                    sessionId,
                    original.Id).ConfigureAwait(false),
                Is.True);
            await keyValueStore.WaitForCleanupAttemptsAsync(5)
                .ConfigureAwait(false);
            await firstStore.DisposeAsync().ConfigureAwait(false);
            await using var restartedStore =
                new SharedKeyValueHistoryContinuationStore(
                    keyValueStore,
                    context,
                    protector);
            HistoryContinuationPointEnvelope replacement =
                CreateReplacement(original, 5);

            await restartedStore.StoreAsync(replacement).ConfigureAwait(false);

            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await restartedStore.LoadAsync(sessionId).ConfigureAwait(false);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo(replacement));
        }

        /// <summary>
        /// Verifies that an indeterminate cleanup compare-and-swap remains recoverable when resolution fails.
        /// </summary>
        [Test]
        public async Task IndeterminateCleanupCasWithFailedResolutionIsRecoverableAsync()
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
            HistoryContinuationPointEnvelope original =
                CreateEnvelope(sessionId);
            await store.StoreAsync(original).ConfigureAwait(false);
            keyValueStore.MakeNextCleanupIndeterminate();

            Assert.That(
                await store.TryTakeAsync(
                    sessionId,
                    original.Id).ConfigureAwait(false),
                Is.True);
            await keyValueStore.WaitForCleanupAttemptsAsync(2)
                .ConfigureAwait(false);
            await keyValueStore.WaitForCleanupCompletionAsync()
                .ConfigureAwait(false);
            HistoryContinuationPointEnvelope replacement =
                CreateReplacement(original, 6);

            await store.StoreAsync(replacement).ConfigureAwait(false);

            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await store.LoadAsync(sessionId).ConfigureAwait(false);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo(replacement));
        }

        /// <summary>
        /// Verifies that delayed scheduled removal does not delete a replacement continuation.
        /// </summary>
        [Test]
        public async Task DelayedScheduledRemovalDoesNotDeleteReplacementAsync()
        {
            using var keyValueStore = new StrongTestStore();
            var protector = new DeterministicRecordProtector();
            var timeProvider = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            await using var remover = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector,
                timeProvider: timeProvider);
            await using var restorer = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector,
                timeProvider: timeProvider);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            HistoryContinuationPointEnvelope original =
                CreateEnvelope(sessionId);
            await remover.StoreAsync(original).ConfigureAwait(false);
            keyValueStore.BlockNextCleanup();

            remover.ScheduleRemove(sessionId, original.Id);
            await keyValueStore.WaitForCleanupBlockedAsync()
                .ConfigureAwait(false);
            Assert.That(
                await restorer.TryTakeAsync(
                    sessionId,
                    original.Id).ConfigureAwait(false),
                Is.True);
            HistoryContinuationPointEnvelope replacement =
                CreateReplacement(original, 1);
            await restorer.StoreAsync(replacement).ConfigureAwait(false);

            keyValueStore.ReleaseCleanup();
            await keyValueStore.WaitForCleanupCompletionAsync()
                .ConfigureAwait(false);

            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await restorer.LoadAsync(sessionId).ConfigureAwait(false);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo(replacement));
        }

        /// <summary>
        /// Verifies that disposal drains queued continuation cleanup before shutdown.
        /// </summary>
        [Test]
        public async Task DisposeDrainsQueuedCleanupBeforeShutdownAsync()
        {
            using var keyValueStore = new StrongTestStore();
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
            keyValueStore.BlockNextCleanup();
            store.ScheduleRemove(sessionId, envelope.Id);
            await keyValueStore.WaitForCleanupBlockedAsync()
                .ConfigureAwait(false);

            Task disposeTask = store.DisposeAsync().AsTask();
            await Task.Delay(50).ConfigureAwait(false);
            Assert.That(disposeTask.IsCompleted, Is.False);
            keyValueStore.ReleaseCleanup();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);

            await using var restartedStore =
                new SharedKeyValueHistoryContinuationStore(
                    keyValueStore,
                    context,
                    protector);
            Assert.That(
                await restartedStore.LoadAsync(sessionId).ConfigureAwait(false),
                Is.Empty);
        }

        /// <summary>
        /// Verifies that an indeterminate store compare-and-swap queues cleanup for the exact stored payload.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task IndeterminateStoreCasQueuesExactPayloadCleanupAsync(bool replaceClaim)
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            await using var claimant = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            await using var store = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            HistoryContinuationPointEnvelope envelope = CreateEnvelope(sessionId);
            if (replaceClaim)
            {
                await claimant.StoreAsync(envelope).ConfigureAwait(false);
                keyValueStore.BlockNextCleanup();
                Assert.That(
                    await claimant.TryTakeAsync(sessionId, envelope.Id).ConfigureAwait(false),
                    Is.True);
                await keyValueStore.WaitForCleanupBlockedAsync().ConfigureAwait(false);
            }
            keyValueStore.CommitThenThrowNextCompareAndSwap = true;
            keyValueStore.FailNextCompareAndSwapResolutionRead = true;

            try
            {
                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await store.StoreAsync(envelope).ConfigureAwait(false));
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            }
            finally
            {
                keyValueStore.ReleaseCleanup();
            }
            await store.DisposeAsync().ConfigureAwait(false);
            await claimant.DisposeAsync().ConfigureAwait(false);

            (bool found, _) = await keyValueStore.TryGetAsync(
                SharedKeyValueHistoryContinuationStore.KeyFor(sessionId, envelope.Id)).ConfigureAwait(false);
            Assert.That(found, Is.False, "A failed save must not leave an untracked live continuation.");
        }

        /// <summary>
        /// Verifies that disposal cancels blocked cleanup resolution.
        /// </summary>
        [Test]
        public async Task DisposeCancelsBlockedCleanupResolutionAsync()
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
            HistoryContinuationPointEnvelope envelope = CreateEnvelope(sessionId);
            await store.StoreAsync(envelope).ConfigureAwait(false);
            keyValueStore.BlockNextCleanupResolution();
            store.ScheduleRemove(sessionId, envelope.Id);
            await keyValueStore.WaitForCleanupResolutionBlockedAsync().ConfigureAwait(false);

            Task disposal = store.DisposeAsync().AsTask();
            try
            {
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            finally
            {
                keyValueStore.ReleaseCleanupResolution();
                await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that store and claim operations resolve compare-and-swap commits that subsequently throw.
        /// </summary>
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

        /// <summary>
        /// Verifies that a missing record after a claim compare-and-swap failure is not reported as claimed.
        /// </summary>
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

        /// <summary>
        /// Verifies that a claim compare-and-swap failure before commit leaves the continuation available.
        /// </summary>
        [Test]
        public async Task ClaimCasFailureBeforeCommitLeavesContinuationAvailableAsync()
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
            keyValueStore.ThrowNextCompareAndSwapBeforeCommit = true;

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await store.TryTakeAsync(
                    sessionId,
                    envelope.Id).ConfigureAwait(false));

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(
                await store.TryTakeAsync(
                    sessionId,
                    envelope.Id).ConfigureAwait(false),
                Is.True);
        }

        /// <summary>
        /// Verifies that a continuation affected by canceled cleanup can be restored after restart.
        /// </summary>
        [Test]
        public async Task CanceledCleanupCanBeRestoredAfterRestartAsync()
        {
            using var keyValueStore = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
            var sessionId = new NodeId(Guid.NewGuid(), 1);
            HistoryContinuationPointEnvelope original =
                CreateEnvelope(sessionId);
            var firstStore = new SharedKeyValueHistoryContinuationStore(
                keyValueStore,
                context,
                protector);
            await firstStore.StoreAsync(original).ConfigureAwait(false);
            keyValueStore.BlockNextCleanup();
            Assert.That(
                await firstStore.TryTakeAsync(
                    sessionId,
                    original.Id).ConfigureAwait(false),
                Is.True);
            await keyValueStore.WaitForCleanupBlockedAsync()
                .ConfigureAwait(false);

            await firstStore.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            await using var restartedStore =
                new SharedKeyValueHistoryContinuationStore(
                    keyValueStore,
                    context,
                    protector);
            HistoryContinuationPointEnvelope replacement =
                CreateReplacement(original, 8);

            await restartedStore.StoreAsync(replacement).ConfigureAwait(false);

            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await restartedStore.LoadAsync(sessionId).ConfigureAwait(false);
            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(loaded[0], Is.EqualTo(replacement));
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

        private static async Task AssertEventuallyStoredAsync(
            SharedKeyValueHistoryContinuationStore store,
            HistoryContinuationPointEnvelope envelope)
        {
            for (int i = 0; i < 300; i++)
            {
                try
                {
                    await store.StoreAsync(envelope).ConfigureAwait(false);
                    return;
                }
                catch (ServiceResultException exception) when (
                    exception.StatusCode == StatusCodes.BadEntryExists)
                {
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail("The replacement continuation was not stored before the timeout.");
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

        private static HistoryContinuationPointEnvelope CreateReplacement(
            HistoryContinuationPointEnvelope envelope,
            byte payload)
        {
            return new HistoryContinuationPointEnvelope
            {
                Id = envelope.Id,
                OwnerSessionId = envelope.OwnerSessionId,
                CodecId = envelope.CodecId,
                CodecVersion = envelope.CodecVersion,
                Payload = ByteString.From([payload])
            };
        }

        private static ByteString EncodeLegacyEnvelope(
            HistoryContinuationPointEnvelope envelope,
            IServiceMessageContext context,
            AesCbcHmacRecordProtector protector,
            DateTimeUtc expiresAt)
        {
            using var encoder = new BinaryEncoder(context);
            encoder.WriteInt32(null, 1);
            encoder.WriteDateTime(null, expiresAt);
            encoder.WriteByteString(
                null,
                ByteString.From(envelope.Id.ToByteArray()));
            encoder.WriteNodeId(null, envelope.OwnerSessionId);
            encoder.WriteString(null, envelope.CodecId);
            encoder.WriteUInt32(null, envelope.CodecVersion);
            encoder.WriteByteString(null, envelope.Payload);
            byte[] payload = encoder.CloseAndReturnBuffer() ??
                throw new InvalidOperationException(
                    "The legacy continuation payload was not encoded.");
            return protector.Protect(ByteString.From(payload));
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
            public bool CommitThenThrowNextCompareAndSwap { get; set; }

            public bool DeleteThenThrowNextCompareAndSwap { get; set; }

            public bool ThrowNextCompareAndSwapBeforeCommit { get; set; }

            public bool FailNextCompareAndSwapResolutionRead { get; set; }

            public bool? ResolutionReadUsedCancelableToken { get; set; }

            public void ObserveNextCleanup()
            {
                m_observeCleanup = true;
            }

            public void BlockNextCleanup()
            {
                ObserveNextCleanup();
                m_blockCleanup = true;
            }

            public void FailNextCleanupAttempts(int attempts)
            {
                ObserveNextCleanup();
                m_cleanupFailuresRemaining = attempts;
            }

            public void MakeNextCleanupIndeterminate()
            {
                ObserveNextCleanup();
                m_commitThenThrowCleanup = true;
            }

            public void BlockNextCleanupResolution()
            {
                FailNextCleanupAttempts(1);
                m_blockCleanupResolution = true;
            }

            public async Task WaitForCleanupResolutionBlockedAsync()
            {
                await m_cleanupResolutionBlocked.Task
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }

            public void ReleaseCleanupResolution()
            {
                m_releaseCleanupResolution.TrySetResult(true);
            }

            public async Task WaitForCleanupAttemptsAsync(int expected)
            {
                for (int i = 0; i < 1_000; i++)
                {
                    if (Volatile.Read(ref m_cleanupAttempts) >= expected)
                    {
                        return;
                    }
                    await Task.Delay(10).ConfigureAwait(false);
                }
                Assert.Fail("The expected cleanup attempts were not observed.");
            }

            public async Task WaitForCleanupBlockedAsync()
            {
                await m_cleanupBlocked.Task
                    .WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            }

            public void ReleaseCleanup()
            {
                m_releaseCleanup.TrySetResult(true);
            }

            public async Task WaitForCleanupCompletionAsync()
            {
                await m_cleanupCompleted.Task
                    .WaitAsync(TimeSpan.FromSeconds(2))
                    .ConfigureAwait(false);
            }

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
                if (m_blockCleanupResolution)
                {
                    m_blockCleanupResolution = false;
                    return ReadBlockedCleanupResolutionAsync(key, ct);
                }
                if (m_failNextCleanupResolutionRead)
                {
                    m_failNextCleanupResolutionRead = false;
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "The simulated cleanup resolution read failed.");
                }
                if (m_expectResolutionRead)
                {
                    m_expectResolutionRead = false;
                    ResolutionReadUsedCancelableToken = ct.CanBeCanceled;
                    if (FailNextCompareAndSwapResolutionRead)
                    {
                        FailNextCompareAndSwapResolutionRead = false;
                        throw new ServiceResultException(
                            StatusCodes.BadUnexpectedError,
                            "The simulated compare-and-swap resolution read failed.");
                    }
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
                if (value.IsNull)
                {
                    Interlocked.Increment(ref m_cleanupAttempts);
                    if (m_observeCleanup && m_blockCleanup)
                    {
                        m_cleanupBlocked.TrySetResult(true);
                        await m_releaseCleanup.Task.WaitAsync(ct)
                            .ConfigureAwait(false);
                        m_blockCleanup = false;
                    }
                    if (m_observeCleanup &&
                        m_cleanupFailuresRemaining > 0)
                    {
                        m_cleanupFailuresRemaining--;
                        throw new ServiceResultException(
                            StatusCodes.BadUnexpectedError,
                            "The simulated cleanup compare-and-swap failed.");
                    }
                    bool cleaned = await m_inner.CompareAndSwapAsync(
                        key,
                        expected,
                        value,
                        ct).ConfigureAwait(false);
                    if (m_observeCleanup &&
                        cleaned &&
                        m_commitThenThrowCleanup)
                    {
                        m_commitThenThrowCleanup = false;
                        m_failNextCleanupResolutionRead = true;
                        throw new ServiceResultException(
                            StatusCodes.BadUnexpectedError,
                            "Cleanup committed before the simulated transport failure.");
                    }
                    if (m_observeCleanup)
                    {
                        m_cleanupCompleted.TrySetResult(true);
                    }
                    return cleaned;
                }
                return await CompareAndSwapCoreAsync(
                    key,
                    expected,
                    value,
                    ct).ConfigureAwait(false);
            }

            public ValueTask<bool> DeleteAsync(
                string key,
                CancellationToken ct = default)
            {
                throw new NotSupportedException(
                    "Continuation cleanup must use compare-and-delete.");
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

            private async ValueTask<(bool Found, ByteString Value)> ReadBlockedCleanupResolutionAsync(
                string key,
                CancellationToken ct)
            {
                m_cleanupResolutionBlocked.TrySetResult(true);
                await m_releaseCleanupResolution.Task.WaitAsync(ct).ConfigureAwait(false);
                return await m_inner.TryGetAsync(key, ct).ConfigureAwait(false);
            }

            private async ValueTask<bool> CompareAndSwapCoreAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct)
            {
                if (ThrowNextCompareAndSwapBeforeCommit)
                {
                    ThrowNextCompareAndSwapBeforeCommit = false;
                    m_expectResolutionRead = true;
                    throw new ServiceResultException(
                        StatusCodes.BadTimeout,
                        "The simulated compare-and-swap failed before commit.");
                }
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

            private readonly InMemorySharedKeyValueStore m_inner = new();

            private readonly TaskCompletionSource<bool> m_cleanupBlocked =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_releaseCleanup =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_cleanupCompleted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_cleanupResolutionBlocked =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private readonly TaskCompletionSource<bool> m_releaseCleanupResolution =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private bool m_expectResolutionRead;
            private bool m_observeCleanup;
            private bool m_blockCleanup;
            private bool m_commitThenThrowCleanup;
            private bool m_failNextCleanupResolutionRead;
            private bool m_blockCleanupResolution;
            private int m_cleanupAttempts;
            private int m_cleanupFailuresRemaining;
        }

        private sealed class DeterministicRecordProtector : IRecordProtector
        {
            public ByteString Protect(ByteString plaintext)
            {
                byte[] protectedRecord = new byte[plaintext.Length + 1];
                protectedRecord[0] = 0x5A;
                plaintext.Span.CopyTo(protectedRecord.AsSpan(1));
                return ByteString.From(protectedRecord);
            }

            public bool TryUnprotect(
                ByteString protectedRecord,
                out ByteString plaintext)
            {
                if (protectedRecord.Length < 1 ||
                    protectedRecord[0] != 0x5A)
                {
                    plaintext = default;
                    return false;
                }
                byte[] value = new byte[protectedRecord.Length - 1];
                protectedRecord.Span[1..].CopyTo(value);
                plaintext = ByteString.From(value);
                return true;
            }
        }
    }
}
