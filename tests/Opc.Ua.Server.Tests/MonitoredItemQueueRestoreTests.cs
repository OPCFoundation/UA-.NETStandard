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

// CA2000: test code; the created queues/monitored items are short-lived and disposed by the runtime.
#pragma warning disable CA2000
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests that the monitored-item restore path consumes a pre-hydrated queue supplied by an asynchronous
    /// <see cref="ISubscriptionStore"/> and otherwise falls back to the synchronous restore.
    /// </summary>
    [TestFixture]
    [Category("MonitoredItem")]
    [Parallelizable]
    public class MonitoredItemQueueRestoreTests
    {
        /// <summary>
        /// Verifies that restoration uses a prehydrated data-change queue without invoking synchronous fallback.
        /// </summary>
        [Test]
        public void RestoreUsesPreHydratedDataChangeQueueAndSkipsSyncFallback()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var storeMock = new Mock<ISubscriptionStore>();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock = CreateServerMock(telemetry, queueFactory, storeMock.Object);

            IDataChangeMonitoredItemQueue preHydrated = queueFactory.CreateDataChangeQueue(false, 2);
            preHydrated.ResetQueue(10, false);
            preHydrated.Enqueue(new DataValue(new Variant(7)), ServiceResult.Good);

            StoredMonitoredItem stored = CreateStoredItem();
            stored.RestoredDataChangeQueue = preHydrated;

            using var item = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored);

            Assert.That(item.Id, Is.EqualTo(stored.Id));
            storeMock.Verify(
                s => s.RestoreDataChangeMonitoredItemQueue(It.IsAny<uint>()),
                Times.Never);
        }

        /// <summary>
        /// Verifies that restoration uses synchronous queue fallback when no prehydrated queue is available.
        /// </summary>
        [Test]
        public void RestoreFallsBackToSyncRestoreWhenNotPreHydrated()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var storeMock = new Mock<ISubscriptionStore>();
            storeMock
                .Setup(s => s.RestoreDataChangeMonitoredItemQueue(It.IsAny<uint>()))
                .Returns((IDataChangeMonitoredItemQueue)null!);
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock = CreateServerMock(telemetry, queueFactory, storeMock.Object);

            StoredMonitoredItem stored = CreateStoredItem();

            using var item = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored);

            storeMock.Verify(
                s => s.RestoreDataChangeMonitoredItemQueue(stored.Id),
                Times.Once);
        }

        /// <summary>
        /// Verifies that restoring a queue does not reinstate transient required-notification protection.
        /// </summary>
        [TestCase(1u, false, false)]
        [TestCase(2u, false, false)]
        [TestCase(2u, true, false)]
        [TestCase(2u, true, true)]
        public void RestoreDoesNotReinstateRequiredNotificationProtection(
            uint queueSize,
            bool restoreQueue,
            bool queueContainsRequired)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ILogger logger =
                telemetry.CreateLogger<MonitoredItemQueueRestoreTests>();
            var storeMock = new Mock<ISubscriptionStore>();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> serverMock =
                CreateServerMock(telemetry, queueFactory, storeMock.Object);
            DateTime timestamp = DateTime.UtcNow;
            var requiredValue = new DataValue(
                Variant.Null,
                StatusCodes.BadCommunicationError,
                timestamp,
                timestamp);
            StoredMonitoredItem stored = StoreItemWithPendingHistoryFailure(
                serverMock.Object,
                queueSize,
                requiredValue);
            if (restoreQueue)
            {
                IDataChangeMonitoredItemQueue preHydrated =
                    queueFactory.CreateDataChangeQueue(false, stored.Id);
                preHydrated.ResetQueue(queueSize, false);
                preHydrated.Enqueue(
                    queueContainsRequired
                        ? requiredValue
                        : new DataValue(Variant.From(7), StatusCodes.Good),
                    queueContainsRequired
                        ? stored.LastError
                        : ServiceResult.Good);
                stored.RestoredDataChangeQueue = preHydrated;
            }

            using var item = new MonitoredItem(
                serverMock.Object,
                new Mock<IAsyncNodeManager>().Object,
                new object(),
                stored);
            item.QueueValue(
                new DataValue(Variant.From(42), StatusCodes.Good),
                ServiceResult.Good);
            item.QueueValue(
                new DataValue(Variant.From(43), StatusCodes.Good),
                ServiceResult.Good);
            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();

            _ = item.Publish(
                new OperationContext(item),
                notifications,
                diagnostics,
                10,
                logger);

            Assert.That(
                notifications.Any(value =>
                    value.Value.StatusCode.Code ==
                        StatusCodes.BadCommunicationError),
                Is.False,
                "A restored error value follows ordinary queue replacement, not live priming protection.");
            Assert.That(notifications, Has.Count.EqualTo(queueSize));
            Assert.That(
                notifications.Last().Value.WrappedValue.TryGetValue(out int lastValue),
                Is.True);
            Assert.That(lastValue, Is.EqualTo(43));
        }

        /// <summary>
        /// Verifies that restoration preserves raw error values without synthesizing missing notifications.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void RestoreKeepsRawErrorValuesWithoutSynthesizingMissingNotifications(bool queueContainsError)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> server = CreateServerMock(
                telemetry,
                queueFactory,
                Mock.Of<ISubscriptionStore>());
            var errorValue = new DataValue(Variant.Null, StatusCodes.BadCommunicationError);
            StoredMonitoredItem stored = StoreItemWithPendingHistoryFailure(server.Object, 2, errorValue);
            IDataChangeMonitoredItemQueue queue = queueFactory.CreateDataChangeQueue(false, stored.Id);
            queue.ResetQueue(2, false);
            queue.Enqueue(
                queueContainsError ? errorValue : new DataValue(Variant.From(7)),
                queueContainsError ? stored.LastError : ServiceResult.Good);
            stored.RestoredDataChangeQueue = queue;
            using var item = new MonitoredItem(
                server.Object,
                Mock.Of<IAsyncNodeManager>(),
                new object(),
                stored);

            item.QueueValue(new DataValue(Variant.From(42)), ServiceResult.Good);
            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();
            _ = item.Publish(
                new OperationContext(item),
                notifications,
                diagnostics,
                10,
                telemetry.CreateLogger<MonitoredItemQueueRestoreTests>());

            Assert.That(notifications, Has.Count.EqualTo(2));
            Assert.That(
                notifications.Any(value => value.Value.StatusCode.Code == StatusCodes.BadCommunicationError),
                Is.EqualTo(queueContainsError),
                "Only values actually present in the restored raw queue should be delivered.");
            Assert.That(
                notifications.Last().Value.WrappedValue.TryGetValue(out int lastValue),
                Is.True);
            Assert.That(lastValue, Is.EqualTo(42));
        }

        /// <summary>
        /// Verifies that queue prehydration continues after a store failure and respects the queue-size gate.
        /// </summary>
        [Test]
        public async Task PreHydrateContinuesAfterStoreFailureAndHonorsQueueSizeGateAsync()
        {
            using var loggerProvider = new CapturingLoggerProvider();
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
            await using MasterNodeManagerFixture fixture = await CreateMasterNodeManagerFixtureAsync(loggerFactory).ConfigureAwait(false);
            using var queueFactory = new MonitoredItemQueueFactory(fixture.Telemetry);
            var storeMock = new Mock<ISubscriptionStore>();
            IDataChangeMonitoredItemQueue restoredQueue = queueFactory.CreateDataChangeQueue(false, 2);
            restoredQueue.ResetQueue(10, false);
            restoredQueue.Enqueue(new DataValue(new Variant(7)), ServiceResult.Good);
            storeMock.Setup(s => s.RestoreDataChangeMonitoredItemQueueAsync(2, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IDataChangeMonitoredItemQueue?>(restoredQueue));
            storeMock.Setup(s => s.RestoreDataChangeMonitoredItemQueueAsync(3, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IDataChangeMonitoredItemQueue?>(
                    Task.FromException<IDataChangeMonitoredItemQueue?>(
                        new InvalidOperationException("store failed"))));

            fixture.Server.Setup(s => s.SubscriptionStore).Returns(storeMock.Object);

            List<IStoredMonitoredItem> items =
            [
                CreateStoredItem(id: 2, queueSize: 10),
                CreateStoredItem(id: 3, queueSize: 10),
                CreateStoredItem(id: 4, queueSize: 1)
            ];

            await InvokePreHydrateAsync(fixture.Manager, items).ConfigureAwait(false);

            Assert.That(items[0].RestoredDataChangeQueue, Is.SameAs(restoredQueue));
            Assert.That(items[1].RestoredDataChangeQueue, Is.Null);
            Assert.That(items[2].RestoredDataChangeQueue, Is.Null);
            storeMock.Verify(s => s.RestoreDataChangeMonitoredItemQueueAsync(2, It.IsAny<CancellationToken>()), Times.Once);
            storeMock.Verify(s => s.RestoreDataChangeMonitoredItemQueueAsync(3, It.IsAny<CancellationToken>()), Times.Once);
            storeMock.Verify(s => s.RestoreDataChangeMonitoredItemQueueAsync(4, It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(
                loggerProvider.Messages,
                Has.Some.Contains("Failed to pre-hydrate queue for monitored item with id 3"));
        }

        /// <summary>
        /// Verifies that the commit of a restored queue is serialized with the values the item queues, so the
        /// storage ends with the newest value.
        /// </summary>
        [Test]
        public async Task CommitOfARestoredQueueIsSerializedWithQueuedValuesAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> server = CreateServerMock(
                telemetry,
                queueFactory,
                Mock.Of<ISubscriptionStore>());
            StoredMonitoredItem stored = CreateStoredItem();
            var queue = new GatedRestorableQueue(stored.Id, telemetry);
            queue.ResetQueue(10, false);
            queue.Enqueue(new DataValue(Variant.From(1)), ServiceResult.Good);
            stored.RestoredDataChangeQueue = queue;
            MonitoredItem item;
            using (RestoredQueueStaging.Begin())
            {
                item = new MonitoredItem(
                    server.Object,
                    Mock.Of<IAsyncNodeManager>(),
                    new object(),
                    stored);
            }
            using MonitoredItem restoredItem = item;
            item.QueueValue(new DataValue(Variant.From(2)), ServiceResult.Good);
            Assert.That(queue.Stored, Is.EqualTo(s_storedValue), "the staged queue keeps its stored contents");

            Task commit = Task.Run(item.CommitQueueRestore);
            Task enqueue = Task.CompletedTask;
            Task first;
            try
            {
                Assert.That(queue.CommitStarted.Wait(TimeSpan.FromSeconds(30)), Is.True);
                enqueue = Task.Run(() => item.QueueValue(new DataValue(Variant.From(3)), ServiceResult.Good));
                first = await Task.WhenAny(enqueue, Task.Delay(500)).ConfigureAwait(false);
            }
            finally
            {
                // Release the gate and let both workers finish before the item, whose lock they hold, is disposed.
                queue.ReleaseCommit();
                await Task.WhenAll(commit, enqueue).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }

            Assert.That(first, Is.Not.SameAs(enqueue), "the value is queued once the commit completed");
            int[] persisted = queue.Stored;
            Assert.That(persisted, Is.EqualTo(queue.Values), "the storage holds the contents of the queue");
            Assert.That(persisted[persisted.Length - 1], Is.EqualTo(3), "the storage ends with the newest value");
        }

        /// <summary>
        /// Verifies that a monitored item restored without the subscription manager does not stage its queue:
        /// the queue writes every change to its storage at once, as before staging existed.
        /// </summary>
        [Test]
        public void RestoreWithoutTheSubscriptionManagerDoesNotStageTheQueue()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var queueFactory = new MonitoredItemQueueFactory(telemetry);
            Mock<IServerInternal> server = CreateServerMock(
                telemetry,
                queueFactory,
                Mock.Of<ISubscriptionStore>());
            StoredMonitoredItem stored = CreateStoredItem();
            var queue = new GatedRestorableQueue(stored.Id, telemetry);
            queue.ResetQueue(10, false);
            queue.Enqueue(new DataValue(Variant.From(1)), ServiceResult.Good);
            stored.RestoredDataChangeQueue = queue;
            using var item = new MonitoredItem(
                server.Object,
                Mock.Of<IAsyncNodeManager>(),
                new object(),
                stored);

            item.QueueValue(new DataValue(Variant.From(2)), ServiceResult.Good);

            Assert.That(queue.BeginRestoreCalled, Is.False);
            Assert.That(queue.Stored, Is.EqualTo(s_storedValues), "the change is written at once");
        }

        private static StoredMonitoredItem StoreItemWithPendingHistoryFailure(
            IServerInternal server,
            uint queueSize,
            in DataValue failure)
        {
            StoredMonitoredItem initialState = CreateStoredItem(queueSize: queueSize);
            initialState.SamplingInterval = 0;
            using var original = new MonitoredItem(
                server,
                Mock.Of<IAsyncNodeManager>(),
                new object(),
                initialState);
            ((IInitialValueMonitoredItem)original).QueueInitialValue(
                failure,
                new ServiceResult(failure.StatusCode),
                ignoreFilters: true);

            return (StoredMonitoredItem)original.ToStorableMonitoredItem();
        }

        private static StoredMonitoredItem CreateStoredItem(uint id = 2, uint queueSize = 10)
        {
            return new StoredMonitoredItem
            {
                Id = id,
                SubscriptionId = 1,
                TypeMask = MonitoredItemTypeMask.DataChange,
                MonitoringMode = MonitoringMode.Reporting,
                NodeId = new NodeId(1),
                AttributeId = Attributes.Value,
                QueueSize = queueSize,
                DiscardOldest = true,
                SamplingInterval = 1000,
                TimestampsToReturn = TimestampsToReturn.Both,
                DiagnosticsMasks = DiagnosticsMasks.All,
                FilterToUse = new MonitoringFilter(),
                IndexRange = string.Empty,
                ParsedIndexRange = NumericRange.Null,
                LastValue = new DataValue(new Variant(0))
            };
        }

        private static Mock<IServerInternal> CreateServerMock(
            ITelemetryContext telemetry,
            IMonitoredItemQueueFactory queueFactory,
            ISubscriptionStore subscriptionStore)
        {
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.TypeTree).Returns(new TypeTable(new NamespaceTable()));
            serverMock.Setup(s => s.MonitoredItemQueueFactory).Returns(queueFactory);
            serverMock.Setup(s => s.SubscriptionStore).Returns(subscriptionStore);
            return serverMock;
        }

        private static async Task<MasterNodeManagerFixture> CreateMasterNodeManagerFixtureAsync(ILoggerFactory loggerFactory)
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
            {
                SecurityNone = true
            };
            await fixture.StartAsync().ConfigureAwait(false);

            var telemetry = new TestTelemetryContext(loggerFactory);
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(fixture.Server.CurrentInstance.NamespaceUris);
            serverMock.Setup(s => s.MainNodeManagerFactory).Returns(fixture.Server.CurrentInstance.MainNodeManagerFactory);
            serverMock.Setup(s => s.SubscriptionStore).Returns((ISubscriptionStore)null!);

            var manager = new MasterNodeManager(serverMock.Object, fixture.Config, null, Array.Empty<INodeManager>());
            return new MasterNodeManagerFixture(fixture, serverMock, manager, telemetry);
        }

        private static ValueTask InvokePreHydrateAsync(
            MasterNodeManager manager,
            IList<IStoredMonitoredItem> items)
        {
            FieldInfo field = typeof(MasterNodeManager).GetField(
                "m_serviceDispatch", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.That(field, Is.Not.Null);
            var dispatcher = (NodeManagerServiceDispatcher)field!.GetValue(manager)!;
            return dispatcher.PreHydrateMonitoredItemQueuesAsync(items, CancellationToken.None);
        }

        private sealed class MasterNodeManagerFixture : IAsyncDisposable
        {
            public MasterNodeManagerFixture(
                ServerFixture<StandardServer> fixture,
                Mock<IServerInternal> server,
                MasterNodeManager manager,
                TestTelemetryContext telemetry)
            {
                Fixture = fixture;
                Server = server;
                Manager = manager;
                Telemetry = telemetry;
            }

            public ServerFixture<StandardServer> Fixture { get; }

            public Mock<IServerInternal> Server { get; }

            public MasterNodeManager Manager { get; }

            public TestTelemetryContext Telemetry { get; }

            public async ValueTask DisposeAsync()
            {
                Manager.Dispose();
                Telemetry.Dispose();
                await Fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private sealed class TestTelemetryContext : ITelemetryContext, IDisposable
        {
            public TestTelemetryContext(ILoggerFactory loggerFactory)
            {
                LoggerFactory = loggerFactory;
                ActivitySource = new ActivitySource(nameof(MonitoredItemQueueRestoreTests));
                m_meter = new Meter(nameof(MonitoredItemQueueRestoreTests));
            }

            public Meter CreateMeter()
            {
                return m_meter;
            }

            public Meter CreateMeter(Assembly assembly)
            {
                return m_meter;
            }

            public ILoggerFactory LoggerFactory { get; }

            public ActivitySource ActivitySource { get; }

            public ActivitySource GetActivitySource(Assembly assembly)
            {
                return ActivitySource;
            }

            private readonly Meter m_meter;

            public void Dispose()
            {
                ActivitySource.Dispose();
                m_meter.Dispose();
            }
        }

        private sealed class CapturingLoggerProvider : ILoggerProvider
        {
            public ConcurrentBag<string> Messages { get; } = [];

            public ILogger CreateLogger(string categoryName)
            {
                return new CapturingLogger(Messages);
            }

            public void Dispose()
            {
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            public CapturingLogger(ConcurrentBag<string> messages)
            {
                m_messages = messages;
            }

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NoopDisposable.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception, string> formatter)
            {
                m_messages.Add(formatter(state, exception!));
            }

            private readonly ConcurrentBag<string> m_messages;
        }

        /// <summary>
        /// A restorable queue that writes its values to a simulated storage, and whose commit waits until the
        /// test releases it after it captured the values to write.
        /// </summary>
        private sealed class GatedRestorableQueue : DataChangeMonitoredItemQueue, IRestorableMonitoredItemQueue
        {
            public GatedRestorableQueue(uint monitoredItemId, ITelemetryContext telemetry)
                : base(false, monitoredItemId, telemetry)
            {
            }

            public ManualResetEventSlim CommitStarted { get; } = new();

            public int[] Values
            {
                get
                {
                    lock (m_contents)
                    {
                        return [.. m_contents];
                    }
                }
            }

            public int[] Stored { get; private set; } = [];

            public override void Enqueue(DataValue value, ServiceResult error)
            {
                base.Enqueue(value, error);
                Change(values => values.Add(ValueOf(value)));
            }

            public override void OverwriteLastValue(DataValue value, ServiceResult error)
            {
                base.OverwriteLastValue(value, error);
                Change(values => values[values.Count - 1] = ValueOf(value));
            }

            public override bool Dequeue(out DataValue value, out ServiceResult error)
            {
                bool dequeued = base.Dequeue(out value, out error);
                if (dequeued)
                {
                    Change(values => values.RemoveAt(0));
                }
                return dequeued;
            }

            public bool BeginRestoreCalled { get; private set; }

            public void BeginRestore()
            {
                BeginRestoreCalled = true;
                m_staged = true;
            }

            public void CommitRestore()
            {
                m_staged = false;
                int[] values = Values;
                CommitStarted.Set();
                m_release.Wait();
                Stored = values;
            }

            public void AbortRestore()
            {
                m_staged = true;
            }

            public void ReleaseCommit()
            {
                m_release.Set();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    CommitStarted.Dispose();
                    m_release.Dispose();
                }
                base.Dispose(disposing);
            }

            private void Change(Action<List<int>> change)
            {
                lock (m_contents)
                {
                    change(m_contents);
                    if (!m_staged)
                    {
                        Stored = [.. m_contents];
                    }
                }
            }

            private static int ValueOf(DataValue value)
            {
                return value.WrappedValue.TryGetValue(out int result) ? result : 0;
            }

            private readonly List<int> m_contents = [];
            private readonly ManualResetEventSlim m_release = new();
            private volatile bool m_staged;
        }

        private static readonly int[] s_storedValue = [1];
        private static readonly int[] s_storedValues = [1, 2];

        private sealed class NoopDisposable : IDisposable
        {
            public static NoopDisposable Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
