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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
        // Generous timeout: reconcile/worker tasks run on the thread pool which
        // can be starved when the broader test suite (e.g. AsyncCustomNodeManager
        // tests with [Parallelizable(ParallelScope.All)]) saturates CPU. 15s
        // keeps green runs under 1s while eliminating false negatives under load.
        private static readonly TimeSpan s_signalTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan s_negativeWindow = TimeSpan.FromMilliseconds(250);

        private Mock<IServerInternal> m_mockServer;
        private ApplicationConfiguration m_configuration;
        private Mock<IMasterNodeManager> m_mockMasterNodeManager;
        private NamespaceTable m_namespaceTable;
        private MonitoredItemQueueFactory m_queueFactory;

        [SetUp]
        public void SetUp()
        {
            m_mockServer = new Mock<IServerInternal>();
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

        #region Lazy / eager activation

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

        #endregion

        #region Event delivery

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

            Channel<BaseEventState> channel = Channel.CreateUnbounded<BaseEventState>();
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

            Channel<BaseEventState> channel = Channel.CreateUnbounded<BaseEventState>();
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

            ByteString customEventId = Uuid.NewUuid().ToByteString();
            var customSource = new NodeId("OtherSource", kNs);
            const string kCustomSourceName = "AlternateName";
            const ushort kCustomSeverity = 800;

            BaseEventState authored = new BaseEventState(parent: null);
            authored.EventId = PropertyState<ByteString>.With<VariantBuilder>(authored, customEventId);
            authored.SourceNode = PropertyState<NodeId>.With<VariantBuilder>(authored, customSource);
            authored.SourceName = PropertyState<string>.With<VariantBuilder>(authored, kCustomSourceName);
            authored.Severity = PropertyState<ushort>.With<VariantBuilder>(authored, kCustomSeverity);

            Channel<BaseEventState> channel = Channel.CreateUnbounded<BaseEventState>();
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

            Channel<BaseEventState> channel = Channel.CreateUnbounded<BaseEventState>();
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

        #endregion

        #region Errors and validation

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

        #endregion

        #region Auto-promote and root-notifier

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
                (notifier.EventNotifier & EventNotifiers.SubscribeToEvents),
                Is.EqualTo(EventNotifiers.SubscribeToEvents),
                "Publish must auto-promote SubscribeToEvents on the notifier.");
        }

        [Test]
        public void Publish_RegisterAsRootNotifier_AddsToRootNotifierSet()
        {
            using TestablePublishManager manager = CreateManager();
            BaseObjectState notifier = MakeNotifier(manager, "Root");

            manager.EventSources.Register(
                notifier,
                (_, _, ct) => EmptyStream(ct),
                new EventPublishOptions { RegisterAsRootNotifier = true });

            Assert.That(manager.RootNotifiers, Contains.Key(notifier.NodeId));
        }

        #endregion

        #region Lifecycle / dispose

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

        #endregion

        #region Extension method (Publish on builder)

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
                nodeBuilder.Publish<BaseObjectState, BaseEventState>(
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
                nodeManager: Mock.Of<IAsyncNodeManager>(),
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

            Channel<BaseEventState> channel = Channel.CreateUnbounded<BaseEventState>();
            builder.Node<BaseObjectState>(notifier.BrowseName.Name)
                .Publish<BaseObjectState, BaseEventState>(
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
                nodeManager: Mock.Of<IAsyncNodeManager>(),
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
                nodeBuilder.Publish<BaseObjectState, BaseEventState>(
                    source: (IAsyncEnumerable<BaseEventState>)null));
        }

        [Test]
        public void AttachToBuilder_NullBuilder_ThrowsArgumentNull()
        {
            using TestablePublishManager manager = CreateManager();
            Assert.Throws<ArgumentNullException>(() => manager.AttachToBuilder(null));
        }

        #endregion

        #region Helpers

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
            manager.AddPublic(notifier);
            return notifier;
        }

        private static async Task WaitForAsync(Task task)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(s_signalTimeout)).ConfigureAwait(false);
            if (completed != task)
            {
                Assert.Fail($"Operation did not signal within {s_signalTimeout.TotalSeconds:F1}s.");
            }
            await task.ConfigureAwait(false);
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

            public AsyncCountdown(int target) => m_remaining = target;

            public void SignalOne()
            {
                if (Interlocked.Decrement(ref m_remaining) == 0)
                {
                    m_done.TrySetResult(true);
                }
            }

            public Task WaitAsync() => m_done.Task;
        }

        private static class AsyncEnumerable
        {
            public static IAsyncEnumerable<T> Empty<T>() => EmptyImpl<T>();

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

            public new NodeIdDictionary<NodeState> PredefinedNodes => base.PredefinedNodes;

            public ValueTask<NodeId> AddPublic(
                BaseInstanceState node,
                CancellationToken cancellationToken = default)
            {
                return AddNodeAsync(SystemContext, default, node, cancellationToken);
            }
        }

        #endregion
    }
}
