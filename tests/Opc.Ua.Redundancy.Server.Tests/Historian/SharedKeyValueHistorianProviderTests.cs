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

#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Tests.Redundancy;
using Opc.Ua.Tests;

namespace Opc.Ua.Redundancy.Server.Tests.Historian
{
    [TestFixture]
    [Category("Distributed")]
    [Category("Historian")]
    [Parallelizable(ParallelScope.All)]
    public sealed class SharedKeyValueHistorianProviderTests
    {
        [Test]
        public void ProcessLocalStoreIsRejected()
        {
            using var store = new InMemorySharedKeyValueStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var election = new TestElection(true);

            Assert.That(
                () => new SharedKeyValueHistorianProvider(
                    store,
                    CreateMessageContext(),
                    protector,
                    election),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task PassiveReplicaRejectsWritesAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(false));

            HistorianUpdateOutcome<DataValue> outcome =
                await provider.InsertAsync(
                    CreateOperationContext(),
                    new NodeId("v", 2),
                    [ValueAt(1, 1)],
                    default).ConfigureAwait(false);

            Assert.That(
                outcome.OperationResults,
                Is.EqualTo([StatusCodes.BadNotWritable]));
            Assert.That(
                (await store.TryGetAsync(
                    SharedKeyValueHistorianProvider.CurrentManifestKey)
                    .ConfigureAwait(false)).Found,
                Is.False);
        }

        [Test]
        public async Task ReplicaReadsCommittedLeaderWriteAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            await using SharedKeyValueHistorianProvider leader = CreateProvider(
                store,
                protector,
                new TestElection(true));
            await using SharedKeyValueHistorianProvider standby = CreateProvider(
                store,
                protector,
                new TestElection(false));
            var nodeId = new NodeId("v", 2);

            HistorianUpdateOutcome<DataValue> outcome =
                await leader.InsertAsync(
                    CreateOperationContext(),
                    nodeId,
                    [ValueAt(7, 1)],
                    default).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> page =
                await standby.ReadRawAsync(
                    CreateOperationContext(),
                    ReadRequest(nodeId, 0),
                    default,
                    default).ConfigureAwait(false);

            Assert.That(
                outcome.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(page.Values, Has.Count.EqualTo(1));
            Assert.That(page.Values[0].Value.WrappedValue, Is.EqualTo(Variant.From(7)));
        }

        [Test]
        public async Task ConcurrentLeadersAllowOnlyFencedWriterAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            await using SharedKeyValueHistorianProvider first = CreateProvider(
                store,
                protector,
                new TestElection(true));
            await using SharedKeyValueHistorianProvider second = CreateProvider(
                store,
                protector,
                new TestElection(true));
            var nodeId = new NodeId("v", 2);
            HistorianOperationContext context = CreateOperationContext();

            HistorianUpdateOutcome<DataValue>[] outcomes = await Task.WhenAll(
                first.InsertAsync(
                    context,
                    nodeId,
                    [ValueAt(1, 1)],
                    default).AsTask(),
                second.InsertAsync(
                    context,
                    nodeId,
                    [ValueAt(2, 1)],
                    default).AsTask()).ConfigureAwait(false);

            Assert.That(
                outcomes.Select(outcome => outcome.OperationResults[0]),
                Is.EquivalentTo(
                [
                    StatusCodes.GoodEntryInserted,
                    StatusCodes.BadNotWritable
                ]));
            HistorianPage<HistoricalDataValue> page = await first.ReadRawAsync(
                context,
                ReadRequest(nodeId, 0),
                default,
                default).ConfigureAwait(false);
            Assert.That(page.Values, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ManifestCasRetryDoesNotExposePartialBatchAsync()
        {
            using var store = new StrongTestStore
            {
                FailNextCurrentManifestCompareAndSwap = true
            };
            using AesCbcHmacRecordProtector protector = CreateProtector();
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true));
            var nodeId = new NodeId("v", 2);

            HistorianUpdateOutcome<DataValue> outcome =
                await provider.InsertAtomicAsync(
                    CreateOperationContext(),
                    nodeId,
                    [ValueAt(1, 1), ValueAt(2, 2)],
                    default).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> page =
                await provider.ReadRawAsync(
                    CreateOperationContext(),
                    ReadRequest(nodeId, 0),
                    default,
                    default).ConfigureAwait(false);

            Assert.That(outcome.TransactionRolledBack, Is.False);
            Assert.That(
                outcome.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(
                outcome.OperationResults[1],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(page.Values, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task StaleWriterIsRejectedAfterNewWriterEpochAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var timeProvider = new FakeTimeProvider(
                new DateTimeOffset(
                    2026,
                    1,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero));
            var options = new SharedKeyValueHistorianOptions
            {
                WriterFenceLeaseDuration = TimeSpan.FromMinutes(1)
            };
            var firstElection = new TestElection(true);
            var secondElection = new TestElection(true);
            await using SharedKeyValueHistorianProvider first = CreateProvider(
                store,
                protector,
                firstElection,
                options,
                timeProvider);
            await using SharedKeyValueHistorianProvider second = CreateProvider(
                store,
                protector,
                secondElection,
                options,
                timeProvider);
            var nodeId = new NodeId("fenced", 2);
            HistorianOperationContext context = CreateOperationContext();
            HistorianUpdateOutcome<DataValue> firstWrite =
                await first.InsertAsync(
                    context,
                    nodeId,
                    [ValueAt(1, 1)],
                    default).ConfigureAwait(false);
            HistorianUpdateOutcome<DataValue> secondWrite =
                await second.InsertAsync(
                    context,
                    nodeId,
                    [ValueAt(2, 2)],
                    default).ConfigureAwait(false);
            firstElection.IsLeader = false;
            timeProvider.Advance(TimeSpan.FromMinutes(2));
            HistorianUpdateOutcome<DataValue> takeoverWrite =
                await second.InsertAsync(
                    context,
                    nodeId,
                    [ValueAt(2, 2)],
                    default).ConfigureAwait(false);
            firstElection.IsLeader = true;

            HistorianUpdateOutcome<DataValue> staleWrite =
                await first.InsertAsync(
                    context,
                    nodeId,
                    [ValueAt(3, 3)],
                    default).ConfigureAwait(false);

            Assert.That(
                firstWrite.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(
                secondWrite.OperationResults[0],
                Is.EqualTo(StatusCodes.BadNotWritable));
            Assert.That(
                takeoverWrite.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(
                staleWrite.OperationResults[0],
                Is.EqualTo(StatusCodes.BadNotWritable));
        }

        [Test]
        public async Task CommitThenThrowManifestCasIsResolvedBeforeCleanupAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true));
            var nodeId = new NodeId("indeterminate-cas", 2);
            HistorianOperationContext context = CreateOperationContext();
            await provider.InsertAsync(
                context,
                nodeId,
                [ValueAt(1, 1)],
                default).ConfigureAwait(false);
            store.CommitThenThrowNextCurrentManifestCompareAndSwap =
                true;

            HistorianUpdateOutcome<DataValue> outcome =
                await provider.InsertAsync(
                    context,
                    nodeId,
                    [ValueAt(2, 2)],
                    default).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> page =
                await provider.ReadRawAsync(
                    context,
                    ReadRequest(nodeId, 0),
                    default,
                    default).ConfigureAwait(false);

            Assert.That(
                outcome.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(page.Values, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task CommitThenCancelManifestCasUsesNonCancelableReadbackAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true));
            var nodeId = new NodeId("indeterminate-cancel", 2);
            HistorianOperationContext context = CreateOperationContext();
            await provider.InsertAsync(
                context,
                nodeId,
                [ValueAt(1, 1)],
                default).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            store.CommitThenThrowNextCurrentManifestCompareAndSwap = true;
            store.CancelAfterCurrentManifestCommit = cancellation;

            HistorianUpdateOutcome<DataValue> outcome =
                await provider.InsertAsync(
                    context,
                    nodeId,
                    [ValueAt(2, 2)],
                    cancellation.Token).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> page =
                await provider.ReadRawAsync(
                    context,
                    ReadRequest(nodeId, 0),
                    default,
                    default).ConfigureAwait(false);

            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(
                outcome.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(page.Values, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task PortablePagePinsManifestDuringConcurrentWriteAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var options = new SharedKeyValueHistorianOptions
            {
                MaxValuesPerPage = 2
            };
            await using SharedKeyValueHistorianProvider leader = CreateProvider(
                store,
                protector,
                new TestElection(true),
                options);
            await using SharedKeyValueHistorianProvider standby = CreateProvider(
                store,
                protector,
                new TestElection(false),
                options);
            var nodeId = new NodeId("v", 2);
            HistorianOperationContext context = CreateOperationContext();
            await leader.InsertAtomicAsync(
                context,
                nodeId,
                [ValueAt(1, 1), ValueAt(3, 3), ValueAt(4, 4)],
                default).ConfigureAwait(false);

            HistorianPage<HistoricalDataValue> first =
                await standby.ReadRawAsync(
                    context,
                    ReadRequest(nodeId, 2),
                    default,
                    default).ConfigureAwait(false);
            await leader.InsertAsync(
                context,
                nodeId,
                [ValueAt(2, 2)],
                default).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> second =
                await standby.ReadRawAsync(
                    context,
                    ReadRequest(nodeId, 2),
                    first.NextToken,
                    default).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> current =
                await standby.ReadRawAsync(
                    context,
                    ReadRequest(nodeId, 0),
                    default,
                    default).ConfigureAwait(false);

            Assert.That(first.IsFinal, Is.False);
            Assert.That(
                first.Values.ToArray()!.Select(
                    value => value.Value.SourceTimestamp),
                Is.EqualTo([TimeAt(1), TimeAt(3)]));
            Assert.That(
                second.Values.ToArray()!.Select(
                    value => value.Value.SourceTimestamp),
                Is.EqualTo([TimeAt(4)]));
            Assert.That(current.Values, Has.Count.EqualTo(2));
            Assert.That(current.IsFinal, Is.False);
        }

        [Test]
        public async Task RenewedContinuationPinsGenerationAcrossCompactionAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var timeProvider = new FakeTimeProvider(
                new DateTimeOffset(
                    2026,
                    1,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero));
            var options = new SharedKeyValueHistorianOptions
            {
                MaxValuesPerPage = 1,
                CompactionSegmentThreshold = 1,
                ContinuationRetentionTime = TimeSpan.FromHours(1),
                GenerationRetentionTime = TimeSpan.FromHours(1),
                GarbageCollectionGraceTime = TimeSpan.Zero
            };
            IServiceMessageContext messageContext = CreateMessageContext();
            await using var provider = new SharedKeyValueHistorianProvider(
                store,
                messageContext,
                protector,
                new TestElection(true),
                options,
                timeProvider);
            await using var continuationStore =
                new SharedKeyValueHistoryContinuationStore(
                    store,
                    messageContext,
                    protector,
                    retentionTime: options.ContinuationRetentionTime,
                    timeProvider: timeProvider);
            var nodeId = new NodeId("retained", 2);
            HistorianOperationContext context = CreateOperationContext();
            await provider.InsertAtomicAsync(
                context,
                nodeId,
                [ValueAt(1, 1), ValueAt(2, 2), ValueAt(3, 3)],
                default).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> first =
                await provider.ReadRawAsync(
                    context,
                    ReadRequest(nodeId, 1),
                    default,
                    default).ConfigureAwait(false);
            HistoryContinuationPointEnvelope firstEnvelope = CreateEnvelope(
                first.NextToken);
            await continuationStore.StoreAsync(firstEnvelope)
                .ConfigureAwait(false);
            await provider.InsertAsync(
                context,
                nodeId,
                [ValueAt(4, 4)],
                default).ConfigureAwait(false);

            timeProvider.Advance(TimeSpan.FromMinutes(50));
            Assert.That(
                await continuationStore.TryTakeAsync(
                    firstEnvelope.OwnerSessionId,
                    firstEnvelope.Id).ConfigureAwait(false),
                Is.True);
            HistorianPage<HistoricalDataValue> second =
                await provider.ReadRawAsync(
                    context,
                    ReadRequest(nodeId, 1),
                    first.NextToken,
                    default).ConfigureAwait(false);
            HistoryContinuationPointEnvelope renewedEnvelope =
                CreateEnvelope(
                    second.NextToken,
                    firstEnvelope.OwnerSessionId);
            await continuationStore.StoreAsync(renewedEnvelope)
                .ConfigureAwait(false);

            timeProvider.Advance(TimeSpan.FromMinutes(20));
            await provider.RecoverGarbageCollectionAsync(default)
                .ConfigureAwait(false);
            ArrayOf<HistoryContinuationPointEnvelope> loaded =
                await continuationStore.LoadAsync(
                    renewedEnvelope.OwnerSessionId).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> third =
                await provider.ReadRawAsync(
                    context,
                    ReadRequest(nodeId, 1),
                    second.NextToken,
                    default).ConfigureAwait(false);

            Assert.That(loaded, Has.Count.EqualTo(1));
            Assert.That(third.Values, Has.Count.EqualTo(1));
            Assert.That(
                third.Values[0].Value.SourceTimestamp,
                Is.EqualTo(TimeAt(3)));
        }

        [Test]
        public async Task TamperedSegmentIsRejectedAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true));
            var nodeId = new NodeId("v", 2);
            await provider.InsertAsync(
                CreateOperationContext(),
                nodeId,
                [ValueAt(1, 1)],
                default).ConfigureAwait(false);
            string segmentKey = await store.FirstKeyAsync(
                "historian/v1/segments/").ConfigureAwait(false);
            await store.SetAsync(
                segmentKey,
                ByteString.From([0x12, 0x34, 0x56])).ConfigureAwait(false);

            ServiceResultException exception =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await provider.ReadRawAsync(
                        CreateOperationContext(),
                        ReadRequest(nodeId, 0),
                        default,
                        default).ConfigureAwait(false))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task RecoverySweepRetriesAndRemovesOrphanSegmentAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var options = new SharedKeyValueHistorianOptions
            {
                GarbageCollectionGraceTime = TimeSpan.Zero
            };
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true),
                options);
            await provider.InsertAsync(
                CreateOperationContext(),
                new NodeId("gc", 2),
                [ValueAt(1, 1)],
                default).ConfigureAwait(false);
            KeyValuePair<string, ByteString> segment =
                await store.FirstEntryAsync(
                    "historian/v1/segments/").ConfigureAwait(false);
            string orphanKey = "historian/v1/segments/" +
                Guid.NewGuid().ToString("N");
            await store.SetAsync(orphanKey, segment.Value)
                .ConfigureAwait(false);
            store.DeleteFailuresRemaining = 1;

            await provider.RecoverGarbageCollectionAsync(default)
                .ConfigureAwait(false);

            Assert.That(
                (await store.TryGetAsync(orphanKey).ConfigureAwait(false))
                    .Found,
                Is.False);
            Assert.That(provider.GarbageCollectionFailure, Is.Null);
        }

        [Test]
        public async Task RecoverySweepTransientDeleteFailureDoesNotPoisonWritesAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var options = new SharedKeyValueHistorianOptions
            {
                GarbageCollectionGraceTime = TimeSpan.Zero
            };
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true),
                options);
            await provider.InsertAsync(
                CreateOperationContext(),
                new NodeId("gc-failure", 2),
                [ValueAt(1, 1)],
                default).ConfigureAwait(false);
            KeyValuePair<string, ByteString> segment =
                await store.FirstEntryAsync(
                    "historian/v1/segments/").ConfigureAwait(false);
            string orphanKey = "historian/v1/segments/" +
                Guid.NewGuid().ToString("N");
            await store.SetAsync(orphanKey, segment.Value)
                .ConfigureAwait(false);
            store.DeleteFailuresRemaining = 3;

            Assert.That(
                async () => await provider.RecoverGarbageCollectionAsync(
                    default).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(provider.GarbageCollectionFailure, Is.Null);

            await provider.RecoverGarbageCollectionAsync(default)
                .ConfigureAwait(false);
            HistorianUpdateOutcome<DataValue> insert =
                await provider.InsertAsync(
                    CreateOperationContext(),
                    new NodeId("after-gc-recovery", 2),
                    [ValueAt(2, 2)],
                    default).ConfigureAwait(false);

            Assert.That(
                (await store.TryGetAsync(orphanKey).ConfigureAwait(false))
                    .Found,
                Is.False);
            Assert.That(provider.GarbageCollectionFailure, Is.Null);
            Assert.That(
                insert.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
        }

        [Test]
        public async Task RecoverySweepLatchesCorruptionUntilSuccessfulRecoveryAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var options = new SharedKeyValueHistorianOptions
            {
                GarbageCollectionGraceTime = TimeSpan.Zero
            };
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true),
                options);
            string corruptKey = "historian/v1/segments/" +
                Guid.NewGuid().ToString("N");
            await store.SetAsync(
                corruptKey,
                protector.Protect(ByteString.From([0x01, 0x02])))
                .ConfigureAwait(false);

            Assert.That(
                async () => await provider.RecoverGarbageCollectionAsync(
                    default).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(
                provider.GarbageCollectionFailure,
                Is.TypeOf<ServiceResultException>());
            Assert.That(
                async () => await provider.InsertAsync(
                    CreateOperationContext(),
                    new NodeId("blocked-by-corruption", 2),
                    [ValueAt(1, 1)],
                    default).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>());

            _ = await store.DeleteAsync(corruptKey).ConfigureAwait(false);
            await provider.RecoverGarbageCollectionAsync(default)
                .ConfigureAwait(false);

            Assert.That(provider.GarbageCollectionFailure, Is.Null);
        }

        [Test]
        public async Task BackgroundCleanupRetriesTransientDeleteWithoutPoisoningWritesAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var options = new SharedKeyValueHistorianOptions
            {
                MaxRecordsPerSegment = 1,
                CompactionSegmentThreshold = 1,
                ContinuationRetentionTime = TimeSpan.FromMilliseconds(10),
                GenerationRetentionTime = TimeSpan.FromMilliseconds(10),
                GarbageCollectionGraceTime = TimeSpan.Zero
            };
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true),
                options);
            HistorianOperationContext context = CreateOperationContext();
            var nodeId = new NodeId("background-gc", 2);
            await provider.InsertAsync(
                context,
                nodeId,
                [ValueAt(1, 1)],
                default).ConfigureAwait(false);
            string oldGeneration = await store.FirstKeyAsync(
                "historian/v1/manifest/generations/")
                .ConfigureAwait(false);
            store.DeleteFailuresRemaining = 4;

            await provider.InsertAsync(
                context,
                nodeId,
                [ValueAt(2, 2)],
                default).ConfigureAwait(false);

            bool removed = false;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (!(await store.TryGetAsync(oldGeneration)
                    .ConfigureAwait(false)).Found)
                {
                    removed = true;
                    break;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
            HistorianUpdateOutcome<DataValue> insert =
                await provider.InsertAsync(
                    context,
                    nodeId,
                    [ValueAt(3, 3)],
                    default).ConfigureAwait(false);

            Assert.That(removed, Is.True);
            Assert.That(provider.GarbageCollectionFailure, Is.Null);
            Assert.That(
                insert.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
        }

        [Test]
        public async Task AnnotationCountIsValidatedBoundedAndCancelableAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true));
            HistorianOperationContext context = CreateOperationContext();
            var request = new HistorianProcessedReadRequest
            {
                NodeId = new NodeId("annotations", 2),
                AggregateId = ObjectIds.AggregateFunction_AnnotationCount,
                StartTime = TimeAt(0),
                EndTime = TimeAt(10),
                ProcessingInterval = double.NaN,
                Configuration = new AggregateConfiguration()
            };

            ServiceResultException invalid =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await provider.ReadProcessedAsync(
                        context,
                        request,
                        default,
                        default).ConfigureAwait(false))!;
            ServiceResultException excessive =
                Assert.ThrowsAsync<ServiceResultException>(
                    async () => await provider.ReadProcessedAsync(
                        context,
                        request with
                        {
                            ProcessingInterval = 0.01
                        },
                        default,
                        default).ConfigureAwait(false))!;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(
                async () => await provider.ReadProcessedAsync(
                    context,
                    request with
                    {
                        ProcessingInterval = 1
                    },
                    default,
                    cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(
                invalid.StatusCode,
                Is.EqualTo(StatusCodes.BadAggregateInvalidInputs));
            Assert.That(
                excessive.StatusCode,
                Is.EqualTo(StatusCodes.BadTooManyOperations));
        }

        [Test]
        public async Task IdentityAndInterfacesAreStableAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var options = new SharedKeyValueHistorianOptions
            {
                ProviderId = "plant-a-history"
            };
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true),
                options);

            Assert.That(provider.ProviderId, Is.EqualTo("plant-a-history"));
            Assert.That(provider, Is.InstanceOf<IHistorianDataProvider>());
            Assert.That(provider, Is.InstanceOf<IHistorianModifiedProvider>());
            Assert.That(provider, Is.InstanceOf<IHistorianAtTimeProvider>());
            Assert.That(provider, Is.InstanceOf<IHistorianProcessedProvider>());
            Assert.That(provider, Is.InstanceOf<IHistorianAnnotationProvider>());
            Assert.That(provider, Is.InstanceOf<IHistorianEventProvider>());
            Assert.That(provider, Is.InstanceOf<IHistorianStructuredDataProvider>());
            Assert.That(provider, Is.InstanceOf<IHistorianBulkInsertProvider>());
            Assert.That(provider, Is.InstanceOf<IHistorianTransactionalProvider>());
            HistorianNodeCapabilities capabilities =
                await provider.GetCapabilitiesAsync(
                    new NodeId("v", 2),
                    default).ConfigureAwait(false);
            Assert.That(capabilities.PortableResumeTokens, Is.True);
            Assert.That(capabilities.ReadStructuredData, Is.False);
        }

        [Test]
        public async Task ModifiedAnnotationsAndEventsRoundTripAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true));
            var nodeId = new NodeId("v", 2);
            HistorianOperationContext context = CreateOperationContext();
            await provider.InsertAsync(
                context,
                nodeId,
                [ValueAt(1, 1)],
                default).ConfigureAwait(false);
            HistorianUpdateOutcome<DataValue> replace =
                await provider.ReplaceAsync(
                    context,
                    nodeId,
                    [ValueAt(2, 1)],
                    default).ConfigureAwait(false);
            var annotation = new Annotation
            {
                Message = "maintenance",
                UserName = "operator",
                AnnotationTime = TimeAt(2)
            };
            await provider.InsertAnnotationsAsync(
                context,
                nodeId,
                [annotation],
                default).ConfigureAwait(false);
            var eventRecord = new HistorianEventRecord(
                ByteString.From([1, 2, 3]),
                ObjectTypeIds.BaseEventType,
                TimeAt(3),
                new Dictionary<string, Variant>
                {
                    [BrowseNames.Message] = Variant.From(
                        LocalizedText.From("alarm"))
                }.ToArrayOf());
            await provider.InsertEventsAsync(
                context,
                nodeId,
                [eventRecord],
                default).ConfigureAwait(false);

            HistorianPage<ModifiedDataValue> modified =
                await provider.ReadModifiedAsync(
                    context,
                    new HistorianModifiedReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = TimeAt(0),
                        EndTime = TimeAt(10),
                        IsForward = true
                    },
                    default,
                    default).ConfigureAwait(false);
            HistorianPage<Annotation> annotations =
                await provider.ReadAnnotationsAsync(
                    context,
                    new HistorianAnnotationReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = TimeAt(0),
                        EndTime = TimeAt(10),
                        IsForward = true
                    },
                    default,
                    default).ConfigureAwait(false);
            HistorianPage<HistorianEventRecord> events =
                await provider.ReadEventsAsync(
                    context,
                    new HistorianEventReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = TimeAt(0),
                        EndTime = TimeAt(10),
                        IsForward = true,
                        Filter = new EventFilter()
                    },
                    default,
                    default).ConfigureAwait(false);

            Assert.That(replace.OldValues, Has.Count.EqualTo(1));
            Assert.That(modified.Values, Has.Count.EqualTo(1));
            Assert.That(annotations.Values, Has.Count.EqualTo(1));
            Assert.That(annotations.Values[0].Message, Is.EqualTo("maintenance"));
            Assert.That(events.Values, Has.Count.EqualTo(1));
            Assert.That(events.Values[0].EventId, Is.EqualTo(eventRecord.EventId));
        }

        [Test]
        public async Task EventReplaceAppliesIndexRangeAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true));
            var nodeId = new NodeId("event-range", 2);
            HistorianOperationContext context = CreateOperationContext();
            var eventId = ByteString.From([4, 3, 8, 7]);
            var fieldKey = new HistorianEventFieldKey(
                ObjectTypeIds.BaseEventType,
                [new QualifiedName("Tags", 2)],
                Attributes.Value,
                null);
            ArrayOf<string> originalTags = ["first", "second", "third"];
            var original = new HistorianEventRecord(
                eventId,
                ObjectTypeIds.BaseEventType,
                TimeAt(3),
                new Dictionary<string, Variant>
                {
                    ["Tags"] = new Variant(originalTags)
                }.ToArrayOf())
            {
                QualifiedFields =
                    new Dictionary<HistorianEventFieldKey, Variant>
                    {
                        [fieldKey] = new Variant(originalTags)
                    }.ToArrayOf()
            };
            await provider.InsertEventsAsync(
                context,
                nodeId,
                [original],
                default).ConfigureAwait(false);
            ArrayOf<string> replacementTag = ["changed"];
            var replacement = new HistorianEventRecord(
                eventId,
                NodeId.Null,
                DateTimeUtc.MinValue,
                new Dictionary<string, Variant>
                {
                    ["Tags"] = new Variant(replacementTag)
                }.ToArrayOf())
            {
                QualifiedFields =
                    new Dictionary<HistorianEventFieldKey, Variant>
                    {
                        [fieldKey with { IndexRange = "1" }] =
                            new Variant(replacementTag)
                    }.ToArrayOf()
            };

            HistorianUpdateOutcome<HistorianEventRecord> outcome =
                await provider.ReplaceEventsAsync(
                    context,
                    nodeId,
                    [replacement],
                    default).ConfigureAwait(false);
            HistorianPage<HistorianEventRecord> page =
                await provider.ReadEventsAsync(
                    context,
                    new HistorianEventReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = TimeAt(0),
                        EndTime = TimeAt(10),
                        IsForward = true,
                        Filter = new EventFilter()
                    },
                    default,
                    default).ConfigureAwait(false);

            Assert.That(
                outcome.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryReplaced));
            Assert.That(
                page.Values[0].TryGetQualifiedField(
                    fieldKey,
                    out Variant storedValue),
                Is.True);
            Assert.That(
                storedValue.TryGetValue(
                    out ArrayOf<string> storedTags),
                Is.True);
            Assert.That(storedTags.Count, Is.EqualTo(3));
            Assert.That(storedTags[0], Is.EqualTo("first"));
            Assert.That(storedTags[1], Is.EqualTo("changed"));
            Assert.That(storedTags[2], Is.EqualTo("third"));
        }

        [Test]
        public async Task EventReplaceRejectsBadRangesAndUsesCanonicalEventIdAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true));
            var nodeId = new NodeId("event-range-errors", 2);
            HistorianOperationContext context = CreateOperationContext();
            var eventId = ByteString.From([9, 8, 7, 6]);
            var tagsKey = new HistorianEventFieldKey(
                ObjectTypeIds.BaseEventType,
                [new QualifiedName("Tags", 2)],
                Attributes.Value,
                null);
            var canonicalEventIdKey = new HistorianEventFieldKey(
                ObjectTypeIds.BaseEventType,
                [new QualifiedName(BrowseNames.EventId)],
                Attributes.Value,
                null);
            var customEventIdKey = new HistorianEventFieldKey(
                new NodeId(1234, 2),
                [new QualifiedName(BrowseNames.EventId)],
                Attributes.Value,
                null);
            var original = new HistorianEventRecord(
                eventId,
                ObjectTypeIds.BaseEventType,
                TimeAt(3),
                [])
            {
                QualifiedFields =
                [
                    new(tagsKey, new Variant(["a", "b"])),
                    new(canonicalEventIdKey, new Variant(eventId)),
                    new(customEventIdKey, Variant.From("old"))
                ]
            };
            await provider.InsertEventsAsync(
                context,
                nodeId,
                [original],
                default).ConfigureAwait(false);
            HistorianEventRecord InvalidRange(string range)
            {
                return new HistorianEventRecord(
                    eventId,
                    NodeId.Null,
                    DateTimeUtc.MinValue,
                    [])
                {
                    QualifiedFields =
                    [
                        new(
                            tagsKey with { IndexRange = range },
                            new Variant(["x"]))
                    ]
                };
            }
            var missingRangeKey = new HistorianEventFieldKey(
                ObjectTypeIds.BaseEventType,
                [new QualifiedName("Missing", 2)],
                Attributes.Value,
                "0");
            var missingRange = new HistorianEventRecord(
                eventId,
                NodeId.Null,
                DateTimeUtc.MinValue,
                [])
            {
                QualifiedFields =
                [
                    new(
                        missingRangeKey,
                        new Variant(["x"]))
                ]
            };
            var identityUpdate = new HistorianEventRecord(
                eventId,
                NodeId.Null,
                DateTimeUtc.MinValue,
                [])
            {
                QualifiedFields =
                [
                    new(
                        canonicalEventIdKey,
                        new Variant(ByteString.From([1]))),
                    new(customEventIdKey, Variant.From("new"))
                ]
            };

            HistorianUpdateOutcome<HistorianEventRecord> invalid =
                await provider.ReplaceEventsAsync(
                    context,
                    nodeId,
                    [InvalidRange("invalid")],
                    default).ConfigureAwait(false);
            HistorianUpdateOutcome<HistorianEventRecord> noData =
                await provider.ReplaceEventsAsync(
                    context,
                    nodeId,
                    [missingRange],
                    default).ConfigureAwait(false);
            HistorianUpdateOutcome<HistorianEventRecord> identity =
                await provider.ReplaceEventsAsync(
                    context,
                    nodeId,
                    [identityUpdate],
                    default).ConfigureAwait(false);
            HistorianPage<HistorianEventRecord> page =
                await provider.ReadEventsAsync(
                    context,
                    new HistorianEventReadRequest
                    {
                        NodeId = nodeId,
                        StartTime = TimeAt(0),
                        EndTime = TimeAt(10),
                        IsForward = true,
                        Filter = new EventFilter()
                    },
                    default,
                    default).ConfigureAwait(false);

            Assert.That(
                invalid.OperationResults[0],
                Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
            Assert.That(
                noData.OperationResults[0],
                Is.EqualTo(StatusCodes.BadIndexRangeNoData));
            Assert.That(
                identity.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryReplaced));
            Assert.That(page.Values[0].EventId, Is.EqualTo(eventId));
            Assert.That(
                page.Values[0].TryGetQualifiedField(
                    canonicalEventIdKey,
                    out Variant canonicalEventId),
                Is.True);
            Assert.That(
                canonicalEventId.TryGetValue(out ByteString storedEventId),
                Is.True);
            Assert.That(storedEventId, Is.EqualTo(eventId));
            Assert.That(
                page.Values[0].TryGetQualifiedField(
                    customEventIdKey,
                    out Variant customEventId),
                Is.True);
            Assert.That(
                customEventId.TryGetValue(out string customValue),
                Is.True);
            Assert.That(customValue, Is.EqualTo("new"));
        }

        [Test]
        public async Task StructuredCompositeKeysAllowSameTimestampAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var nodeId = new NodeId("structured", 2);
            var options = new SharedKeyValueHistorianOptions
            {
                StructuredNodes =
                [
                    new SharedKeyValueStructuredHistorianNode
                    {
                        NodeId = nodeId,
                        KeySelector = Int32KeySelector.Instance
                    }
                ]
            };
            await using SharedKeyValueHistorianProvider provider = CreateProvider(
                store,
                protector,
                new TestElection(true),
                options);
            HistorianOperationContext context = CreateOperationContext();
            ArrayOf<DataValue> values =
            [
                ValueAt(1, 1),
                ValueAt(2, 1)
            ];

            HistorianUpdateOutcome<DataValue> insert =
                await provider.InsertStructuredDataAsync(
                    context,
                    nodeId,
                    values,
                    default).ConfigureAwait(false);
            HistorianUpdateOutcome<DataValue> duplicate =
                await provider.InsertStructuredDataAsync(
                    context,
                    nodeId,
                    [ValueAt(1, 1)],
                    default).ConfigureAwait(false);
            HistorianUpdateOutcome<DataValue> ordinary =
                await provider.InsertAsync(
                    context,
                    nodeId,
                    [ValueAt(3, 1)],
                    default).ConfigureAwait(false);
            ArrayOf<HistorianUpdateOutcome<DataValue>> bulk =
                await provider.InsertBatchAsync(
                    context,
                    [new HistorianDataBatch(nodeId, [ValueAt(4, 1)])],
                    default).ConfigureAwait(false);
            HistorianUpdateOutcome<DataValue> atomic =
                await provider.InsertAtomicAsync(
                    context,
                    nodeId,
                    [ValueAt(5, 1)],
                    default).ConfigureAwait(false);
            HistorianPage<HistoricalDataValue> page =
                await provider.ReadRawAsync(
                    context,
                    ReadRequest(nodeId, 0),
                    default,
                    default).ConfigureAwait(false);

            Assert.That(
                insert.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(
                insert.OperationResults[1],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(
                duplicate.OperationResults[0],
                Is.EqualTo(StatusCodes.BadEntryExists));
            Assert.That(
                ordinary.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(
                bulk[0].OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(
                atomic.OperationResults[0],
                Is.EqualTo(StatusCodes.GoodEntryInserted));
            Assert.That(page.Values, Has.Count.EqualTo(5));
            Assert.That(
                (await provider.GetCapabilitiesAsync(nodeId, default)
                    .ConfigureAwait(false)).ReadStructuredData,
                Is.True);
        }

        [Test]
        public async Task RegistrationKeepsExplicitHistorianProviderAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IHistorianProvider custom = Mock.Of<IHistorianProvider>();
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton<ISharedKeyValueStore>(store);
            builder.Services.AddSingleton<IRecordProtector>(protector);
            builder.Services.AddSingleton<ILeaderElection>(
                new TestElection(true));
            builder.Services.AddSingleton(custom);

            builder.UseDistributedHistorian();

            await using ServiceProvider services =
                builder.Services.BuildServiceProvider();
            Assert.That(
                services.GetRequiredService<IHistorianProvider>(),
                Is.SameAs(custom));
            Assert.That(
                services.GetRequiredService<
                    IHistoryContinuationPointStore>(),
                Is.InstanceOf<
                    SharedKeyValueHistoryContinuationStore>());
            Assert.That(
                services.GetRequiredService<IHistorianFencingAuthority>(),
                Is.InstanceOf<
                    SharedKeyValueHistorianFencingAuthority>());
            Assert.That(
                services.GetServices<IStrongKeyspaceProvider>()
                    .Any(provider => provider.GetStrongKeyPrefixes()
                        .Contains("historian/v1/")),
                Is.True);
            Assert.That(
                services.GetServices<IStrongKeyspaceProvider>()
                    .Any(provider => provider.GetStrongKeyPrefixes()
                        .Contains("history-continuation/v1/")),
                Is.True);
            Assert.That(
                services.GetServices<IStrongKeyspaceProvider>(),
                Has.Some.InstanceOf<
                    DistributedHistorianStrongKeyspaceProvider>());
        }

        [Test]
        public async Task RegistrationKeepsExplicitHistoryContinuationStoreAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            IHistoryContinuationPointStore custom = Mock.Of<IHistoryContinuationPointStore>();
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton<ISharedKeyValueStore>(store);
            builder.Services.AddSingleton<IRecordProtector>(protector);
            builder.Services.AddSingleton<ILeaderElection>(
                new TestElection(true));
            builder.Services.AddSingleton(custom);

            builder.UseDistributedHistorian();

            await using ServiceProvider services =
                builder.Services.BuildServiceProvider();
            Assert.That(
                services.GetRequiredService<
                    IHistoryContinuationPointStore>(),
                Is.SameAs(custom));
            Assert.That(
                services.GetRequiredService<
                    DistributedHistorianStartupTask>(),
                Is.Not.Null);
        }

        [Test]
        public void RegistrationRejectsProcessLocalStoreOnResolution()
        {
            var builder = new DiTestServerBuilder();
            builder.Services.AddSingleton<ISharedKeyValueStore>(
                new InMemorySharedKeyValueStore());
            builder.Services.AddSingleton<IRecordProtector>(CreateProtector());
            builder.Services.AddSingleton<ILeaderElection>(
                new TestElection(true));
            builder.UseDistributedHistorian();

            using ServiceProvider services =
                builder.Services.BuildServiceProvider();

            Assert.That(
                () => services.GetRequiredService<
                    SharedKeyValueHistorianProvider>(),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task HybridStoreResolvesPrefixContributorWithoutCycleAsync()
        {
            var builder = new DiTestServerBuilder();
            builder.UseRedundancyConsistency(
                    RedundancyConsistencyMode.Eventual)
                .UseDistributedHistorian();
            await using ServiceProvider services =
                builder.Services.BuildServiceProvider();

            ISharedKeyValueStore store =
                services.GetRequiredService<ISharedKeyValueStore>();
            var consistency =
                (ISharedKeyValueStoreConsistency)store;

            Assert.That(store, Is.InstanceOf<HybridSharedKeyValueStore>());
            Assert.That(consistency.IsLinearizable("historian/v1/a"), Is.True);
            Assert.That(
                consistency.IsLinearizable("history-continuation/v1/a"),
                Is.True);
        }

        [Test]
        public async Task StartupRegistersSelectedProviderAndStartsElectionAsync()
        {
            using var store = new StrongTestStore();
            using AesCbcHmacRecordProtector protector = CreateProtector();
            var election = new TestElection(true);
            await using var provider = new SharedKeyValueHistorianProvider(
                store,
                protector,
                election);
            var namespaceUris = new NamespaceTable();
            var registry = new HistorianProviderRegistry(namespaceUris);
            var internalServer = new Mock<IServerInternal>();
            internalServer.Setup(value => value.NamespaceUris)
                .Returns(namespaceUris);
            internalServer.Setup(value => value.ServerUris)
                .Returns(new StringTable());
            internalServer.Setup(value => value.TypeTree)
                .Returns(new TypeTable(namespaceUris));
            internalServer.Setup(value => value.Factory)
                .Returns(EncodeableFactory.Create());
            internalServer.Setup(value => value.Telemetry)
                .Returns(Mock.Of<ITelemetryContext>());
            internalServer.As<IHistorianRegistryProvider>()
                .SetupGet(value => value.HistorianRegistry)
                .Returns(registry);
            var systemContext = new ServerSystemContext(
                internalServer.Object,
                new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.HistoryRead,
                    RequestLifetime.None));
            var server = new Mock<IServerContext>();
            IServiceMessageContext messageContext = CreateMessageContext();
            server.SetupGet(value => value.MessageContext)
                .Returns(messageContext);
            server.SetupGet(value => value.DefaultSystemContext)
                .Returns(systemContext);
            await using var continuationStore =
                new SharedKeyValueHistoryContinuationStore(
                    store,
                    protector);
            var startup = new DistributedHistorianStartupTask(
                store,
                provider,
                provider,
                election,
                continuationStore);

            await startup.OnServerStartedAsync(server.Object)
                .ConfigureAwait(false);
            var envelope = new HistoryContinuationPointEnvelope
            {
                Id = Guid.NewGuid(),
                OwnerSessionId = new NodeId("session", 2),
                CodecId = "test",
                CodecVersion = 1,
                Payload = (ByteString)new byte[] { 1 }
            };
            await continuationStore.StoreAsync(envelope)
                .ConfigureAwait(false);

            Assert.That(
                registry.Resolve(new NodeId("v", 2)),
                Is.SameAs(provider));
            Assert.That(election.Started, Is.True);
            Assert.That(
                await continuationStore.TryTakeAsync(
                    envelope.OwnerSessionId,
                    envelope.Id).ConfigureAwait(false),
                Is.True);
        }

        private static SharedKeyValueHistorianProvider CreateProvider(
            StrongTestStore store,
            IRecordProtector protector,
            ILeaderElection election,
            SharedKeyValueHistorianOptions? options = null,
            TimeProvider? timeProvider = null)
        {
            return new SharedKeyValueHistorianProvider(
                store,
                CreateMessageContext(),
                protector,
                election,
                options,
                timeProvider);
        }

        private static HistorianRawReadRequest ReadRequest(
            NodeId nodeId,
            uint maxValues)
        {
            return new HistorianRawReadRequest
            {
                NodeId = nodeId,
                StartTime = TimeAt(0),
                EndTime = TimeAt(10),
                MaxValues = maxValues,
                IsForward = true
            };
        }

        private static HistoryContinuationPointEnvelope CreateEnvelope(
            HistorianResumeToken token,
            NodeId ownerSessionId = default)
        {
            return new HistoryContinuationPointEnvelope
            {
                Id = Guid.NewGuid(),
                OwnerSessionId = ownerSessionId.IsNull
                    ? new NodeId("session", 2)
                    : ownerSessionId,
                CodecId = "shared-historian-test",
                CodecVersion = 1,
                Payload = token.State
            };
        }

        private static DataValue ValueAt(int value, int second)
        {
            return new DataValue(
                Variant.From(value),
                StatusCodes.Good,
                TimeAt(second),
                DateTimeUtc.MinValue);
        }

        private static DateTimeUtc TimeAt(int second)
        {
            return new DateTimeUtc(
                new DateTime(2026, 1, 1, 0, 0, second, DateTimeKind.Utc));
        }

        private static HistorianOperationContext CreateOperationContext()
        {
            var telemetry = new Mock<ITelemetryContext>();
            var server = new Mock<IServerInternal>();
            var namespaceUris = new NamespaceTable();
            server.Setup(value => value.NamespaceUris).Returns(namespaceUris);
            server.Setup(value => value.ServerUris).Returns(new StringTable());
            server.Setup(value => value.TypeTree).Returns(
                new TypeTable(namespaceUris));
            server.Setup(value => value.Factory).Returns(
                EncodeableFactory.Create());
            server.Setup(value => value.Telemetry).Returns(telemetry.Object);
            var operationContext = new OperationContext(
                new RequestHeader(),
                null,
                RequestType.HistoryUpdate,
                RequestLifetime.None);
            var systemContext = new ServerSystemContext(
                server.Object,
                operationContext);
            return new HistorianOperationContext(
                systemContext,
                operationContext,
                null,
                HistoryUpdateType.Insert);
        }

        private static ServiceMessageContext CreateMessageContext()
        {
            return ServiceMessageContext.CreateEmpty(
                NUnitTelemetryContext.Create());
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

        private sealed class Int32KeySelector :
            IHistorianStructuredDataKeySelector
        {
            public static Int32KeySelector Instance { get; } = new();

            public ArrayOf<QualifiedName> UniquenessFields { get; } =
                [new QualifiedName("Value")];

            public bool TryGetUniquenessKey(
                in DataValue value,
                out ByteString uniquenessKey)
            {
                if (!value.WrappedValue.TryGetValue(out int key))
                {
                    uniquenessKey = ByteString.Empty;
                    return false;
                }
                byte[] bytes = new byte[sizeof(int)];
                BinaryPrimitives.WriteInt32LittleEndian(bytes, key);
                uniquenessKey = ByteString.From(bytes);
                return true;
            }
        }

        private sealed class TestElection : ILeaderElection
        {
            public TestElection(bool isLeader)
            {
                m_isLeader = isLeader;
            }

            public bool IsLeader
            {
                get => m_isLeader;
                set
                {
                    if (m_isLeader == value)
                    {
                        return;
                    }
                    m_isLeader = value;
                    LeadershipChanged?.Invoke(value);
                }
            }

            public bool Started { get; private set; }

            public event Action<bool>? LeadershipChanged;

            public ValueTask<bool> TryAcquireOrRenewAsync(
                CancellationToken ct = default)
            {
                return new(IsLeader);
            }

            public void Start()
            {
                Started = true;
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            private bool m_isLeader;
        }

        private sealed class StrongTestStore :
            ISharedKeyValueStore,
            ISharedKeyValueStoreConsistency,
            IDisposable
        {
            public bool FailNextCurrentManifestCompareAndSwap { get; set; }

            public bool CommitThenThrowNextCurrentManifestCompareAndSwap { get; set; }

            public CancellationTokenSource? CancelAfterCurrentManifestCommit { get; set; }

            public int DeleteFailuresRemaining { get; set; }

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
                if (FailNextCurrentManifestCompareAndSwap &&
                    key == SharedKeyValueHistorianProvider.CurrentManifestKey)
                {
                    FailNextCurrentManifestCompareAndSwap = false;
                    return false;
                }
                bool result = await m_inner.CompareAndSwapAsync(
                    key,
                    expected,
                    value,
                    ct).ConfigureAwait(false);
                if (result &&
                    CommitThenThrowNextCurrentManifestCompareAndSwap &&
                    key == SharedKeyValueHistorianProvider.CurrentManifestKey)
                {
                    CommitThenThrowNextCurrentManifestCompareAndSwap =
                        false;
                    CancelAfterCurrentManifestCommit?.Cancel();
                    CancelAfterCurrentManifestCommit = null;
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
                if (DeleteFailuresRemaining > 0)
                {
                    DeleteFailuresRemaining--;
                    throw new InvalidOperationException(
                        "Injected delete failure.");
                }
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

            public async Task<string> FirstKeyAsync(string prefix)
            {
                await foreach (KeyValuePair<string, ByteString> item in
                    m_inner.ScanAsync(prefix).ConfigureAwait(false))
                {
                    return item.Key;
                }
                throw new AssertionException("No matching key was found.");
            }

            public async Task<KeyValuePair<string, ByteString>> FirstEntryAsync(
                string prefix)
            {
                await foreach (KeyValuePair<string, ByteString> item in
                    m_inner.ScanAsync(prefix).ConfigureAwait(false))
                {
                    return item;
                }
                throw new AssertionException("No matching entry was found.");
            }

            public void Dispose()
            {
                m_inner.Dispose();
            }

            private readonly InMemorySharedKeyValueStore m_inner = new();
        }
    }
}
