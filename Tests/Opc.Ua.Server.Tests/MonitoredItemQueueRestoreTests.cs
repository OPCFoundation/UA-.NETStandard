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

        [Test]
        public void RestoreFallsBackToSyncRestoreWhenNotPreHydrated()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var storeMock = new Mock<ISubscriptionStore>();
            storeMock
                .Setup(s => s.RestoreDataChangeMonitoredItemQueue(It.IsAny<uint>()))
                .Returns((IDataChangeMonitoredItemQueue)null);
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

        [Test]
        public async Task PreHydrateContinuesAfterStoreFailureAndHonorsQueueSizeGateAsync()
        {
            using var loggerProvider = new CapturingLoggerProvider();
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
            await using var fixture = await CreateMasterNodeManagerFixtureAsync(loggerFactory).ConfigureAwait(false);
            using var queueFactory = new MonitoredItemQueueFactory(fixture.Telemetry);
            var storeMock = new Mock<ISubscriptionStore>();
            IDataChangeMonitoredItemQueue restoredQueue = queueFactory.CreateDataChangeQueue(false, 2);
            restoredQueue.ResetQueue(10, false);
            restoredQueue.Enqueue(new DataValue(new Variant(7)), ServiceResult.Good);
            storeMock.Setup(s => s.RestoreDataChangeMonitoredItemQueueAsync(2, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IDataChangeMonitoredItemQueue>(restoredQueue));
            storeMock.Setup(s => s.RestoreDataChangeMonitoredItemQueueAsync(3, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<IDataChangeMonitoredItemQueue>(
                    Task.FromException<IDataChangeMonitoredItemQueue>(
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

        private static async Task InvokePreHydrateAsync(
            MasterNodeManager manager,
            IList<IStoredMonitoredItem> items)
        {
            MethodInfo method = typeof(MasterNodeManager).GetMethod(
                "PreHydrateMonitoredItemQueuesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var task = (ValueTask)method.Invoke(manager, [items, CancellationToken.None])!;
            await task.ConfigureAwait(false);
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

            public ILoggerFactory LoggerFactory { get; }

            public ActivitySource ActivitySource { get; }

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
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                m_messages.Add(formatter(state, exception));
            }

            private readonly ConcurrentBag<string> m_messages;
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static NoopDisposable Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
