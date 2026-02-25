using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using Opc.Ua;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("MonitoredItem")]
    [Parallelizable]
    public class MonitoredItemIsReadyToPublishTests
    {
        [Test]
        public void IsReadyToPublish_NotReady_ReturnsFalse()
        {
            var telemetry = NUnitTelemetryContext.Create();
            using var item = CreateMonitoredItem(telemetry);

            // Default state: not ready
            Assert.That(item.IsReadyToPublish, Is.False);
        }

        [Test]
        public void IsReadyToPublish_Ready_ReturnsTrue()
        {
            var telemetry = NUnitTelemetryContext.Create();
            using var item = CreateMonitoredItem(telemetry);

            // Queue a value to make it ready
            item.QueueValue(new DataValue(new Variant(1)), null);

            Assert.That(item.IsReadyToPublish, Is.True);
        }

        [Test]
        public void IsReadyToPublish_Disabled_ReturnsFalse()
        {
            var telemetry = NUnitTelemetryContext.Create();
            using var item = CreateMonitoredItem(telemetry);
            item.QueueValue(new DataValue(new Variant(1)), null);

            item.SetMonitoringMode(MonitoringMode.Disabled);

            Assert.That(item.IsReadyToPublish, Is.False);
        }

        [Test]
        public void IsReadyToPublish_SamplingMode_ReturnsFalse()
        {
            var telemetry = NUnitTelemetryContext.Create();
            using var item = CreateMonitoredItem(telemetry);
            item.QueueValue(new DataValue(new Variant(1)), null);

            item.SetMonitoringMode(MonitoringMode.Sampling);

            Assert.That(item.IsReadyToPublish, Is.False);
        }

        [Test]
        public void IsReadyToPublish_SamplingModeWithTrigger_ReturnsTrue()
        {
            var telemetry = NUnitTelemetryContext.Create();
            using var item = CreateMonitoredItem(telemetry);
            item.QueueValue(new DataValue(new Variant(1)), null);
            item.SetMonitoringMode(MonitoringMode.Sampling);

            // Triggering requires m_readyToPublish to be true, which it is after QueueValue
            bool triggered = item.SetTriggered();
            Assert.That(triggered, Is.True, "Should accept trigger");

            Assert.That(item.IsReadyToPublish, Is.True);
        }

        [Test]
        public void IsReadyToPublish_AggregateCalculator_ReturnsTrue_RegardlessOfReadyState()
        {
            var telemetry = NUnitTelemetryContext.Create();
            var mockCalculator = new Mock<IAggregateCalculator>();
            mockCalculator.Setup(c => c.HasEndTimePassed(It.IsAny<DateTime>())).Returns(true);

            // Setup Aggregate Manager with a custom factory
            var serverMockWrapper = CreateServerMock(telemetry);
            var serverMock = serverMockWrapper.Mock;
            
            var aggregateManager = new AggregateManager(serverMock.Object);
            serverMock.Setup(s => s.AggregateManager).Returns(aggregateManager);

            NodeId aggId = new NodeId(1234);
            aggregateManager.RegisterFactory(aggId, "TestAgg", 
                (id, start, end, interval, stepped, config, tel) => mockCalculator.Object);

            var filter = new ServerAggregateFilter
            {
                AggregateType = aggId,
                StartTime = DateTime.UtcNow,
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };

            using var item = CreateMonitoredItem(
                telemetry, 
                filterToUse: filter, 
                serverMock: serverMockWrapper);

            // Even if no value is queued (m_readyToPublish is false), HasEndTimePassed is true
            Assert.That(item.IsReadyToPublish, Is.True);
        }

        [Test]
        public void IsReadyToPublish_WaitSourceSamplingInterval_ReturnsFalse()
        {
            var telemetry = NUnitTelemetryContext.Create();
            
            // Create item with sourceSamplingInterval = 0
            // logic: if (m_sourceSamplingInterval == 0) { check timestamp }
            using var item = CreateMonitoredItem(telemetry, sourceSamplingInterval: 0);
            
            // First queue a value so m_readyToPublish = true
            item.QueueValue(new DataValue(new Variant(1)), null);

            // Set a large sampling interval so m_nextSamplingTime is in future
            item.SetSamplingInterval(5000); 

            // Should be false because we are waiting
            Assert.That(item.IsReadyToPublish, Is.False);
        }

        [Test]
        public void IsReadyToPublish_WaitSourceSamplingInterval_Expired_ReturnsTrue()
        {
            var telemetry = NUnitTelemetryContext.Create();
            using var item = CreateMonitoredItem(telemetry, sourceSamplingInterval: 0);
            
            item.QueueValue(new DataValue(new Variant(1)), null);

            // Set sampling interval to 0. 
            // Logic: m_nextSamplingTime = 0.
            // 0 > now is False.
            // So return True (Normal).
            item.SetSamplingInterval(0);

            Assert.That(item.IsReadyToPublish, Is.True);
        }

        // Helpers

        private class ServerMockWrapper
        {
            public Mock<IServerInternal> Mock { get; set; }
        }

        private ServerMockWrapper CreateServerMock(ITelemetryContext telemetry)
        {
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            
            serverMock.Setup(s => s.MonitoredItemQueueFactory)
                .Returns(new MonitoredItemQueueFactory(telemetry));
                
            serverMock.Setup(s => s.SubscriptionStore).Returns(new Mock<ISubscriptionStore>().Object);
            
            var diagMock = new Mock<IDiagnosticsNodeManager>();
            serverMock.Setup(s => s.DiagnosticsNodeManager).Returns(diagMock.Object);

            return new ServerMockWrapper { Mock = serverMock };
        }

        private MonitoredItem CreateMonitoredItem(
            ITelemetryContext telemetry,
            double samplingInterval = 1000.0,
            double sourceSamplingInterval = 1000,
            MonitoringFilter filterToUse = null,
            ServerMockWrapper serverMock = null,
            MonitoringMode monitoringMode = MonitoringMode.Reporting)
        {
            if (serverMock == null)
            {
                serverMock = CreateServerMock(telemetry);
            }
            
            var nodeMangerMock = new Mock<INodeManager>();

            if (filterToUse == null)
            {
                filterToUse = new MonitoringFilter();
            }

            // Create ReadValueId with NodeId for aggregation
            var readValueId = new ReadValueId { NodeId = new NodeId(1) };

            return new MonitoredItem(
                serverMock.Mock.Object,
                nodeMangerMock.Object,
                null,           // managerHandle
                1,              // subscriptionId
                2,              // id
                readValueId,
                DiagnosticsMasks.All,
                TimestampsToReturn.Server,
                monitoringMode,
                3,              // clientHandle
                null,           // originalFilter
                filterToUse,
                null,           // range
                samplingInterval,
                10,             // queueSize
                true,           // discardOldest
                sourceSamplingInterval);
        }
    }
}
