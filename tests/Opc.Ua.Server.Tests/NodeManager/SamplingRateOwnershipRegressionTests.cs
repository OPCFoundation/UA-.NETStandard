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

using System.Collections.Generic;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    [TestFixture]
    [Category("NodeManager")]
    public sealed class SamplingRateOwnershipRegressionTests
    {
        [Test]
        public void DisposingSamplingGroupPreservesBorrowedRatesForOtherGroups()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using OperationContext context = CreateContext();
                var rates = new List<SamplingRateGroup> { new(100, 100, 9) };
                using var surviving = new SamplingGroup(server.Object, nodeManager.Object, rates, context, 125);
                using (var retired = new SamplingGroup(server.Object, nodeManager.Object, rates, context, 225))
                {
                    Assert.That(retired.ApplyChanges(), Is.True);
                }

                using MonitoredItem first = CreateItem(server.Object, nodeManager.Object, 1, 125);
                Assert.That(surviving.StartMonitoring(context, first), Is.True);
                Assert.That(first.SamplingInterval, Is.EqualTo(200));

                using var later = new SamplingGroup(server.Object, nodeManager.Object, rates, context, 225);
                using MonitoredItem second = CreateItem(server.Object, nodeManager.Object, 2, 225);
                Assert.That(later.StartMonitoring(context, second), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(second.SamplingInterval, Is.EqualTo(300));
                    Assert.That(rates, Has.Count.EqualTo(1));
                    Assert.That(rates[0].Start, Is.EqualTo(100));
                    Assert.That(rates[0].Increment, Is.EqualTo(100));
                });
            }
        }

        [Test]
        public void DisposingSamplingGroupPreservesManagerRateRevision()
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(
                out MonitoredItemQueueFactory queueFactory);
            using (queueFactory)
            {
                var nodeManager = new Mock<IAsyncNodeManager>();
                using OperationContext context = CreateContext();
                using var manager = new SamplingGroupManager(
                    server.Object, nodeManager.Object, 10, 10, [new SamplingRateGroup(100, 100, 9)]);
                using MonitoredItem first = CreateItem(server.Object, nodeManager.Object, 1, 125);
                manager.StartMonitoring(context, first);
                Assert.That(first.SamplingInterval, Is.EqualTo(200));
                manager.StopMonitoring(first);
                manager.ApplyChanges();

                using MonitoredItem second = CreateItem(server.Object, nodeManager.Object, 2, 125);
                using MonitoredItem third = CreateItem(server.Object, nodeManager.Object, 3, 175);
                manager.StartMonitoring(context, second);
                manager.StartMonitoring(context, third);
                Assert.Multiple(() =>
                {
                    Assert.That(second.SamplingInterval, Is.EqualTo(200));
                    Assert.That(third.SamplingInterval, Is.EqualTo(200));
                });
                manager.StopMonitoring(second);
                manager.StopMonitoring(third);
                manager.ApplyChanges();
            }
        }

        private static OperationContext CreateContext()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(1));
            return new OperationContext(
                new RequestHeader(), null, RequestType.CreateMonitoredItems, RequestLifetime.None, session.Object);
        }

        private static MonitoredItem CreateItem(
            IServerInternal server,
            IAsyncNodeManager nodeManager,
            uint id,
            double samplingInterval)
        {
            return new MonitoredItem(
                server,
                nodeManager,
                new NodeHandle(),
                subscriptionId: 1,
                id,
                new ReadValueId { NodeId = new NodeId(id, 1), AttributeId = Attributes.Value },
                DiagnosticsMasks.None,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                clientHandle: id,
                originalFilter: null,
                filterToUse: null,
                range: null,
                samplingInterval,
                queueSize: 1,
                discardOldest: true,
                sourceSamplingInterval: 1);
        }
    }
}
