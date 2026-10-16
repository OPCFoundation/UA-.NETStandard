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
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Server.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises dynamic tool ownership, rollback, disposal and actual server-publication delivery.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineToolEquipmentLifetimeTests
    {
        /// <summary>
        /// Reordering does not replace nodes; removal delivers final values and drains a real owned callback.
        /// </summary>
        [Test]
        public async Task DynamicMultiToolRemovalPublishesFinalSamplesAndDrainsResourcesAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolTool first = Tool("First", 1);
            MachineToolTool child = Tool("Insert", 2) with
            {
                Control = new MachineToolToolControl(22, ToolManagement.GroupBased, false,
                    ToolLocked.CapabilityUnavailable)
                {
                    ControlIdentifier2 = 2,
                    PlannedForOperating = true,
                    LastUsage = MachineToolEquipmentTests.Sample(
                        Variant.From((DateTimeUtc)new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)), first.Sample)
                }
            };
            var multi = new MachineToolTool
            {
                Key = "Holder",
                Kind = MachineToolToolKind.MultiTool,
                Name = "Holder",
                Tools = [child],
                Location = new MachineToolLocation("Revolver", 1),
                Sample = first.Sample
            };
            var initial = new MachineToolSnapshot { Equipment = new() { Tools = [first, multi] } };
            var provider = new InMemoryMachineToolProvider(initial);
            var resources = new Dictionary<string, EquipmentResource>(StringComparer.Ordinal);
            await using MachineToolRuntime<MachineToolState> runtime = await Builder(fixture, provider, "Dynamic")
                .WithEquipment(new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools | MachineToolFeatures.DynamicTools,
                    CreateToolAsync = (context, parent, name, _, _) =>
                    {
                        var resource = new EquipmentResource();
                        resources.Add(name.Name!, resource);
                        return new ValueTask<MachineToolEquipmentNode<ToolState>>(
                            new MachineToolEquipmentNode<ToolState>(
                                context.CreateInstanceOfToolType(parent, name), resource));
                    }
                }).BuildAsync().ConfigureAwait(false);
            ToolListState list = runtime.Equipment.State.Tools!;
            var holder = (MultiToolState)list.FindChild(fixture.Context.Context,
                new QualifiedName("Holder", fixture.Context.InstanceNamespaceIndex))!;
            var insert = (ToolState)holder.FindChild(fixture.Context.Context,
                new QualifiedName("Insert", fixture.Context.InstanceNamespaceIndex))!;
            NodeId originalInsert = insert.NodeId;
            string before = list.GetNodeVersionProperty()!.Value;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var client = new ClientFixture(telemetry);
            await client.LoadClientConfigurationAsync(fixture.Root, "DynamicToolsClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using var facade = new MachineToolClient(session, telemetry);
            MachineToolEquipmentReadSnapshot inventory = await facade.ReadEquipmentAsync(runtime.NodeId)
                .ConfigureAwait(false);
            Assert.That(inventory.Tools[1].Tools[0].NodeId, Is.EqualTo(originalInsert));
            Assert.That(inventory.Tools[1].Tools[0].ControlIdentifier2.WrappedValue.TryGetValue(out uint second),
                Is.True);
            Assert.That(second, Is.EqualTo(2));
            Assert.That(inventory.Tools[1].Tools[0].LocationNodeId.IsNull, Is.True);
            Assert.That(inventory.Tools[1].Tools[0].LastUsage.IsNull, Is.False);
            await using var receiver = new MachineToolEquipmentReceiver(session);
            await receiver.SubscribeAsync(list.NodeId, insert.ControlIdentifier1!.NodeId, runtime.NodeId)
                .ConfigureAwait(false);
            await provider.SetSnapshotAsync(initial with { Equipment = new() { Tools = [multi, first] } })
                .ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            await receiver.WaitAsync(r => r.NestedModelEvents > 0 && r.RootModelEvents > 0).ConfigureAwait(false);
            Assert.That(receiver.RootModelEvents, Is.EqualTo(1));
            Assert.That(receiver.NestedModelEvents, Is.EqualTo(1));
            Assert.That(list.GetNodeVersionProperty()!.Value, Is.Not.EqualTo(before));
            Assert.That(insert.NodeId, Is.EqualTo(originalInsert));
            Assert.That(resources["Insert"].Disposals, Is.Zero);
            inventory = await facade.ReadEquipmentAsync(runtime.NodeId).ConfigureAwait(false);
            Assert.That(inventory.Tools[0].NodeId, Is.EqualTo(holder.NodeId));
            Assert.That(inventory.Tools[1].BrowseName.Name, Is.EqualTo("First"));
            child = child with
            {
                Control = child.Control! with
                {
                    ControlIdentifier1 = 99,
                    Locked = true,
                    ReasonForLocking = ToolLocked.ByOperator
                }
            };
            multi = multi with { Tools = [child] };
            await provider.SetSnapshotAsync(initial with { Equipment = new() { Tools = [multi, first] } })
                .ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            Task delivery = receiver.WaitAsync(r => r.Deleted.Contains(holder.NodeId)).AsTask();
            await provider.SetSnapshotAsync(initial with { Equipment = new() { Tools = [first] } })
                .ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            await delivery.ConfigureAwait(false);
            Assert.That(receiver.Values.Exists(value =>
                value.WrappedValue.TryGetValue(out uint count) && count == 99), Is.True);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(originalInsert), Is.Null);
            Assert.That(resources["Insert"].Disposals, Is.EqualTo(1));
            Assert.That(resources["Insert"].WorkCompleted, Is.True);
            Assert.That(resources["First"].Disposals, Is.Zero);
            Assert.That(resources["First"].WorkCompleted, Is.False);
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// A late factory rejection rolls back all accepted ownership and preserves monitoring and production too.
        /// </summary>
        [Test]
        public async Task FailedFactoryRollsBackEquipmentAndWholeMachineObservationAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolTool first = Tool("First", 1);
            var initial = new MachineToolSnapshot { Equipment = new() { Tools = [first] } };
            var provider = new InMemoryMachineToolProvider(initial);
            EquipmentResource? staged = null;
            ToolState? allocated = null;
            await using MachineToolRuntime<MachineToolState> runtime = await Builder(fixture, provider, "Rollback")
                .WithEquipment(new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools | MachineToolFeatures.DynamicTools,
                    CreateToolAsync = (context, parent, name, _, _) =>
                    {
                        if (name.Name == "Reject")
                        {
                            throw new InvalidOperationException("Factory rejected the device.");
                        }
                        ToolState node = context.CreateInstanceOfToolType(parent, name);
                        if (name.Name == "New")
                        {
                            allocated = node;
                            staged = new EquipmentResource();
                            return new ValueTask<MachineToolEquipmentNode<ToolState>>(
                                new MachineToolEquipmentNode<ToolState>(node, staged));
                        }
                        return new ValueTask<MachineToolEquipmentNode<ToolState>>(
                            new MachineToolEquipmentNode<ToolState>(node));
                    }
                }).BuildAsync().ConfigureAwait(false);
            await provider.SetSnapshotAsync(initial with
            {
                Operation = initial.Operation with { Mode = MachineOperationMode.Service },
                ActiveProgram = initial.ActiveProgram with { Name = "NotCommitted" },
                Equipment = new() { Tools = [first, Tool("New", 2), Tool("Reject", 3)] }
            }).ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.RefreshAsync().ConfigureAwait(false));
            Assert.That(staged, Is.Not.Null);
            Assert.That(staged!.WorkCompleted, Is.True);
            Assert.That(staged.Disposals, Is.EqualTo(1));
            Assert.That(fixture.Context.Manager.FindPredefinedNode(allocated!.NodeId), Is.Null);
            Assert.That(runtime.Equipment.Observation!.Revision, Is.Zero);
            Assert.That(runtime.Production.Observation!.Revision, Is.Zero);
            Assert.That(runtime.State.Production!.ActiveProgram!.Name!.Value, Is.EqualTo(string.Empty));
            Assert.That(runtime.State.Monitoring!.MachineTool!.OperationMode!.Value,
                Is.EqualTo(initial.Operation.Mode));
            await provider.SetSnapshotAsync(initial with { Equipment = new() { Tools = [first, Tool("Accepted", 4)] } })
                .ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            Assert.That(runtime.Equipment.Observation!.Equipment!.Tools.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Teardown cancels admitted work, awaits a late factory, then rolls back its newly transferred resource.
        /// </summary>
        [Test]
        public async Task DisposalDrainsLateFactoryAndRefusesReentrantUpdatesAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolTool first = Tool("First", 1);
            var initial = new MachineToolSnapshot { Equipment = new() { Tools = [first] } };
            var provider = new InMemoryMachineToolProvider(initial);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EquipmentResource? lateResource = null;
            MachineToolRuntime<MachineToolState>? runtime = null;
            runtime = await Builder(fixture, provider, "Late")
                .WithEquipment(new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools | MachineToolFeatures.DynamicTools,
                    CreateToolAsync = async (context, parent, name, _, _) =>
                    {
                        if (name.Name == "Late")
                        {
                            Assert.ThrowsAsync<ServiceResultException>(async () =>
                                await runtime!.RefreshAsync().ConfigureAwait(false));
                            Assert.ThrowsAsync<ServiceResultException>(async () =>
                                await runtime!.DisposeAsync().ConfigureAwait(false));
                            entered.TrySetResult(true);
                            await release.Task.ConfigureAwait(false);
                            lateResource = new EquipmentResource();
                        }
                        return new MachineToolEquipmentNode<ToolState>(
                            context.CreateInstanceOfToolType(parent, name), lateResource);
                    }
                }).BuildAsync().ConfigureAwait(false);
            await using (runtime.ConfigureAwait(false))
            {
                await provider.SetSnapshotAsync(initial with { Equipment = new() { Tools = [first, Tool("Late", 2)] } })
                    .ConfigureAwait(false);
                Task refresh = runtime.RefreshAsync().AsTask();
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Task disposal = runtime.DisposeAsync().AsTask();
                Assert.That(disposal.IsCompleted, Is.False);
                release.TrySetResult(true);
                Assert.That(async () => await refresh.ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>());
                await disposal.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(lateResource!.Disposals, Is.EqualTo(1));
                Assert.That(lateResource.WorkCompleted, Is.True);
                Assert.That(runtime.Equipment.Observation!.Revision, Is.Zero);
                Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await runtime.RefreshAsync().ConfigureAwait(false));
            }
        }

        /// <summary>
        /// A failed event callback retains its exact event identity and does not roll back accepted samples.
        /// </summary>
        [Test]
        public async Task RetryingPublicationRetainsEquipmentEventIdentityAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var initial = new MachineToolSnapshot { Equipment = new() { Tools = [Tool("First", 1)] } };
            var provider = new InMemoryMachineToolProvider(initial);
            await using MachineToolRuntime<MachineToolState> runtime = await Builder(fixture, provider, "Retry")
                .WithEquipment(new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools | MachineToolFeatures.DynamicTools
                }).BuildAsync().ConfigureAwait(false);
            ByteString eventId = default;
            int attempts = 0;
            runtime.Equipment.State.Tools!.OnReportEventAsync = (_, _, e, _) =>
            {
                if (e is GeneralModelChangeEventState change)
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        eventId = change.EventId!.Value;
                        throw new InvalidOperationException("Notification sink failed.");
                    }
                    Assert.That(change.EventId!.Value, Is.EqualTo(eventId));
                }
                return default;
            };
            await provider.SetSnapshotAsync(initial with
            {
                Equipment = new() { Tools = [Tool("First", 1), Tool("New", 2)] }
            }).ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.RefreshAsync().ConfigureAwait(false));
            Assert.That(runtime.Equipment.Observation!.Equipment!.Tools.Count, Is.EqualTo(2));
            string version = runtime.Equipment.State.Tools.GetNodeVersionProperty()!.Value;
            await runtime.RetryPublicationAsync().ConfigureAwait(false);
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(runtime.Equipment.State.Tools.GetNodeVersionProperty()!.Value, Is.EqualTo(version));
        }

        /// <summary>
        /// An owned teardown callback cannot await its own enclosing runtime's disposal and deadlock the drain.
        /// </summary>
        [Test]
        public async Task ResourceDisposalCallbackRejectsSelfDisposalWithoutDeadlockingAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolRuntime<MachineToolState>? runtime = null;
            bool rejected = false;
            var resource = new EquipmentResource(onDispose: () =>
            {
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await runtime!.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
                rejected = true;
                return default;
            });
            var provider = new InMemoryMachineToolProvider(new MachineToolSnapshot
            {
                Equipment = new() { Tools = [Tool("Tool", 1)] }
            });
            runtime = await Builder(fixture, provider, "DisposalCallback")
                .WithEquipment(new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools,
                    CreateToolAsync = (context, parent, name, _, _) =>
                        new ValueTask<MachineToolEquipmentNode<ToolState>>(
                            new MachineToolEquipmentNode<ToolState>(
                                context.CreateInstanceOfToolType(parent, name), resource))
                }).BuildAsync().ConfigureAwait(false);
            await using (runtime.ConfigureAwait(false))
            {
                await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(rejected, Is.True);
                Assert.That(resource.Disposals, Is.EqualTo(1));
                Assert.That(resource.WorkCompleted, Is.True);
            }
        }

        /// <summary>
        /// Creates a minimal explicit single-tool observation without optional fabricated values.
        /// </summary>
        internal static MachineToolTool Tool(string key, uint identifier) => new()
        {
            Key = key,
            Name = key,
            Control = new MachineToolToolControl(identifier, ToolManagement.NumberBased, false,
                ToolLocked.CapabilityUnavailable),
            Sample = new MachineToolSampleInfo(
                new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc), StatusCodes.UncertainLastUsableValue)
        };

        /// <summary>
        /// Creates a common public builder with an explicit provider and required nameplate.
        /// </summary>
        internal static MachineToolBuilder<MachineToolState> Builder(
            MachineToolRuntimeFixture fixture, IMachineToolProvider provider, string name) =>
            fixture.Context.AddMachineTool(name).WithProvider(provider).WithIdentification(identity =>
            {
                identity.Manufacturer = new LocalizedText("Equipment lifetime tests");
                identity.SerialNumber = name;
                identity.ProductInstanceUri = "urn:equipment-tests:" + name;
            });
    }

    /// <summary>
    /// An actual pending asynchronous observation resource which must be canceled and joined on removal.
    /// </summary>
    internal sealed class EquipmentResource : IAsyncDisposable
    {
        /// <summary>
        /// Starts an observation wait whose lifetime is owned by the factory transfer.
        /// </summary>
        internal EquipmentResource(bool failDisposal = false, Func<ValueTask>? onDispose = null)
        {
            m_work = ObserveAsync();
            m_failDisposal = failDisposal;
            m_onDispose = onDispose;
        }

        /// <summary>
        /// Gets the number of resource releases.
        /// </summary>
        internal int Disposals { get; private set; }

        /// <summary>
        /// Gets whether the asynchronous observation has fully drained.
        /// </summary>
        internal bool WorkCompleted => m_work.IsCompleted;

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Disposals++;
            m_stop.Cancel();
            await m_work.ConfigureAwait(false);
            m_stop.Dispose();
            if (m_onDispose != null)
            {
                await m_onDispose().ConfigureAwait(false);
            }
            if (m_failDisposal)
            {
                throw new InvalidOperationException("Independent observer disposal failed.");
            }
        }

        /// <summary>
        /// Owns a real pending asynchronous operation until cancellation is observed.
        /// </summary>
        private async Task ObserveAsync()
        {
            try
            {
                await Task.Delay(Timeout.Infinite, m_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (m_stop.IsCancellationRequested)
            {
            }
        }

        /// <summary>The observer's shutdown token.</summary>
        private readonly CancellationTokenSource m_stop = new();

        /// <summary>The active observation operation.</summary>
        private readonly Task m_work;

        /// <summary>Whether this resource injects an independent teardown failure.</summary>
        private readonly bool m_failDisposal;

        /// <summary>An optional resource-owned teardown callback.</summary>
        private readonly Func<ValueTask>? m_onDispose;
    }

    /// <summary>
    /// Receives actual opc.tcp Publish responses for queue-size-one values and standard model-change events.
    /// </summary>
    internal sealed class MachineToolEquipmentReceiver : IAsyncDisposable
    {
        /// <summary>
        /// Captures an application-owned real session.
        /// </summary>
        internal MachineToolEquipmentReceiver(ISession session)
        {
            m_session = session;
        }

        /// <summary>
        /// Gets delivered value samples in arrival order.
        /// </summary>
        internal List<DataValue> Values { get; } = [];

        /// <summary>
        /// Gets actual identifiers named by standard NodeDeleted model changes.
        /// </summary>
        internal HashSet<NodeId> Deleted { get; } = [];

        /// <summary>
        /// Gets the number of model-change events received.
        /// </summary>
        internal int ModelEvents { get; private set; }

        /// <summary>
        /// Gets model changes delivered through the root notifier.
        /// </summary>
        internal int RootModelEvents { get; private set; }

        /// <summary>
        /// Gets model changes delivered through the list's own notifier.
        /// </summary>
        internal int NestedModelEvents { get; private set; }

        /// <summary>
        /// Subscribes to the actual nested list notifier and one original value.
        /// </summary>
        internal async ValueTask SubscribeAsync(NodeId list, NodeId variable, NodeId root = default)
        {
            CreateSubscriptionResponse subscription = await m_session.CreateSubscriptionAsync(
                null, 20, 1000, 100, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
            m_subscription = subscription.SubscriptionId;
            var filter = new EventFilter
            {
                SelectClauses =
                [
                    new SimpleAttributeOperand
                    {
                        TypeDefinitionId = Opc.Ua.ObjectTypeIds.GeneralModelChangeEventType,
                        BrowsePath = [new QualifiedName(Opc.Ua.BrowseNames.Changes)], AttributeId = Attributes.Value
                    }
                ]
            };
            var items = new List<MonitoredItemCreateRequest>
            {
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId { NodeId = list, AttributeId = Attributes.EventNotifier },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1, QueueSize = 100, DiscardOldest = true,
                            Filter = new ExtensionObject(filter)
                        }
                    },
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId { NodeId = variable, AttributeId = Attributes.Value },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 2, SamplingInterval = 0, QueueSize = 1, DiscardOldest = true
                        }
                    }
            };
            if (!root.IsNull)
            {
                items.Add(new MonitoredItemCreateRequest
                {
                    ItemToMonitor = new ReadValueId { NodeId = root, AttributeId = Attributes.EventNotifier },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = 3, QueueSize = 100, DiscardOldest = true, Filter = new ExtensionObject(filter)
                    }
                });
            }
            CreateMonitoredItemsResponse result = await m_session.CreateMonitoredItemsAsync(
                null, m_subscription, TimestampsToReturn.Both, items.ToArrayOf(), CancellationToken.None)
                .ConfigureAwait(false);
            foreach (MonitoredItemCreateResult item in result.Results)
            {
                Assert.That(StatusCode.IsGood(item.StatusCode), Is.True, item.StatusCode.ToString());
            }
        }

        /// <summary>
        /// Waits for actual observations with a fixed service-request and retry budget.
        /// </summary>
        internal async ValueTask WaitAsync(Func<MachineToolEquipmentReceiver, bool> predicate)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                if (predicate(this))
                {
                    return;
                }
                PublishResponse response;
                try
                {
                    response = await m_session.PublishWithTimeoutAsync(m_acknowledgements, 1000).ConfigureAwait(false);
                }
                catch (ServiceResultException error) when (error.StatusCode == StatusCodes.BadRequestTimeout)
                {
                    continue;
                }
                m_acknowledgements =
                [
                    new SubscriptionAcknowledgement
                    {
                        SubscriptionId = response.SubscriptionId,
                        SequenceNumber = response.NotificationMessage.SequenceNumber
                    }
                ];
                foreach (ExtensionObject data in response.NotificationMessage.NotificationData)
                {
                    if (data.TryGetValue(out DataChangeNotification? values) && values != null)
                    {
                        foreach (MonitoredItemNotification value in values.MonitoredItems)
                        {
                            Values.Add(value.Value);
                        }
                    }
                    if (data.TryGetValue(out EventNotificationList? events) && events != null)
                    {
                        foreach (EventFieldList e in events.Events)
                        {
                            if (e.EventFields[0].TryGetValue(
                                out ArrayOf<ModelChangeStructureDataType> changes, m_session.MessageContext))
                            {
                                ModelEvents++;
                                if (e.ClientHandle == 3)
                                {
                                    RootModelEvents++;
                                }
                                else
                                {
                                    NestedModelEvents++;
                                }
                                foreach (ModelChangeStructureDataType change in changes)
                                {
                                    if ((change.Verb & (byte)ModelChangeVerbs.NodeDeleted) != 0)
                                    {
                                        Deleted.Add(change.Affected);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            Assert.Fail("The bounded Publish workflow did not receive the required equipment observations.");
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_subscription != 0 && m_session.Connected)
            {
                await m_session.DeleteSubscriptionsAsync(null, [m_subscription], CancellationToken.None)
                    .ConfigureAwait(false);
                m_subscription = 0;
            }
        }

        /// <summary>The application-owned real session.</summary>
        private readonly ISession m_session;

        /// <summary>The active server subscription.</summary>
        private uint m_subscription;

        /// <summary>The last received sequence to acknowledge.</summary>
        private ArrayOf<SubscriptionAcknowledgement> m_acknowledgements;
    }
}
