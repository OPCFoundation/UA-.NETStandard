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
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Historian;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test MonitoredItem
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    [MemoryDiagnoser]
    public class MonitoredItemTests
    {
        private static readonly int[] s_initialThenLive = [1, 2];
        private static readonly int[] s_liveThenInitial = [2, 1];

        [Test]
        public void CreateMI()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<MonitoredItemTests>();

            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ItemsInQueue, Is.Zero);

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            monitoredItem.QueueValue(dataValue, statuscode);

            var result = new Queue<MonitoredItemNotification>();
            var result2 = new Queue<DiagnosticInfo>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, result2, 1, logger);

            Assert.That(result, Is.Not.Empty);
            Assert.That(monitoredItem.ItemsInQueue, Is.Zero);
            MonitoredItemNotification publishResult = result.FirstOrDefault();
            Assert.That(publishResult?.Value, Is.EqualTo(dataValue));
            DiagnosticInfo publishErrorResult = result2.FirstOrDefault();
            Assert.That(
                publishErrorResult.InnerStatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void CreateEventMI()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, true);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ItemsInQueue, Is.Zero);

            var event1 = new AuditUrlMismatchEventState(null);
            monitoredItem.QueueEvent(event1);

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(1));

            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 1);

            Assert.That(result, Is.Not.Empty);
            Assert.That(monitoredItem.ItemsInQueue, Is.Zero);
            EventFieldList publishResult = result.FirstOrDefault();
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Handle, Is.AssignableTo<AuditUrlMismatchEventState>());
        }

        [Test]
        public void CreateMIQueueNoQueue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<MonitoredItemTests>();

            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, false, 0);

            Assert.That(monitoredItem.QueueSize, Is.EqualTo(1));

            var statuscode = new ServiceResult(StatusCodes.Good);
            var dataValue = new DataValue(new Variant(true));

            monitoredItem.QueueValue(dataValue, statuscode);

            var result = new Queue<MonitoredItemNotification>();
            var result2 = new Queue<DiagnosticInfo>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, result2, 1, logger);

            Assert.That(result, Is.Not.Empty);
            MonitoredItemNotification publishResult = result.FirstOrDefault();
            Assert.That(publishResult?.Value, Is.EqualTo(dataValue));
            DiagnosticInfo publishErrorResult = result2.FirstOrDefault();
            Assert.That(
                publishErrorResult.InnerStatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AggregateInitialValueBufferingHonorsPrimeFlag(bool prime)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<MonitoredItemTests>();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock =
                CreateServerMock(telemetry, queueFactory);
            using var aggregateManager = new AggregateManager(serverMock.Object);
            serverMock
                .Setup(value => value.AggregateManager)
                .Returns(aggregateManager);
            var filter = new ServerAggregateFilter
            {
                AggregateType = new NodeId("unsupported", 1),
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration(),
                PrimeInitialValue = prime
            };
            using var monitoredItem = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                1,
                2,
                new ReadValueId
                {
                    NodeId = new NodeId("V", 1),
                    AttributeId = Attributes.Value
                },
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                3,
                filter,
                filter,
                null,
                0,
                10,
                discardOldest: false,
                sourceSamplingInterval: 0);
            DateTime historyTime = DateTime.UtcNow.AddSeconds(-2);
            DateTime liveTime = historyTime.AddSeconds(1);
            var history = new DataValue(
                new Variant(1),
                StatusCodes.Good,
                historyTime,
                historyTime);
            var live = new DataValue(
                new Variant(2),
                StatusCodes.Good,
                liveTime,
                liveTime);

            monitoredItem.QueueValue(live, ServiceResult.Good);
            ((IInitialValueMonitoredItem)monitoredItem).QueueInitialValue(
                history,
                ServiceResult.Good,
                ignoreFilters: false);
            ((IInitialValueMonitoredItem)monitoredItem).CompleteInitialValue();

            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();
            _ = monitoredItem.Publish(
                new OperationContext(monitoredItem),
                notifications,
                diagnostics,
                10,
                logger);

            Assert.That(
                notifications.Select(value => (int)value.Value.WrappedValue),
                Is.EqualTo(prime ? s_initialThenLive : s_liveThenInitial));
        }

        [Test]
        public async Task AggregateInitialValueHandoffKeepsDistinctValuesWithoutSourceTimestampsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock =
                CreateServerMock(telemetry, queueFactory);
            using var aggregateManager = new AggregateManager(serverMock.Object);
            serverMock
                .Setup(value => value.AggregateManager)
                .Returns(aggregateManager);
            serverMock
                .Setup(value => value.DiagnosticsNodeManager)
                .Returns(new Mock<IDiagnosticsNodeManager>().Object);
            var queuedRawValues = new List<int>();
            var calculator = new Mock<IAggregateCalculator>();
            calculator
                .Setup(value => value.QueueRawValue(It.IsAny<DataValue>()))
                .Callback<DataValue>(
                    value => queuedRawValues.Add((int)value.WrappedValue))
                .Returns(true);
            await aggregateManager.RegisterFactoryAsync(
                ObjectIds.AggregateFunction_Average,
                "Average",
                (id, start, end, interval, stepped, configuration, context) =>
                    calculator.Object).ConfigureAwait(false);
            var filter = new ServerAggregateFilter
            {
                AggregateType = ObjectIds.AggregateFunction_Average,
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration(),
                PrimeInitialValue = true,
                HistorianKeySelector = ConstantValueKeySelector.Instance
            };
            using var monitoredItem = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                1,
                2,
                new ReadValueId
                {
                    NodeId = new NodeId("V", 1),
                    AttributeId = Attributes.Value
                },
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                3,
                filter,
                filter,
                null,
                0,
                10,
                discardOldest: false,
                sourceSamplingInterval: 0);
            var serverTimestamp =
                new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc);
            var historical = new DataValue(
                new Variant(1),
                StatusCodes.Good,
                DateTimeUtc.MinValue,
                serverTimestamp);
            DataValue duplicateLive = historical.Copy();
            var distinctLive = new DataValue(
                new Variant(2),
                StatusCodes.Good,
                DateTimeUtc.MinValue,
                serverTimestamp);

            monitoredItem.QueueValue(duplicateLive, ServiceResult.Good);
            monitoredItem.QueueValue(distinctLive, ServiceResult.Good);
            ((IInitialValueMonitoredItem)monitoredItem).QueueInitialValue(
                historical,
                ServiceResult.Good,
                ignoreFilters: false);
            ServiceResult completion =
                ((IInitialValueMonitoredItem)monitoredItem)
                    .CompleteInitialValue();

            Assert.That(ServiceResult.IsGood(completion), Is.True);
            Assert.That(queuedRawValues, Is.EqualTo(s_initialThenLive));
        }

        [Test]
        public async Task AggregateInitialValueHandoffUsesStructuredCompositeIdentityAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock =
                CreateServerMock(telemetry, queueFactory);
            using var aggregateManager = new AggregateManager(serverMock.Object);
            serverMock
                .Setup(value => value.AggregateManager)
                .Returns(aggregateManager);
            serverMock
                .Setup(value => value.DiagnosticsNodeManager)
                .Returns(new Mock<IDiagnosticsNodeManager>().Object);
            var queuedRawValues = new List<int>();
            var calculator = new Mock<IAggregateCalculator>();
            calculator
                .Setup(value => value.QueueRawValue(It.IsAny<DataValue>()))
                .Callback<DataValue>(
                    value => queuedRawValues.Add((int)value.WrappedValue))
                .Returns(true);
            await aggregateManager.RegisterFactoryAsync(
                ObjectIds.AggregateFunction_Average,
                "Average",
                (id, start, end, interval, stepped, configuration, context) =>
                    calculator.Object).ConfigureAwait(false);
            var filter = new ServerAggregateFilter
            {
                AggregateType = ObjectIds.AggregateFunction_Average,
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration(),
                PrimeInitialValue = true,
                HistorianKeySelector = Int32ValueKeySelector.Instance
            };
            using var monitoredItem = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                1,
                2,
                new ReadValueId
                {
                    NodeId = new NodeId("V", 1),
                    AttributeId = Attributes.Value
                },
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                3,
                filter,
                filter,
                null,
                0,
                10,
                discardOldest: false,
                sourceSamplingInterval: 0);
            DateTime timestamp =
                new(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc);
            var historical = new DataValue(
                Variant.From(1),
                StatusCodes.Good,
                timestamp,
                timestamp);

            monitoredItem.QueueValue(
                historical.Copy(),
                ServiceResult.Good);
            monitoredItem.QueueValue(
                new DataValue(
                    Variant.From(2),
                    StatusCodes.Good,
                    timestamp,
                    timestamp),
                ServiceResult.Good);
            ((IInitialValueMonitoredItem)monitoredItem).QueueInitialValue(
                historical,
                ServiceResult.Good,
                ignoreFilters: false);
            ServiceResult completion =
                ((IInitialValueMonitoredItem)monitoredItem)
                    .CompleteInitialValue();

            Assert.That(ServiceResult.IsGood(completion), Is.True);
            Assert.That(queuedRawValues, Is.EqualTo(s_initialThenLive));
        }

        [Test]
        public async Task AggregateInitialValueHandoffMatchesDeepCopiedValuesWithoutSourceTimestampsAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock =
                CreateServerMock(telemetry, queueFactory);
            using var aggregateManager = new AggregateManager(serverMock.Object);
            serverMock
                .Setup(value => value.AggregateManager)
                .Returns(aggregateManager);
            serverMock
                .Setup(value => value.DiagnosticsNodeManager)
                .Returns(new Mock<IDiagnosticsNodeManager>().Object);
            var queuedRawValues = new List<DataValue>();
            var calculator = new Mock<IAggregateCalculator>();
            calculator
                .Setup(value => value.QueueRawValue(It.IsAny<DataValue>()))
                .Callback<DataValue>(queuedRawValues.Add)
                .Returns(true);
            await aggregateManager.RegisterFactoryAsync(
                ObjectIds.AggregateFunction_Average,
                "Average",
                (id, start, end, interval, stepped, configuration, context) =>
                    calculator.Object).ConfigureAwait(false);
            var filter = new ServerAggregateFilter
            {
                AggregateType = ObjectIds.AggregateFunction_Average,
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration(),
                PrimeInitialValue = true
            };
            using var monitoredItem = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                1,
                2,
                new ReadValueId
                {
                    NodeId = new NodeId("V", 1),
                    AttributeId = Attributes.Value
                },
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                3,
                filter,
                filter,
                null,
                0,
                10,
                discardOldest: false,
                sourceSamplingInterval: 0);
            var pair = new KeyValuePair
            {
                Key = new QualifiedName("A"),
                Value = Variant.From(1)
            };
            var historical = new DataValue(
                Variant.From(new ExtensionObject(pair)),
                StatusCodes.Good);
            DataValue duplicateLive = historical.Copy();
            var distinctLive = new DataValue(
                Variant.From(new ExtensionObject(new KeyValuePair
                {
                    Key = new QualifiedName("B"),
                    Value = Variant.From(2)
                })),
                StatusCodes.Good);

            monitoredItem.QueueValue(duplicateLive, ServiceResult.Good);
            monitoredItem.QueueValue(distinctLive, ServiceResult.Good);
            ((IInitialValueMonitoredItem)monitoredItem).QueueInitialValue(
                historical,
                ServiceResult.Good,
                ignoreFilters: false);
            ServiceResult completion =
                ((IInitialValueMonitoredItem)monitoredItem)
                    .CompleteInitialValue();

            Assert.That(ServiceResult.IsGood(completion), Is.True);
            Assert.That(queuedRawValues, Has.Count.EqualTo(2));
            Assert.That(queuedRawValues[0], Is.EqualTo(historical));
            Assert.That(queuedRawValues[1], Is.EqualTo(distinctLive));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void QueueSizeOneKeepsLatestValueAfterInitialHistoryFailure(bool discardOldest)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<MonitoredItemTests>();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock =
                CreateServerMock(telemetry, queueFactory);
            using var aggregateManager = new AggregateManager(serverMock.Object);
            serverMock
                .Setup(value => value.AggregateManager)
                .Returns(aggregateManager);
            var filter = new ServerAggregateFilter
            {
                AggregateType = new NodeId("unsupported", 1),
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration(),
                PrimeInitialValue = true
            };
            using var monitoredItem = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                1,
                2,
                new ReadValueId
                {
                    NodeId = new NodeId("V", 1),
                    AttributeId = Attributes.Value
                },
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                3,
                filter,
                filter,
                null,
                0,
                1,
                discardOldest,
                sourceSamplingInterval: 0);
            DateTime timestamp = DateTime.UtcNow;
            var live = new DataValue(
                new Variant(1),
                StatusCodes.Good,
                timestamp,
                timestamp);
            var historyError = new DataValue(
                Variant.Null,
                StatusCodes.BadCommunicationError,
                timestamp,
                timestamp);

            monitoredItem.QueueValue(live, ServiceResult.Good);
            ((IInitialValueMonitoredItem)monitoredItem).QueueInitialValue(
                historyError,
                new ServiceResult(StatusCodes.BadCommunicationError),
                ignoreFilters: true);
            Assert.That(
                ((IInitialValueMonitoredItem)monitoredItem)
                    .CompleteInitialValue(),
                Is.EqualTo(ServiceResult.Good));
            monitoredItem.QueueValue(live, ServiceResult.Good);

            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();
            _ = monitoredItem.Publish(
                new OperationContext(monitoredItem),
                notifications,
                diagnostics,
                10,
                logger);
            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(
                notifications.Peek().Value.StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(notifications.Dequeue().Value, Is.EqualTo(live));

            var nextLive = new DataValue(Variant.From(2), StatusCodes.Good);
            monitoredItem.QueueValue(nextLive, ServiceResult.Good);
            _ = monitoredItem.Publish(
                new OperationContext(monitoredItem),
                notifications,
                diagnostics,
                10,
                logger);
            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications.Dequeue().Value, Is.EqualTo(nextLive));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ShrinkingToQueueSizeOneKeepsLatestValueWithoutProtection(bool discardOldest)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger = telemetry.CreateLogger<MonitoredItemTests>();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock =
                CreateServerMock(telemetry, queueFactory);
            using var aggregateManager = new AggregateManager(serverMock.Object);
            serverMock
                .Setup(value => value.AggregateManager)
                .Returns(aggregateManager);
            var filter = new ServerAggregateFilter
            {
                AggregateType = new NodeId("unsupported", 1),
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration(),
                PrimeInitialValue = true
            };
            using var monitoredItem = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                1,
                2,
                new ReadValueId
                {
                    NodeId = new NodeId("V", 1),
                    AttributeId = Attributes.Value
                },
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                3,
                filter,
                filter,
                null,
                0,
                2,
                discardOldest,
                sourceSamplingInterval: 0);
            DateTime timestamp = DateTime.UtcNow;
            var historyError = new DataValue(
                Variant.Null,
                StatusCodes.BadCommunicationError,
                timestamp,
                timestamp);
            ((IInitialValueMonitoredItem)monitoredItem).QueueInitialValue(
                historyError,
                new ServiceResult(StatusCodes.BadCommunicationError),
                ignoreFilters: true);
            Assert.That(
                ((IInitialValueMonitoredItem)monitoredItem)
                    .CompleteInitialValue(),
                Is.EqualTo(ServiceResult.Good));
            monitoredItem.QueueValue(
                new DataValue(new Variant(1), StatusCodes.Good),
                ServiceResult.Good);
            monitoredItem.QueueValue(
                new DataValue(new Variant(2), StatusCodes.Good),
                ServiceResult.Good);

            ServiceResult result = monitoredItem.ModifyAttributes(
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                3,
                filter,
                filter,
                null,
                0,
                1,
                discardOldest);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();

            _ = monitoredItem.Publish(
                new OperationContext(monitoredItem),
                notifications,
                diagnostics,
                10,
                logger);

            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(
                notifications.Peek().Value.StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(notifications.Dequeue().Value.WrappedValue, Is.EqualTo(Variant.From(2)));

            monitoredItem.QueueValue(
                new DataValue(Variant.From(3), StatusCodes.Good),
                ServiceResult.Good);
            _ = monitoredItem.Publish(
                new OperationContext(monitoredItem),
                notifications,
                diagnostics,
                10,
                logger);

            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications.Peek().Value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(notifications.Peek().Value.WrappedValue, Is.EqualTo(Variant.From(3)));
        }

        [Test]
        public void GrowingFromQueueSizeOneDoesNotReinstateNotificationProtection()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, queueSize: 1);
            ((IInitialValueMonitoredItem)monitoredItem).QueueInitialValue(
                new DataValue(Variant.Null, StatusCodes.BadCommunicationError),
                new ServiceResult(StatusCodes.BadCommunicationError),
                ignoreFilters: true);

            ServiceResult result = monitoredItem.ModifyAttributes(
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                3,
                monitoredItem.Filter,
                monitoredItem.Filter,
                null,
                0,
                2,
                discardOldest: true);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            monitoredItem.QueueValue(new DataValue(Variant.From(1)), ServiceResult.Good);
            monitoredItem.QueueValue(new DataValue(Variant.From(2)), ServiceResult.Good);
            var notifications = new Queue<MonitoredItemNotification>();
            _ = monitoredItem.Publish(
                new OperationContext(monitoredItem),
                notifications,
                new Queue<DiagnosticInfo>(),
                10,
                telemetry.CreateLogger<MonitoredItemTests>());

            Assert.That(notifications, Has.Count.EqualTo(2));
            Assert.That(notifications.All(value => StatusCode.IsGood(value.Value.StatusCode)), Is.True);
            Assert.That(
                notifications.Select(value => value.Value.WrappedValue),
                Is.EqualTo([Variant.From(1), Variant.From(2)]));
        }

        [Test]
        public async Task ModifyAttributesRebuildsAndClearsAggregateCalculatorAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock =
                CreateServerMock(telemetry, queueFactory);
            using var aggregateManager = new AggregateManager(serverMock.Object);
            serverMock
                .Setup(value => value.AggregateManager)
                .Returns(aggregateManager);
            serverMock
                .Setup(value => value.DiagnosticsNodeManager)
                .Returns(new Mock<IDiagnosticsNodeManager>().Object);
            var calculator = new Mock<IAggregateCalculator>();
            calculator
                .Setup(value => value.QueueRawValue(
                    It.IsAny<DataValue>()))
                .Returns(true);
            var aggregateId = new NodeId("TestAggregate", 1);
            int calculatorCalls = 0;
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "TestAggregate",
                (id, start, end, interval, stepped, configuration, context) =>
                {
                    calculatorCalls++;
                    return calculator.Object;
                }).ConfigureAwait(false);
            var initialFilter = new ServerAggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                ProcessingInterval = 1000,
                Stepped = false,
                AggregateConfiguration = new AggregateConfiguration()
            };
            using var monitoredItem = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                1,
                2,
                new ReadValueId
                {
                    NodeId = new NodeId("V", 1),
                    AttributeId = Attributes.Value
                },
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                3,
                initialFilter,
                initialFilter,
                null,
                0,
                10,
                discardOldest: false,
                sourceSamplingInterval: 0);
            Assert.That(calculatorCalls, Is.EqualTo(1));
            var revisedFilter = new ServerAggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = initialFilter.StartTime,
                ProcessingInterval = initialFilter.ProcessingInterval,
                Stepped = true,
                AggregateConfiguration = new AggregateConfiguration()
            };

            ServiceResult result = monitoredItem.ModifyAttributes(
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                3,
                revisedFilter,
                revisedFilter,
                null,
                0,
                10,
                discardOldest: false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(calculatorCalls, Is.EqualTo(2));

            result = monitoredItem.ModifyAttributes(
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                3,
                null,
                null,
                null,
                0,
                10,
                discardOldest: false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            calculator.Invocations.Clear();
            var liveValue = new DataValue(
                new Variant(42),
                StatusCodes.Good,
                DateTime.UtcNow,
                DateTime.UtcNow);

            monitoredItem.QueueValue(liveValue, ServiceResult.Good);

            calculator.Verify(
                value => value.QueueRawValue(
                    It.IsAny<DataValue>()),
                Times.Never);
        }

        [Test]
        public async Task EquivalentAggregateModificationDoesNotRebuildOrReprimeAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock =
                CreateServerMock(telemetry, queueFactory);
            using var aggregateManager = new AggregateManager(serverMock.Object);
            serverMock
                .Setup(value => value.AggregateManager)
                .Returns(aggregateManager);
            serverMock
                .Setup(value => value.DiagnosticsNodeManager)
                .Returns(new Mock<IDiagnosticsNodeManager>().Object);
            var calculator = new Mock<IAggregateCalculator>();
            var aggregateId = new NodeId("EquivalentAggregate", 1);
            int calculatorCalls = 0;
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "EquivalentAggregate",
                (id, start, end, interval, stepped, configuration, context) =>
                {
                    calculatorCalls++;
                    return calculator.Object;
                }).ConfigureAwait(false);
            var initialFilter = new ServerAggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };
            using var monitoredItem = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                1,
                2,
                new ReadValueId
                {
                    NodeId = new NodeId("V", 1),
                    AttributeId = Attributes.Value
                },
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                3,
                initialFilter,
                initialFilter,
                null,
                0,
                10,
                discardOldest: false,
                sourceSamplingInterval: 0);
            var equivalentFilter = new ServerAggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = initialFilter.StartTime,
                ProcessingInterval = initialFilter.ProcessingInterval,
                AggregateConfiguration = new AggregateConfiguration(),
                PrimeInitialValue = true
            };

            ServiceResult result = monitoredItem.ModifyAttributes(
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                4,
                equivalentFilter,
                equivalentFilter,
                null,
                0,
                10,
                discardOldest: false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(calculatorCalls, Is.EqualTo(1));
            Assert.That(equivalentFilter.PrimeInitialValue, Is.False);
            Assert.That(
                ((IInitialValueMonitoredItem)monitoredItem)
                    .CompleteInitialValue(),
                Is.EqualTo(ServiceResult.Good));
        }

        [Test]
        public async Task AggregateCalculatorCreationFailureLeavesAttributesUnchangedAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock =
                CreateServerMock(telemetry, queueFactory);
            using var aggregateManager = new AggregateManager(serverMock.Object);
            serverMock
                .Setup(value => value.AggregateManager)
                .Returns(aggregateManager);
            serverMock
                .Setup(value => value.DiagnosticsNodeManager)
                .Returns(new Mock<IDiagnosticsNodeManager>().Object);
            var aggregateId = new NodeId("FailingAggregate", 1);
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "FailingAggregate",
                (id, start, end, interval, stepped, configuration, context) =>
                {
                    if (stepped)
                    {
                        throw new InvalidOperationException("calculator failure");
                    }
                    return new Mock<IAggregateCalculator>().Object;
                }).ConfigureAwait(false);
            var initialFilter = new ServerAggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = DateTime.UtcNow.AddSeconds(-10),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };
            using var monitoredItem = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                1,
                2,
                new ReadValueId
                {
                    NodeId = new NodeId("V", 1),
                    AttributeId = Attributes.Value
                },
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                3,
                initialFilter,
                initialFilter,
                null,
                100,
                10,
                discardOldest: false,
                sourceSamplingInterval: 0);
            var revisedFilter = new ServerAggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = initialFilter.StartTime,
                ProcessingInterval = initialFilter.ProcessingInterval,
                Stepped = true,
                AggregateConfiguration = new AggregateConfiguration(),
                PrimeInitialValue = true
            };

            Assert.Throws<InvalidOperationException>(() =>
                monitoredItem.ModifyAttributes(
                    DiagnosticsMasks.All,
                    TimestampsToReturn.Server,
                    4,
                    revisedFilter,
                    revisedFilter,
                    null,
                    200,
                    20,
                    discardOldest: true));

            IStoredMonitoredItem stored = monitoredItem.ToStorableMonitoredItem();
            Assert.Multiple(() =>
            {
                Assert.That(stored.ClientHandle, Is.EqualTo(3));
                Assert.That(stored.TimestampsToReturn, Is.EqualTo(TimestampsToReturn.Both));
                Assert.That(stored.FilterToUse, Is.SameAs(initialFilter));
                Assert.That(stored.SamplingInterval, Is.EqualTo(100));
                Assert.That(stored.QueueSize, Is.EqualTo(10));
                Assert.That(stored.DiscardOldest, Is.False);
            });
        }

        [Test]
        public async Task AggregateModificationStartsFreshPrimingCycleAfterOverflowAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock =
                CreateServerMock(telemetry, queueFactory);
            using var aggregateManager = new AggregateManager(serverMock.Object);
            serverMock
                .Setup(value => value.AggregateManager)
                .Returns(aggregateManager);
            serverMock
                .Setup(value => value.DiagnosticsNodeManager)
                .Returns(new Mock<IDiagnosticsNodeManager>().Object);
            var calculator = new Mock<IAggregateCalculator>();
            calculator
                .Setup(value => value.QueueRawValue(It.IsAny<DataValue>()))
                .Returns(true);
            var aggregateId = new NodeId("OverflowAggregate", 1);
            await aggregateManager.RegisterFactoryAsync(
                aggregateId,
                "OverflowAggregate",
                (id, start, end, interval, stepped, configuration, context) =>
                    calculator.Object).ConfigureAwait(false);
            using var monitoredItem = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                null,
                1,
                2,
                new ReadValueId
                {
                    NodeId = new NodeId("V", 1),
                    AttributeId = Attributes.Value
                },
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                MonitoringMode.Reporting,
                3,
                null,
                null,
                null,
                0,
                10,
                discardOldest: false,
                sourceSamplingInterval: 0);
            DateTime timestamp = DateTime.UtcNow;
            var firstFilter = new ServerAggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = timestamp.AddSeconds(-10),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration(),
                PrimeInitialValue = true
            };
            _ = monitoredItem.ModifyAttributes(
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                3,
                firstFilter,
                firstFilter,
                null,
                0,
                10,
                discardOldest: false);
            var live = new DataValue(
                new Variant(1),
                StatusCodes.Good,
                timestamp,
                timestamp);
            for (int ii = 0; ii <= 100_000; ii++)
            {
                monitoredItem.QueueValue(live, ServiceResult.Good);
            }

            Assert.That(
                ((IInitialValueMonitoredItem)monitoredItem)
                    .CompleteInitialValue().StatusCode,
                Is.EqualTo(StatusCodes.BadTooManyOperations));

            var secondFilter = new ServerAggregateFilter
            {
                AggregateType = aggregateId,
                StartTime = firstFilter.StartTime,
                ProcessingInterval = 2000,
                AggregateConfiguration = new AggregateConfiguration(),
                PrimeInitialValue = true
            };
            _ = monitoredItem.ModifyAttributes(
                DiagnosticsMasks.All,
                TimestampsToReturn.Both,
                3,
                secondFilter,
                secondFilter,
                null,
                0,
                10,
                discardOldest: false);
            monitoredItem.QueueValue(live, ServiceResult.Good);

            Assert.That(
                ((IInitialValueMonitoredItem)monitoredItem)
                    .CompleteInitialValue(),
                Is.EqualTo(ServiceResult.Good));
            calculator.Verify(
                value => value.QueueRawValue(It.IsAny<DataValue>()),
                Times.Once);
        }

        [Test]
        public void CreateEventMIOverflow()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, true, 2);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ItemsInQueue, Is.Zero);

            var overflowEvent1 = new AuditUrlMismatchEventState(null);
            var overflowEvent2 = new AuditUrlMismatchEventState(null);
            monitoredItem.QueueEvent(overflowEvent1);
            monitoredItem.QueueEvent(overflowEvent2);

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));

            var overflowEvent3 = new AuditUrlMismatchEventState(null);
            monitoredItem.QueueEvent(overflowEvent3);

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));

            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 3);

            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Has.Count.EqualTo(3));
            EventFieldList publishResult = result.LastOrDefault();
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Handle, Is.AssignableTo<EventQueueOverflowEventState>());
        }

        [Test]
        public void CreateEventMIOverflowMultiplePublish()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, true, 2);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ItemsInQueue, Is.Zero);

            var multiEvent1 = new AuditUrlMismatchEventState(null);
            var multiEvent2 = new AuditUrlMismatchEventState(null);
            monitoredItem.QueueEvent(multiEvent1);
            monitoredItem.QueueEvent(multiEvent2);

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));

            var multiEvent3 = new AuditUrlMismatchEventState(null);
            monitoredItem.QueueEvent(multiEvent3);

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));

            var result = new Queue<EventFieldList>();
            bool moreItems = monitoredItem.Publish(new OperationContext(monitoredItem), result, 2);

            Assert.That(moreItems, Is.True);
            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Has.Count.EqualTo(2));
            EventFieldList publishResult = result.LastOrDefault();
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Handle, Is.AssignableTo<AuditUrlMismatchEventState>());

            var result2 = new Queue<EventFieldList>();
            bool moreItems2 = monitoredItem.Publish(
                new OperationContext(monitoredItem),
                result2,
                2);

            Assert.That(moreItems2, Is.False);
            Assert.That(result2, Is.Not.Empty);
            Assert.That(result2, Has.Count.EqualTo(1));
            EventFieldList publishResult2 = result2.FirstOrDefault();
            Assert.That(publishResult2, Is.Not.Null);
            Assert.That(publishResult2.Handle, Is.AssignableTo<EventQueueOverflowEventState>());
        }

        [Test]
        public void CreateEventMIOverflowNoDiscard()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, true, 2, true);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ItemsInQueue, Is.Zero);

            var noDiscardEvent1 = new AuditUrlMismatchEventState(null);
            var noDiscardEvent2 = new AuditUrlMismatchEventState(null);
            monitoredItem.QueueEvent(noDiscardEvent1);
            monitoredItem.QueueEvent(noDiscardEvent2);

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));

            var noDiscardEvent3 = new AuditUrlMismatchEventState(null);
            monitoredItem.QueueEvent(noDiscardEvent3);

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(2));

            var result = new Queue<EventFieldList>();
            monitoredItem.Publish(new OperationContext(monitoredItem), result, 3);

            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Has.Count.EqualTo(3));
            EventFieldList publishResult = result.FirstOrDefault();
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Handle, Is.AssignableTo<EventQueueOverflowEventState>());
        }

        [Test]
        public void CreateEventMIPublishPartial()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            using MonitoredItem monitoredItem = CreateMonitoredItem(telemetry, true, 3);
            Assert.That(monitoredItem, Is.Not.Null);
            Assert.That(monitoredItem.ItemsInQueue, Is.Zero);

            var partialEvent1 = new AuditUrlMismatchEventState(null);
            var partialEvent2 = new AuditUrlMismatchEventState(null);
            var partialEvent3 = new AuditUrlMismatchEventState(null);
            monitoredItem.QueueEvent(partialEvent1);
            monitoredItem.QueueEvent(partialEvent2);
            monitoredItem.QueueEvent(partialEvent3);

            Assert.That(monitoredItem.ItemsInQueue, Is.EqualTo(3));

            var result = new Queue<EventFieldList>();
            bool moreItems = monitoredItem.Publish(new OperationContext(monitoredItem), result, 2);

            Assert.That(moreItems, Is.True);
            Assert.That(result, Is.Not.Empty);
            Assert.That(result, Has.Count.EqualTo(2));
            EventFieldList publishResult = result.LastOrDefault();
            Assert.That(publishResult, Is.Not.Null);
            Assert.That(publishResult.Handle, Is.AssignableTo<AuditUrlMismatchEventState>());

            var result2 = new Queue<EventFieldList>();
            bool moreItems2 = monitoredItem.Publish(
                new OperationContext(monitoredItem),
                result2,
                2);

            Assert.That(moreItems2, Is.False);
            Assert.That(result2, Is.Not.Empty);
            Assert.That(result2, Has.Count.EqualTo(1));
            EventFieldList publishResult2 = result2.LastOrDefault();
            Assert.That(publishResult2, Is.Not.Null);
            Assert.That(publishResult2.Handle, Is.AssignableTo<AuditUrlMismatchEventState>());
        }

#pragma warning disable CS0618 // Test coverage for the obsolete compatibility constructor.
        [Test]
        public void ObsoleteConstructorDelegatesToAsyncConstructor()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock = CreateServerMock(telemetry, queueFactory);
            var nodeManagerMock = new Mock<INodeManager>();

            using var monitoredItem = new MonitoredItem(
                serverMock.Object,
                nodeManagerMock.Object,
                managerHandle: null,
                subscriptionId: 1,
                id: 2,
                itemToMonitor: new ReadValueId
                {
                    NodeId = new NodeId("Compatibility", 2),
                    AttributeId = Attributes.Value
                },
                diagnosticsMasks: DiagnosticsMasks.All,
                timestampsToReturn: TimestampsToReturn.Server,
                monitoringMode: MonitoringMode.Reporting,
                clientHandle: 3,
                originalFilter: new MonitoringFilter(),
                filterToUse: new MonitoringFilter(),
                range: new Range(10, 4),
                samplingInterval: 1000,
                queueSize: 10,
                discardOldest: true,
                sourceSamplingInterval: 1000);

            Assert.Multiple(() =>
            {
                Assert.That(monitoredItem.Id, Is.EqualTo(2u));
                Assert.That(monitoredItem.NodeId, Is.EqualTo(new NodeId("Compatibility", 2)));
                Assert.That(monitoredItem.QueueSize, Is.EqualTo(10u));
            });
        }
#pragma warning restore CS0618

        [Test]
        public void ConstructorThrowsForNullItemToMonitor()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock = CreateServerMock(telemetry, queueFactory);

            Assert.Throws<ArgumentNullException>(() =>
                new MonitoredItem(
                    serverMock.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    managerHandle: null,
                    subscriptionId: 1,
                    id: 2,
                    itemToMonitor: null,
                    diagnosticsMasks: DiagnosticsMasks.All,
                    timestampsToReturn: TimestampsToReturn.Server,
                    monitoringMode: MonitoringMode.Reporting,
                    clientHandle: 3,
                    originalFilter: new MonitoringFilter(),
                    filterToUse: new MonitoringFilter(),
                    range: null,
                    samplingInterval: 1000,
                    queueSize: 10,
                    discardOldest: true,
                    sourceSamplingInterval: 1000));
        }

        private static MonitoredItem CreateMonitoredItem(
            ITelemetryContext telemetry,
            bool events = false,
            uint queueSize = 10,
            bool discardOldest = false)
        {
            MonitoringFilter filter = events ? new EventFilter() : new MonitoringFilter();

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            serverMock.Setup(s => s.MonitoredItemQueueFactory)
                .Returns(queueFactory);

            var nodeMangerMock = new Mock<IAsyncNodeManager>();

            return new MonitoredItem(
                serverMock.Object,
                nodeMangerMock.Object,
                null,
                1,
                2,
                new ReadValueId(),
                DiagnosticsMasks.All,
                TimestampsToReturn.Server,
                MonitoringMode.Reporting,
                3,
                filter,
                filter,
                null,
                1000.0,
                queueSize,
                discardOldest,
                1000);
        }

        private static Mock<IServerInternal> CreateServerMock(
            ITelemetryContext telemetry,
            IMonitoredItemQueueFactory queueFactory)
        {
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);
            return serverMock;
        }

        private sealed class Int32ValueKeySelector :
            IHistorianStructuredDataKeySelector
        {
            public static Int32ValueKeySelector Instance { get; } = new();

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
                uniquenessKey = ByteString.From(BitConverter.GetBytes(key));
                return true;
            }
        }

        private sealed class ConstantValueKeySelector :
            IHistorianStructuredDataKeySelector
        {
            public static ConstantValueKeySelector Instance { get; } = new();

            public ArrayOf<QualifiedName> UniquenessFields { get; } =
                [new QualifiedName("Value")];

            public bool TryGetUniquenessKey(
                in DataValue value,
                out ByteString uniquenessKey)
            {
                uniquenessKey = ByteString.From([1]);
                return true;
            }
        }
    }
}
