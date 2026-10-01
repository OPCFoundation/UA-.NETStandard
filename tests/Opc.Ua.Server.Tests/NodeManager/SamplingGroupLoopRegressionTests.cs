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
                        null,
                        null,
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

        private static int QueuedCount(Mock<ISampledDataChangeMonitoredItem> item)
        {
            return item.Invocations.Count(i => i.Method.Name == nameof(IDataChangeMonitoredItem.QueueValue));
        }

        private static OperationContext CreateContext()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(1));
            return new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None, session.Object);
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
