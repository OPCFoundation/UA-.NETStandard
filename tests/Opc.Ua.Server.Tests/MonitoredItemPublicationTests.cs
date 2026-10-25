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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests the optional final-observation interface without confusing item dequeue with a Publish response.
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [Category("ValuePublication")]
    [Parallelizable(ParallelScope.All)]
    public sealed class MonitoredItemPublicationTests
    {
        /// <summary>
        /// Dequeuing the exact final sample is still insufficient until a real Publish response contains it.
        /// </summary>
        [TestCase(1u)]
        [TestCase(3u)]
        public async Task DequeueDoesNotClaimPublication(uint queueSize)
        {
            using var harness = new Harness(queueSize);
            using var cancellation = new CancellationTokenSource();
            Task observation = harness.ObserveAsync(42, cancellation.Token);
            harness.Item.QueueValue(new DataValue(99), ServiceResult.Good);

            Queue<MonitoredItemNotification> notifications = harness.Dequeue(1);
            Assert.That(notifications, Has.Count.EqualTo(1));
            Assert.That(notifications.Peek().Value.WrappedValue, Is.EqualTo(new Variant(42)));
            Assert.That(observation.IsCompleted, Is.False);
            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () => await observation.ConfigureAwait(false));

            harness.Item.QueueValue(new DataValue(100), ServiceResult.Good);
            Assert.That(harness.Dequeue(1).Peek().Value.WrappedValue, Is.EqualTo(new Variant(100)));
        }

        /// <summary>
        /// A partial batch of older entries cannot satisfy the final sample's observation.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task PartialQueueDrainKeepsWaitingForTheFinalEntry(bool discardOldest)
        {
            using var harness = new Harness(3, discardOldest);
            using var cancellation = new CancellationTokenSource();
            harness.Item.QueueValue(new DataValue(10), ServiceResult.Good);
            harness.Item.QueueValue(new DataValue(11), ServiceResult.Good);
            Task observation = harness.ObserveAsync(12, cancellation.Token);

            Assert.That(harness.Dequeue(1).Peek().Value.WrappedValue, Is.EqualTo(new Variant(10)));
            Assert.That(observation.IsCompleted, Is.False);
            Assert.That(harness.Dequeue(1).Peek().Value.WrappedValue, Is.EqualTo(new Variant(11)));
            Assert.That(observation.IsCompleted, Is.False);
            Assert.That(harness.Dequeue(1).Peek().Value.WrappedValue, Is.EqualTo(new Variant(12)));
            Assert.That(observation.IsCompleted, Is.False, "Dequeue is not response construction.");
            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () => await observation.ConfigureAwait(false));
        }

        /// <summary>
        /// A zero-sized publication budget cannot consume a queue-size-one final value.
        /// </summary>
        [Test]
        public async Task ZeroPublicationBudgetDoesNotConsumeFinalValue()
        {
            using var harness = new Harness(1);
            using var cancellation = new CancellationTokenSource();
            Task observation = harness.ObserveAsync(42, cancellation.Token);
            Assert.That(harness.Dequeue(0), Is.Empty);
            Assert.That(harness.Dequeue(1).Peek().Value.WrappedValue, Is.EqualTo(new Variant(42)));
            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () => await observation.ConfigureAwait(false));
        }

        /// <summary>
        /// An overlapping finalization fails without changing or cancelling its predecessor.
        /// </summary>
        [Test]
        public async Task CompetingFinalizationDoesNotSupersedeTheFirst()
        {
            using var harness = new Harness(1);
            using var cancellation = new CancellationTokenSource();
            Task first = harness.ObserveAsync(42, cancellation.Token);
            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await harness.ObserveAsync(99, cancellation.Token).ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(harness.Dequeue(1).Peek().Value.WrappedValue, Is.EqualTo(new Variant(42)));
            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () => await first.ConfigureAwait(false));

            using var retryCancellation = new CancellationTokenSource();
            Task retry = harness.ObserveAsync(99, retryCancellation.Token);
            Assert.That(harness.Dequeue(1).Peek().Value.WrappedValue, Is.EqualTo(new Variant(99)));
            retryCancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () => await retry.ConfigureAwait(false));
        }

        /// <summary>
        /// The injected clock expires a wait and releases protection without disposing the item.
        /// </summary>
        [TestCase(1u)]
        [TestCase(3u)]
        public async Task DeadlineUsesTimeProviderAndReleasesProtection(uint queueSize)
        {
            using var harness = new Harness(queueSize);
            Task observation = harness.ObserveAsync(42, CancellationToken.None);
            harness.Clock.Advance(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(async () => await observation.ConfigureAwait(false));

            harness.Item.QueueValue(new DataValue(100), ServiceResult.Good);
            harness.Item.QueueValue(new DataValue(101), ServiceResult.Good);
            harness.Item.QueueValue(new DataValue(102), ServiceResult.Good);
            Queue<MonitoredItemNotification> notifications = harness.Dequeue(10);
            Assert.That(notifications, Is.Not.Empty);
            Assert.That(notifications.Peek().Value.WrappedValue, Is.Not.EqualTo(new Variant(42)));
        }

        /// <summary>
        /// Non-reporting modes have no final-publication obligation and are not forced to report.
        /// </summary>
        [TestCase(MonitoringMode.Disabled)]
        [TestCase(MonitoringMode.Sampling)]
        public async Task NonReportingItemIsNotForcedToPublish(MonitoringMode mode)
        {
            using var harness = new Harness(1);
            harness.Item.SetMonitoringMode(mode);
            await harness.ObserveAsync(42, CancellationToken.None).ConfigureAwait(false);
            Assert.That(harness.Dequeue(10), Is.Empty);
        }

        /// <summary>
        /// Leaving Reporting releases only this item's observation.
        /// </summary>
        [TestCase(MonitoringMode.Disabled)]
        [TestCase(MonitoringMode.Sampling)]
        public async Task LeavingReportingReleasesObservation(MonitoringMode mode)
        {
            using var harness = new Harness(1);
            Task observation = harness.ObserveAsync(42, CancellationToken.None);
            harness.Item.SetMonitoringMode(mode);
            await observation.ConfigureAwait(false);
            Assert.That(harness.Dequeue(10), Is.Empty);
        }

        /// <summary>
        /// Removing a subscriber releases its wait without claiming a value was sent.
        /// </summary>
        [Test]
        public async Task ItemDisposalReleasesObservation()
        {
            using var harness = new Harness(1);
            Task observation = harness.ObserveAsync(42, CancellationToken.None);
            harness.Item.Dispose();
            await observation.ConfigureAwait(false);
        }

        /// <summary>
        /// Deleting the source before observation is a failure, not a successfully drained queue.
        /// </summary>
        [Test]
        public async Task PrematureSourceDeletionFailsObservation()
        {
            using var harness = new Harness(1);
            Task observation = harness.ObserveAsync(42, CancellationToken.None);
            ((IMonitoredItemGenerationBinding)harness.Item).Invalidate(terminal: false);
            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await observation.ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// A custom subscription without observation support must not return a successful no-op.
        /// </summary>
        [Test]
        public async Task UnsupportedSubscriptionFailsClearly()
        {
            using var harness = new Harness(1);
            harness.Item.SubscriptionCallback = new Mock<ISubscription>().Object;
            await Assert.ThrowsAsync<NotSupportedException>(
                async () => await harness.ObserveAsync(42, CancellationToken.None).ConfigureAwait(false));
        }

        /// <summary>
        /// A raw final value cannot be represented as the result of an aggregate filter.
        /// </summary>
        [Test]
        public async Task AggregateFilterFailsClearly()
        {
            using var harness = new Harness(1, filter: CreateAggregateFilter());
            await Assert.ThrowsAsync<NotSupportedException>(
                async () => await harness.ObserveAsync(42, CancellationToken.None).ConfigureAwait(false));
        }

        /// <summary>
        /// Introducing aggregation while a terminal observation is pending fails that observation.
        /// </summary>
        [Test]
        public async Task AggregationChangeFailsThePendingObservation()
        {
            using var harness = new Harness(1);
            Task observation = harness.ObserveAsync(42, CancellationToken.None);
            ServerAggregateFilter aggregate = CreateAggregateFilter();
            harness.Item.ModifyAttributes(
                DiagnosticsMasks.None, TimestampsToReturn.Both, 1, aggregate, aggregate, null, 0, 1, true);
            ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await observation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// Switching between single-value and queued storage preserves the required final observation.
        /// </summary>
        [TestCase(1u, 3u)]
        [TestCase(3u, 1u)]
        public async Task QueueResizePreservesFinalSample(uint initialSize, uint revisedSize)
        {
            using var harness = new Harness(initialSize);
            using var cancellation = new CancellationTokenSource();
            Task observation = harness.ObserveAsync(42, cancellation.Token);
            harness.Item.ModifyAttributes(
                DiagnosticsMasks.None, TimestampsToReturn.Both, 1, null, null, null, 0, revisedSize, true);
            harness.Item.QueueValue(new DataValue(99), ServiceResult.Good);
            Assert.That(harness.Dequeue(1).Peek().Value.WrappedValue, Is.EqualTo(new Variant(42)));
            Assert.That(observation.IsCompleted, Is.False);
            cancellation.Cancel();
            await Assert.CatchAsync<OperationCanceledException>(async () => await observation.ConfigureAwait(false));
        }

        /// <summary>
        /// Every direct call must use a finite, positive deadline.
        /// </summary>
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(double.MaxValue)]
        public async Task InvalidDeadlinesAreRejected(double milliseconds)
        {
            using var harness = new Harness(1);
            TimeSpan timeout = milliseconds == double.MaxValue
                ? TimeSpan.MaxValue
                : TimeSpan.FromMilliseconds(milliseconds);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await harness.Item.WaitForValuePublicationAsync(
                    harness.Session, new DataValue(42), timeout).ConfigureAwait(false));
        }

        private static ServerAggregateFilter CreateAggregateFilter()
        {
            return new ServerAggregateFilter
            {
                AggregateType = ObjectIds.AggregateFunction_Average,
                StartTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ProcessingInterval = 1000,
                AggregateConfiguration = new AggregateConfiguration()
            };
        }

        private sealed class Harness : IDisposable
        {
            public Harness(uint queueSize, bool discardOldest = true, MonitoringFilter? filter = null)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                m_logger = telemetry.CreateLogger<MonitoredItemPublicationTests>();
                m_queueFactory = new MonitoredItemQueueFactory(telemetry);
                var server = new Mock<IServerInternal>();
                server.SetupGet(value => value.Telemetry).Returns(telemetry);
                server.SetupGet(value => value.NamespaceUris).Returns(new NamespaceTable());
                server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(m_queueFactory);
                server.SetupGet(value => value.TypeTree).Returns(new TypeTable(new NamespaceTable()));
                server.SetupGet(value => value.ServerUris).Returns(new StringTable());
                server.SetupGet(value => value.Factory).Returns(new Mock<IEncodeableFactory>().Object);
                server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
                var diagnostics = new Mock<IDiagnosticsNodeManager>();
                diagnostics.Setup(value => value.CreateSubscriptionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<SubscriptionDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>()))
                    .ReturnsAsync(new NodeId(1));
                server.SetupGet(value => value.DiagnosticsNodeManager).Returns(diagnostics.Object);
                m_aggregateManager = new AggregateManager(server.Object);
                server.SetupGet(value => value.AggregateManager).Returns(m_aggregateManager);
                var session = new Mock<ISession>();
                var identity = new UserIdentity(new AnonymousIdentityToken());
                session.SetupGet(value => value.Id).Returns(new NodeId(1));
                session.SetupGet(value => value.Identity).Returns(identity);
                session.SetupGet(value => value.EffectiveIdentity).Returns(identity);
                session.SetupGet(value => value.IdentityToken).Returns(identity.TokenHandler);
                Session = session.Object;
                Clock = new FakeTimeProvider();
                m_subscription = new Subscription(
                    server.Object, Session, 1, 100, 100, 10, 1, 0, true, 10, Clock);
                Item = new MonitoredItem(
                    server.Object,
                    new Mock<IAsyncNodeManager>().Object,
                    null!,
                    1,
                    1,
                    new ReadValueId { NodeId = new NodeId("Value", 1), AttributeId = Attributes.Value },
                    DiagnosticsMasks.None,
                    TimestampsToReturn.Both,
                    MonitoringMode.Reporting,
                    1,
                    filter,
                    filter,
                    null,
                    0,
                    queueSize,
                    discardOldest,
                    0,
                    false,
                    Clock)
                {
                    SubscriptionCallback = m_subscription
                };
            }

            public MonitoredItem Item { get; }

            public ISession Session { get; }

            public FakeTimeProvider Clock { get; }

            public Task ObserveAsync(int value, CancellationToken cancellationToken)
            {
                return Item.WaitForValuePublicationAsync(
                    Session, new DataValue(value), TimeSpan.FromSeconds(5), cancellationToken).AsTask();
            }

            public Queue<MonitoredItemNotification> Dequeue(uint limit)
            {
                var notifications = new Queue<MonitoredItemNotification>();
                using var context = new OperationContext(Session, DiagnosticsMasks.None);
                Item.Publish(context, notifications, new Queue<DiagnosticInfo>(), limit, m_logger);
                return notifications;
            }

            public void Dispose()
            {
                Item.Dispose();
                m_subscription.Dispose();
                m_queueFactory.Dispose();
                m_aggregateManager.Dispose();
            }

            private readonly ILogger m_logger;
            private readonly MonitoredItemQueueFactory m_queueFactory;
            private readonly Subscription m_subscription;
            private readonly AggregateManager m_aggregateManager;
        }
    }
}
