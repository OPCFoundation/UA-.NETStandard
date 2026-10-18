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
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Server.NodeManager;
using Opc.Ua.Tests;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Validates retained production through real opc.tcp data and nested/root event subscriptions.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineToolProductionSessionTests
    {
        /// <summary>
        /// Section 8.4.1 requires final job values to reach every active subscriber before dynamic deletion.
        /// Both clients intentionally use queue size one, where simply queuing a value before deletion is insufficient.
        /// </summary>
        [Test]
        public async Task TwoClientsReceiveFinalValuesAndNestedEventsBeforeJobRemovalAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            Receiver? firstReceiver = null;
            Receiver? secondReceiver = null;
            bool deliveredBeforeRemoval = false;
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc), StatusCodes.UncertainLastUsableValue);
            var program = new MachineToolProgram
            {
                Name = "MAIN.NC",
                JobIdentifier = "Job",
                Phase = MachineToolProductionPhase.Running,
                Sample = sample
            };
            var job = new MachineToolProductionJob
            {
                Identifier = "Job",
                Phase = MachineToolProductionPhase.Running,
                RunsPlanned = 1,
                RunsPlannedIsValid = true,
                Programs = [program],
                Sample = sample
            };
            var initial = new MachineToolSnapshot
            {
                ActiveProgram = program,
                Production = new MachineToolProductionObservation { Jobs = [job] }
            };
            var provider = new InMemoryMachineToolProvider(initial);
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context
                .AddMachineTool("Subscribed")
                .WithProvider(provider)
                .WithProduction(new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.DynamicProductionPlan,
                    PublishFinalObservationsAsync = async (nodes, ct) =>
                    {
                        Assert.That(nodes.Count, Is.EqualTo(1));
                        Assert.That(fixture.Context.Manager.FindPredefinedNode(nodes[0].NodeId), Is.SameAs(nodes[0]));
                        await fixture.Context.Manager.WaitForValuePublicationAsync(nodes, ct).ConfigureAwait(false);
                        deliveredBeforeRemoval = true;
                    }
                })
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Production test");
                    identity.SerialNumber = "Production-1";
                    identity.ProductInstanceUri = "urn:production-tests:1";
                }).BuildAsync().ConfigureAwait(false);
            MachineToolState machine = runtime.State;
            MachineToolProductionController production = runtime.Production;
            var children = new List<BaseInstanceState>();
            production.State.ProductionPlan!.GetChildren(fixture.Context.Context, children);
            var node = (ProductionJobState)children.Find(static child => child is ProductionJobState)!;
            Assert.That(node, Is.Not.Null);
            NodeId jobNodeId = node.NodeId;
            NodeId counterNodeId = node.RunsCompleted!.NodeId;
            string version = production.State.ProductionPlan.GetNodeVersionProperty()!.Value;

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var firstClient = new ClientFixture(telemetry);
            await using var secondClient = new ClientFixture(telemetry);
            await firstClient.LoadClientConfigurationAsync(fixture.Root, "ProductionClientA").ConfigureAwait(false);
            await secondClient.LoadClientConfigurationAsync(fixture.Root, "ProductionClientB").ConfigureAwait(false);
            using ClientSession first = await firstClient.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            using ClientSession second = await secondClient.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            firstReceiver = new Receiver(first, machine, node);
            secondReceiver = new Receiver(second, machine, node);
            await firstReceiver.SubscribeAsync().ConfigureAwait(false);
            await secondReceiver.SubscribeAsync().ConfigureAwait(false);
            WriteResponse write = await first.WriteAsync(null,
                [new WriteValue { NodeId = counterNodeId, AttributeId = Attributes.Value, Value = new DataValue(99u) }],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(write.Results[0], Is.EqualTo(StatusCodes.BadNotWritable),
                "Legacy production is observation-only, not a job-control interface.");
            program = program with { Phase = MachineToolProductionPhase.Ended, TransitionSequence = 1 };
            job = job with
            {
                Phase = MachineToolProductionPhase.Ended,
                TransitionSequence = 1,
                RunsCompleted = 1,
                Programs = [program]
            };
            var finished = initial with
            {
                Revision = 1,
                ActiveProgram = program,
                Production = initial.Production with { Jobs = [job] }
            };
            await provider.SetSnapshotAsync(finished).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            Task firstPublished = firstReceiver.WaitAsync(static receiver => receiver.HasDeletionModelChange,
                CancellationToken.None).AsTask();
            Task secondPublished = secondReceiver.WaitAsync(static receiver => receiver.HasDeletionModelChange,
                CancellationToken.None).AsTask();
            await provider.SetSnapshotAsync(finished with
            {
                Revision = 2,
                ActiveProgram = program with { Name = string.Empty, JobIdentifier = string.Empty },
                Production = finished.Production with { Jobs = [] }
            }).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            await Task.WhenAll(firstPublished, secondPublished).ConfigureAwait(false);

            Assert.That(deliveredBeforeRemoval, Is.True);
            Assert.That(firstReceiver.HasFinalObservation, Is.True);
            Assert.That(secondReceiver.HasFinalObservation, Is.True);
            Assert.That(firstReceiver.FinalValue.StatusCode, Is.EqualTo(sample.StatusCode));
            Assert.That(firstReceiver.FinalValue.SourceTimestamp, Is.EqualTo(sample.SourceTimestamp));
            Assert.That(secondReceiver.FinalValue.SourceTimestamp, Is.EqualTo(sample.SourceTimestamp));
            Assert.That(firstReceiver.RootJobEvents, Is.EqualTo(1));
            Assert.That(firstReceiver.NestedJobEvents, Is.EqualTo(1));
            Assert.That(secondReceiver.RootJobEvents, Is.EqualTo(1));
            Assert.That(secondReceiver.NestedJobEvents, Is.EqualTo(1));
            Assert.That(fixture.Context.Manager.FindPredefinedNode(jobNodeId), Is.Null);
            Assert.That(production.State.ProductionPlan.GetNodeVersionProperty()!.Value, Is.Not.EqualTo(version));
            Assert.That(production.State.ActiveProgram!.JobNodeId!.Value.IsNull, Is.True);
            Assert.That(production.State.ActiveProgram.NumberInList!.Value, Is.Zero);
            Assert.That(firstReceiver.HasDeletionModelChange, Is.True);
            ServiceResultException deleted = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await second.ReadValueAsync(counterNodeId).ConfigureAwait(false))!;
            Assert.That(deleted.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            await first.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await second.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads actual service notifications, rather than inferring delivery from a server-side callback.
        /// </summary>
        private sealed class Receiver
        {
            /// <summary>
            /// Creates a receiver for one real session.
            /// </summary>
            public Receiver(ClientSession session, MachineToolState machine, ProductionJobState job)
            {
                m_session = session;
                m_machine = machine;
                m_job = job;
                m_jobNodeId = job.NodeId;
                m_stateNodeId = job.State!.NodeId;
            }

            /// <summary>
            /// Gets whether both event routes and the final value have reached this client.
            /// </summary>
            public bool HasFinalObservation => m_hasFinalValue && RootJobEvents > 0 && NestedJobEvents > 0;

            /// <summary>
            /// Gets the final data-change value received over opc.tcp.
            /// </summary>
            public DataValue FinalValue { get; private set; }

            /// <summary>
            /// Gets the number of job transition events delivered through the root.
            /// </summary>
            public int RootJobEvents { get; private set; }

            /// <summary>
            /// Gets the number of job transition events delivered through its state-machine notifier.
            /// </summary>
            public int NestedJobEvents { get; private set; }

            /// <summary>
            /// Gets whether a model-change event named the removed job.
            /// </summary>
            public bool HasDeletionModelChange { get; private set; }

            /// <summary>
            /// Creates real event and queue-size-one data monitored items.
            /// </summary>
            public async Task SubscribeAsync()
            {
                CreateSubscriptionResponse subscription = await m_session.CreateSubscriptionAsync(
                    null, 20, 1000, 100, 0, true, 0, CancellationToken.None).ConfigureAwait(false);
                m_subscriptionId = subscription.SubscriptionId;
                NodeId jobEvent = ExpandedNodeId.ToNodeId(ObjectTypeIds.ProductionJobTransitionEventType,
                    m_session.NamespaceUris);
                ushort mt = (ushort)m_session.NamespaceUris.GetIndex(Namespaces.MachineTool);
                var filter = new EventFilter
                {
                    SelectClauses =
                    [
                        Field(Opc.Ua.ObjectTypeIds.BaseEventType, new QualifiedName("EventType")),
                        Field(Opc.Ua.ObjectTypeIds.BaseEventType, new QualifiedName("SourceNode")),
                        Field(Opc.Ua.ObjectTypeIds.BaseEventType, new QualifiedName("Time")),
                        Field(jobEvent, new QualifiedName("Identifier", mt)),
                        Field(jobEvent, new QualifiedName("RunsCompleted", mt)),
                        Field(Opc.Ua.ObjectTypeIds.TransitionEventType, new QualifiedName("ToState"),
                            new QualifiedName("Number")),
                        Field(Opc.Ua.ObjectTypeIds.GeneralModelChangeEventType, new QualifiedName("Changes"))
                    ],
                    WhereClause = new ContentFilter()
                };
                CreateMonitoredItemsResponse result = await m_session.CreateMonitoredItemsAsync(
                    null, m_subscriptionId, TimestampsToReturn.Both,
                    [
                        EventItem(m_machine.NodeId, 1, filter),
                        EventItem(m_job.State!.NodeId, 2, filter),
                        new MonitoredItemCreateRequest
                        {
                            ItemToMonitor = new ReadValueId
                            {
                                NodeId = m_job.RunsCompleted!.NodeId, AttributeId = Attributes.Value
                            },
                            MonitoringMode = MonitoringMode.Reporting,
                            RequestedParameters = new MonitoringParameters
                            {
                                ClientHandle = 3, SamplingInterval = 0, QueueSize = 1, DiscardOldest = true
                            }
                        }
                    ], CancellationToken.None).ConfigureAwait(false);
                foreach (MonitoredItemCreateResult item in result.Results)
                {
                    Assert.That(StatusCode.IsGood(item.StatusCode), Is.True, item.StatusCode.ToString());
                }
            }

            /// <summary>
            /// Waits a bounded number of publish responses for a concrete received observation.
            /// </summary>
            public async ValueTask WaitAsync(Func<Receiver, bool> predicate, CancellationToken cancellationToken)
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (predicate(this))
                    {
                        return;
                    }
                    PublishResponse response;
                    try
                    {
                        response = await m_session.PublishWithTimeoutAsync(m_acknowledgements, 1000)
                            .ConfigureAwait(false);
                    }
                    catch (ServiceResultException exception)
                        when (exception.StatusCode == StatusCodes.BadRequestTimeout)
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
                                if (value.ClientHandle == 3 &&
                                    value.Value.WrappedValue.TryGetValue(out uint count) && count == 1)
                                {
                                    FinalValue = value.Value;
                                    m_hasFinalValue = true;
                                }
                            }
                        }
                        if (data.TryGetValue(out EventNotificationList? events) && events != null)
                        {
                            foreach (EventFieldList e in events.Events)
                            {
                                Receive(e);
                            }
                        }
                    }
                }
                Assert.Fail(
                    "The client did not receive the required production notification within 30 publish attempts.");
            }

            /// <summary>
            /// Checks the independent expected wire values of a job transition and model change.
            /// </summary>
            private void Receive(EventFieldList e)
            {
                if (e.EventFields[1].TryGetValue(out NodeId source) && source == m_stateNodeId)
                {
                    Assert.That(e.EventFields[3].TryGetValue(out string? identifier), Is.True);
                    Assert.That(identifier, Is.EqualTo("Job"));
                    Assert.That(e.EventFields[4].TryGetValue(out uint completed), Is.True);
                    Assert.That(completed, Is.EqualTo(1));
                    Assert.That(e.EventFields[5].TryGetValue(out uint state), Is.True);
                    Assert.That(state, Is.EqualTo(2), "Ended has standard StateNumber 2.");
                    if (e.ClientHandle == 1)
                    {
                        RootJobEvents++;
                    }
                    else if (e.ClientHandle == 2)
                    {
                        NestedJobEvents++;
                    }
                }
                if (e.EventFields[6].TryGetValue(
                    out ArrayOf<ModelChangeStructureDataType> changes, m_session.MessageContext))
                {
                    foreach (ModelChangeStructureDataType change in changes)
                    {
                        HasDeletionModelChange |= change.Affected == m_jobNodeId && (change.Verb & 2) != 0;
                    }
                }
            }

            /// <summary>
            /// Selects a standard or companion event field.
            /// </summary>
            private static SimpleAttributeOperand Field(NodeId type, params QualifiedName[] path) =>
                new()
                {
                    TypeDefinitionId = type, BrowsePath = new ArrayOf<QualifiedName>(path),
                    AttributeId = Attributes.Value
                };

            /// <summary>
            /// Creates an event request for a particular notifier.
            /// </summary>
            private static MonitoredItemCreateRequest EventItem(NodeId source, uint handle, EventFilter filter) =>
                new()
                {
                    ItemToMonitor = new ReadValueId { NodeId = source, AttributeId = Attributes.EventNotifier },
                    MonitoringMode = MonitoringMode.Reporting,
                    RequestedParameters = new MonitoringParameters
                    {
                        ClientHandle = handle,
                        QueueSize = 100,
                        DiscardOldest = true,
                        Filter = new ExtensionObject(filter)
                    }
                };

            /// <summary>The actual client session.</summary>
            private readonly ClientSession m_session;

            /// <summary>The root notifier.</summary>
            private readonly MachineToolState m_machine;

            /// <summary>The modeled job and nested notifier.</summary>
            private readonly ProductionJobState m_job;

            /// <summary>The job identity retained after its generated node is disposed.</summary>
            private readonly NodeId m_jobNodeId;

            /// <summary>The state-machine identity retained after its generated node is disposed.</summary>
            private readonly NodeId m_stateNodeId;

            /// <summary>The raw service subscription, not a simulated notification sink.</summary>
            private uint m_subscriptionId;

            /// <summary>The next publish acknowledgements.</summary>
            private ArrayOf<SubscriptionAcknowledgement> m_acknowledgements;

            /// <summary>Whether the final value was delivered before any deletion status.</summary>
            private bool m_hasFinalValue;
        }
    }
}
