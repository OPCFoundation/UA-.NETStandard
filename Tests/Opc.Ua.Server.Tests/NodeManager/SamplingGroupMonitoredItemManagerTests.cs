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

using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

#nullable enable

// CA2000: test code; the manager is short-lived and torn down per test.
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Deterministic, offline unit tests for
    /// <see cref="SamplingGroupMonitoredItemManager"/> covering the
    /// validation, monitoring-mode and unsubscribe branches without spinning
    /// up sampling timers.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Category("SamplingGroupMonitoredItemManager")]
    [Parallelizable(ParallelScope.All)]
    public class SamplingGroupMonitoredItemManagerTests
    {
        private static ApplicationConfiguration CreateConfiguration()
        {
            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200,
                    AvailableSamplingRates = new ArrayOf<SamplingRateGroup>()
                }
            };
        }

        private static SamplingGroupMonitoredItemManager CreateManager(
            out Mock<IServerInternal> mockServer,
            out ServerSystemContext context)
        {
            mockServer = DeterministicServerMock.Create(out _);
            context = mockServer.Object.DefaultSystemContext;
            var mockNodeManager = new Mock<IAsyncNodeManager>();
            return new SamplingGroupMonitoredItemManager(
                mockNodeManager.Object,
                mockServer.Object,
                CreateConfiguration());
        }

        private static Mock<ISampledDataChangeMonitoredItem> CreateItem(uint id)
        {
            var item = new Mock<ISampledDataChangeMonitoredItem>();
            item.SetupGet(m => m.Id).Returns(id);
            return item;
        }

        [Test]
        public void DeleteMonitoredItemUnknownIdReturnsBadMonitoredItemIdInvalid()
        {
            using SamplingGroupMonitoredItemManager manager = CreateManager(out _, out ServerSystemContext ctx);
            Mock<ISampledDataChangeMonitoredItem> item = CreateItem(1);

            StatusCode result = manager.DeleteMonitoredItem(ctx, item.Object, null!);

            Assert.That(result, Is.EqualTo((StatusCode)StatusCodes.BadMonitoredItemIdInvalid));
        }

        [Test]
        public void DeleteMonitoredItemReferenceMismatchReturnsBadMonitoredItemIdInvalid()
        {
            using SamplingGroupMonitoredItemManager manager = CreateManager(out _, out ServerSystemContext ctx);
            Mock<ISampledDataChangeMonitoredItem> existing = CreateItem(1);
            manager.MonitoredItems[1] = existing.Object;
            Mock<ISampledDataChangeMonitoredItem> other = CreateItem(1);

            StatusCode result = manager.DeleteMonitoredItem(ctx, other.Object, null!);

            Assert.That(result, Is.EqualTo((StatusCode)StatusCodes.BadMonitoredItemIdInvalid));
        }

        [Test]
        public void DeleteMonitoredItemRemovesOwnedItem()
        {
            using SamplingGroupMonitoredItemManager manager = CreateManager(out _, out ServerSystemContext ctx);
            Mock<ISampledDataChangeMonitoredItem> existing = CreateItem(5);
            manager.MonitoredItems[5] = existing.Object;

            StatusCode result = manager.DeleteMonitoredItem(ctx, existing.Object, null!);

            Assert.That(result, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(manager.MonitoredItems.ContainsKey(5), Is.False);
        }

        [Test]
        public void ModifyMonitoredItemUnknownIdReturnsBadMonitoredItemIdInvalid()
        {
            using SamplingGroupMonitoredItemManager manager = CreateManager(out _, out ServerSystemContext ctx);
            Mock<ISampledDataChangeMonitoredItem> item = CreateItem(1);

            ServiceResult? result = manager.ModifyMonitoredItem(
                ctx,
                DiagnosticsMasks.None,
                TimestampsToReturn.Both,
                new MonitoringFilter(),
                new Opc.Ua.Range(),
                1000.0,
                5,
                item.Object,
                new MonitoredItemModifyRequest());

            Assert.That(result, Is.Not.Null);
            Assert.That(
                (StatusCode)result!.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadMonitoredItemIdInvalid));
        }

        [Test]
        public void ModifyMonitoredItemReferenceMismatchReturnsBadMonitoredItemIdInvalid()
        {
            using SamplingGroupMonitoredItemManager manager = CreateManager(out _, out ServerSystemContext ctx);
            Mock<ISampledDataChangeMonitoredItem> existing = CreateItem(2);
            manager.MonitoredItems[2] = existing.Object;
            Mock<ISampledDataChangeMonitoredItem> other = CreateItem(2);

            ServiceResult? result = manager.ModifyMonitoredItem(
                ctx,
                DiagnosticsMasks.None,
                TimestampsToReturn.Both,
                new MonitoringFilter(),
                new Opc.Ua.Range(),
                1000.0,
                5,
                other.Object,
                new MonitoredItemModifyRequest());

            Assert.That(result, Is.Not.Null);
            Assert.That(
                (StatusCode)result!.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadMonitoredItemIdInvalid));
        }

        [Test]
        public async Task SetMonitoringModeAsyncUnknownIdReturnsBadMonitoredItemIdInvalidAsync()
        {
            using SamplingGroupMonitoredItemManager manager = CreateManager(out _, out ServerSystemContext ctx);
            Mock<ISampledDataChangeMonitoredItem> item = CreateItem(1);

            (ServiceResult result, MonitoringMode? previous) = await manager
                .SetMonitoringModeAsync(ctx, item.Object, MonitoringMode.Reporting, null!)
                .ConfigureAwait(false);

            Assert.That(
                (StatusCode)result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadMonitoredItemIdInvalid));
            Assert.That(previous, Is.Null);
        }

        [Test]
        public async Task SetMonitoringModeAsyncReferenceMismatchReturnsBadMonitoredItemIdInvalidAsync()
        {
            using SamplingGroupMonitoredItemManager manager = CreateManager(out _, out ServerSystemContext ctx);
            Mock<ISampledDataChangeMonitoredItem> existing = CreateItem(3);
            manager.MonitoredItems[3] = existing.Object;
            Mock<ISampledDataChangeMonitoredItem> other = CreateItem(3);

            (ServiceResult result, MonitoringMode? previous) = await manager
                .SetMonitoringModeAsync(ctx, other.Object, MonitoringMode.Reporting, null!)
                .ConfigureAwait(false);

            Assert.That(
                (StatusCode)result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadMonitoredItemIdInvalid));
            Assert.That(previous, Is.Null);
        }

        [Test]
        public async Task SetMonitoringModeAsyncEnableFromDisabledQueuesInitialValueAsync()
        {
            using SamplingGroupMonitoredItemManager manager = CreateManager(out _, out ServerSystemContext ctx);
            Mock<ISampledDataChangeMonitoredItem> item = CreateItem(4);
            item.Setup(m => m.SetMonitoringMode(MonitoringMode.Reporting))
                .Returns(MonitoringMode.Disabled);
            item.SetupGet(m => m.ManagerHandle).Returns(new object());
            item.SetupGet(m => m.AttributeId).Returns(Attributes.Value);
            manager.MonitoredItems[4] = item.Object;

            (ServiceResult result, MonitoringMode? previous) = await manager
                .SetMonitoringModeAsync(ctx, item.Object, MonitoringMode.Reporting, null!)
                .ConfigureAwait(false);

            Assert.That((StatusCode)result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(previous, Is.EqualTo(MonitoringMode.Disabled));
        }

        [Test]
        public async Task SetMonitoringModeAsyncNoInitialValueWhenAlreadyEnabledAsync()
        {
            using SamplingGroupMonitoredItemManager manager = CreateManager(out _, out ServerSystemContext ctx);
            Mock<ISampledDataChangeMonitoredItem> item = CreateItem(6);
            item.Setup(m => m.SetMonitoringMode(MonitoringMode.Reporting))
                .Returns(MonitoringMode.Sampling);
            manager.MonitoredItems[6] = item.Object;

            (ServiceResult result, MonitoringMode? previous) = await manager
                .SetMonitoringModeAsync(ctx, item.Object, MonitoringMode.Reporting, null!)
                .ConfigureAwait(false);

            Assert.That((StatusCode)result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(previous, Is.EqualTo(MonitoringMode.Sampling));
        }

        [Test]
        public void SubscribeToEventsUnsubscribeUnknownNodeReturnsBadNodeIdUnknown()
        {
            using SamplingGroupMonitoredItemManager manager = CreateManager(out _, out ServerSystemContext ctx);
            var source = new BaseObjectState(null)
            {
                NodeId = new NodeId("EventSource", 3)
            };
            var monitoredItem = new Mock<IEventMonitoredItem>();

            (MonitoredNode2? node, ServiceResult result) = manager.SubscribeToEvents(
                ctx, source, monitoredItem.Object, true);

            Assert.That(node, Is.Null);
            Assert.That(
                (StatusCode)result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadNodeIdUnknown));
        }
    }
}
