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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    public sealed class SamplingAuditRegressionTests
    {
        [TestCase(0, 100)]
        [TestCase(50, 100)]
        [TestCase(100, 100)]
        [TestCase(250, 250)]
        public void SamplingGroupsCanLowerCreatedIntervals(double requested, double revised)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (OperationContext operation = CreateContext())
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                var configuration = new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration()
                };
                using var manager = new SamplingGroupMonitoredItemManager(
                    nodeManager.Object, server.Object, configuration);
                ServerSystemContext context = server.Object.DefaultSystemContext.Copy(operation);
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(1, 1),
                    MinimumSamplingInterval = 100
                };
                ISampledDataChangeMonitoredItem item = manager.CreateMonitoredItem(
                    server.Object, nodeManager.Object, context, new NodeHandle { Node = node },
                    1, 500, DiagnosticsMasks.None, TimestampsToReturn.Both,
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId { NodeId = node.NodeId, AttributeId = Attributes.Value },
                        MonitoringMode = MonitoringMode.Disabled,
                        RequestedParameters = new MonitoringParameters { SamplingInterval = 1000, QueueSize = 1 }
                    },
                    new Range(), new DataChangeFilter(), 1000, 1, false,
                    new MonitoredItemIdFactory(), (_, _, current) => current);
                double sourceMinimum = item.MinimumSamplingInterval;

                ServiceResult error = manager.ModifyMonitoredItem(
                    context, DiagnosticsMasks.None, TimestampsToReturn.Both,
                    new DataChangeFilter(), new Range(), revised, 1, item,
                    new MonitoredItemModifyRequest
                    {
                        MonitoredItemId = item.Id,
                        RequestedParameters = new MonitoringParameters { SamplingInterval = requested, QueueSize = 1 }
                    });

                Assert.Multiple(() =>
                {
                    Assert.That(sourceMinimum, Is.EqualTo(100));
                    Assert.That(ServiceResult.IsGood(error), Is.True);
                    Assert.That(item.SamplingInterval, Is.EqualTo(revised));
                });
            }
        }

        [Test]
        public async Task NonFiniteIntervalWithDiscreteRateGroupDoesNotSpinAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (OperationContext context = CreateContext())
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                var item = new Mock<ISampledDataChangeMonitoredItem>();
                item.SetupGet(value => value.MonitoredItemType).Returns(MonitoredItemTypeMask.DataChange);
                item.SetupGet(value => value.MonitoringMode).Returns(MonitoringMode.Reporting);
                item.SetupGet(value => value.SamplingInterval).Returns(double.NaN);
                var adjusting = Task.Run(() =>
                {
                    using var group = new SamplingGroup(
                        server.Object, nodeManager.Object, [new SamplingRateGroup(100, 0, 1)], context, double.NaN);
                    Assert.That(group.StartMonitoring(context, item.Object), Is.True);
                    item.Verify(value => value.SetSamplingInterval(100), Times.Once);
                });
                await adjusting.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task NodeManagerDisposesSamplingOutsideItsNodeLockAsync()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var readCompleted = new ManualResetEventSlim())
            {
                var monitoredItems = new Mock<IMonitoredItemManager>();
                using var manager = new TestNodeManager(server.Object, monitoredItems.Object);
                Task reader = Task.CompletedTask;
                monitoredItems.Setup(value => value.Dispose()).Callback(() =>
                {
                    reader = Task.Run(() =>
                    {
                        using var context = new OperationContext(
                            new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
                        manager.Read(context, 0, [], [], []);
                        readCompleted.Set();
                    });
                    Assert.That(readCompleted.Wait(TimeSpan.FromSeconds(2)), Is.True,
                        "Sampling shutdown cannot join a reader while holding the reader's node lock.");
                });
                try
                {
                    await Task.Run(manager.Dispose).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                finally
                {
                    await reader.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
        }

        private static OperationContext CreateContext()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(1));
            return new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None, session.Object);
        }

        private sealed class TestNodeManager : CustomNodeManager2
        {
            public TestNodeManager(IServerInternal server, IMonitoredItemManager manager)
                : base(server, NullLogger.Instance, "urn:tests:sampling-audit")
            {
                m_monitoredItemManager.Dispose();
                m_monitoredItemManager = manager;
            }
        }
    }
}
