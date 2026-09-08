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
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Exercises event-source ownership through the fluent and monitored-item service surfaces.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    [Category("Server")]
    [NonParallelizable]
    public sealed class EventMonitoredItemReadinessTests
    {
        [SetUp]
        public async Task SetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(EventMonitoredItemReadinessTests),
                Guid.NewGuid().ToString("N"));
            m_fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry))
            {
                SecurityNone = true,
                AutoAccept = true
            };
            m_server = await m_fixture.StartAsync(m_pkiRoot).ConfigureAwait(false);
            (m_requestHeader, m_channel) = await m_server.CreateAndActivateSessionAsync(
                TestContext.CurrentContext.Test.Name).ConfigureAwait(false);
            m_services = new ServerTestServices(m_server, m_channel);
            CreateSubscriptionResponse subscription = await m_services.CreateSubscriptionAsync(
                NewRequestHeader(), 100, 1000, 10, 0, true, 0).ConfigureAwait(false);
            m_subscriptionId = subscription.SubscriptionId;
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_requestHeader is not null)
            {
                await m_server.CloseSessionAsync(
                    m_channel, NewRequestHeader(), true, RequestLifetime.None).ConfigureAwait(false);
            }
            if (m_fixture is not null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            if (Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DirectNotifierStartupFailureReturnsItemErrorWithoutOwnershipAsync(bool ancestor)
        {
            var source = new ControlledEventSource("Source", useAncestor: ancestor);
            await AddSourcesAsync(source).ConfigureAwait(false);
            Task<CreateMonitoredItemsResponse> creation = CreateAsync(source.NotifierId);
            ControlledStream activation = await source.NextActivationAsync().ConfigureAwait(false);
            await activation.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(creation.IsCompleted, Is.False, "Creation must wait for producer readiness.");

            activation.Ready.SetException(new ServiceResultException(StatusCodes.BadServerNotConnected));

            await Assert.ThatAsync(
                () => creation.WaitAsync(s_signalTimeout),
                Throws.Nothing).ConfigureAwait(false);
            CreateMonitoredItemsResponse response = await creation.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.Results, Has.Count.EqualTo(1));
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadServerNotConnected));
                Assert.That(response.Results[0].MonitoredItemId, Is.Zero);
                Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems(), Is.Empty);
            });
            await activation.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(source.ActiveCount, Is.Zero);
        }

        [Test]
        public async Task ServerNotifierStartupFailurePreservesExistingSubscriptionAsync()
        {
            var healthy = new ControlledEventSource("Healthy");
            var failing = new ControlledEventSource("Failing");
            await AddSourcesAsync(healthy).ConfigureAwait(false);
            await AddSourcesAsync(failing).ConfigureAwait(false);

            Task<CreateMonitoredItemsResponse> existingCreation = CreateAsync(healthy.NodeId);
            ControlledStream existingActivation = await healthy.NextActivationAsync().ConfigureAwait(false);
            existingActivation.Ready.SetResult(true);
            CreateMonitoredItemsResponse existing = await existingCreation.WaitAsync(s_signalTimeout)
                .ConfigureAwait(false);
            Assert.That(existing.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));

            Task<CreateMonitoredItemsResponse> creation = CreateAsync(ObjectIds.Server);
            ControlledStream failedActivation = await failing.NextActivationAsync().ConfigureAwait(false);
            await failedActivation.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            failedActivation.Ready.SetException(new ServiceResultException(StatusCodes.BadServerNotConnected));

            CreateMonitoredItemsResponse response = await creation.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadServerNotConnected));
                Assert.That(response.Results[0].MonitoredItemId, Is.Zero);
                Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems(), Has.Count.EqualTo(1));
                Assert.That(healthy.ActiveCount, Is.EqualTo(1));
                Assert.That(existingActivation.Stopped.Task.IsCompleted, Is.False);
            });
            await AssertEventDeliveredAsync(existingActivation).ConfigureAwait(false);

            DeleteMonitoredItemsResponse deleted = await m_services.DeleteMonitoredItemsAsync(
                NewRequestHeader(), m_subscriptionId, [existing.Results[0].MonitoredItemId]).ConfigureAwait(false);
            Assert.That(deleted.Results[0], Is.EqualTo(StatusCodes.Good));
            await existingActivation.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            await failedActivation.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(healthy.ActiveCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationPreservesSuccessfulSiblingAsync(bool serverNotifier)
        {
            var healthy = new ControlledEventSource("Healthy");
            var pending = new ControlledEventSource("Pending");
            await AddSourcesAsync(healthy).ConfigureAwait(false);
            await AddSourcesAsync(pending).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            Task<CreateMonitoredItemsResponse> creation = m_services.CreateMonitoredItemsAsync(
                NewRequestHeader(),
                m_subscriptionId,
                TimestampsToReturn.Both,
                [
                    CreateRequest(healthy.NodeId),
                    CreateRequest(serverNotifier ? ObjectIds.Server : pending.NodeId),
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                            AttributeId = Attributes.Value
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 3,
                            SamplingInterval = 100,
                            QueueSize = 1
                        }
                    }
                ],
                cancellation.Token).AsTask();
            ControlledStream healthyActivation = await healthy.NextActivationAsync().ConfigureAwait(false);
            healthyActivation.Ready.SetResult(true);
            ControlledStream pendingActivation = await pending.NextActivationAsync().ConfigureAwait(false);
            await pendingActivation.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);

            cancellation.Cancel();

            await Assert.ThatAsync(
                () => creation.WaitAsync(s_signalTimeout),
                Throws.Nothing).ConfigureAwait(false);
            CreateMonitoredItemsResponse response = await creation.ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.Results, Has.Count.EqualTo(3));
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[0].MonitoredItemId, Is.Not.Zero);
                Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                Assert.That(response.Results[1].MonitoredItemId, Is.Zero);
                Assert.That(response.Results[2].StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                Assert.That(response.Results[2].MonitoredItemId, Is.Zero);
                Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems(), Has.Count.EqualTo(1));
                Assert.That(healthyActivation.Stopped.Task.IsCompleted, Is.False);
            });
            await pendingActivation.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);

            DeleteMonitoredItemsResponse deleted = await m_services.DeleteMonitoredItemsAsync(
                NewRequestHeader(), m_subscriptionId, [response.Results[0].MonitoredItemId]).ConfigureAwait(false);
            Assert.That(deleted.Results[0], Is.EqualTo(StatusCodes.Good));
            await healthyActivation.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(healthy.ActiveCount, Is.Zero);
            Assert.That(pending.ActiveCount, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ServerNotifierOwnsEveryRootUntilDeletionAsync(bool useSamplingGroups)
        {
            var first = new ControlledEventSource("First", readyOnOpen: true);
            var second = new ControlledEventSource("Second", readyOnOpen: true);
            await AddSourcesAsync(useSamplingGroups, first, second).ConfigureAwait(false);

            CreateMonitoredItemsResponse response = await CreateAsync(ObjectIds.Server)
                .WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(first.ActivationCount, Is.EqualTo(1));
                Assert.That(second.ActivationCount, Is.EqualTo(1));
                Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems(), Has.Count.EqualTo(1));
            });
            ControlledStream firstActivation = await first.NextActivationAsync().ConfigureAwait(false);
            ControlledStream secondActivation = await second.NextActivationAsync().ConfigureAwait(false);
            await firstActivation.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            await secondActivation.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);

            DeleteMonitoredItemsResponse deleted = await m_services.DeleteMonitoredItemsAsync(
                NewRequestHeader(), m_subscriptionId, [response.Results[0].MonitoredItemId]).ConfigureAwait(false);
            Assert.That(deleted.Results[0], Is.EqualTo(StatusCodes.Good));
            await firstActivation.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            await secondActivation.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(first.ActiveCount, Is.Zero);
            Assert.That(second.ActiveCount, Is.Zero);
        }

        [Test]
        public async Task RetryAfterFailedCreationHasOneSourceOwnerAsync(
            [Values(false, true)] bool serverNotifier,
            [Values(false, true)] bool cancel,
            [Values(false, true)] bool useSamplingGroups)
        {
            var source = new ControlledEventSource("Source", useAncestor: true);
            await AddSourcesAsync(useSamplingGroups, source).ConfigureAwait(false);
            NodeId nodeId = serverNotifier ? ObjectIds.Server : source.NotifierId;
            using var cancellation = new CancellationTokenSource();
            Task<CreateMonitoredItemsResponse> creation = CreateAsync(nodeId, cancellation.Token);
            ControlledStream failedActivation = await source.NextActivationAsync().ConfigureAwait(false);
            await failedActivation.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            if (cancel)
            {
                cancellation.Cancel();
            }
            else
            {
                failedActivation.Ready.SetException(new ServiceResultException(StatusCodes.BadServerNotConnected));
            }
            CreateMonitoredItemsResponse failed = await creation.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(
                failed.Results[0].StatusCode,
                Is.EqualTo(cancel ? StatusCodes.BadRequestCancelledByClient : StatusCodes.BadServerNotConnected));
            Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems(), Is.Empty);

            source.ReadyOnOpen = true;
            CreateMonitoredItemsResponse retry = await CreateAsync(nodeId)
                .WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(retry.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            ControlledStream active = await source.NextActivationAsync().ConfigureAwait(false);
            await active.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            CreateMonitoredItemsResponse shared = await CreateAsync(source.NodeId)
                .WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(shared.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(source.ActivationCount, Is.EqualTo(2));
                Assert.That(source.ActiveCount, Is.EqualTo(1));
                Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems(), Has.Count.EqualTo(2));
            });

            DeleteMonitoredItemsResponse deleted = await m_services.DeleteMonitoredItemsAsync(
                NewRequestHeader(), m_subscriptionId, [retry.Results[0].MonitoredItemId]).ConfigureAwait(false);
            Assert.That(deleted.Results[0], Is.EqualTo(StatusCodes.Good));
            Assert.That(active.Stopped.Task.IsCompleted, Is.False);
            deleted = await m_services.DeleteMonitoredItemsAsync(
                NewRequestHeader(), m_subscriptionId, [shared.Results[0].MonitoredItemId]).ConfigureAwait(false);
            Assert.That(deleted.Results[0], Is.EqualTo(StatusCodes.Good));
            await active.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            await failedActivation.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(source.ActiveCount, Is.Zero);
            Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems(), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MixedRequestCanRetryFailedNotifierAsync(bool serverNotifier)
        {
            var healthy = new ControlledEventSource("Healthy", readyOnOpen: true);
            var retrying = new ControlledEventSource("Retrying");
            await AddSourcesAsync(healthy).ConfigureAwait(false);
            await AddSourcesAsync(retrying).ConfigureAwait(false);
            NodeId nodeId = serverNotifier ? ObjectIds.Server : retrying.NodeId;
            Task<CreateMonitoredItemsResponse> creation = m_services.CreateMonitoredItemsAsync(
                NewRequestHeader(),
                m_subscriptionId,
                TimestampsToReturn.Both,
                [CreateRequest(healthy.NodeId), CreateRequest(nodeId), CreateRequest(nodeId)]).AsTask();
            ControlledStream failedActivation = await retrying.NextActivationAsync().ConfigureAwait(false);
            await failedActivation.Entered.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            retrying.ReadyOnOpen = true;

            failedActivation.Ready.SetException(new InvalidOperationException("Producer startup failed."));

            CreateMonitoredItemsResponse response = await creation.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(response.Results, Has.Count.EqualTo(3));
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
                Assert.That(response.Results[1].MonitoredItemId, Is.Zero);
                Assert.That(response.Results[2].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(healthy.ActivationCount, Is.EqualTo(1));
                Assert.That(retrying.ActivationCount, Is.EqualTo(2));
                Assert.That(m_server.CurrentInstance.EventManager.GetMonitoredItems(), Has.Count.EqualTo(2));
            });
            ControlledStream healthyActivation = await healthy.NextActivationAsync().ConfigureAwait(false);
            ControlledStream retryActivation = await retrying.NextActivationAsync().ConfigureAwait(false);
            DeleteMonitoredItemsResponse deleted = await m_services.DeleteMonitoredItemsAsync(
                NewRequestHeader(),
                m_subscriptionId,
                [response.Results[0].MonitoredItemId, response.Results[2].MonitoredItemId]).ConfigureAwait(false);
            Assert.That(deleted.Results, Has.Count.EqualTo(2));
            Assert.That(deleted.Results.ToArray(), Is.All.EqualTo(StatusCodes.Good));
            await healthyActivation.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            await failedActivation.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            await retryActivation.Stopped.Task.WaitAsync(s_signalTimeout).ConfigureAwait(false);
            Assert.That(healthy.ActiveCount, Is.Zero);
            Assert.That(retrying.ActiveCount, Is.Zero);
        }

        private async Task AssertEventDeliveredAsync(ControlledStream stream)
        {
            const string Message = "The existing subscription still receives events.";
            var occurrence = new BaseEventState(null);
            occurrence.Message = PropertyState<LocalizedText>.With<VariantBuilder>(
                occurrence, new LocalizedText(Message));
            await stream.EmitAsync(occurrence).ConfigureAwait(false);

            using var timeout = new CancellationTokenSource(s_signalTimeout);
            while (true)
            {
                PublishResponse published = await m_services.PublishAsync(
                    NewRequestHeader(), [], timeout.Token).ConfigureAwait(false);
                for (int ii = 0; ii < published.NotificationMessage.NotificationData.Count; ii++)
                {
                    if (published.NotificationMessage.NotificationData[ii].TryGetValue(
                        out EventNotificationList events))
                    {
                        Assert.That(events.Events, Has.Count.EqualTo(1));
                        Assert.That(events.Events[0].ClientHandle, Is.EqualTo(1));
                        Assert.That(events.Events[0].EventFields, Has.Count.EqualTo(1));
                        Assert.That(events.Events[0].EventFields[0].TryGetValue(out LocalizedText message), Is.True);
                        Assert.That(message.Text, Is.EqualTo(Message));
                        return;
                    }
                }
            }
        }

        private Task AddSourcesAsync(params ControlledEventSource[] sources)
        {
            return AddSourcesAsync(false, sources);
        }

        private async Task AddSourcesAsync(bool useSamplingGroups, params ControlledEventSource[] sources)
        {
            await m_server.NodeManagerLifecycle.AddAsync(
                new EventNodeManagerFactory(sources, useSamplingGroups), null).ConfigureAwait(false);
        }

        private Task<CreateMonitoredItemsResponse> CreateAsync(
            NodeId nodeId,
            CancellationToken cancellationToken = default)
        {
            return m_services.CreateMonitoredItemsAsync(
                NewRequestHeader(),
                m_subscriptionId,
                TimestampsToReturn.Both,
                [CreateRequest(nodeId)],
                cancellationToken).AsTask();
        }

        private RequestHeader NewRequestHeader()
        {
            return new RequestHeader
            {
                AuthenticationToken = m_requestHeader.AuthenticationToken,
                Timestamp = DateTimeUtc.Now
            };
        }

        private static MonitoredItemCreateRequest CreateRequest(NodeId nodeId)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.EventNotifier
                },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 0,
                    QueueSize = 10,
                    DiscardOldest = true,
                    Filter = new ExtensionObject(new EventFilter
                    {
                        SelectClauses =
                        [
                            new SimpleAttributeOperand
                            {
                                TypeDefinitionId = ObjectTypeIds.BaseEventType,
                                BrowsePath = [new QualifiedName(BrowseNames.Message)],
                                AttributeId = Attributes.Value
                            }
                        ]
                    })
                }
            };
        }

        private sealed class EventNodeManagerFactory : IAsyncNodeManagerFactory
        {
            public EventNodeManagerFactory(ArrayOf<ControlledEventSource> sources, bool useSamplingGroups)
            {
                m_sources = sources;
                m_useSamplingGroups = useSamplingGroups;
                NamespacesUris = ["urn:opcfoundation:tests:event-readiness:" + sources[0].Name];
            }

            public ArrayOf<string> NamespacesUris { get; }

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncNodeManager>(
                    new EventNodeManager(server, configuration, NamespacesUris[0], m_sources, m_useSamplingGroups));
            }

            private readonly ArrayOf<ControlledEventSource> m_sources;
            private readonly bool m_useSamplingGroups;
        }

        private sealed class EventNodeManager : FluentNodeManagerBase
        {
            public EventNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                string namespaceUri,
                ArrayOf<ControlledEventSource> sources,
                bool useSamplingGroups)
                : base(server, configuration, useSamplingGroups, namespaceUri)
            {
                m_sources = sources;
            }

            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                NodeManagerBuilder builder = CreateFluentBuilder(NamespaceIndexes[0]);
                for (int ii = 0; ii < m_sources.Count; ii++)
                {
                    ControlledEventSource source = m_sources[ii];
                    var notifier = new BaseObjectState(null)
                    {
                        NodeId = new NodeId(source.Name, NamespaceIndexes[0]),
                        BrowseName = new QualifiedName(source.Name, NamespaceIndexes[0]),
                        DisplayName = new LocalizedText(source.Name),
                        TypeDefinitionId = ObjectTypeIds.BaseObjectType,
                        EventNotifier = EventNotifiers.SubscribeToEvents
                    };
                    source.NodeId = notifier.NodeId;
                    source.NotifierId = notifier.NodeId;
                    await AddPredefinedNodeAsync(
                        SystemContext, notifier, cancellationToken).ConfigureAwait(false);
                    builder.Node<BaseObjectState>(source.Name).Publish((_, _, _) => source.Open());
                    BaseObjectState root = notifier;
                    if (source.UseAncestor)
                    {
                        root = new BaseObjectState(null)
                        {
                            NodeId = new NodeId(source.Name + "Area", NamespaceIndexes[0]),
                            BrowseName = new QualifiedName(source.Name + "Area", NamespaceIndexes[0]),
                            DisplayName = new LocalizedText(source.Name + "Area"),
                            TypeDefinitionId = ObjectTypeIds.BaseObjectType,
                            EventNotifier = EventNotifiers.SubscribeToEvents
                        };
                        root.AddNotifier(SystemContext, ReferenceTypeIds.HasNotifier, false, notifier);
                        notifier.AddNotifier(SystemContext, ReferenceTypeIds.HasNotifier, true, root);
                        source.NotifierId = root.NodeId;
                        await AddPredefinedNodeAsync(
                            SystemContext, root, cancellationToken).ConfigureAwait(false);
                    }
                    await AddRootNotifierAsync(root, cancellationToken).ConfigureAwait(false);
                }
                builder.Seal();
            }

            private readonly ArrayOf<ControlledEventSource> m_sources;
        }

        private sealed class ControlledEventSource
        {
            public ControlledEventSource(string name, bool readyOnOpen = false, bool useAncestor = false)
            {
                Name = name;
                ReadyOnOpen = readyOnOpen;
                UseAncestor = useAncestor;
            }

            public string Name { get; }

            public NodeId NodeId { get; set; }

            public NodeId NotifierId { get; set; }

            public bool UseAncestor { get; }

            public int ActiveCount => Volatile.Read(ref m_activeCount);

            public int ActivationCount => Volatile.Read(ref m_activationCount);

            public bool ReadyOnOpen
            {
                get => Volatile.Read(ref m_readyOnOpen);
                set => Volatile.Write(ref m_readyOnOpen, value);
            }

            public ControlledStream Open()
            {
                var stream = new ControlledStream(
                    () => Interlocked.Increment(ref m_activeCount),
                    () => Interlocked.Decrement(ref m_activeCount));
                Interlocked.Increment(ref m_activationCount);
                if (ReadyOnOpen)
                {
                    stream.Ready.SetResult(true);
                }
                Assert.That(m_activations.Writer.TryWrite(stream), Is.True);
                return stream;
            }

            public Task<ControlledStream> NextActivationAsync()
            {
                return m_activations.Reader.ReadAsync().AsTask().WaitAsync(s_signalTimeout);
            }

            private readonly Channel<ControlledStream> m_activations = Channel.CreateUnbounded<ControlledStream>();
            private bool m_readyOnOpen;
            private int m_activeCount;
            private int m_activationCount;
        }

        private sealed class ControlledStream : IAsyncEnumerable<BaseEventState>, IEventSourceReadiness
        {
            public ControlledStream(Action entered, Action stopped)
            {
                m_entered = entered;
                m_stopped = stopped;
            }

            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Ready { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Stopped { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
            {
                return new ValueTask(Ready.Task.WaitAsync(cancellationToken));
            }

            public ValueTask EmitAsync(BaseEventState occurrence)
            {
                return m_events.Writer.WriteAsync(occurrence);
            }

            public IAsyncEnumerator<BaseEventState> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return RunAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            }

            private async IAsyncEnumerable<BaseEventState> RunAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                m_entered();
                Entered.TrySetResult(true);
                try
                {
                    await foreach (BaseEventState occurrence in m_events.Reader.ReadAllAsync(cancellationToken)
                        .ConfigureAwait(false))
                    {
                        yield return occurrence;
                    }
                }
                finally
                {
                    m_stopped();
                    Stopped.TrySetResult(true);
                }
            }

            private readonly Action m_entered;
            private readonly Action m_stopped;
            private readonly Channel<BaseEventState> m_events = Channel.CreateUnbounded<BaseEventState>();
        }

        private static readonly TimeSpan s_signalTimeout = TimeSpan.FromSeconds(10);
        private ServerFixture<StandardServer> m_fixture;
        private StandardServer m_server;
        private ServerTestServices m_services;
        private RequestHeader m_requestHeader;
        private SecureChannelContext m_channel;
        private string m_pkiRoot;
        private uint m_subscriptionId;
    }
}
