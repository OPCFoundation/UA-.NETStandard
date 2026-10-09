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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

// CA2000: the group under test is disposed explicitly to stop it mid-sample.
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies the sampling-group rate quantisation and the lifetime of its sampling loop.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("SamplingGroup")]
    public sealed class SamplingGroupLoopRegressionTests
    {
        /// <summary>
        /// A rate group with a count of 0 has no limit: intervals beyond its start are rounded up to a
        /// multiple of the increment, so distinct client intervals share one group.
        /// </summary>
        [Test]
        public void IntervalsBeyondUnlimitedRateGroupAreQuantised()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using OperationContext context = CreateContext();
                var rates = new List<SamplingRateGroup> { new(1000, 1000, 4), new(3600000, 900000, 0) };
                using var group = new SamplingGroup(
                    server.Object, nodeManager.Object, rates, context, 3600001);
                Mock<ISampledDataChangeMonitoredItem> first = CreateItem(1, 3600001);
                Mock<ISampledDataChangeMonitoredItem> second = CreateItem(2, 3600002);
                Mock<ISampledDataChangeMonitoredItem> later = CreateItem(3, 5400000);

                Assert.That(group.StartMonitoring(context, first.Object), Is.True);
                Assert.That(group.StartMonitoring(context, second.Object), Is.True);
                Assert.That(group.StartMonitoring(context, later.Object), Is.False);
                first.Verify(m => m.SetSamplingInterval(4500000), Times.Once);
                second.Verify(m => m.SetSamplingInterval(4500000), Times.Once);
            }
        }

        /// <summary>
        /// A sample in flight when the group is emptied and disposed is abandoned instead of being
        /// queued to items that were just removed.
        /// </summary>
        [Test]
        public async Task InFlightSampleIsNotQueuedAfterGroupIsDisposedAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                int reads = 0;
                var loopReadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var loopReadFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var nodeManager = new Mock<IAsyncNodeManager>();
                nodeManager
                    .Setup(m => m.ReadAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<double>(),
                        It.IsAny<ArrayOf<ReadValueId>>(),
                        It.IsAny<IList<DataValue>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<OperationContext, double, ArrayOf<ReadValueId>, IList<DataValue>,
                        IList<ServiceResult>, CancellationToken>(
                        (_, _, _, values, _, ct) => new ValueTask(
                            ReadAsync(values, ct, Interlocked.Increment(ref reads) > 1)));
                nodeManager
                    .Setup(m => m.ValidateRolePermissionsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<NodeId>(),
                        It.IsAny<PermissionType>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

                using OperationContext context = CreateContext();
                var group = new SamplingGroup(
                    server.Object, nodeManager.Object, [new SamplingRateGroup(50, 50, 4)], context, 50);
                Mock<ISampledDataChangeMonitoredItem> item = CreateItem(1, 50);

                Assert.That(group.StartMonitoring(context, item.Object), Is.True);
                Assert.That(group.ApplyChanges(), Is.False);
                await loopReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                int queuedBeforeStop = QueuedCount(item);

                group.StopMonitoring(item.Object);
                Assert.That(group.ApplyChanges(), Is.True);
                group.Dispose();
                release.TrySetResult(true);
                await loopReadFinished.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                await Task.Delay(200).ConfigureAwait(false);

                Assert.That(QueuedCount(item), Is.EqualTo(queuedBeforeStop));

                async Task ReadAsync(IList<DataValue> values, CancellationToken ct, bool block)
                {
                    try
                    {
                        if (block)
                        {
                            loopReadStarted.TrySetResult(true);
                            await release.Task.ConfigureAwait(false);
                        }

                        for (int ii = 0; ii < values.Count; ii++)
                        {
                            values[ii] = new DataValue(new Variant(1));
                        }
                    }
                    finally
                    {
                        if (block)
                        {
                            loopReadFinished.TrySetResult(true);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// A read hook that registers a throwing callback on the sampling token must not make
        /// stopping the loop throw out of ApplyChanges or Dispose.
        /// </summary>
        [Test]
        public async Task ThrowingCancellationCallbackDoesNotFailApplyChangesAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var readStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var nodeManager = new Mock<IAsyncNodeManager>();
                nodeManager
                    .Setup(m => m.ReadAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<double>(),
                        It.IsAny<ArrayOf<ReadValueId>>(),
                        It.IsAny<IList<DataValue>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<OperationContext, double, ArrayOf<ReadValueId>, IList<DataValue>,
                        IList<ServiceResult>, CancellationToken>(
                        (_, _, _, _, _, ct) => new ValueTask(ReadAsync(ct)));

                using OperationContext context = CreateContext();
                var group = new SamplingGroup(
                    server.Object, nodeManager.Object, [new SamplingRateGroup(50, 50, 4)], context, 50);
                Mock<ISampledDataChangeMonitoredItem> item = CreateItem(1, 50);

                Assert.That(group.StartMonitoring(context, item.Object, null, initialValueQueued: true), Is.True);
                Assert.That(group.ApplyChanges(), Is.False);
                await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

                group.StopMonitoring(item.Object);
                Assert.That(group.ApplyChanges, Throws.Nothing);
                Assert.That(group.Dispose, Throws.Nothing);

                async Task ReadAsync(CancellationToken ct)
                {
                    _ = ct.Register(static () => throw new InvalidOperationException("read hook"));
                    readStarted.TrySetResult(true);
                    try
                    {
                        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // stopped.
                    }
                }
            }
        }

        /// <summary>
        /// A created item already gets its initial value from the node manager, so applying the
        /// changes does not take another immediate sample (Part 4 5.13.2.1).
        /// </summary>
        [Test]
        public async Task CreatedItemIsNotSampledAgainWhenChangesAreAppliedAsync()
        {
            Assert.That(await ImmediateSampleTakenAsync(created: true).ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// An item started without an initial value (for example a restored item) is sampled
        /// immediately when the changes are applied.
        /// </summary>
        [Test]
        public async Task StartedItemIsSampledWhenChangesAreAppliedAsync()
        {
            Assert.That(await ImmediateSampleTakenAsync(created: false).ConfigureAwait(false), Is.True);
        }

        private static async Task<bool> ImmediateSampleTakenAsync(bool created)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var nodeManager = new Mock<IAsyncNodeManager>();
                nodeManager
                    .Setup(m => m.ReadAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<double>(),
                        It.IsAny<ArrayOf<ReadValueId>>(),
                        It.IsAny<IList<DataValue>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Callback(() => read.TrySetResult(true))
                    .Returns(default(ValueTask));
                nodeManager
                    .Setup(m => m.ValidateRolePermissionsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<NodeId>(),
                        It.IsAny<PermissionType>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

                using OperationContext context = CreateContext();
                using var manager = new SamplingGroupManager(
                    server.Object, nodeManager.Object, 10, 10, [new SamplingRateGroup(60000, 0, 1)]);
                ISampledDataChangeMonitoredItem item;
                if (created)
                {
                    item = manager.CreateMonitoredItem(
                        context,
                        1,
                        1000,
                        TimestampsToReturn.Both,
                        1,
                        new NodeHandle(),
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId { NodeId = new NodeId(1, 1), AttributeId = Attributes.Value },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1,
                                SamplingInterval = 60000,
                                QueueSize = 1,
                                DiscardOldest = true
                            }
                        },
                        null!,
                        null!,
                        0,
                        false,
                        sourceSamplingInterval: 1);
                }
                else
                {
                    item = new MonitoredItem(
                        server.Object,
                        nodeManager.Object,
                        new NodeHandle(),
                        subscriptionId: 1,
                        1,
                        new ReadValueId { NodeId = new NodeId(1, 1), AttributeId = Attributes.Value },
                        DiagnosticsMasks.None,
                        TimestampsToReturn.Both,
                        MonitoringMode.Reporting,
                        clientHandle: 1,
                        originalFilter: null,
                        filterToUse: null,
                        range: null,
                        60000,
                        queueSize: 1,
                        discardOldest: true,
                        sourceSamplingInterval: 1);
                    manager.StartMonitoring(context, item);
                }

                using (item)
                {
                    manager.ApplyChanges();
                    Task completed = await Task.WhenAny(read.Task, Task.Delay(1000)).ConfigureAwait(false);
                    manager.StopMonitoring(item);
                    manager.ApplyChanges();
                    return completed == read.Task;
                }
            }
        }

        /// <summary>
        /// A custom item whose initial value the node manager queues itself is not sampled again
        /// when the changes are applied; one created without an initial value is (Part 4 5.13.2.1).
        /// </summary>
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task CustomItemIsSampledImmediatelyOnlyWithoutQueuedInitialValueAsync(
            bool initialValueQueued,
            bool expectImmediateSample)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var read = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var nodeManager = new Mock<IAsyncNodeManager>();
                nodeManager
                    .Setup(m => m.ReadAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<double>(),
                        It.IsAny<ArrayOf<ReadValueId>>(),
                        It.IsAny<IList<DataValue>>(),
                        It.IsAny<IList<ServiceResult>>(),
                        It.IsAny<CancellationToken>()))
                    .Callback(() => read.TrySetResult(true))
                    .Returns(default(ValueTask));
                nodeManager
                    .Setup(m => m.ValidateRolePermissionsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<NodeId>(),
                        It.IsAny<PermissionType>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));

                using OperationContext context = CreateContext();
                var samplingGroups = new SamplingGroupManager(
                    server.Object, nodeManager.Object, 10, 10, [new SamplingRateGroup(60000, 0, 1)]);
                using var manager = new SamplingGroupMonitoredItemManager(
                    nodeManager.Object, server.Object, samplingGroups);
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("Custom", 1),
                    DataType = DataTypeIds.Int32,
                    Value = 1
                };
                var handle = new NodeHandle(node.NodeId, node);

                ISampledDataChangeMonitoredItem item =
                    ((ICustomMonitoredItemManager)manager).CreateCustomMonitoredItem(
                        server.Object,
                        nodeManager.Object,
                        new ServerSystemContext(server.Object, context),
                        handle,
                        1,
                        1000,
                        DiagnosticsMasks.None,
                        TimestampsToReturn.Both,
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 1,
                                SamplingInterval = 60000,
                                QueueSize = 1,
                                DiscardOldest = true
                            }
                        },
                        null!,
                        null!,
                        60000,
                        1,
                        false,
                        new MonitoredItemIdFactory(),
                        static (_, _, value) => value,
                        static (_, _) => { },
                        factoryContext => new MonitoredItem(
                            factoryContext.Server,
                            factoryContext.NodeManager,
                            factoryContext.Handle,
                            factoryContext.SubscriptionId,
                            factoryContext.MonitoredItemId,
                            factoryContext.Request.ItemToMonitor,
                            DiagnosticsMasks.None,
                            TimestampsToReturn.Both,
                            MonitoringMode.Reporting,
                            clientHandle: 1,
                            originalFilter: null,
                            filterToUse: null,
                            range: null,
                            60000,
                            queueSize: 1,
                            discardOldest: true,
                            sourceSamplingInterval: 1),
                        initialValueQueued);

                using (item)
                {
                    manager.ApplyChanges();
                    Task completed = await Task.WhenAny(read.Task, Task.Delay(1000)).ConfigureAwait(false);
                    samplingGroups.StopMonitoring(item);
                    manager.ApplyChanges();
                    Assert.That(completed == read.Task, Is.EqualTo(expectImmediateSample));
                }
            }
        }

        /// <summary>
        /// A timer that wakes up late on every wait does not slow the schedule down: with a
        /// Windows-like 5.6 ms overshoot on a 10 ms interval the schedule still takes a sample
        /// every 10 ms on average, where waiting one interval after each sample takes 64 %.
        /// </summary>
        [TestCase(5.6)]
        [TestCase(1.0)]
        [TestCase(0.0)]
        public void ScheduleKeepsTheRateWhenTheTimerOvershoots(double overshootMs)
        {
            int samples = SimulateSchedule(10, overshootMs, sampleCostMs: 0.3, seconds: 10, out int restarts);
            int fixedDelaySamples = SimulateFixedDelay(10, overshootMs, sampleCostMs: 0.3, seconds: 10);

            Assert.That(samples, Is.InRange(995, 1001));
            Assert.That(restarts, Is.Zero);
            if (overshootMs > 0)
            {
                Assert.That(fixedDelaySamples, Is.LessThan(975), "the simulation must reproduce the drift");
            }
        }

        /// <summary>
        /// A pause shorter than the tolerated lag, such as a garbage collection, is made up by
        /// sampling again right away, so no sample of the interval is lost.
        /// </summary>
        [Test]
        public void SchedulePausesShorterThanTheLagAreMadeUp()
        {
            int samples = SimulateSchedule(10, 1.0, 0.3, 10, out int restarts, stallAtMs: 5000, stallMs: 200);

            Assert.That(samples, Is.InRange(995, 1001));
            Assert.That(restarts, Is.Zero);
        }

        /// <summary>
        /// A stall longer than the tolerated lag restarts the schedule once instead of bursting
        /// through every missed sample.
        /// </summary>
        [Test]
        public void ScheduleRestartsAfterAStallLongerThanTheLag()
        {
            int samples = SimulateSchedule(10, 1.0, 0.3, 10, out int restarts, stallAtMs: 5000, stallMs: 3000);

            Assert.That(restarts, Is.EqualTo(1));
            Assert.That(samples, Is.InRange(690, 710), "the 3 s stall is skipped, not made up");
        }

        /// <summary>
        /// Samples that take longer than the interval run back to back without the schedule
        /// running away: it restarts whenever it falls a full lag behind.
        /// </summary>
        [Test]
        public void ScheduleDoesNotRunAwayWhenSamplingIsSlowerThanTheInterval()
        {
            int samples = SimulateSchedule(10, 0.0, 15, 10, out int restarts);

            Assert.That(samples, Is.InRange(660, 670));
            Assert.That(restarts, Is.GreaterThan(0));
        }

        private const long kTicksPerSecond = 10_000_000;

        private static long Ticks(double milliseconds)
        {
            return (long)(milliseconds * kTicksPerSecond / 1000);
        }

        private static int SimulateSchedule(
            double intervalMs,
            double overshootMs,
            double sampleCostMs,
            double seconds,
            out int restarts,
            double stallAtMs = -1,
            double stallMs = 0)
        {
            long now = 0;
            long end = Ticks(seconds * 1000);
            bool stalled = false;
            var schedule = SamplingSchedule.Create(intervalMs, kTicksPerSecond, now);
            int samples = 0;
            restarts = 0;
            while (true)
            {
                long wait = schedule.GetWait(now);
                if (wait > 0)
                {
                    now += wait + Ticks(overshootMs);
                }
                if (!stalled && stallAtMs >= 0 && now >= Ticks(stallAtMs))
                {
                    stalled = true;
                    now += Ticks(stallMs);
                }
                if (now >= end)
                {
                    return samples;
                }
                now += Ticks(sampleCostMs);
                samples++;
                if (schedule.Advance(now))
                {
                    restarts++;
                }
            }
        }

        private static int SimulateFixedDelay(double intervalMs, double overshootMs, double sampleCostMs, double seconds)
        {
            long now = 0;
            long end = Ticks(seconds * 1000);
            int samples = 0;
            while (true)
            {
                now += Ticks(intervalMs + overshootMs);
                if (now >= end)
                {
                    return samples;
                }
                now += Ticks(sampleCostMs);
                samples++;
            }
        }

        private static int QueuedCount(Mock<ISampledDataChangeMonitoredItem> item)
        {
            return item.Invocations.Count(i => i.Method.Name == nameof(IDataChangeMonitoredItem.QueueValue));
        }

        private static OperationContext CreateContext()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(1));
            return new OperationContext(
                new RequestHeader(), null!, RequestType.CreateMonitoredItems, RequestLifetime.None, session.Object);
        }

        private static Mock<ISampledDataChangeMonitoredItem> CreateItem(uint id, double samplingInterval)
        {
            var item = new Mock<ISampledDataChangeMonitoredItem>();
            item.SetupGet(m => m.Id).Returns(id);
            item.SetupGet(m => m.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
            item.SetupGet(m => m.MonitoringMode).Returns(MonitoringMode.Reporting);
            item.SetupGet(m => m.SamplingInterval).Returns(samplingInterval);
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(1));
            item.SetupGet(m => m.Session).Returns(session.Object);
            item.Setup(m => m.GetReadValueId()).Returns(() => new ReadValueId
            {
                NodeId = new NodeId(id, 1),
                AttributeId = Attributes.Value
            });
            return item;
        }
    }
}
