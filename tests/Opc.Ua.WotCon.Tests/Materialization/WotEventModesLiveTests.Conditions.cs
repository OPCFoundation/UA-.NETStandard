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

#if NET8_0_OR_GREATER
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeNotificationOnlyConditionsKeepSeparateBranchesAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            await source.InstallAsync(ConditionSourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var factory = new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session));
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle, factory);
            WotProjectionHandle handle = await host.AddAsync(
                ConditionProjection(mode, source.EndpointUrl), ct).ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeConditionEvents events = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct).ConfigureAwait(false);
                var conditions = new ExpandedNodeId[4];
                var branches = new ExpandedNodeId[4];
                var eventIds = new ByteString[4];
                for (int index = 0; index < 4; index++)
                {
                    string conditionName = index < 2 ? "ConditionA" : "ConditionB";
                    string pumpName = index < 2 ? "PumpA" : "PumpB";
                    bool retainedBranch = index % 2 != 0;
                    bool enabled = index is 0 or 3;
                    ByteString originalId = ByteString.From(new byte[] { 0xD3, 0xC0, 0x01, (byte)index });
                    DateTimeUtc boundary = DateTimeUtc.Now;
                    await ReportConditionAsync(
                        source, conditionName, pumpName, retainedBranch, enabled, originalId, ct)
                        .ConfigureAwait(false);
                    ArrayOf<Variant> fields = await events.ReadAsync(ct).ConfigureAwait(false);
                    Assert.That(fields.Count, Is.EqualTo(13));
                    Assert.That(fields[0].TryGetValue(out ByteString eventId), Is.True);
                    Assert.That(fields[1].TryGetValue(out NodeId eventType), Is.True);
                    Assert.That(fields[2].TryGetValue(out NodeId sourceNode), Is.True);
                    Assert.That(fields[4].TryGetValue(out DateTimeUtc time), Is.True);
                    Assert.That(fields[5].TryGetValue(out DateTimeUtc received), Is.True);
                    Assert.That(fields[8].TryGetValue(out NodeId conditionId), Is.True);
                    Assert.That(fields[9].TryGetValue(out NodeId branchId), Is.True);
                    Assert.That(fields[10].TryGetValue(out string? actualName), Is.True);
                    Assert.That(fields[11].TryGetValue(out bool actualEnabled), Is.True);
                    Assert.That(fields[12].TryGetValue(out bool retain), Is.True);
                    ExpandedNodeId portableCondition = NodeId.ToExpandedNodeId(
                        conditionId, destination.Session.NamespaceUris);
                    ExpandedNodeId portableBranch = NodeId.ToExpandedNodeId(
                        branchId, destination.Session.NamespaceUris);
                    bool transparent = mode == "transparent-forwarding";
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(eventId.IsEmpty, Is.False);
                        Assert.That(eventId == originalId, Is.EqualTo(transparent));
                        Assert.That(NodeId.ToExpandedNodeId(eventType, destination.Session.NamespaceUris),
                            Is.EqualTo(new ExpandedNodeId("ConditionEventType",
                                transparent ? SourceNamespace : LocalNamespace)));
                        Assert.That(NodeId.ToExpandedNodeId(sourceNode, destination.Session.NamespaceUris),
                            Is.EqualTo(transparent
                                ? new ExpandedNodeId(pumpName, SourceNamespace)
                                : new ExpandedNodeId("Owner", LocalNamespace)));
                        Assert.That(conditionId.IsNull, Is.False);
                        Assert.That(portableCondition == new ExpandedNodeId(conditionName, SourceNamespace),
                            Is.EqualTo(transparent));
                        Assert.That(branchId.IsNull, Is.EqualTo(!retainedBranch));
                        if (transparent && retainedBranch)
                        {
                            Assert.That(portableBranch, Is.EqualTo(new ExpandedNodeId("Retained", SourceNamespace)));
                        }
                        Assert.That(actualName, Is.EqualTo(conditionName));
                        Assert.That(actualEnabled, Is.EqualTo(enabled));
                        Assert.That(retain, Is.True);
                        Assert.That(time, Is.EqualTo(SourceTime));
                        Assert.That(received, Is.GreaterThanOrEqualTo(boundary));
                        Assert.That(received, Is.Not.EqualTo(SourceReceiveTime));
                    }
                    conditions[index] = portableCondition;
                    branches[index] = portableBranch;
                    eventIds[index] = eventId;
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(conditions[0], Is.EqualTo(conditions[1]));
                    Assert.That(conditions[2], Is.EqualTo(conditions[3]));
                    Assert.That(conditions[0], Is.Not.EqualTo(conditions[2]));
                    Assert.That(eventIds, Is.Unique);
                    if (mode == "local-re-emission")
                    {
                        Assert.That(branches[1], Is.Not.EqualTo(branches[3]));
                    }
                }
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static UANodeSet ConditionSourceNodes()
        {
            return new UANodeSet
            {
                NamespaceUris = [SourceNamespace],
                Models = [new ModelTableEntry { ModelUri = SourceNamespace, Version = "1.0.0" }],
                Items =
                [
                    Object("ns=1;s=PumpA", "1:PumpA"),
                    Object("ns=1;s=PumpB", "1:PumpB"),
                    ConditionEventType(1, Ua.ObjectTypeIds.ConditionType.ToString())
                ]
            };
        }

        private static UAObjectType ConditionEventType(int index, string superType)
        {
            return new UAObjectType
            {
                NodeId = $"ns={index};s=ConditionEventType",
                BrowseName = $"{index}:ConditionEventType",
                References =
                [
                    new Reference { ReferenceType = "i=45", IsForward = false, Value = superType }
                ]
            };
        }

        private static WotProjectionDocument ConditionProjection(
            string mode, string endpoint, WotEventSelection? selection = null, bool useDefaultMode = false,
            NodeId conditionType = default)
        {
            if (conditionType.IsNull)
            {
                conditionType = Ua.ObjectTypeIds.ConditionType;
            }
            bool transparent = mode == "transparent-forwarding";
            ExpandedNodeId type = new("ConditionEventType", transparent ? SourceNamespace : LocalNamespace);
            using JsonDocument definition = JsonDocument.Parse(useDefaultMode
                ? $$"""{"uav:conditionTypeId":"{{conditionType}}"}"""
                : $$"""{"uav:eventIdentityMode":"{{mode}}","uav:conditionTypeId":"{{conditionType}}"}""");
            WotProjectedAffordance declaration = WotProjectedAffordance.FromConverted(new WotConvertedAffordance(
                Wot.WotAffordanceKind.Event, "condition", "/events/condition", type,
                new ExpandedNodeId("Owner", LocalNamespace), definition.RootElement));
            var form = new WotCompiledForm(
                new WotBindingIdentity("opc.opcua", "10101", "urn:opcfoundation:wot:binding:opcua"),
                Opc.Ua.WotCon.Bindings.WotAffordanceKind.Event, "condition", "/events/condition/forms/0",
                WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent",
                new WotEndpointDescriptor("opc.tcp", null, -1, endpoint),
                new WotAddressingDescriptor("i=2253"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent", "Monitor"),
                new WotPayloadDescriptor("application/opcua+uabinary", "opcua"), [], true,
                null, selection ?? s_conditionSelection, null);
            var sourceTypes = new UANodeSet
            {
                NamespaceUris = [SourceNamespace],
                Models = [new ModelTableEntry { ModelUri = SourceNamespace, Version = "1.0.0" }],
                Items = [ConditionEventType(1, conditionType.ToString())]
            };
            UAObject owner = Object("ns=1;s=Owner", "1:Owner");
            owner.References =
            [
                .. owner.References!,
                new Reference
                {
                    ReferenceType = Ua.ReferenceTypeIds.GeneratesEvent.ToString(),
                    Value = transparent ? "ns=2;s=ConditionEventType" : "ns=1;s=ConditionEventType"
                }
            ];
            var local = new UANodeSet
            {
                NamespaceUris = [LocalNamespace, SourceNamespace],
                Models =
                [
                    new ModelTableEntry
                    {
                        ModelUri = LocalNamespace,
                        Version = "1.0.0",
                        RequiredModel = [new ModelTableEntry { ModelUri = SourceNamespace, Version = "1.0.0" }]
                    }
                ],
                Items = transparent
                    ? [owner]
                    : [owner, ConditionEventType(1, "ns=2;s=ConditionEventType")]
            };
            WotBindingPlan plan = new WotBindingPlan("urn:wot:d3:conditions", [], [form], [], [])
                .WithProjectedAffordances([declaration]);
            return new WotProjectionDocument(
                "d3-condition-branches",
                [
                    new WotProjectionSource("source-condition-types", [SourceNamespace], Serialize(sourceTypes)),
                    new WotProjectionSource("local-condition-owner", [LocalNamespace], Serialize(local))
                ],
                [plan]);
        }

        private static async Task ReportConditionAsync(
            NativeEndpoint endpoint,
            string conditionName,
            string pumpName,
            bool retainedBranch,
            bool enabled,
            ByteString eventId,
            CancellationToken ct,
            Action<ConditionState>? configure = null)
        {
            var context = endpoint.Server.CurrentInstance.DefaultSystemContext;
            NodeId conditionId = ExpandedNodeId.Parse(
                "nsu=" + SourceNamespace + ";s=" + conditionName, context.NamespaceUris);
            NodeId eventTypeId = ExpandedNodeId.Parse(
                "nsu=" + SourceNamespace + ";s=ConditionEventType", context.NamespaceUris);
            ConditionState state = context.TypeTable.IsTypeOf(eventTypeId, Ua.ObjectTypeIds.AcknowledgeableConditionType)
                ? new AcknowledgeableConditionState(null) : new ConditionState(null);
            state.Create(context, conditionId, new QualifiedName(conditionName, conditionId.NamespaceIndex),
                new LocalizedText(conditionName), assignNodeIds: false);
            state.TypeDefinitionId = eventTypeId;
            state.EventId!.Value = eventId;
            state.EventType!.Value = state.TypeDefinitionId;
            state.SourceNode!.Value = ExpandedNodeId.Parse(
                "nsu=" + SourceNamespace + ";s=" + pumpName, context.NamespaceUris);
            state.SourceName!.Value = pumpName;
            state.Time!.Value = SourceTime;
            state.ReceiveTime!.Value = SourceReceiveTime;
            state.Message!.Value = new LocalizedText("D3 Condition occurrence");
            state.Severity!.Value = 412;
            state.ConditionName!.Value = conditionName;
            state.BranchId!.Value = retainedBranch
                ? ExpandedNodeId.Parse("nsu=" + SourceNamespace + ";s=Retained", context.NamespaceUris)
                : NodeId.Null;
            state.EnabledState!.Id!.Value = enabled;
            state.Retain!.Value = true;
            if (state is AcknowledgeableConditionState acknowledgeable)
            {
                acknowledgeable.AckedState!.Value = new LocalizedText("Unacknowledged");
                acknowledgeable.AckedState.Id!.Value = false;
            }
            configure?.Invoke(state);
            await endpoint.Server.CurrentInstance.ReportEventAsync(context, state, ct).ConfigureAwait(false);
        }

        private sealed class NativeConditionEvents(ISession session, Subscription subscription) : IAsyncDisposable
        {
            public static async Task<NativeConditionEvents> OpenAsync(
                ISession session, CancellationToken ct, NodeId eventNotifier = default,
                WotEventSelection? selection = null)
            {
                selection ??= s_conditionSelection;
                var subscription = new Subscription(session.DefaultSubscription)
                {
                    PublishingInterval = 20,
                    PublishingEnabled = true
                };
                var events = new NativeConditionEvents(session, subscription);
                session.AddSubscription(subscription);
                try
                {
                    await subscription.CreateAsync(ct).ConfigureAwait(false);
                    var filter = new EventFilter
                    {
                        SelectClauses = selection.Clauses.ConvertAll(clause =>
                            new SimpleAttributeOperand
                            {
                                TypeDefinitionId = ExpandedNodeId.Parse(clause.TypeDefinitionId, session.NamespaceUris),
                                AttributeId = clause.IsConditionIdSelection ? Attributes.NodeId : Attributes.Value,
                                BrowsePath = clause.PathElements.ConvertAll(QualifiedName.From)
                            })
                    };
                    var item = new MonitoredItem(subscription.DefaultItem)
                    {
                        StartNodeId = eventNotifier.IsNull ? Ua.ObjectIds.Server : eventNotifier,
                        AttributeId = Attributes.EventNotifier,
                        Filter = filter,
                        QueueSize = 64,
                        DiscardOldest = false
                    };
                    item.Notification += (_, notification) =>
                    {
                        if (notification.NotificationValue is EventFieldList fields &&
                            fields.EventFields.Count == selection.Clauses.Count &&
                            fields.EventFields[6].TryGetValue(out LocalizedText message) &&
                            message.Text == "D3 Condition occurrence" &&
                            !events.m_events.Writer.TryWrite(fields.EventFields))
                        {
                            events.m_events.Writer.TryComplete(
                                new InvalidOperationException("The native Condition queue is full."));
                        }
                    };
                    subscription.AddItem(item);
                    await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
                    Assert.That(item.Created, Is.True, item.Status.Error?.ToString());
                    return events;
                }
                catch
                {
                    await events.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public ValueTask<ArrayOf<Variant>> ReadAsync(CancellationToken ct)
            {
                return m_events.Reader.ReadAsync(ct);
            }

            public async ValueTask DisposeAsync()
            {
                m_events.Writer.TryComplete();
                await session.RemoveSubscriptionAsync(subscription, CancellationToken.None).ConfigureAwait(false);
                subscription.Dispose();
            }

            private readonly Channel<ArrayOf<Variant>> m_events = Channel.CreateBounded<ArrayOf<Variant>>(64);
        }

        private static readonly WotEventSelection s_conditionSelection = new(
            [
                .. WotEventSelection.Default.Clauses,
                new WotResolvedEventSelectClause("i=2782", string.Empty),
                new WotResolvedEventSelectClause("i=2782", "BranchId"),
                new WotResolvedEventSelectClause("i=2782", "ConditionName"),
                new WotResolvedEventSelectClause("i=2782", "EnabledState/Id"),
                new WotResolvedEventSelectClause("i=2782", "Retain")
            ],
            WotEventSelectionOrigin.Standard);
    }
}
#endif
