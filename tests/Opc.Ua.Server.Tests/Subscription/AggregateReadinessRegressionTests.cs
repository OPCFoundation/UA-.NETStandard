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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Tests.NodeManager;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("MonitoredItem")]
    public sealed class AggregateReadinessRegressionTests
    {
        [TestCase(MonitoringMode.Disabled, false, false, false)]
        [TestCase(MonitoringMode.Disabled, true, false, false)]
        [TestCase(MonitoringMode.Disabled, true, true, false)]
        [TestCase(MonitoringMode.Sampling, false, false, false)]
        [TestCase(MonitoringMode.Sampling, true, false, false)]
        [TestCase(MonitoringMode.Sampling, true, true, true)]
        [TestCase(MonitoringMode.Reporting, false, false, true)]
        [TestCase(MonitoringMode.Reporting, true, false, true)]
        public async Task AggregateEndTimeDoesNotBypassMonitoringModeAsync(
            MonitoringMode monitoringMode,
            bool queueValue,
            bool trigger,
            bool expectedReady)
        {
            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            using (var aggregates = new AggregateManager(server.Object))
            {
                server.SetupGet(value => value.AggregateManager).Returns(aggregates);
                server.SetupGet(value => value.DiagnosticsNodeManager).Returns(new Mock<IDiagnosticsNodeManager>().Object);
                var calculator = new Mock<IAggregateCalculator>();
                calculator.Setup(value => value.HasEndTimePassed(It.IsAny<DateTimeUtc>())).Returns(true);
                calculator.Setup(value => value.QueueRawValue(It.IsAny<DataValue>())).Returns(true);
                var completedValue = new DataValue(42.0);
                calculator.SetupSequence(value => value.TryGetProcessedValue(false, out completedValue))
                    .Returns(true)
                    .Returns(false);
                var partialValue = new DataValue(84.0);
                calculator.SetupSequence(value => value.TryGetProcessedValue(true, out partialValue))
                    .Returns(true)
                    .Returns(false);
                var aggregateId = new NodeId("ReadinessAggregate", 1);
                await aggregates.RegisterFactoryAsync(
                    aggregateId,
                    "Readiness aggregate",
                    (id, start, end, interval, stepped, configuration, telemetry) => calculator.Object)
                    .ConfigureAwait(false);
                var filter = new ServerAggregateFilter
                {
                    AggregateType = aggregateId,
                    StartTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    ProcessingInterval = 1000,
                    AggregateConfiguration = new AggregateConfiguration(),
                    PrimeInitialValue = false
                };
                using var item = new MonitoredItem(
                    server.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    null,
                    1,
                    2,
                    new ReadValueId { NodeId = new NodeId(1, 1), AttributeId = Attributes.Value },
                    DiagnosticsMasks.None,
                    TimestampsToReturn.Both,
                    MonitoringMode.Sampling,
                    3,
                    filter,
                    filter,
                    null,
                    0,
                    4,
                    true,
                    1000);
                if (queueValue)
                {
                    item.QueueValue(new DataValue(1.0), ServiceResult.Good);
                }
                if (trigger)
                {
                    Assert.That(item.SetTriggered(), Is.True, "A queued aggregate accepts a legitimate trigger.");
                }
                else if (!queueValue)
                {
                    Assert.That(item.SetTriggered(), Is.False, "An expired interval alone is not a trigger.");
                }
                item.SetMonitoringMode(monitoringMode);

                Assert.That(item.IsReadyToPublish, Is.EqualTo(expectedReady));
                var notifications = new Queue<MonitoredItemNotification>();
                var diagnostics = new Queue<DiagnosticInfo>();
                var context = new OperationContext(item);
                bool more = item.Publish(context, notifications, diagnostics, 4, NullLogger.Instance);

                Assert.That(more, Is.False);
                if (expectedReady)
                {
                    Assert.That(notifications, Has.Count.EqualTo(2));
                    Assert.That(diagnostics, Has.Count.EqualTo(2));
                    Assert.That(diagnostics, Is.All.Null);
                    AssertValue(notifications.Dequeue().Value, 42.0);
                    AssertValue(notifications.Dequeue().Value, 84.0);
                }
                else
                {
                    Assert.That(notifications, Is.Empty);
                    Assert.That(diagnostics, Is.Empty);
                }
                if (monitoringMode != MonitoringMode.Reporting)
                {
                    Assert.That(item.IsReadyToPublish, Is.False, "A consumed trigger cannot enable another report.");
                    Assert.That(item.Publish(context, notifications, diagnostics, 4, NullLogger.Instance), Is.False);
                    Assert.That(notifications, Is.Empty);
                }
            }
        }

        private static void AssertValue(in DataValue value, double expected)
        {
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
