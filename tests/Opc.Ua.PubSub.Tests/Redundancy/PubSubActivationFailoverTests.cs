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
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.Redundancy;
using Opc.Ua.PubSub.Scheduling;
using Opc.Ua.Redundancy;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Redundancy
{
    /// <summary>
    /// Covers the Part 14 §9.1.6 activation coordinator failover wiring used
    /// by redundant PubSub publishers and subscribers.
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    [Category("Redundancy")]
    [TestSpec("9.1.6", Summary = "PubSub HA activation coordinator failover")]
    public class PubSubActivationFailoverTests
    {
        private const string WriterComponentId = "pubsub:writergroup:conn:wg";
        private const string ReaderComponentId = "pubsub:readergroup:conn:rg";

        [Test]
        [TestSpec("9.1.6")]
        public async Task LeaseActivationCoordinator_ElectsSingleActiveAndFailsOverOnStopAsync()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var store = new InMemoryPubSubLeaseStore(clock);
            var ttl = TimeSpan.FromSeconds(9);
            var interval = TimeSpan.FromSeconds(1);
            var firstEvents = new List<PubSubRoleChangedEventArgs>();
            var secondEvents = new List<PubSubRoleChangedEventArgs>();

            await using var first = new LeaseActivationCoordinator(
                store,
                NUnitTelemetryContext.Create(),
                ownerId: "publisher-a",
                leaseDuration: ttl,
                renewInterval: interval,
                retryInterval: interval,
                timeProvider: clock);
            await using var second = new LeaseActivationCoordinator(
                store,
                NUnitTelemetryContext.Create(),
                ownerId: "publisher-b",
                leaseDuration: ttl,
                renewInterval: interval,
                retryInterval: interval,
                timeProvider: clock);
            first.RoleChanged += (_, e) => firstEvents.Add(e);
            second.RoleChanged += (_, e) => secondEvents.Add(e);

            await first.StartAsync().ConfigureAwait(false);
            await second.StartAsync().ConfigureAwait(false);
            _ = await first.GetRoleAsync(WriterComponentId).ConfigureAwait(false);
            _ = await second.GetRoleAsync(WriterComponentId).ConfigureAwait(false);

            await WaitForRolesAsync(
                clock,
                first,
                second,
                activeCount: 1).ConfigureAwait(false);

            PubSubComponentRole firstRole = await first.GetRoleAsync(WriterComponentId).ConfigureAwait(false);
            LeaseActivationCoordinator active = firstRole == PubSubComponentRole.Active ? first : second;
            LeaseActivationCoordinator standby = ReferenceEquals(active, first) ? second : first;
            List<PubSubRoleChangedEventArgs> standbyEvents = ReferenceEquals(standby, first)
                ? firstEvents
                : secondEvents;

            await active.StopAsync().ConfigureAwait(false);
            clock.Advance(interval);

            await WaitForRoleAsync(
                clock,
                standby,
                PubSubComponentRole.Active).ConfigureAwait(false);

            Assert.That(
                standbyEvents,
                Has.Some.Matches<PubSubRoleChangedEventArgs>(e =>
                    string.Equals(e.ComponentId, WriterComponentId, StringComparison.Ordinal) &&
                    e.Role == PubSubComponentRole.Active));
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task SharedStoreLeaseActivationCoordinatorElectsSingleActiveAndFailsOverAsync()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using var sharedBackend = new InMemorySharedKeyValueStore();
            var store = new SharedStorePubSubLeaseStore(sharedBackend, clock);
            var ttl = TimeSpan.FromSeconds(9);
            var interval = TimeSpan.FromSeconds(1);

            await using var first = new LeaseActivationCoordinator(
                store,
                NUnitTelemetryContext.Create(),
                ownerId: "publisher-a",
                leaseDuration: ttl,
                renewInterval: interval,
                retryInterval: interval,
                timeProvider: clock);
            await using var second = new LeaseActivationCoordinator(
                store,
                NUnitTelemetryContext.Create(),
                ownerId: "publisher-b",
                leaseDuration: ttl,
                renewInterval: interval,
                retryInterval: interval,
                timeProvider: clock);

            await first.StartAsync().ConfigureAwait(false);
            await second.StartAsync().ConfigureAwait(false);
            _ = await first.GetRoleAsync(WriterComponentId).ConfigureAwait(false);
            _ = await second.GetRoleAsync(WriterComponentId).ConfigureAwait(false);

            await WaitForRolesAsync(
                clock,
                first,
                second,
                activeCount: 1).ConfigureAwait(false);

            PubSubComponentRole firstRole = await first.GetRoleAsync(WriterComponentId).ConfigureAwait(false);
            PubSubComponentRole secondRole = await second.GetRoleAsync(WriterComponentId).ConfigureAwait(false);
            LeaseActivationCoordinator active = firstRole == PubSubComponentRole.Active ? first : second;
            LeaseActivationCoordinator standby = ReferenceEquals(active, first) ? second : first;

            Assert.That(
                new[] { firstRole, secondRole },
                Has.Exactly(1).EqualTo(PubSubComponentRole.Active));

            await active.DisposeAsync().ConfigureAwait(false);
            clock.Advance(ttl + interval);
            await Task.Delay(25).ConfigureAwait(false);

            await WaitForRoleAsync(
                clock,
                standby,
                PubSubComponentRole.Active).ConfigureAwait(false);
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task InMemoryLeaseStore_IncrementsFencingTokenOnOwnershipChangeAsync()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var store = new InMemoryPubSubLeaseStore(clock);
            var ttl = TimeSpan.FromSeconds(30);

            PubSubLease? firstLease = await store.TryAcquireAsync(
                WriterComponentId,
                "owner-a",
                ttl).ConfigureAwait(false);
            Assert.That(firstLease, Is.Not.Null);
            PubSubLease first = firstLease.GetValueOrDefault();

            PubSubLease? blockedLease = await store.TryAcquireAsync(
                WriterComponentId,
                "owner-b",
                ttl).ConfigureAwait(false);
            Assert.That(blockedLease, Is.Null);

            await store.ReleaseAsync(first).ConfigureAwait(false);
            PubSubLease? secondLease = await store.TryAcquireAsync(
                WriterComponentId,
                "owner-b",
                ttl).ConfigureAwait(false);

            Assert.That(secondLease, Is.Not.Null);
            PubSubLease second = secondLease.GetValueOrDefault();
            Assert.That(second.FencingToken, Is.GreaterThan(first.FencingToken));
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task InMemoryLeaseStore_RejectsRenewalOfExpiredLeaseAsync()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var store = new InMemoryPubSubLeaseStore(clock);
            var ttl = TimeSpan.FromSeconds(10);

            PubSubLease? acquired = await store.TryAcquireAsync(
                WriterComponentId,
                "owner-a",
                ttl).ConfigureAwait(false);
            Assert.That(acquired, Is.Not.Null);
            PubSubLease lease = acquired.GetValueOrDefault();

            PubSubLease? renewedInTime = await store.TryRenewAsync(lease, ttl).ConfigureAwait(false);
            Assert.That(renewedInTime, Is.Not.Null);

            clock.Advance(ttl + TimeSpan.FromSeconds(1));

            PubSubLease? renewedExpired = await store.TryRenewAsync(
                renewedInTime.GetValueOrDefault(),
                ttl).ConfigureAwait(false);
            Assert.That(renewedExpired, Is.Null);

            PubSubLease? reacquired = await store.TryAcquireAsync(
                WriterComponentId,
                "owner-a",
                ttl).ConfigureAwait(false);
            Assert.That(reacquired, Is.Not.Null);
            Assert.That(
                reacquired.GetValueOrDefault().FencingToken,
                Is.GreaterThan(lease.FencingToken));
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task WriterGroup_StandbyRolePausesPublishingUntilActiveAsync()
        {
            PubSubComponentRole role = PubSubComponentRole.Standby;
            Mock<IPubSubActivationCoordinator> coordinator = CreateCoordinatorMock(
                WriterComponentId,
                () => role);
            var captured = new List<PubSubNetworkMessage>();
            WriterGroup group = CreateWriterGroup(coordinator.Object, captured);

            await group.EnableAsync().ConfigureAwait(false);
            await group.PublishOnceAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(group.State.State, Is.EqualTo(PubSubState.Paused));
                Assert.That(captured, Is.Empty);
            });

            role = PubSubComponentRole.Active;
            coordinator.Raise(
                c => c.RoleChanged += null!,
                new PubSubRoleChangedEventArgs(WriterComponentId, PubSubComponentRole.Active));
            await group.PublishOnceAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(group.State.State, Is.EqualTo(PubSubState.Operational));
                Assert.That(captured, Has.Count.EqualTo(1));
            });
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task WriterGroup_HotStandbyContinuesSequenceNumberFromSharedCheckpointAsync()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using var sharedBackend = new InMemorySharedKeyValueStore();
            var checkpointStore = new SharedStorePubSubWriterCheckpointStore(sharedBackend);
            const PubSubComponentRole activeRole = PubSubComponentRole.Active;
            PubSubComponentRole standbyRole = PubSubComponentRole.Standby;
            Mock<IPubSubActivationCoordinator> activeCoordinator = CreateCoordinatorMock(
                WriterComponentId,
                () => activeRole);
            Mock<IPubSubActivationCoordinator> standbyCoordinator = CreateCoordinatorMock(
                WriterComponentId,
                () => standbyRole);
            var activeMessages = new List<PubSubNetworkMessage>();
            var promotedMessages = new List<PubSubNetworkMessage>();
            WriterGroup activeGroup = CreateWriterGroup(activeCoordinator.Object, activeMessages, clock);
            WriterGroup promotedGroup = CreateWriterGroup(standbyCoordinator.Object, promotedMessages, clock);
            activeGroup.ConfigureWriterCheckpointStore(checkpointStore);
            promotedGroup.ConfigureWriterCheckpointStore(checkpointStore);

            await activeGroup.EnableAsync().ConfigureAwait(false);
            await activeGroup.PublishOnceAsync().ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMilliseconds(1100));
            await activeGroup.PublishOnceAsync().ConfigureAwait(false);
            clock.Advance(TimeSpan.FromMilliseconds(1100));
            await activeGroup.PublishOnceAsync().ConfigureAwait(false);
            uint lastActiveSequenceNumber = GetSingleDataSetMessage(activeMessages[^1]).SequenceNumber;

            await promotedGroup.EnableAsync().ConfigureAwait(false);
            standbyRole = PubSubComponentRole.Active;
            standbyCoordinator.Raise(
                c => c.RoleChanged += null!,
                new PubSubRoleChangedEventArgs(WriterComponentId, PubSubComponentRole.Active));
            await promotedGroup.PublishOnceAsync().ConfigureAwait(false);

            Assert.That(promotedMessages, Has.Count.EqualTo(1));
            uint promotedSequenceNumber = GetSingleDataSetMessage(promotedMessages[0]).SequenceNumber;
            Assert.Multiple(() =>
            {
                Assert.That(lastActiveSequenceNumber, Is.GreaterThan(1u));
                Assert.That(promotedSequenceNumber, Is.GreaterThan(lastActiveSequenceNumber));
            });
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task WriterGroup_ColdStandbyWithoutCheckpointResetsSequenceNumberAsync()
        {
            var clock = new FakeTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            const PubSubComponentRole activeRole = PubSubComponentRole.Active;
            PubSubComponentRole standbyRole = PubSubComponentRole.Standby;
            Mock<IPubSubActivationCoordinator> activeCoordinator = CreateCoordinatorMock(
                WriterComponentId,
                () => activeRole);
            Mock<IPubSubActivationCoordinator> standbyCoordinator = CreateCoordinatorMock(
                WriterComponentId,
                () => standbyRole);
            var activeMessages = new List<PubSubNetworkMessage>();
            var promotedMessages = new List<PubSubNetworkMessage>();
            WriterGroup activeGroup = CreateWriterGroup(activeCoordinator.Object, activeMessages, clock);
            WriterGroup promotedGroup = CreateWriterGroup(standbyCoordinator.Object, promotedMessages, clock);

            await activeGroup.EnableAsync().ConfigureAwait(false);
            await activeGroup.PublishOnceAsync().ConfigureAwait(false);
            await activeGroup.PublishOnceAsync().ConfigureAwait(false);
            uint lastActiveSequenceNumber = GetSingleDataSetMessage(activeMessages[^1]).SequenceNumber;

            await promotedGroup.EnableAsync().ConfigureAwait(false);
            standbyRole = PubSubComponentRole.Active;
            standbyCoordinator.Raise(
                c => c.RoleChanged += null!,
                new PubSubRoleChangedEventArgs(WriterComponentId, PubSubComponentRole.Active));
            await promotedGroup.PublishOnceAsync().ConfigureAwait(false);

            Assert.That(lastActiveSequenceNumber, Is.GreaterThan(1u));
            Assert.That(GetSingleDataSetMessage(promotedMessages[0]).SequenceNumber, Is.EqualTo(1u));
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task WriterGroup_ActiveTakeoverRestoresSequenceNumberFromCheckpointAsync()
        {
            const uint checkpoint = 5000;
            PubSubComponentRole role = PubSubComponentRole.Standby;
            Mock<IPubSubActivationCoordinator> coordinator = CreateCoordinatorMock(
                WriterComponentId,
                () => role);
            var captured = new List<PubSubNetworkMessage>();
            WriterGroup group = CreateWriterGroup(coordinator.Object, captured);
            group.ConfigureWriterCheckpointStore(new FixedCheckpointStore(checkpoint));

            await group.EnableAsync().ConfigureAwait(false);
            role = PubSubComponentRole.Active;
            coordinator.Raise(
                c => c.RoleChanged += null!,
                new PubSubRoleChangedEventArgs(WriterComponentId, PubSubComponentRole.Active));
            await group.PublishOnceAsync().ConfigureAwait(false);

            Assert.That(captured, Has.Count.EqualTo(1));
            UadpDataSetMessage dataSetMessage = GetSingleDataSetMessage(captured[0]);
            Assert.That(dataSetMessage.SequenceNumber, Is.GreaterThan(checkpoint));
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task WriterGroup_NullCheckpointStoreKeepsInitialSequenceNumberAsync()
        {
            PubSubComponentRole role = PubSubComponentRole.Standby;
            Mock<IPubSubActivationCoordinator> coordinator = CreateCoordinatorMock(
                WriterComponentId,
                () => role);
            var captured = new List<PubSubNetworkMessage>();
            WriterGroup group = CreateWriterGroup(coordinator.Object, captured);

            await group.EnableAsync().ConfigureAwait(false);
            role = PubSubComponentRole.Active;
            coordinator.Raise(
                c => c.RoleChanged += null!,
                new PubSubRoleChangedEventArgs(WriterComponentId, PubSubComponentRole.Active));
            await group.PublishOnceAsync().ConfigureAwait(false);

            Assert.That(captured, Has.Count.EqualTo(1));
            UadpDataSetMessage dataSetMessage = GetSingleDataSetMessage(captured[0]);
            Assert.That(dataSetMessage.SequenceNumber, Is.EqualTo(1u));
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task WriterGroup_RetriesRestoreOnNextTickAfterTransientCheckpointFailureAsync()
        {
            const uint checkpoint = 5000;
            PubSubComponentRole role = PubSubComponentRole.Standby;
            Mock<IPubSubActivationCoordinator> coordinator = CreateCoordinatorMock(
                WriterComponentId,
                () => role);
            var captured = new List<PubSubNetworkMessage>();
            WriterGroup group = CreateWriterGroup(coordinator.Object, captured);
            group.ConfigureWriterCheckpointStore(new ThrowOnceCheckpointStore(checkpoint));

            await group.EnableAsync().ConfigureAwait(false);
            role = PubSubComponentRole.Active;
            coordinator.Raise(
                c => c.RoleChanged += null!,
                new PubSubRoleChangedEventArgs(WriterComponentId, PubSubComponentRole.Active));

            await group.PublishOnceAsync().ConfigureAwait(false);
            await group.PublishOnceAsync().ConfigureAwait(false);

            Assert.That(captured, Has.Count.EqualTo(1));
            UadpDataSetMessage dataSetMessage = GetSingleDataSetMessage(captured[0]);
            Assert.That(dataSetMessage.SequenceNumber, Is.GreaterThan(checkpoint));
        }

        [Test]
        [TestSpec("9.1.6")]
        public async Task ReaderGroup_StandbyRoleSuppressesDispatchUntilActiveAsync()
        {
            PubSubComponentRole role = PubSubComponentRole.Standby;
            Mock<IPubSubActivationCoordinator> coordinator = CreateCoordinatorMock(
                ReaderComponentId,
                () => role);
            var sink = new CountingSink();
            ReaderGroup group = CreateReaderGroup(coordinator.Object, sink);
            PubSubNetworkMessage message = CreateNetworkMessage();

            await group.EnableAsync().ConfigureAwait(false);
            await group.DispatchAsync(message).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(group.State.State, Is.EqualTo(PubSubState.Paused));
                Assert.That(sink.CallCount, Is.Zero);
            });

            role = PubSubComponentRole.Active;
            coordinator.Raise(
                c => c.RoleChanged += null!,
                new PubSubRoleChangedEventArgs(ReaderComponentId, PubSubComponentRole.Active));
            await group.DispatchAsync(message).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(group.State.State, Is.EqualTo(PubSubState.Operational));
                Assert.That(sink.CallCount, Is.EqualTo(1));
            });
        }

        private static Mock<IPubSubActivationCoordinator> CreateCoordinatorMock(
            string componentId,
            Func<PubSubComponentRole> role)
        {
            var coordinator = new Mock<IPubSubActivationCoordinator>(MockBehavior.Strict);
            coordinator
                .Setup(c => c.GetRoleAsync(componentId, It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<PubSubComponentRole>(role()));
            coordinator
                .Setup(c => c.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            coordinator
                .Setup(c => c.StopAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            return coordinator;
        }

        private static WriterGroup CreateWriterGroup(
            IPubSubActivationCoordinator coordinator,
            List<PubSubNetworkMessage> captured,
            TimeProvider? timeProvider = null)
        {
            var dataSetConfig = new PublishedDataSetDataType
            {
                Name = "pds",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Fields = [new FieldMetaData { Name = "value" }]
                }
            };
            var dataSet = new PublishedDataSet(dataSetConfig, new CountingSource());
            var writer = new DataSetWriter(
                new DataSetWriterDataType
                {
                    Name = "writer",
                    DataSetWriterId = 1,
                    DataSetName = "pds",
                    KeyFrameCount = 1
                },
                dataSet,
                NUnitTelemetryContext.Create());
            return new WriterGroup(
                new WriterGroupDataType
                {
                    Name = "wg",
                    WriterGroupId = 1,
                    PublishingInterval = 100
                },
                [writer],
                new PubSubSchedule(
                    TimeSpan.FromMilliseconds(100),
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero),
                NoOpScheduler.Instance,
                NUnitTelemetryContext.Create(),
                timeProvider ?? TimeProvider.System,
                coordinator,
                WriterComponentId)
            {
                PublishSink = (message, _) =>
                {
                    captured.Add(message);
                    return default;
                }
            };
        }

        private static ReaderGroup CreateReaderGroup(
            IPubSubActivationCoordinator coordinator,
            ISubscribedDataSetSink sink)
        {
            var reader = new DataSetReader(
                new DataSetReaderDataType
                {
                    Name = "reader",
                    DataSetWriterId = 1
                },
                sink,
                NUnitTelemetryContext.Create(),
                TimeProvider.System);
            return new ReaderGroup(
                new ReaderGroupDataType { Name = "rg" },
                [reader],
                NUnitTelemetryContext.Create(),
                scheduler: null,
                diagnostics: null,
                coordinator,
                ReaderComponentId);
        }

        private static UadpNetworkMessage CreateNetworkMessage()
        {
            return new UadpNetworkMessage
            {
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1,
                        Fields = [new DataSetField { Name = "value", Value = new Variant(1) }]
                    }
                ]
            };
        }

        private static UadpDataSetMessage GetSingleDataSetMessage(PubSubNetworkMessage message)
        {
            var networkMessage = (UadpNetworkMessage)message;
            Assert.That(((PubSubDataSetMessage[]?)networkMessage.DataSetMessages) ?? [], Has.Length.EqualTo(1));
            return (UadpDataSetMessage)networkMessage.DataSetMessages[0];
        }

        private static async Task WaitForRolesAsync(
            FakeTimeProvider clock,
            LeaseActivationCoordinator first,
            LeaseActivationCoordinator second,
            int activeCount)
        {
            await WaitForConditionAsync(clock, async () =>
            {
                int count = 0;
                if (await first.GetRoleAsync(WriterComponentId).ConfigureAwait(false) == PubSubComponentRole.Active)
                {
                    count++;
                }
                if (await second.GetRoleAsync(WriterComponentId).ConfigureAwait(false) == PubSubComponentRole.Active)
                {
                    count++;
                }
                return count == activeCount;
            }).ConfigureAwait(false);
        }

        private static async Task WaitForRoleAsync(
            FakeTimeProvider clock,
            LeaseActivationCoordinator coordinator,
            PubSubComponentRole role)
        {
            await WaitForConditionAsync(clock, async () =>
                await coordinator.GetRoleAsync(WriterComponentId).ConfigureAwait(false) == role).ConfigureAwait(false);
        }

        private static async Task WaitForConditionAsync(
            FakeTimeProvider clock,
            Func<Task<bool>> condition)
        {
            for (int i = 0; i < 80; i++)
            {
                if (await condition().ConfigureAwait(false))
                {
                    return;
                }

                clock.Advance(TimeSpan.FromSeconds(1));

                // The coordinator loops run on background threads driven by the
                // fake clock; a short real delay reliably yields CPU so they can
                // observe the advance and apply the resulting role change before
                // the next check. Task.Yield alone starves them on contended
                // CI agents (observed flaky on the net48 Windows runner).
                await Task.Delay(25).ConfigureAwait(false);
            }

            Assert.Fail("The expected HA role transition did not occur.");
        }

        private sealed class CountingSource : IPublishedDataSetSource
        {
            private int m_value;

            public DataSetMetaDataType BuildMetaData()
            {
                return new DataSetMetaDataType
                {
                    Fields = [new FieldMetaData { Name = "value" }]
                };
            }

            public ValueTask<PublishedDataSetSnapshot> SampleAsync(
                DataSetMetaDataType metaData,
                CancellationToken cancellationToken = default)
            {
                int value = Interlocked.Increment(ref m_value);
                return new ValueTask<PublishedDataSetSnapshot>(
                    new PublishedDataSetSnapshot(
                        new ConfigurationVersionDataType(),
                        [new DataSetField { Name = "value", Value = new Variant(value) }],
                        DateTimeUtc.From(DateTimeOffset.UtcNow)));
            }
        }

        private sealed class CountingSink : ISubscribedDataSetSink
        {
            public int CallCount { get; private set; }

            public ValueTask WriteAsync(
                IReadOnlyList<DataSetField> fields,
                CancellationToken cancellationToken = default)
            {
                CallCount++;
                return default;
            }
        }

        private sealed class FixedCheckpointStore : IPubSubWriterCheckpointStore
        {
            private readonly uint m_checkpoint;

            public FixedCheckpointStore(uint checkpoint)
            {
                m_checkpoint = checkpoint;
            }

            public ValueTask<uint?> GetSequenceNumberAsync(
                string writerGroupComponentId,
                ushort dataSetWriterId,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<uint?>(m_checkpoint);
            }

            public ValueTask SetSequenceNumberAsync(
                string writerGroupComponentId,
                ushort dataSetWriterId,
                uint sequenceNumber,
                CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask SetSequenceNumberAsync(
                string writerGroupComponentId,
                ushort dataSetWriterId,
                uint sequenceNumber,
                long fencingToken,
                CancellationToken cancellationToken = default)
            {
                return default;
            }
        }

        private sealed class ThrowOnceCheckpointStore : IPubSubWriterCheckpointStore
        {
            private readonly uint m_checkpoint;
            private int m_shouldThrow = 1;

            public ThrowOnceCheckpointStore(uint checkpoint)
            {
                m_checkpoint = checkpoint;
            }

            public ValueTask<uint?> GetSequenceNumberAsync(
                string writerGroupComponentId,
                ushort dataSetWriterId,
                CancellationToken cancellationToken = default)
            {
                if (Interlocked.Exchange(ref m_shouldThrow, 0) == 1)
                {
                    throw new InvalidOperationException("Transient checkpoint read failure.");
                }

                return new ValueTask<uint?>(m_checkpoint);
            }

            public ValueTask SetSequenceNumberAsync(
                string writerGroupComponentId,
                ushort dataSetWriterId,
                uint sequenceNumber,
                CancellationToken cancellationToken = default)
            {
                return default;
            }

            public ValueTask SetSequenceNumberAsync(
                string writerGroupComponentId,
                ushort dataSetWriterId,
                uint sequenceNumber,
                long fencingToken,
                CancellationToken cancellationToken = default)
            {
                return default;
            }
        }

        private sealed class NoOpScheduler : IPubSubScheduler
        {
            public static NoOpScheduler Instance { get; } = new();

            public ValueTask<IAsyncDisposable> ScheduleAsync(
                PubSubSchedule schedule,
                Func<CancellationToken, ValueTask> action,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncDisposable>(NoOpHandle.Instance);
            }

            private sealed class NoOpHandle : IAsyncDisposable
            {
                public static NoOpHandle Instance { get; } = new();

                public ValueTask DisposeAsync()
                {
                    return default;
                }
            }
        }
    }
}
