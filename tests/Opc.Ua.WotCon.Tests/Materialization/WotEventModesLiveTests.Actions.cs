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
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Export;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeConditionActionsRejectWrongConditionAndStaleSourceAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            var calls = Channel.CreateBounded<NativeConditionCall>(8);
            NodeId methodId = await InstallActionSourceAsync(source, calls.Writer, ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            string sourceMethod = NodeId.ToExpandedNodeId(methodId, source.Session.NamespaceUris).ToString();
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session)));
            WotProjectionHandle handle = await host.AddAsync(
                ActionProjection(mode, source.EndpointUrl, sourceMethod), ct).ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeConditionEvents events = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct).ConfigureAwait(false);
                ByteString sourceEventId = ByteString.From(new byte[] { 0xD3, 0xA0, 0x01, 0x01 });
                await ReportConditionAsync(source, "ConditionA", "PumpA", false, true, sourceEventId, ct)
                    .ConfigureAwait(false);
                ArrayOf<Variant> notification = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(notification[0].TryGetValue(out ByteString localEventId), Is.True);
                Assert.That(notification[8].TryGetValue(out NodeId localConditionId), Is.True);
                if (mode == "transparent-forwarding")
                {
                    Assert.That(NodeId.ToExpandedNodeId(localConditionId, destination.Session.NamespaceUris),
                        Is.EqualTo(new ExpandedNodeId("ConditionA", SourceNamespace)));
                }
                NodeId ownerId = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                NodeId actionId = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Ack", destination.Session.NamespaceUris);
                var comment = new LocalizedText("Captured source action");
                CallMethodResult first = await InvokeNativeActionAsync(
                    destination.Session, ownerId, actionId, localEventId, comment, ct).ConfigureAwait(false);
                Assert.That(first.StatusCode, Is.EqualTo(StatusCodes.Good));
                NativeConditionCall call = await calls.Reader.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(call.EventId, Is.EqualTo(sourceEventId));
                Assert.That(call.ObjectId, Is.EqualTo(new ExpandedNodeId("ConditionA", SourceNamespace)));
                Assert.That(call.SessionId, Is.EqualTo(source.Session.SessionId));
                Assert.That(call.Comment, Is.EqualTo(comment));

                ByteString otherId = ByteString.From(new byte[] { 0xD3, 0xA0, 0x01, 0x02 });
                await ReportConditionAsync(source, "ConditionB", "PumpB", true, false, otherId, ct)
                    .ConfigureAwait(false);
                ArrayOf<Variant> other = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(other[0].TryGetValue(out ByteString otherLocalId), Is.True);
                CallMethodResult wrong = await InvokeNativeActionAsync(
                    destination.Session, ownerId, actionId, otherLocalId, comment, ct).ConfigureAwait(false);
                Assert.That(wrong.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
                Assert.That(calls.Reader.TryRead(out _), Is.False);

                source.Session.NamespaceUris.GetIndexOrAppend("urn:wot:d3:invalidated-mapping");
                CallMethodResult stale = await InvokeNativeActionAsync(
                    destination.Session, ownerId, actionId, localEventId, comment, ct).ConfigureAwait(false);
                Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(calls.Reader.TryRead(out _), Is.False,
                    "A stale occurrence must not dispatch using the Session's current mapping.");
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
                calls.Writer.TryComplete();
            }
        }

        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeActionableConditionLimitIncludesDeclaredInstanceAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            destination.Session.Factory.Builder.AddOpcUaWotCon().Commit();
            var calls = Channel.CreateBounded<NativeConditionCall>(4);
            NodeId methodId = await InstallActionSourceAsync(source, calls.Writer, ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            string method = NodeId.ToExpandedNodeId(methodId, source.Session.NamespaceUris).ToString();
            var factory = new WotProjectionBindingRuntimeFactory(
                new NativeChannels(source.Session), null, new WotProjectionEventPublisher(),
                new WotProjectionConditionFactory(), new WotProjectionBindingRuntimeOptions { MaxEventRoutes = 1 });
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle, factory);
            WotProjectionHandle handle = await host.AddAsync(
                ActionProjection(mode, source.EndpointUrl, method), ct).ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeConditionEvents events = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct).ConfigureAwait(false);
                NodeId owner = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                NodeId action = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Ack", destination.Session.NamespaceUris);
                ushort metadata = checked((ushort)destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon));
                NodeId folder = await FindNativeChildAsync(destination.Session, owner,
                    new QualifiedName("EventBindings", metadata), ct).ConfigureAwait(false);
                NodeId descriptor = await FindNativeChildAsync(destination.Session, folder,
                    new QualifiedName("condition", metadata), ct).ConfigureAwait(false);
                NodeId availability = await FindNativeChildAsync(destination.Session, descriptor,
                    new QualifiedName("Availability", metadata), ct).ConfigureAwait(false);
                var client = new WoTEventBindingTypeClient(
                    destination.Session, descriptor, NUnitTelemetryContext.Create());
                ByteString mainSourceId = ByteString.From(new byte[] { 0xD3, 0xB0, 0x01, 0x01 });
                await ReportConditionAsync(source, "ConditionA", "PumpA", false, true, mainSourceId, ct)
                    .ConfigureAwait(false);
                ArrayOf<Variant> main = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(main[0].TryGetValue(out ByteString mainEventId), Is.True);
                Assert.That(main[8].TryGetValue(out NodeId mainCondition), Is.True);

                ByteString branchSourceId = ByteString.From(new byte[] { 0xD3, 0xB0, 0x01, 0x02 });
                await ReportConditionAsync(source, "ConditionA", "PumpA", true, false, branchSourceId, ct)
                    .ConfigureAwait(false);
                ArrayOf<Variant> retained = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(retained[0].TryGetValue(out ByteString branchEventId), Is.True);
                Assert.That(retained[8].TryGetValue(out NodeId branchCondition), Is.True);
                Assert.That(retained[9].TryGetValue(out NodeId branch), Is.True);
                Assert.That(branchCondition, Is.EqualTo(mainCondition));
                Assert.That(branch.IsNull, Is.False);
                var comment = new LocalizedText("Bounded retained branch");
                CallMethodResult accepted = await InvokeNativeActionAsync(
                    destination.Session, owner, action, branchEventId, comment, ct).ConfigureAwait(false);
                Assert.That(accepted.StatusCode, Is.EqualTo(StatusCodes.Good));
                NativeConditionCall call = await calls.Reader.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(call.EventId, Is.EqualTo(branchSourceId));
                Assert.That(call.ObjectId, Is.EqualTo(new ExpandedNodeId("ConditionA", SourceNamespace)));
                Assert.That(call.SessionId, Is.EqualTo(source.Session.SessionId));
                Assert.That(calls.Reader.TryRead(out _), Is.False);
                CallMethodResult evicted = await InvokeNativeActionAsync(
                    destination.Session, owner, action, mainEventId, comment, ct).ConfigureAwait(false);
                Assert.That(evicted.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
                ServiceResultException? expired = Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    _ = await client.GetEventProvenanceAsync(mainEventId, ct).ConfigureAwait(false);
                });
                Assert.That(expired!.StatusCode, Is.EqualTo(StatusCodes.BadNoData));

                ByteString excessId = ByteString.From(new byte[] { 0xD3, 0xB0, 0x02, 0x01 });
                await ReportConditionAsync(source, "ConditionB", "PumpB", false, true, excessId, ct)
                    .ConfigureAwait(false);
                StatusCode status = StatusCodes.Good;
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    DataValue value = await destination.Session.ReadValueAsync(availability, ct).ConfigureAwait(false);
                    Assert.That(value.WrappedValue.TryGetValue(out status), Is.True);
                    if (StatusCode.IsBad(status))
                    {
                        break;
                    }
                    await Task.Delay(25, ct).ConfigureAwait(false);
                }
                Assert.That(status, Is.EqualTo(StatusCodes.BadTooManyOperations),
                    "The declared actionable instance already consumes the one-Condition bound.");
                WoTEventOriginDataType origin = await client.GetEventProvenanceAsync(branchEventId, ct)
                    .ConfigureAwait(false);
                Assert.That(origin.SourceEventId, Is.EqualTo(branchSourceId));
                Assert.That(origin.SourceConditionId, Is.EqualTo(new ExpandedNodeId("ConditionA", SourceNamespace)));
                Assert.That(origin.SourceBranchId, Is.EqualTo(new ExpandedNodeId("Retained", SourceNamespace)));
                Assert.That(origin.Generation, Is.EqualTo(checked((uint)handle.Generation)));
                Assert.That(calls.Reader.TryRead(out _), Is.False);
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
                calls.Writer.TryComplete();
            }
        }

        private static async Task<NodeId> InstallActionSourceAsync(
            NativeEndpoint source,
            ChannelWriter<NativeConditionCall> calls,
            CancellationToken ct,
            string conditionName = "ConditionA",
            string pumpName = "PumpA")
        {
            UANodeSet nodes = ConditionSourceNodes();
            nodes.Items![2] = ConditionEventType(1, Ua.ObjectTypeIds.AcknowledgeableConditionType.ToString());
            byte[] bytes = Serialize(nodes);
            NodeId methodId = NodeId.Null;
            _ = await source.Server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream("source-actions",
                        _ => new ValueTask<Stream>(new MemoryStream(bytes, writable: false)), [SourceNamespace])
                ],
                ConfigureAsync = async (builder, token) =>
                {
                    NodeId pumpId = ExpandedNodeId.Parse(
                        "nsu=" + SourceNamespace + ";s=" + pumpName, builder.Context.NamespaceUris);
                    NodeId typeId = ExpandedNodeId.Parse(
                        "nsu=" + SourceNamespace + ";s=ConditionEventType", builder.Context.NamespaceUris);
                    NodeId conditionId = ExpandedNodeId.Parse(
                        "nsu=" + SourceNamespace + ";s=" + conditionName, builder.Context.NamespaceUris);
                    var declaration = new WotProjectedAffordance(
                        Opc.Ua.WotCon.Bindings.WotAffordanceKind.Event, conditionName, "/source/condition",
                        typeId.ToString(), pumpId.ToString(), conditionTypeId: "i=2881");
                    ConditionState condition = await new WotProjectionConditionFactory().CreateInstanceAsync(
                        builder, builder.Node<BaseObjectState>(pumpId).Node, declaration, typeId, conditionId, token)
                        .ConfigureAwait(false);
                    Assert.That(condition, Is.InstanceOf<AcknowledgeableConditionState>());
                    var acknowledgeable = (AcknowledgeableConditionState)condition;
                    AddCommentMethodState method = acknowledgeable.Acknowledge!;
                    method.OnCall = null;
                    method.OnCallAsync = null;
                    methodId = method.NodeId;
                    builder.Node(method.NodeId).OnCallWithResult((context, _, receiver, arguments, cancellation) =>
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (arguments.Count != 2 ||
                            !arguments[0].TryGetValue(out ByteString eventId) ||
                            !arguments[1].TryGetValue(out LocalizedText text) ||
                            context is not ServerSystemContext serverContext ||
                            serverContext.SessionId is not NodeId sourceSessionId)
                        {
                            return new ValueTask<MethodInvocationResult>(
                                new MethodInvocationResult(StatusCodes.BadInvalidArgument));
                        }
                        bool written = calls.TryWrite(new NativeConditionCall(
                            NodeId.ToExpandedNodeId(receiver, context.NamespaceUris),
                            eventId, text, sourceSessionId));
                        return new ValueTask<MethodInvocationResult>(new MethodInvocationResult(
                            written ? StatusCodes.Good : StatusCodes.BadTooManyOperations));
                    });
                    return null;
                }
            }, null, ct).ConfigureAwait(false);
            Assert.That(methodId.IsNull, Is.False);
            return methodId;
        }

        private static WotProjectionDocument ActionProjection(string mode, string endpoint, string methodId)
        {
            WotProjectionDocument document = ConditionProjection(mode, endpoint);
            WotBindingPlan existing = document.BindingPlans[0];
            WotProjectedAffordance original = existing.ProjectedAffordances[0];
            WotProjectedAffordance condition = new WotProjectedAffordance(
                original.Kind, original.Name, original.JsonPointer, original.NodeId, original.OwnerNodeId,
                conditionTypeId: "i=2881").WithIdentityMode(original.IdentityMode);
            var action = new WotProjectedAffordance(
                Opc.Ua.WotCon.Bindings.WotAffordanceKind.Action, "ack", "/actions/ack",
                "nsu=" + LocalNamespace + ";s=Ack", "nsu=" + LocalNamespace + ";s=Owner",
                "Acknowledge", "condition");
            var form = new WotCompiledForm(
                new WotBindingIdentity("opc.opcua", "10101", "urn:opcfoundation:wot:binding:opcua"),
                Opc.Ua.WotCon.Bindings.WotAffordanceKind.Action, "ack", "/actions/ack/forms/0",
                WoTBindingCapabilityEnum.InvokeAction, "invokeaction",
                new WotEndpointDescriptor("opc.tcp", null, -1, endpoint),
                new WotAddressingDescriptor(methodId, ImmutableDictionary<string, string>.Empty.Add(
                    "componentOf", "nsu=" + SourceNamespace + ";s=ConditionA")),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.InvokeAction, "invokeaction", "Call"),
                new WotPayloadDescriptor("application/opcua+uabinary", "opcua"), [], true);
            using var sourceStream = new MemoryStream(document.Sources[0].NodeSetXml, writable: false);
            UANodeSet sourceTypes = UANodeSet.Read(sourceStream) ??
                throw new InvalidOperationException("The source action type NodeSet was not read.");
            sourceTypes.Items![0] = ConditionEventType(1, Ua.ObjectTypeIds.AcknowledgeableConditionType.ToString());
            using var localStream = new MemoryStream(document.Sources[1].NodeSetXml, writable: false);
            UANodeSet local = UANodeSet.Read(localStream) ??
                throw new InvalidOperationException("The local action NodeSet was not read.");
            ExportNativeAction(local, "Ack");
            WotBindingPlan plan = new WotBindingPlan(
                existing.ResourceXid, [], [existing.CompiledForms[0], form], [], [])
                .WithProjectedAffordances([condition, action]);
            return new WotProjectionDocument(document.ClosureKey,
                [
                    new WotProjectionSource("action-source-types", [SourceNamespace], Serialize(sourceTypes)),
                    new WotProjectionSource("action-local-owner", [LocalNamespace], Serialize(local))
                ],
                [plan]);
        }

        private static void ExportNativeAction(UANodeSet local, string name)
        {
            var context = new SystemContext(NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable([Ua.Namespaces.OpcUa, LocalNamespace, SourceNamespace])
            };
            var method = new MethodState(null)
            {
                NodeId = new NodeId(name, 1),
                BrowseName = new QualifiedName(name, 1),
                DisplayName = new LocalizedText(name),
                Executable = true,
                UserExecutable = true
            };
            method.AddReference(Ua.ReferenceTypeIds.HasComponent, true, new NodeId("Owner", 1));
            method.InputArguments = method.CreateOrReplaceInputArguments(context, null);
            method.InputArguments.NodeId = new NodeId(name + ".InputArguments", 1);
            method.InputArguments.BrowseName = QualifiedName.From(Ua.BrowseNames.InputArguments);
            method.InputArguments.DisplayName = new LocalizedText(Ua.BrowseNames.InputArguments);
            method.InputArguments.TypeDefinitionId = Ua.VariableTypeIds.PropertyType;
            method.InputArguments.ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty;
            method.InputArguments.DataType = Ua.DataTypeIds.Argument;
            method.InputArguments.ValueRank = ValueRanks.OneDimension;
            method.InputArguments.Value =
            [
                new Argument { Name = "EventId", DataType = Ua.DataTypeIds.ByteString, ValueRank = -1 },
                new Argument { Name = "Comment", DataType = Ua.DataTypeIds.LocalizedText, ValueRank = -1 }
            ];
            local.Export(context, method);
        }

        private static async Task<CallMethodResult> InvokeNativeActionAsync(
            ISession session, NodeId owner, NodeId method, ByteString eventId,
            LocalizedText comment, CancellationToken ct)
        {
            CallResponse response = await session.CallAsync(null,
                [
                    new CallMethodRequest
                    {
                        ObjectId = owner,
                        MethodId = method,
                        InputArguments = [new Variant(eventId), new Variant(comment)]
                    }
                ],
                ct).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private readonly record struct NativeConditionCall(
            ExpandedNodeId ObjectId, ByteString EventId, LocalizedText Comment, NodeId SessionId);
    }
}
#endif
