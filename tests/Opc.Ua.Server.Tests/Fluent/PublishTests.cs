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

// CA2000: per-test fixture lifecycle is managed by NUnit; managers and other
// disposables are explicitly disposed in TearDown or by the using-block.
#pragma warning disable CA2000
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Tests.Fluent
{
    [TestFixture]
    [Category("Fluent")]
    [Parallelizable(ParallelScope.None)]
    public class PublishTests
    {
        private const ushort kNs = 2;
        private const string kNamespaceUri = "http://test.org/UA/Publish/";

        /// <summary>
        /// Generous timeout: reconcile/worker tasks run on the thread pool which
        /// can be starved when the broader test suite (e.g. AsyncCustomNodeManager
        /// tests with [Parallelizable(ParallelScope.All)]) saturates CPU. 15s
        /// keeps green runs under 1s while eliminating false negatives under load.
        /// </summary>
        private static readonly TimeSpan s_signalTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan s_negativeWindow = TimeSpan.FromMilliseconds(250);

        private Mock<IServerInternal> m_mockServer;
        private ApplicationConfiguration m_configuration;
        private Mock<IMasterNodeManager> m_mockMasterNodeManager;
        private NamespaceTable m_namespaceTable;
        private MonitoredItemQueueFactory m_queueFactory;
        private TimeProvider m_timeProvider;

        private sealed class FixedEventIdProvider : IEventIdProvider
        {
            public FixedEventIdProvider(ByteString eventId)
            {
                m_eventId = eventId;
            }

            public ByteString CreateEventId(BaseObjectState notifier, ISystemContext context, BaseEventState eventState)
            {
                return m_eventId;
            }

            private readonly ByteString m_eventId;
        }

        [SetUp]
        public void SetUp()
        {
            m_mockServer = new Mock<IServerInternal>();
            m_timeProvider = TimeProvider.System;
            m_mockServer.As<ITimeProviderProvider>().SetupGet(server => server.TimeProvider)
                .Returns(() => m_timeProvider);
            m_mockMasterNodeManager = new Mock<IMasterNodeManager>();
            var mockConfigurationNodeManager = new Mock<IConfigurationNodeManager>();

            m_namespaceTable = new NamespaceTable();
            m_namespaceTable.Append(kNamespaceUri);

            m_mockServer.Setup(s => s.NamespaceUris).Returns(m_namespaceTable);
            m_mockServer.Setup(s => s.ServerUris).Returns(new StringTable());
            m_mockServer.Setup(s => s.TypeTree).Returns(new TypeTable(m_namespaceTable));
            m_mockServer.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            m_mockServer.Setup(s => s.NodeManager).Returns(m_mockMasterNodeManager.Object);
            m_mockMasterNodeManager
                .Setup(m => m.ConfigurationNodeManager)
                .Returns(mockConfigurationNodeManager.Object);

            var mockTelemetry = new Mock<ITelemetryContext>();
            m_mockServer.Setup(s => s.Telemetry).Returns(mockTelemetry.Object);

            m_queueFactory = new MonitoredItemQueueFactory(mockTelemetry.Object);
            m_mockServer.Setup(s => s.MonitoredItemQueueFactory).Returns(m_queueFactory);

            var defaultContext = new ServerSystemContext(m_mockServer.Object);
            m_mockServer.Setup(s => s.DefaultSystemContext).Returns(defaultContext);

            m_configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };
        }

        [TearDown]
        public void TearDown()
        {
            m_queueFactory?.Dispose();
        }

        [Test]
        public async Task Publish_LazyDefault_DoesNotInvokeFactoryUntilEventsAreMonitoredAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "LazyNotifier");

            var factoryStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var stopFactory = new CancellationTokenSource();

            manager.EventSources.Register(
                notifier,
                (_, _, ct) => CountingStream(factoryStarted, ct),
                options: null);

            // Lazy mode: factory MUST NOT be invoked while no monitor is attached.
            await Task.Delay(s_negativeWindow).ConfigureAwait(false);
            Assert.That(factoryStarted.Task.IsCompleted, Is.False,
                "Factory must not run before AreEventsMonitored flips on.");

            // Flip the flag and signal — registry should activate within s_signalTimeout.
            notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
            manager.EventSources.SignalReconcile();

            await WaitForAsync(factoryStarted.Task).ConfigureAwait(false);
            stopFactory.Cancel();
        }

        [Test]
        public async Task Publish_AlwaysOn_StartsFactoryWithoutSubscribersAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "EagerNotifier");

            var factoryStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            manager.EventSources.Register(
                notifier,
                (_, _, ct) => CountingStream(factoryStarted, ct),
                new EventPublishOptions { AlwaysOn = true });

            await WaitForAsync(factoryStarted.Task).ConfigureAwait(false);
            Assert.That(notifier.AreEventsMonitored, Is.False,
                "AlwaysOn must not require AreEventsMonitored to be true.");
        }

        [Test]
        public async Task Publish_LazyMonitorThenUnmonitor_DeactivatesFactoryAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "Toggling");

            var iteratorEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var iteratorObservedCancel = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            manager.EventSources.Register(
                notifier,
                (_, _, ct) => CancelObservingStream(iteratorEntered, iteratorObservedCancel, ct),
                options: null);

            notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
            manager.EventSources.SignalReconcile();

            // Wait until the worker is actually inside the iterator before
            // unmonitoring — otherwise the unsubscribe could race ahead and the
            // cancel would be observed during enumerator setup, not the await.
            await WaitForAsync(iteratorEntered.Task).ConfigureAwait(false);

            notifier.SetAreEventsMonitored(manager.SystemContext, false, false);
            manager.EventSources.SignalReconcile();

            await WaitForAsync(iteratorObservedCancel.Task).ConfigureAwait(false);
        }

        [Test]
        public async Task Publish_ActivatedSource_DeliversEventsThroughOnReportEventAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "Delivering");

            var observed = new ConcurrentQueue<BaseEventState>();
            var observedCount = new AsyncCountdown(target: 3);
            notifier.OnReportEvent = (_, _, e) =>
            {
                if (e is BaseEventState evt)
                {
                    observed.Enqueue(evt);
                    observedCount.SignalOne();
                }
            };

            var channel = Channel.CreateUnbounded<BaseEventState>();
            manager.EventSources.Register(
                notifier,
                (_, _, ct) => channel.Reader.ReadAllAsync(ct),
                new EventPublishOptions { AlwaysOn = true });

            for (int i = 0; i < 3; i++)
            {
                await channel.Writer.WriteAsync(new BaseEventState(parent: null)).ConfigureAwait(false);
            }

            await WaitForAsync(observedCount.WaitAsync()).ConfigureAwait(false);
            Assert.That(observed, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task Publish_DispatchedEvent_PopulatesDefaultsAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "Defaults");

            var captured = new TaskCompletionSource<BaseEventState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            notifier.OnReportEvent = (_, _, e) =>
            {
                if (e is BaseEventState evt)
                {
                    captured.TrySetResult(evt);
                }
            };

            var channel = Channel.CreateUnbounded<BaseEventState>();
            manager.EventSources.Register(
                notifier,
                (_, _, ct) => channel.Reader.ReadAllAsync(ct),
                new EventPublishOptions { AlwaysOn = true });

            await channel.Writer.WriteAsync(new BaseEventState(parent: null)).ConfigureAwait(false);

            BaseEventState seen = await WaitForAsync(captured.Task).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(seen.EventId, Is.Not.Null);
                Assert.That(seen.EventId.Value.IsNull, Is.False);
                Assert.That(seen.EventType, Is.Not.Null);
                Assert.That(seen.EventType.Value.IsNull, Is.False);
                Assert.That(seen.SourceNode, Is.Not.Null);
                Assert.That(seen.SourceNode.Value, Is.EqualTo(notifier.NodeId));
                Assert.That(seen.SourceName, Is.Not.Null);
                Assert.That(seen.SourceName.Value, Is.EqualTo(notifier.BrowseName.Name));
                Assert.That(seen.Time, Is.Not.Null);
                Assert.That(seen.Time.Value.IsNull, Is.False);
                Assert.That(seen.ReceiveTime, Is.Not.Null);
                Assert.That(seen.Severity, Is.Not.Null);
                Assert.That(seen.Severity.Value, Is.EqualTo((ushort)EventSeverity.Medium));
                Assert.That(seen.Message, Is.Not.Null);
            });
        }

        [Test]
        public async Task Publish_DispatchedEvent_UsesConfiguredEventIdProviderAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "CustomEventId");

            var captured = new TaskCompletionSource<BaseEventState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            notifier.OnReportEvent = (_, _, e) =>
            {
                if (e is BaseEventState evt)
                {
                    captured.TrySetResult(evt);
                }
            };

            var customEventId = ByteString.From(new byte[] { 4, 3, 2, 1 });
            var channel = Channel.CreateUnbounded<BaseEventState>();
            manager.EventSources.Register(
                notifier,
                (_, _, ct) => channel.Reader.ReadAllAsync(ct),
                new EventPublishOptions
                {
                    AlwaysOn = true,
                    EventIdProvider = new FixedEventIdProvider(customEventId)
                });

            await channel.Writer.WriteAsync(new BaseEventState(parent: null)).ConfigureAwait(false);

            BaseEventState seen = await WaitForAsync(captured.Task).ConfigureAwait(false);
            Assert.That(seen.EventId, Is.Not.Null);
            Assert.That(seen.EventId!.Value, Is.EqualTo(customEventId));
        }

        [Test]
        public async Task Publish_DispatchedEvent_PreservesUserPopulatedFieldsAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "Custom");

            var captured = new TaskCompletionSource<BaseEventState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            notifier.OnReportEvent = (_, _, e) =>
            {
                if (e is BaseEventState evt)
                {
                    captured.TrySetResult(evt);
                }
            };

            var customEventId = Uuid.NewUuid().ToByteString();
            var customSource = new NodeId("OtherSource", kNs);
            const string kCustomSourceName = "AlternateName";
            const ushort kCustomSeverity = 800;

            var authored = new BaseEventState(parent: null);
            authored.EventId = PropertyState<ByteString>.With<VariantBuilder>(authored, customEventId);
            authored.SourceNode = PropertyState<NodeId>.With<VariantBuilder>(authored, customSource);
            authored.SourceName = PropertyState<string>.With<VariantBuilder>(authored, kCustomSourceName);
            authored.Severity = PropertyState<ushort>.With<VariantBuilder>(authored, kCustomSeverity);

            var channel = Channel.CreateUnbounded<BaseEventState>();
            manager.EventSources.Register(
                notifier,
                (_, _, ct) => channel.Reader.ReadAllAsync(ct),
                new EventPublishOptions { AlwaysOn = true });

            await channel.Writer.WriteAsync(authored).ConfigureAwait(false);

            BaseEventState seen = await WaitForAsync(captured.Task).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(seen.EventId.Value, Is.EqualTo(customEventId));
                Assert.That(seen.SourceNode.Value, Is.EqualTo(customSource));
                Assert.That(seen.SourceName.Value, Is.EqualTo(kCustomSourceName));
                Assert.That(seen.Severity.Value, Is.EqualTo(kCustomSeverity));
            });
        }

        [Test]
        public async Task Publish_DispatchedEventWithEventTypeSetsMissingTypeDefinitionAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "EventType");

            var captured = new TaskCompletionSource<BaseEventState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            notifier.OnReportEvent = (_, _, e) =>
            {
                if (e is BaseEventState evt)
                {
                    captured.TrySetResult(evt);
                }
            };

            var customEventType = new NodeId("CustomEventType", kNs);
            var authored = new BaseEventState(parent: null)
            {
                EventType = PropertyState<NodeId>.With<VariantBuilder>(null, customEventType)
            };

            var channel = Channel.CreateUnbounded<BaseEventState>();
            manager.EventSources.Register(
                notifier,
                (_, _, ct) => channel.Reader.ReadAllAsync(ct),
                new EventPublishOptions { AlwaysOn = true });

            await channel.Writer.WriteAsync(authored).ConfigureAwait(false);

            BaseEventState seen = await WaitForAsync(captured.Task).ConfigureAwait(false);
            Assert.That(seen.TypeDefinitionId, Is.EqualTo(customEventType));
        }

        [Test]
        public async Task Publish_SkipDefaultPopulation_LeavesFieldsUntouchedAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "SkipDefaults");

            var captured = new TaskCompletionSource<BaseEventState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            notifier.OnReportEvent = (_, _, e) =>
            {
                if (e is BaseEventState evt)
                {
                    captured.TrySetResult(evt);
                }
            };

            var channel = Channel.CreateUnbounded<BaseEventState>();
            manager.EventSources.Register(
                notifier,
                (_, _, ct) => channel.Reader.ReadAllAsync(ct),
                new EventPublishOptions { AlwaysOn = true, SkipDefaultPopulation = true });

            await channel.Writer.WriteAsync(new BaseEventState(parent: null)).ConfigureAwait(false);

            BaseEventState seen = await WaitForAsync(captured.Task).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(seen.EventId, Is.Null);
                Assert.That(seen.EventType, Is.Null);
                Assert.That(seen.SourceNode, Is.Null);
                Assert.That(seen.SourceName, Is.Null);
                Assert.That(seen.Time, Is.Null);
                Assert.That(seen.ReceiveTime, Is.Null);
                Assert.That(seen.Severity, Is.Null);
                Assert.That(seen.Message, Is.Null);
            });
        }

        [Test]
        public async Task Publish_FactoryThrows_InvokesOnErrorAndStopsSourceAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "FactoryThrows");

            var thrown = new InvalidOperationException("factory boom");
            var captured = new TaskCompletionSource<Exception>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            manager.EventSources.Register(
                notifier,
                (_, _, _) => throw thrown,
                new EventPublishOptions
                {
                    AlwaysOn = true,
                    OnError = ex => captured.TrySetResult(ex)
                });

            Exception observed = await WaitForAsync(captured.Task).ConfigureAwait(false);
            Assert.That(observed, Is.SameAs(thrown));
        }

        [Test]
        public async Task Publish_IteratorThrows_InvokesOnErrorAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "IteratorThrows");

            var thrown = new InvalidOperationException("iterator boom");
            var captured = new TaskCompletionSource<Exception>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            manager.EventSources.Register(
                notifier,
                (_, _, ct) => ThrowingStream(thrown, ct),
                new EventPublishOptions
                {
                    AlwaysOn = true,
                    OnError = ex => captured.TrySetResult(ex)
                });

            Exception observed = await WaitForAsync(captured.Task).ConfigureAwait(false);
            Assert.That(observed, Is.SameAs(thrown));
        }

        [Test]
        public async Task Publish_FactoryReturnsNull_DoesNotInvokeOnReportEventAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "NullStream");
            var factoryStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            bool reported = false;
            notifier.OnReportEvent = (_, _, _) => reported = true;

            manager.EventSources.Register(
                notifier,
                (_, _, _) =>
                {
                    factoryStarted.TrySetResult(true);
                    return null;
                },
                new EventPublishOptions { AlwaysOn = true });

            await WaitForAsync(factoryStarted.Task).ConfigureAwait(false);
            await Task.Delay(s_negativeWindow).ConfigureAwait(false);

            Assert.That(reported, Is.False);
        }

        [Test]
        public async Task Publish_ReportEventThrows_InvokesOnErrorAndContinuesIteratorAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "ReportThrows");

            var thrown = new InvalidOperationException("report boom");
            var captured = new TaskCompletionSource<Exception>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            notifier.OnReportEvent = (_, _, _) => throw thrown;

            var channel = Channel.CreateUnbounded<BaseEventState>();
            manager.EventSources.Register(
                notifier,
                (_, _, ct) => channel.Reader.ReadAllAsync(ct),
                new EventPublishOptions
                {
                    AlwaysOn = true,
                    OnError = ex => captured.TrySetResult(ex)
                });

            await channel.Writer.WriteAsync(new BaseEventState(parent: null)).ConfigureAwait(false);

            Exception observed = await WaitForAsync(captured.Task).ConfigureAwait(false);
            Assert.That(observed, Is.SameAs(thrown));
        }

        [Test]
        public void Publish_DuplicateRegistration_ThrowsBadConfigurationError()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "Duplicate");

            manager.EventSources.Register(
                notifier,
                (_, _, ct) => EmptyStream(ct),
                options: null);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                manager.EventSources.Register(
                    notifier,
                    (_, _, ct) => EmptyStream(ct),
                    options: null));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void Publish_RegisterAfterEventSourcesDisposed_ThrowsObjectDisposedException()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "DisposedRegistry");

            manager.EventSources.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                manager.EventSources.Register(
                    notifier,
                    (_, _, ct) => EmptyStream(ct),
                    options: null));
        }

        [Test]
        public void Publish_NegativeCancellationTimeout_ThrowsArgumentOutOfRange()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "BadTimeout");

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                manager.EventSources.Register(
                    notifier,
                    (_, _, ct) => EmptyStream(ct),
                    new EventPublishOptions { CancellationTimeout = TimeSpan.FromSeconds(-1) }));
        }

        [Test]
        public void Publish_InfiniteCancellationTimeout_IsAccepted()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "InfiniteTimeout");

            Assert.DoesNotThrow(() =>
                manager.EventSources.Register(
                    notifier,
                    (_, _, ct) => EmptyStream(ct),
                    new EventPublishOptions { CancellationTimeout = Timeout.InfiniteTimeSpan }));
        }

        [Test]
        public void Publish_NullNotifier_ThrowsArgumentNull()
        {
            using TestablePublishManager manager = CreateManager();

            Assert.Throws<ArgumentNullException>(() =>
                manager.EventSources.Register(
                    notifier: null,
                    (_, _, ct) => EmptyStream(ct),
                    options: null));
        }

        [Test]
        public void Publish_NullFactory_ThrowsArgumentNull()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "NullFactory");

            Assert.Throws<ArgumentNullException>(() =>
                manager.EventSources.Register(
                    notifier,
                    factory: null,
                    options: null));
        }

        [Test]
        public void Publish_AutoPromotesEventNotifierBit()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "AutoPromote", eventNotifier: EventNotifiers.None);
            Assert.That(notifier.EventNotifier, Is.EqualTo(EventNotifiers.None),
                "Sanity: notifier started without SubscribeToEvents.");

            manager.EventSources.Register(
                notifier,
                (_, _, ct) => EmptyStream(ct),
                options: null);

            Assert.That(
                notifier.EventNotifier & EventNotifiers.SubscribeToEvents,
                Is.EqualTo(EventNotifiers.SubscribeToEvents),
                "Publish must auto-promote SubscribeToEvents on the notifier.");
        }

        [Test]
        public async Task Publish_RegisterAsRootNotifier_AddsToRootNotifierSetOnSealAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "Root");

            manager.EventSources.Register(
                notifier,
                (_, _, ct) => EmptyStream(ct),
                new EventPublishOptions { RegisterAsRootNotifier = true });

            // Registration runs inside the synchronous Configure pass, which
            // cannot await the manager's monitored-item semaphore, so the
            // root-notifier registration is staged rather than performed.
            Assert.That(
                manager.RootNotifiers,
                Does.Not.ContainKey(notifier.NodeId),
                "Root-notifier registration must be deferred to the seal.");

            await manager.EventSources.CompleteRegistrationsAsync()
                .ConfigureAwait(false);

            Assert.That(manager.RootNotifiers, Contains.Key(notifier.NodeId));
        }

        [Test]
        public async Task Publish_RegisterAsRootNotifier_CancelledDrainStaysStagedAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "RootCancelled");

            manager.EventSources.Register(
                notifier,
                (_, _, ct) => EmptyStream(ct),
                new EventPublishOptions { RegisterAsRootNotifier = true });

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Cancelling the seal is not a configuration error, so it must not
            // be wrapped as one.
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await manager.EventSources
                    .CompleteRegistrationsAsync(cts.Token).ConfigureAwait(false));
            Assert.That(manager.RootNotifiers, Does.Not.ContainKey(notifier.NodeId));

            // The registration stayed staged, so a later seal still completes it.
            await manager.EventSources.CompleteRegistrationsAsync()
                .ConfigureAwait(false);

            Assert.That(manager.RootNotifiers, Contains.Key(notifier.NodeId));
        }

        [Test]
        public async Task Publish_RegisterAsRootNotifier_IsDrainedOnlyOnceAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "RootOnce");

            manager.EventSources.Register(
                notifier,
                (_, _, ct) => EmptyStream(ct),
                new EventPublishOptions { RegisterAsRootNotifier = true });

            await manager.EventSources.CompleteRegistrationsAsync()
                .ConfigureAwait(false);
            await manager.EventSources.CompleteRegistrationsAsync()
                .ConfigureAwait(false);

            Assert.That(manager.RootNotifiers, Contains.Key(notifier.NodeId));
            Assert.That(manager.RootNotifiers, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task Dispose_CancelsActiveIteratorAsync()
        {
            TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "DisposeCancel");

            var iteratorEntered = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var iteratorObservedCancel = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            manager.EventSources.Register(
                notifier,
                (_, _, ct) => CancelObservingStream(iteratorEntered, iteratorObservedCancel, ct),
                new EventPublishOptions { AlwaysOn = true });

            // Wait until the worker is actually inside the iterator before
            // disposing the manager so we observe cancel propagation, not setup.
            await WaitForAsync(iteratorEntered.Task).ConfigureAwait(false);

            manager.Dispose();

            await WaitForAsync(iteratorObservedCancel.Task).ConfigureAwait(false);
        }

        [Test]
        public void Publish_OnNonFluentManager_ThrowsBadConfigurationErrorWithManagerType()
        {
            using TestablePublishManager fluent = CreateManager();
            BaseObjectState notifier = MakeNotifier(fluent, "WrongBase");

            // Build a NodeManagerBuilder backed by a non-fluent (Mock) manager
            // and feed in only the resolver for `notifier`. Publish must reject
            // it because the registry was never attached.
            var roots = new Dictionary<QualifiedName, NodeState> { [notifier.BrowseName] = notifier };
            var byId = new Dictionary<NodeId, NodeState> { [notifier.NodeId] = notifier };

            var nonFluentManager = new Mock<IAsyncNodeManager>();

            var nonFluentBuilder = new NodeManagerBuilder(
                fluent.SystemContext,
                nodeManager: nonFluentManager.Object,
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState n) ? n : null,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState n) ? n : null,
                typeIdResolver: _ => []);

            INodeBuilder<BaseObjectState> nodeBuilder = nonFluentBuilder.Node<BaseObjectState>(notifier.BrowseName.Name);
            ServiceResultException ex = Assert.Throws<ServiceResultException>(() =>
                nodeBuilder.Publish(
                    (_, _, ct) => EmptyStream(ct)));

            Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(ex.Message, Does.Contain("FluentNodeManagerBase"),
                "Error message must reference the required base class.");
        }

        [Test]
        public async Task Publish_FactoryOverloadOnAttachedBuilder_RegistersAndDeliversAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "ExtFactory");

            var roots = new Dictionary<QualifiedName, NodeState> { [notifier.BrowseName] = notifier };
            var byId = new Dictionary<NodeId, NodeState> { [notifier.NodeId] = notifier };

            var builder = new NodeManagerBuilder(
                manager.SystemContext,
                nodeManager: FluentTestNodeManager.Create(kNs),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState n) ? n : null,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState n) ? n : null,
                typeIdResolver: _ => []);
            manager.AttachToBuilder(builder);

            var captured = new TaskCompletionSource<BaseEventState>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            notifier.OnReportEvent = (_, _, e) =>
            {
                if (e is BaseEventState evt)
                {
                    captured.TrySetResult(evt);
                }
            };

            var channel = Channel.CreateUnbounded<BaseEventState>();
            builder.Node<BaseObjectState>(notifier.BrowseName.Name)
                .Publish(
                    (_, _, ct) => channel.Reader.ReadAllAsync(ct),
                    new EventPublishOptions { AlwaysOn = true });

            await channel.Writer.WriteAsync(new BaseEventState(parent: null)).ConfigureAwait(false);

            BaseEventState seen = await WaitForAsync(captured.Task).ConfigureAwait(false);
            Assert.That(seen, Is.Not.Null);
        }

        [Test]
        public void Publish_NullArgumentsOnExtension_Throw()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "NullArgExt");

            var roots = new Dictionary<QualifiedName, NodeState> { [notifier.BrowseName] = notifier };
            var byId = new Dictionary<NodeId, NodeState> { [notifier.NodeId] = notifier };

            var builder = new NodeManagerBuilder(
                manager.SystemContext,
                nodeManager: FluentTestNodeManager.Create(kNs),
                defaultNamespaceIndex: kNs,
                rootResolver: q => roots.TryGetValue(q, out NodeState n) ? n : null,
                nodeIdResolver: id => byId.TryGetValue(id, out NodeState n) ? n : null,
                typeIdResolver: _ => []);
            manager.AttachToBuilder(builder);

            INodeBuilder<BaseObjectState> nodeBuilder = builder.Node<BaseObjectState>(notifier.BrowseName.Name);

            Assert.Throws<ArgumentNullException>(() =>
                EventNotifierBuilderExtensions.Publish<BaseObjectState, BaseEventState>(
                    nodeBuilder: null,
                    factory: (_, _, ct) => EmptyStream(ct)));

            Assert.Throws<ArgumentNullException>(() =>
                nodeBuilder.Publish<BaseObjectState, BaseEventState>(
                    factory: null));

            Assert.Throws<ArgumentNullException>(() =>
                EventNotifierBuilderExtensions.Publish<BaseObjectState, BaseEventState>(
                    nodeBuilder: null,
                    source: AsyncEnumerable.Empty<BaseEventState>()));

            Assert.Throws<ArgumentNullException>(() =>
                nodeBuilder.Publish(
                    source: (IAsyncEnumerable<BaseEventState>)null));
        }

        [Test]
        public void AttachToBuilder_NullBuilder_ThrowsArgumentNull()
        {
            using TestablePublishManager manager = CreateManager();
            Assert.Throws<ArgumentNullException>(() => manager.AttachToBuilder(null));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SubscriptionWaitsForProducerReadinessWithoutWaitingForAnEvent(bool ancestor)
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = await MakeReadinessNotifierAsync(manager, "Producer").ConfigureAwait(false);
            BaseObjectState monitored = notifier;
            if (ancestor)
            {
                monitored = await MakeReadinessNotifierAsync(manager, "Area").ConfigureAwait(false);
                monitored.AddNotifier(manager.SystemContext, ReferenceTypeIds.HasNotifier, false, notifier);
                notifier.AddNotifier(manager.SystemContext, ReferenceTypeIds.HasNotifier, true, monitored);
            }
            var stream = new ControlledReadyStream();
            var observed = new TaskCompletionSource<BaseEventState>(TaskCreationOptions.RunContinuationsAsynchronously);
            notifier.OnReportEvent = (_, _, value) => observed.TrySetResult((BaseEventState)value);
            manager.EventSources.Register(notifier, (_, _, _) => stream, null);
            Assert.That(stream.Entered.Task.IsCompleted, Is.False);

            monitored.SetAreEventsMonitored(manager.SystemContext, true, true);
            Task subscribed = manager.EventSources.WaitUntilReadyAsync(monitored, CancellationToken.None).AsTask();
            await stream.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(subscribed.IsCompleted, Is.False);
            stream.Ready.TrySetResult(true);
            await subscribed.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(observed.Task.IsCompleted, Is.False, "Readiness is not the first-event boundary.");

            var occurrence = new BaseEventState(null);
            await stream.Events.Writer.WriteAsync(occurrence).ConfigureAwait(false);
            Assert.That(await observed.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false), Is.SameAs(occurrence));
        }

        [Test]
        public async Task SubscriptionReadinessReportsTheProducerStartupFailure()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = await MakeReadinessNotifierAsync(manager, "FailingProducer")
                .ConfigureAwait(false);
            var stream = new ControlledReadyStream();
            manager.EventSources.Register(notifier, (_, _, _) => stream, null);
            notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
            Task subscribed = manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask();
            await stream.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            var failure = new ServiceResultException(StatusCodes.BadServerNotConnected);
            stream.Ready.TrySetException(failure);

            await Assert.ThatAsync(
                () => subscribed.WaitAsync(s_signalTimeout),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
        }

        [Test]
        public async Task ReadinessDoesNotWaitForUnrelatedMonitoredSources()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState slow = await MakeReadinessNotifierAsync(manager, "Slow").ConfigureAwait(false);
            BaseObjectState fast = await MakeReadinessNotifierAsync(manager, "Fast").ConfigureAwait(false);
            var slowStream = new ControlledReadyStream();
            var fastStream = new ControlledReadyStream();
            manager.EventSources.Register(slow, (_, _, _) => slowStream, null);
            manager.EventSources.Register(fast, (_, _, _) => fastStream, null);
            slow.SetAreEventsMonitored(manager.SystemContext, true, false);
            fast.SetAreEventsMonitored(manager.SystemContext, true, false);
            Task slowSubscription = manager.EventSources.WaitUntilReadyAsync(slow, CancellationToken.None).AsTask();
            Task fastSubscription = manager.EventSources.WaitUntilReadyAsync(fast, CancellationToken.None).AsTask();
            await Task.WhenAll(slowStream.Entered.Task, fastStream.Entered.Task)
                .WaitAsync(s_signalTimeout).ConfigureAwait(false);

            fastStream.Ready.TrySetResult(true);
            await fastSubscription.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(slowSubscription.IsCompleted, Is.False);
            slowStream.Ready.TrySetResult(true);
            await slowSubscription.WaitAsync(s_signalTimeout).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SlowSourceShutdownDoesNotBlockAnotherSourcesReadinessAsync(bool infinite)
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState slow = await MakeReadinessNotifierAsync(manager, "SlowStop").ConfigureAwait(false);
            BaseObjectState fast = await MakeReadinessNotifierAsync(manager, "FastStart").ConfigureAwait(false);
            var slowStream = new ControlledReadyStream { BlockStop = true };
            var fastStream = new ControlledReadyStream();
            slowStream.Ready.TrySetResult(true);
            fastStream.Ready.TrySetResult(true);
            manager.EventSources.Register(slow, (_, _, _) => slowStream,
                new EventPublishOptions
                {
                    CancellationTimeout = infinite ? Timeout.InfiniteTimeSpan : TimeSpan.FromMinutes(1)
                });
            manager.EventSources.Register(fast, (_, _, _) => fastStream, null);
            slow.SetAreEventsMonitored(manager.SystemContext, true, false);
            await manager.EventSources.WaitUntilReadyAsync(slow, CancellationToken.None).AsTask()
                .WaitAsync(s_signalTimeout).ConfigureAwait(false);
            try
            {
                slow.SetAreEventsMonitored(manager.SystemContext, false, false);
                manager.EventSources.SignalReconcile();
                await slowStream.StopRequested.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
                fast.SetAreEventsMonitored(manager.SystemContext, true, false);

                await Task.WhenAll(
                    manager.EventSources.WaitUntilReadyAsync(fast, CancellationToken.None).AsTask(),
                    fastStream.Entered.Task)
                    .WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);

                Assert.That(fastStream.Entered.Task.IsCompleted, Is.True);
                Assert.That(slowStream.Stopped.Task.IsCompleted, Is.False);
            }
            finally
            {
                slowStream.ReleaseStop.TrySetResult(true);
                await slowStream.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task EachSourceReactivationHasItsOwnReadinessBoundary()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = await MakeReadinessNotifierAsync(manager, "Reactivated")
                .ConfigureAwait(false);
            var first = new ControlledReadyStream();
            var second = new ControlledReadyStream();
            int activations = 0;
            manager.EventSources.Register(
                notifier, (_, _, _) => Interlocked.Increment(ref activations) == 1 ? first : second, null);
            notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
            Task firstSubscription = manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask();
            await first.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            first.Ready.TrySetResult(true);
            await firstSubscription.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            notifier.SetAreEventsMonitored(manager.SystemContext, false, false);
            manager.EventSources.SignalReconcile();
            await first.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);

            notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
            Task secondSubscription = manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask();
            await second.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(secondSubscription.IsCompleted, Is.False);
            second.Ready.TrySetResult(true);
            await secondSubscription.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(activations, Is.EqualTo(2));
        }

        [Test]
        public async Task ReactivationReadinessIncludesSourceWhoseDrainCompletesDuringReconcileAsync()
        {
            var clock = new DeadlineObservingClock();
            m_timeProvider = clock;
            await using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = await MakeReadinessNotifierAsync(manager, "DrainBoundary")
                .ConfigureAwait(false);
            var first = new ControlledReadyStream { BlockStop = true };
            var second = new ControlledReadyStream();
            first.Ready.TrySetResult(true);
            int activations = 0;
            manager.EventSources.Register(notifier,
                (_, _, _) => Interlocked.Increment(ref activations) == 1 ? first : second,
                new EventPublishOptions { CancellationTimeout = Timeout.InfiniteTimeSpan });
            using var releaseReconcile = new ManualResetEventSlim();
            try
            {
                notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
                await manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                    .WaitAsync(s_signalTimeout).ConfigureAwait(false);
                notifier.SetAreEventsMonitored(manager.SystemContext, false, false);
                await manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                    .WaitAsync(s_signalTimeout).ConfigureAwait(false);
                await first.StopRequested.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
                Task draining = GetSourceDrain(manager.EventSources, notifier.NodeId);
                Assert.That(draining.IsCompleted, Is.False);

                var reconcileEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                clock.RunOnNextTimerChange(() =>
                {
                    reconcileEntered.TrySetResult(true);
                    if (!releaseReconcile.Wait(s_signalTimeout))
                    {
                        throw new TimeoutException("The controlled reconcile pass was not released.");
                    }
                });
                notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
                Task subscribed = manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask();
                await reconcileEntered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
                Assert.That(Volatile.Read(ref activations), Is.EqualTo(1));
                Assert.That(subscribed.IsCompleted, Is.False);
                first.ReleaseStop.TrySetResult(true);
                await draining.WaitAsync(s_signalTimeout).ConfigureAwait(false);
                releaseReconcile.Set();

                await second.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
                Assert.That(Volatile.Read(ref activations), Is.EqualTo(2));
                Assert.That(second.Ready.Task.IsCompleted, Is.False);
                Assert.That(subscribed.IsCompleted, Is.False);
                second.Ready.TrySetResult(true);
                await subscribed.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            }
            finally
            {
                releaseReconcile.Set();
                first.ReleaseStop.TrySetResult(true);
                second.Ready.TrySetResult(true);
                await manager.EventSources.ReleaseAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SourceReactivationWaitsForItsOwnDrainAndFreshReadinessAsync()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = await MakeReadinessNotifierAsync(manager, "Restarting").ConfigureAwait(false);
            BaseObjectState control = await MakeReadinessNotifierAsync(manager, "Control").ConfigureAwait(false);
            var first = new ControlledReadyStream { BlockStop = true };
            var second = new ControlledReadyStream();
            var independent = new ControlledReadyStream();
            first.Ready.TrySetResult(true);
            independent.Ready.TrySetResult(true);
            int starts = 0;
            manager.EventSources.Register(notifier,
                (_, _, _) => Interlocked.Increment(ref starts) == 1 ? first : second,
                new EventPublishOptions { CancellationTimeout = Timeout.InfiniteTimeSpan });
            manager.EventSources.Register(control, (_, _, _) => independent, null);
            notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
            await manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                .WaitAsync(s_signalTimeout).ConfigureAwait(false);
            notifier.SetAreEventsMonitored(manager.SystemContext, false, false);
            manager.EventSources.SignalReconcile();
            await first.StopRequested.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
            Task restarted = manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask();
            try
            {
                control.SetAreEventsMonitored(manager.SystemContext, true, false);
                await manager.EventSources.WaitUntilReadyAsync(control, CancellationToken.None).AsTask()
                    .WaitAsync(s_signalTimeout).ConfigureAwait(false);
                Assert.That(starts, Is.EqualTo(1));
                Assert.That(restarted.IsCompleted, Is.False);

                first.ReleaseStop.TrySetResult(true);
                await second.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
                Assert.That(starts, Is.EqualTo(2));
                Assert.That(restarted.IsCompleted, Is.False);
                second.Ready.TrySetResult(true);
                await restarted.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            }
            finally
            {
                first.ReleaseStop.TrySetResult(true);
                second.Ready.TrySetResult(true);
                await first.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CancelledSubscriptionReadinessDoesNotPreventSourceTeardown()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = await MakeReadinessNotifierAsync(manager, "Cancelled").ConfigureAwait(false);
            var stream = new ControlledReadyStream();
            manager.EventSources.Register(notifier, (_, _, _) => stream, null);
            notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
            using var cancellation = new CancellationTokenSource();
            Task subscribed = manager.EventSources.WaitUntilReadyAsync(notifier, cancellation.Token).AsTask();
            await stream.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);

            cancellation.Cancel();
            Task completed = await Task.WhenAny(subscribed, Task.Delay(s_signalTimeout)).ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(subscribed));
            await Assert.ThatAsync(() => subscribed,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            notifier.SetAreEventsMonitored(manager.SystemContext, false, false);
            manager.EventSources.SignalReconcile();
            await stream.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
        }

        [Test]
        public async Task AlwaysOnReadinessFailureIsReportedAndStopsTheProducer()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = await MakeReadinessNotifierAsync(manager, "EagerFailure").ConfigureAwait(false);
            var stream = new ControlledReadyStream();
            var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.EventSources.Register(notifier, (_, _, _) => stream,
                new EventPublishOptions
                {
                    AlwaysOn = true,
                    OnError = exception => reported.TrySetResult(exception)
                });
            await stream.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            var failure = new ServiceResultException(StatusCodes.BadServerNotConnected);

            stream.Ready.TrySetException(failure);

            Assert.That(await reported.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false), Is.SameAs(failure));
            await stream.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
        }

        [Test]
        public async Task RepeatedSourceFailureRetriesOnlyAfterBackoffAsync()
        {
            var clock = new FakeTimeProvider();
            m_timeProvider = clock;
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = await MakeReadinessNotifierAsync(manager, "Backoff").ConfigureAwait(false);
            var errors = Channel.CreateUnbounded<Exception>();
            int attempts = 0;
            var failure = new InvalidOperationException("source unavailable");
            manager.EventSources.Register(notifier,
                (_, _, _) =>
                {
                    Interlocked.Increment(ref attempts);
                    throw failure;
                },
                new EventPublishOptions
                {
                    AlwaysOn = true,
                    OnError = exception => errors.Writer.TryWrite(exception)
                });

            Assert.That(await errors.Reader.ReadAsync().AsTask().WaitAsync(s_signalTimeout).ConfigureAwait(false),
                Is.SameAs(failure));
            await Assert.ThatAsync(
                () => manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                    .WaitAsync(s_signalTimeout),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref attempts), Is.EqualTo(1));

            clock.Advance(TimeSpan.FromMilliseconds(500));
            await Assert.ThatAsync(
                () => manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                    .WaitAsync(s_signalTimeout),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref attempts), Is.EqualTo(1));

            clock.Advance(TimeSpan.FromMilliseconds(500));
            await errors.Reader.ReadAsync().AsTask().WaitAsync(s_signalTimeout).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                    .WaitAsync(s_signalTimeout),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref attempts), Is.EqualTo(2));

            clock.Advance(TimeSpan.FromSeconds(1));
            await Assert.ThatAsync(
                () => manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                    .WaitAsync(s_signalTimeout),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref attempts), Is.EqualTo(2));
            clock.Advance(TimeSpan.FromSeconds(1));
            await errors.Reader.ReadAsync().AsTask().WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref attempts), Is.EqualTo(3));
        }

        [Test]
        public async Task ReadinessFailureCanRecoverWithANewSourceGenerationAsync()
        {
            var clock = new FakeTimeProvider();
            m_timeProvider = clock;
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = await MakeReadinessNotifierAsync(manager, "RecoverReady").ConfigureAwait(false);
            var first = new ControlledReadyStream();
            var second = new ControlledReadyStream();
            int attempts = 0;
            manager.EventSources.Register(
                notifier, (_, _, _) => Interlocked.Increment(ref attempts) == 1 ? first : second, null);
            notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
            Task initial = manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask();
            await first.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            var failure = new InvalidOperationException("startup failed");
            first.Ready.TrySetException(failure);
            await Assert.ThatAsync(() => initial.WaitAsync(s_signalTimeout), Throws.Exception.SameAs(failure))
                .ConfigureAwait(false);
            await first.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            await Assert.ThatAsync(
                () => manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                    .WaitAsync(s_signalTimeout),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);

            clock.Advance(TimeSpan.FromSeconds(1));
            await second.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            second.Ready.TrySetResult(true);
            await manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                .WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref attempts), Is.EqualTo(2));
        }

        [Test]
        public async Task FailedSubscriptionReadinessRollsBackNotifierMembershipAsync()
        {
            m_timeProvider = new FakeTimeProvider();
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = await MakeReadinessNotifierAsync(manager, "FailedSubscription")
                .ConfigureAwait(false);
            var stream = new ControlledReadyStream();
            manager.EventSources.Register(notifier, (_, _, _) => stream, null);
            var item = new Mock<IEventMonitoredItem>();
            item.SetupGet(value => value.Id).Returns(37);
            item.SetupGet(value => value.NodeId).Returns(notifier.NodeId);
            item.SetupGet(value => value.MonitoredItemType).Returns(MonitoredItemTypeMask.Events);
            Task subscribed = manager.SubscribeEventAsync(notifier, item.Object, false).AsTask();
            await stream.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            var failure = new ServiceResultException(StatusCodes.BadServerNotConnected);
            stream.Ready.TrySetException(failure);

            await Assert.ThatAsync(() => subscribed.WaitAsync(s_signalTimeout), Throws.Exception.SameAs(failure))
                .ConfigureAwait(false);
            Assert.That(notifier.AreEventsMonitored, Is.False);
            Assert.That(manager.EventItemCount, Is.Zero);
            Assert.That(manager.EventNodeCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EventCompensationReadinessHasFiniteDeadlineWithInfiniteSourceTimeoutAsync(bool deleteNode)
        {
            var clock = new DeadlineObservingClock();
            m_timeProvider = clock;
            m_mockMasterNodeManager.SetupGet(value => value.CoreNodeManager)
                .Returns(Mock.Of<ICoreNodeManager>());
            await using TestablePublishManager manager = CreateManager();
            BaseObjectState source = await MakeReadinessNotifierAsync(manager, "CompensatedSource")
                .ConfigureAwait(false);
            BaseObjectState independent = await MakeReadinessNotifierAsync(manager, "IndependentSource")
                .ConfigureAwait(false);
            var first = new ControlledReadyStream();
            var replacement = new ControlledReadyStream();
            var healthy = new ControlledReadyStream();
            first.Ready.TrySetResult(true);
            healthy.Ready.TrySetResult(true);
            int generations = 0;
            manager.EventSources.Register(source, (_, _, _) =>
                Interlocked.Increment(ref generations) == 1 ? first : replacement,
                new EventPublishOptions { CancellationTimeout = Timeout.InfiniteTimeSpan });
            manager.EventSources.Register(independent, (_, _, _) => healthy, null);
            var identity = new Mock<IUserIdentity>();
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Identity).Returns(identity.Object);
            session.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
            session.SetupGet(value => value.PreferredLocales).Returns([]);
            var subscription = new Mock<ISubscription>();
            subscription.SetupGet(value => value.Session).Returns(session.Object);
            subscription.SetupGet(value => value.EffectiveIdentity).Returns(identity.Object);
            var filter = new EventFilter();
            using var item = new MonitoredItem(
                m_mockServer.Object, manager, new NodeHandle(), 1, 37,
                new ReadValueId { NodeId = source.NodeId, AttributeId = Attributes.EventNotifier },
                DiagnosticsMasks.None, TimestampsToReturn.Both, MonitoringMode.Reporting, 3,
                filter, filter, null, 0, 10, true, 0)
            {
                SubscriptionCallback = subscription.Object
            };
            var detachable = (IDetachableMonitoredItem)item;
            var lifecycle = (INodeManagerMonitoredItemLifecycle)manager;
            detachable.Detach(m_mockServer.Object);
            ServiceResult attached = await lifecycle.AttachMonitoredItemAsync(item).ConfigureAwait(false);
            Assert.That(attached.StatusCode, Is.EqualTo(StatusCodes.Good));
            var originalFailure = new InvalidOperationException("Event unsubscribe failed after stopping its source.");
            manager.UnsubscribeCallback = async ct =>
            {
                await first.Stopped.Task.WaitAsync(ct).ConfigureAwait(false);
                throw originalFailure;
            };
            Task transition = deleteNode
                ? manager.DeleteNodeAsync(manager.SystemContext, source.NodeId).AsTask()
                : lifecycle.DetachMonitoredItemAsync(item).AsTask();
            var independentItem = new Mock<IEventMonitoredItem>();
            independentItem.SetupGet(value => value.Id).Returns(38);
            independentItem.SetupGet(value => value.NodeId).Returns(independent.NodeId);
            independentItem.SetupGet(value => value.MonitoredItemType).Returns(MonitoredItemTypeMask.Events);
            try
            {
                await replacement.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
                ServiceResult unrelated = await manager.SubscribeEventAsync(independent, independentItem.Object, false)
                    .AsTask().WaitAsync(s_signalTimeout).ConfigureAwait(false);
                Assert.That(unrelated.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(independent.AreEventsMonitored, Is.True);
                Assert.That(transition.IsCompleted, Is.False);
                Assert.That(replacement.Ready.Task.IsCompleted, Is.False);

                TimeSpan deadline = await clock.DeadlineScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                Assert.That(deadline, Is.GreaterThan(TimeSpan.Zero));
                Assert.That(deadline, Is.Not.EqualTo(Timeout.InfiniteTimeSpan));
                clock.Advance(deadline - TimeSpan.FromTicks(1));
                Assert.That(transition.IsCompleted, Is.False);
                clock.Advance(TimeSpan.FromTicks(1));
                AggregateException failure = Assert.ThrowsAsync<AggregateException>(
                    async () => await transition.WaitAsync(s_signalTimeout).ConfigureAwait(false))!;
                Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(failure.InnerExceptions[0], Is.SameAs(originalFailure));
                Assert.That(failure.InnerExceptions[1],
                    Is.InstanceOf<OperationCanceledException>().Or.InstanceOf<TimeoutException>());
                Assert.That(replacement.Ready.Task.IsCompleted, Is.False);
                Assert.That(detachable.IsDetached, Is.False);
                Assert.That(item.NodeManager, Is.SameAs(manager));
                Assert.That(source.AreEventsMonitored, Is.True);
                Assert.That(manager.Find(source.NodeId), Is.SameAs(source));
            }
            finally
            {
                replacement.Ready.TrySetResult(true);
                try
                {
                    await transition.WaitAsync(s_signalTimeout).ConfigureAwait(false);
                }
                catch (InvalidOperationException exception) when (ReferenceEquals(exception, originalFailure))
                {
                }
                catch (AggregateException exception) when (exception.InnerExceptions.Contains(originalFailure))
                {
                }
                manager.UnsubscribeCallback = null;
                await lifecycle.DetachMonitoredItemAsync(item).ConfigureAwait(false);
                await manager.SubscribeEventAsync(independent, independentItem.Object, true).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task NullFactoryReportsFailureWithoutRestartingBeforeBackoffAsync()
        {
            m_timeProvider = new FakeTimeProvider();
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = await MakeReadinessNotifierAsync(manager, "NullFactory").ConfigureAwait(false);
            var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            int attempts = 0;
            manager.EventSources.Register(notifier,
                (_, _, _) =>
                {
                    Interlocked.Increment(ref attempts);
                    return null;
                },
                new EventPublishOptions
                {
                    AlwaysOn = true,
                    OnError = error => reported.TrySetResult(error)
                });
            Exception failure = await reported.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)failure).StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            await Assert.ThatAsync(
                () => manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                    .WaitAsync(s_signalTimeout),
                Throws.Exception.SameAs(failure)).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref attempts), Is.EqualTo(1));
        }

        [Test]
        public async Task LateFailureCannotRetireANewerSourceGenerationAsync()
        {
            m_timeProvider = new FakeTimeProvider();
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = await MakeReadinessNotifierAsync(manager, "LateGeneration").ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lateFailure = new TaskCompletionSource<BaseEventState>(TaskCreationOptions.RunContinuationsAsynchronously);
            var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new ControlledReadyStream();
            int attempts = 0;
            async IAsyncEnumerable<BaseEventState> DelayedStream()
            {
                entered.TrySetResult(true);
                yield return await lateFailure.Task.ConfigureAwait(false);
            }

            manager.EventSources.Register(notifier,
                (_, _, _) => Interlocked.Increment(ref attempts) == 1 ? DelayedStream() : second,
                new EventPublishOptions
                {
                    CancellationTimeout = TimeSpan.Zero,
                    OnError = error => reported.TrySetResult(error)
                });
            notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
            await manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                .WaitAsync(s_signalTimeout).ConfigureAwait(false);
            await entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);

            notifier.SetAreEventsMonitored(manager.SystemContext, false, false);
            await manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                .WaitAsync(s_signalTimeout).ConfigureAwait(false);
            notifier.SetAreEventsMonitored(manager.SystemContext, true, false);
            Task ready = manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask();
            await second.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            second.Ready.TrySetResult(true);
            await ready.WaitAsync(s_signalTimeout).ConfigureAwait(false);

            var failure = new InvalidOperationException("old source failed late");
            lateFailure.TrySetException(failure);
            Assert.That(await reported.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false), Is.SameAs(failure));
            await manager.EventSources.WaitUntilReadyAsync(notifier, CancellationToken.None).AsTask()
                .WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref attempts), Is.EqualTo(2));
            Assert.That(second.Stopped.Task.IsCompleted, Is.False);
        }

        private static async Task<BaseObjectState> MakeReadinessNotifierAsync(
            TestablePublishManager manager, string name)
        {
            var notifier = new BaseObjectState(null)
            {
                NodeId = new NodeId(name, manager.NamespaceIndexes[0]),
                BrowseName = new QualifiedName(name, manager.NamespaceIndexes[0]),
                EventNotifier = EventNotifiers.SubscribeToEvents
            };
            await manager.AddPublic(notifier).ConfigureAwait(false);
            return notifier;
        }

        private static Task GetSourceDrain(EventSourceRegistry registry, NodeId notifierId)
        {
            FieldInfo sourcesField = typeof(EventSourceRegistry).GetField(
                "m_sources", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("The event-source registry was not found.");
            if (sourcesField.GetValue(registry) is not IDictionary sources ||
                sources[notifierId] is not { } entry)
            {
                throw new InvalidOperationException("The event source was not registered.");
            }
            return entry.GetType().GetField("StoppingTask", BindingFlags.Instance | BindingFlags.Public)?
                .GetValue(entry) as Task
                ?? throw new InvalidOperationException("The event-source drain task was not found.");
        }

        private sealed class DeadlineObservingClock : TimeProvider
        {
            public TaskCompletionSource<TimeSpan> DeadlineScheduled { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public override long TimestampFrequency => m_clock.TimestampFrequency;

            public override DateTimeOffset GetUtcNow()
            {
                return m_clock.GetUtcNow();
            }

            public override long GetTimestamp()
            {
                return m_clock.GetTimestamp();
            }

            public override ITimer CreateTimer(
                TimerCallback callback,
                object state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                ITimer timer = m_clock.CreateTimer(callback, state, dueTime, period);
                if (dueTime > TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                {
                    DeadlineScheduled.TrySetResult(dueTime);
                }
                var observed = new Mock<ITimer>();
                observed.Setup(value => value.Change(It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                    .Returns<TimeSpan, TimeSpan>((due, interval) =>
                    {
                        Interlocked.Exchange(ref m_onTimerChange, null)?.Invoke();
                        return timer.Change(due, interval);
                    });
                observed.Setup(value => value.Dispose()).Callback(timer.Dispose);
                observed.Setup(value => value.DisposeAsync()).Returns(timer.DisposeAsync);
                return observed.Object;
            }

            public void RunOnNextTimerChange(Action callback)
            {
                Interlocked.Exchange(ref m_onTimerChange, callback);
            }

            public void Advance(TimeSpan elapsed)
            {
                m_clock.Advance(elapsed);
            }

            private readonly FakeTimeProvider m_clock = new();
            private Action m_onTimerChange;
        }

        private sealed class ControlledReadyStream : IAsyncEnumerable<BaseEventState>, IEventSourceReadiness
        {
            public bool BlockStop { get; init; }

            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Ready { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Stopped { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> StopRequested { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> ReleaseStop { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Channel<BaseEventState> Events { get; } = Channel.CreateUnbounded<BaseEventState>();

            public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask(Ready.Task.WaitAsync(cancellationToken));
            }

            public IAsyncEnumerator<BaseEventState> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return RunAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            }

            private async IAsyncEnumerable<BaseEventState> RunAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                Entered.TrySetResult(true);
                try
                {
                    await foreach (BaseEventState occurrence in Events.Reader.ReadAllAsync(cancellationToken)
                        .ConfigureAwait(false))
                    {
                        yield return occurrence;
                    }
                }
                finally
                {
                    StopRequested.TrySetResult(true);
                    if (BlockStop)
                    {
                        await ReleaseStop.Task.ConfigureAwait(false);
                    }
                    Stopped.TrySetResult(true);
                }
            }
        }

        private TestablePublishManager CreateManager()
        {
            var manager = new TestablePublishManager(
                m_mockServer.Object,
                m_configuration,
                logger: null,
                kNamespaceUri);
            SetupMasterNodeManager(manager);
            return manager;
        }

        private void SetupMasterNodeManager(TestablePublishManager manager)
        {
            m_mockMasterNodeManager
                .Setup(m => m.GetManagerHandleAsync(It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Returns<NodeId, CancellationToken>((nodeId, _) =>
                {
                    NodeState nodeState = manager.Find(nodeId);
                    if (nodeState == null)
                    {
                        return new ValueTask<(object handle, IAsyncNodeManager nodeManager)>((null, null));
                    }
                    var handle = new NodeHandle(nodeId, nodeState);
                    return new ValueTask<(object handle, IAsyncNodeManager nodeManager)>((handle, manager));
                });
        }

        private static BaseObjectState MakeNotifier(
            TestablePublishManager manager,
            string browseName,
            byte eventNotifier = EventNotifiers.SubscribeToEvents)
        {
            var notifier = new BaseObjectState(parent: null)
            {
                NodeId = new NodeId(browseName, kNs),
                BrowseName = new QualifiedName(browseName, kNs),
                DisplayName = new LocalizedText(browseName),
                EventNotifier = eventNotifier
            };
            // CA2012: AddPublic is a synchronous test helper that completes immediately;
            // calling AsTask/.GetAwaiter().GetResult() in test setup is intentional.
            ValueTask<NodeId> addTask = manager.AddPublic(notifier);
            if (!addTask.IsCompletedSuccessfully)
            {
                _ = addTask.AsTask().GetAwaiter().GetResult();
            }
            return notifier;
        }

        private static async Task<T> WaitForAsync<T>(Task<T> task)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(s_signalTimeout)).ConfigureAwait(false);
            if (completed != task)
            {
                Assert.Fail($"Operation did not signal within {s_signalTimeout.TotalSeconds:F1}s.");
            }
            return await task.ConfigureAwait(false);
        }

        private static async IAsyncEnumerable<BaseEventState> CountingStream(
            TaskCompletionSource<bool> started,
            [EnumeratorCancellation] CancellationToken ct)
        {
            started.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            yield break;
        }

        private static async IAsyncEnumerable<BaseEventState> CancelObservingStream(
            TaskCompletionSource<bool> iteratorEntered,
            TaskCompletionSource<bool> observedCancel,
            [EnumeratorCancellation] CancellationToken ct)
        {
            iteratorEntered.TrySetResult(true);
            try
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                observedCancel.TrySetResult(true);
                throw;
            }
            yield break;
        }

        private static async IAsyncEnumerable<BaseEventState> ThrowingStream(
            Exception toThrow,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            throw toThrow;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        private static async IAsyncEnumerable<BaseEventState> EmptyStream(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            yield break;
        }

        private sealed class AsyncCountdown
        {
            private int m_remaining;

            private readonly TaskCompletionSource<bool> m_done =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public AsyncCountdown(int target)
            {
                m_remaining = target;
            }

            public void SignalOne()
            {
                if (Interlocked.Decrement(ref m_remaining) == 0)
                {
                    m_done.TrySetResult(true);
                }
            }

            public Task<bool> WaitAsync()
            {
                return m_done.Task;
            }
        }

        private static class AsyncEnumerable
        {
            public static IAsyncEnumerable<T> Empty<T>()
            {
                return EmptyImpl<T>();
            }

            private static async IAsyncEnumerable<T> EmptyImpl<T>(
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                yield break;
            }
        }

        public class TestablePublishManager : FluentNodeManagerBase
        {
            public TestablePublishManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ILogger logger,
                params string[] namespaceUris)
                : base(server, configuration, logger, namespaceUris)
            {
            }

            public new NodeIdDictionary<NodeState> RootNotifiers => base.RootNotifiers;

            /// <summary>
            /// Honours the cancellation token before the base implementation
            /// registers anything, so a test can drive the cancelled path of
            /// <c>EventSourceRegistry.CompleteRegistrationsAsync</c>
            /// deterministically rather than racing the framework.
            /// </summary>
            protected override ValueTask AddRootNotifierAsync(
                NodeState notifier,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return base.AddRootNotifierAsync(notifier, cancellationToken);
            }

            public new NodeIdDictionary<NodeState> PredefinedNodes => base.PredefinedNodes;

            public int EventItemCount => MonitoredItems.Count;

            public int EventNodeCount => MonitoredNodes.Count;

            public Func<CancellationToken, ValueTask> UnsubscribeCallback { get; set; }

            public ValueTask<ServiceResult> SubscribeEventAsync(
                NodeState source,
                IEventMonitoredItem item,
                bool unsubscribe,
                CancellationToken ct = default)
            {
                return SubscribeToEventsAsync(SystemContext, source, item, unsubscribe, ct);
            }

            public ValueTask<NodeId> AddPublic(
                BaseInstanceState node,
                CancellationToken cancellationToken = default)
            {
                return AddNodeAsync(SystemContext, default, node, cancellationToken);
            }

            protected override async ValueTask OnSubscribeToEventsAsync(
                ServerSystemContext context,
                MonitoredNode2 monitoredNode,
                bool unsubscribe,
                CancellationToken cancellationToken = default)
            {
                await base.OnSubscribeToEventsAsync(context, monitoredNode, unsubscribe, cancellationToken)
                    .ConfigureAwait(false);
                if (unsubscribe && UnsubscribeCallback != null)
                {
                    await UnsubscribeCallback(cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
