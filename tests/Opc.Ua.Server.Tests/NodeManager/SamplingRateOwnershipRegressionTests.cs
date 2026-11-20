// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies that sampling-group disposal does not mutate rate tables borrowed by other groups or their manager.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class SamplingRateOwnershipRegressionTests
    {
        /// <summary>
        /// Verifies that disposing one group preserves shared rate definitions for surviving and newly created groups.
        /// </summary>
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

        /// <summary>
        /// Verifies that retiring a sampling group leaves the manager able to revise later items to the same rate.
        /// </summary>
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

        /// <summary>
        /// Creates a session-backed monitored-item context for sampling-group ownership checks.
        /// </summary>
        private static OperationContext CreateContext()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(1));
            return new OperationContext(
                new RequestHeader(), null!, RequestType.CreateMonitoredItems, RequestLifetime.None, session.Object);
        }

        /// <summary>
        /// Creates a reporting data-change item with the requested sampling interval for rate revision.
        /// </summary>
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
