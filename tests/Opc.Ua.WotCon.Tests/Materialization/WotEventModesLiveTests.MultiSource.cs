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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Export;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeTwoSourcesKeepConditionActionsAndBranchesIsolatedAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using var first = new NativeEndpoint();
            await using var second = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await first.StartAsync(false, ct).ConfigureAwait(false);
            await second.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            destination.Session.Factory.Builder.AddOpcUaWotCon().Commit();
            var firstCalls = Channel.CreateBounded<NativeConditionCall>(8);
            var secondCalls = Channel.CreateBounded<NativeConditionCall>(8);
            NodeId firstMethod = await InstallActionSourceAsync(first, firstCalls.Writer, ct).ConfigureAwait(false);
            NodeId secondMethod = await InstallActionSourceAsync(
                second, secondCalls.Writer, ct, "ConditionB", "PumpB").ConfigureAwait(false);
            await first.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            await second.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            WotProjectionDocument document = TwoSourceActionProjection(
                mode, first.EndpointUrl, second.EndpointUrl,
                NodeId.ToExpandedNodeId(firstMethod, first.Session.NamespaceUris).ToString(),
                NodeId.ToExpandedNodeId(secondMethod, second.Session.NamespaceUris).ToString());
            var channels = new EndpointNativeChannels(new Dictionary<string, ISession>(StringComparer.Ordinal)
            {
                [first.EndpointUrl] = first.Session,
                [second.EndpointUrl] = second.Session
            });
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(channels));
            WotProjectionHandle handle = await host.AddAsync(document, ct).ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeConditionEvents events = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct).ConfigureAwait(false);
                NodeId owner = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                NodeId firstAction = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Ack", destination.Session.NamespaceUris);
                NodeId secondAction = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=AckB", destination.Session.NamespaceUris);
                ushort metadata = checked((ushort)destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon));
                NodeId folder = await FindNativeChildAsync(destination.Session, owner,
                    new QualifiedName("EventBindings", metadata), ct).ConfigureAwait(false);
                var conditions = new NodeId[4];
                var branches = new NodeId[4];
                var eventIds = new ByteString[8];
                ArrayOf<string> names = ["ConditionA", "NoticeA", "ConditionB", "NoticeB"];
                for (int index = 0; index < names.Count; index++)
                {
                    bool fromFirst = index < 2;
                    bool actionable = index % 2 == 0;
                    NativeEndpoint source = fromFirst ? first : second;
                    ChannelReader<NativeConditionCall> calls = fromFirst ? firstCalls.Reader : secondCalls.Reader;
                    string pump = fromFirst ? "PumpA" : "PumpB";
                    string bindingName = fromFirst ? "condition" : "other";
                    NodeId descriptor = await FindNativeChildAsync(destination.Session, folder,
                        new QualifiedName(bindingName, metadata), ct).ConfigureAwait(false);
                    var provenance = new WoTEventBindingTypeClient(
                        destination.Session, descriptor, NUnitTelemetryContext.Create());
                    for (int branchIndex = 0; branchIndex < 2; branchIndex++)
                    {
                        bool retained = branchIndex != 0;
                        bool enabled = (index + branchIndex) % 2 == 0;
                        ByteString sourceId = ByteString.From(new byte[]
                        {
                            0xD3, 0xB2, (byte)index, (byte)branchIndex
                        });
                        await ReportConditionAsync(
                            source, names[index], pump, retained, enabled, sourceId, ct).ConfigureAwait(false);
                        ArrayOf<Variant> fields = await events.ReadAsync(ct).ConfigureAwait(false);
                        Assert.That(fields[0].TryGetValue(out ByteString eventId), Is.True);
                        Assert.That(fields[2].TryGetValue(out NodeId sourceNode), Is.True);
                        Assert.That(fields[8].TryGetValue(out NodeId condition), Is.True);
                        Assert.That(fields[9].TryGetValue(out NodeId branch), Is.True);
                        Assert.That(fields[10].TryGetValue(out string? conditionName), Is.True);
                        Assert.That(fields[11].TryGetValue(out bool actualEnabled), Is.True);
                        bool transparent = mode == "transparent-forwarding";
                        Assert.That(conditionName, Is.EqualTo(names[index]));
                        Assert.That(actualEnabled, Is.EqualTo(enabled));
                        Assert.That(eventId == sourceId, Is.EqualTo(transparent));
                        Assert.That(branch.IsNull, Is.EqualTo(!retained));
                        Assert.That(NodeId.ToExpandedNodeId(sourceNode, destination.Session.NamespaceUris),
                            Is.EqualTo(transparent ? new ExpandedNodeId(pump, SourceNamespace)
                                : new ExpandedNodeId("Owner", LocalNamespace)));
                        if (transparent)
                        {
                            Assert.That(NodeId.ToExpandedNodeId(condition, destination.Session.NamespaceUris),
                                Is.EqualTo(new ExpandedNodeId(names[index], SourceNamespace)));
                        }
                        if (!retained)
                        {
                            conditions[index] = condition;
                        }
                        else
                        {
                            Assert.That(condition, Is.EqualTo(conditions[index]));
                            branches[index] = branch;
                        }
                        eventIds[(2 * index) + branchIndex] = eventId;
                        WoTEventOriginDataType origin = await provenance.GetEventProvenanceAsync(eventId, ct)
                            .ConfigureAwait(false);
                        using (Assert.EnterMultipleScope())
                        {
                            Assert.That(origin.SourceEventId, Is.EqualTo(sourceId));
                            Assert.That(origin.SourceConditionId,
                                Is.EqualTo(new ExpandedNodeId(names[index], SourceNamespace)));
                            Assert.That(origin.SourceBranchId, Is.EqualTo(retained
                                ? new ExpandedNodeId("Retained", SourceNamespace) : ExpandedNodeId.Null));
                            Assert.That(origin.BindingId, Is.EqualTo("/events/" + bindingName));
                            Assert.That(origin.Generation, Is.EqualTo(checked((uint)handle.Generation)));
                            Assert.That(origin.EncodingMask, Is.EqualTo(0xFFU));
                        }
                        NodeId inherited = await FindNativeChildAsync(destination.Session, condition,
                            QualifiedName.From(Ua.BrowseNames.Acknowledge), ct).ConfigureAwait(false);
                        var comment = new LocalizedText(names[index] + (retained ? " retained" : " main"));
                        if (actionable)
                        {
                            NodeId receiver = retained ? condition : owner;
                            NodeId method = retained ? inherited : fromFirst ? firstAction : secondAction;
                            CallMethodResult result = await InvokeNativeActionAsync(
                                destination.Session, receiver, method, eventId, comment, ct).ConfigureAwait(false);
                            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                            NativeConditionCall call = await calls.ReadAsync(ct).ConfigureAwait(false);
                            Assert.That(call.EventId, Is.EqualTo(sourceId));
                            Assert.That(call.ObjectId, Is.EqualTo(new ExpandedNodeId(names[index], SourceNamespace)));
                            Assert.That(call.SessionId, Is.EqualTo(source.Session.SessionId));
                            Assert.That(call.Comment, Is.EqualTo(comment));
                        }
                        else
                        {
                            ReadResponse attributes = await destination.Session.ReadAsync(
                                null, 0, TimestampsToReturn.Neither,
                                [new ReadValueId { NodeId = inherited, AttributeId = Attributes.Executable }], ct)
                                .ConfigureAwait(false);
                            Assert.That(attributes.Results.Count, Is.EqualTo(1));
                            Assert.That(attributes.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                            Assert.That(attributes.Results[0].WrappedValue.TryGetValue(out bool executable), Is.True);
                            Assert.That(executable, Is.False);
                            CallMethodResult rejected = await InvokeNativeActionAsync(
                                destination.Session, condition, inherited, eventId, comment, ct).ConfigureAwait(false);
                            Assert.That(rejected.StatusCode, Is.AnyOf(
                                StatusCodes.BadNotExecutable, StatusCodes.BadUserAccessDenied));
                        }
                        Assert.That(firstCalls.Reader.TryRead(out _), Is.False);
                        Assert.That(secondCalls.Reader.TryRead(out _), Is.False);
                    }
                }
                Assert.That(conditions, Is.Unique);
                Assert.That(eventIds, Is.Unique);
                if (mode == "local-re-emission")
                {
                    Assert.That(branches, Is.Unique);
                }
                for (int index = 0; index < conditions.Length; index++)
                {
                    NodeId enabledState = await FindNativeChildAsync(destination.Session, conditions[index],
                        QualifiedName.From(Ua.BrowseNames.EnabledState), ct).ConfigureAwait(false);
                    NodeId id = await FindNativeChildAsync(destination.Session, enabledState,
                        QualifiedName.From(Ua.BrowseNames.Id), ct).ConfigureAwait(false);
                    DataValue current = await destination.Session.ReadValueAsync(id, ct).ConfigureAwait(false);
                    Assert.That(current.WrappedValue.TryGetValue(out bool enabled), Is.True);
                    Assert.That(enabled, Is.EqualTo(index % 2 == 0),
                        "Another Condition or a retained branch must not overwrite the current branch state.");
                }
                var wrongComment = new LocalizedText("Wrong source");
                CallMethodResult wrongFirst = await InvokeNativeActionAsync(
                    destination.Session, owner, firstAction, eventIds[4], wrongComment, ct).ConfigureAwait(false);
                CallMethodResult wrongSecond = await InvokeNativeActionAsync(
                    destination.Session, owner, secondAction, eventIds[0], wrongComment, ct).ConfigureAwait(false);
                Assert.That(wrongFirst.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
                Assert.That(wrongSecond.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
                Assert.That(firstCalls.Reader.TryRead(out _), Is.False);
                Assert.That(secondCalls.Reader.TryRead(out _), Is.False);
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
                firstCalls.Writer.TryComplete();
                secondCalls.Writer.TryComplete();
            }
        }

        private static WotProjectionDocument TwoSourceActionProjection(
            string mode, string firstEndpoint, string secondEndpoint, string firstMethod, string secondMethod)
        {
            WotProjectionDocument first = ActionProjection(mode, firstEndpoint, firstMethod);
            WotBindingPlan original = first.BindingPlans[0];
            WotProjectedAffordance primary = original.ProjectedAffordances[0];
            var endpoint = new WotEndpointDescriptor("opc.tcp", null, -1, secondEndpoint);
            WotBindingIdentity binding = original.CompiledForms[0].Binding;
            var source = new WotCompiledForm(
                binding, Opc.Ua.WotCon.Bindings.WotAffordanceKind.Event, "other", "/events/other/forms/0",
                WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent", endpoint,
                new WotAddressingDescriptor("i=2253"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent", "Monitor"),
                new WotPayloadDescriptor("application/opcua+uabinary", "opcua"), [], true,
                null, s_conditionSelection, null);
            var action = new WotCompiledForm(
                binding, Opc.Ua.WotCon.Bindings.WotAffordanceKind.Action, "ackB", "/actions/ackB/forms/0",
                WoTBindingCapabilityEnum.InvokeAction, "invokeaction", endpoint,
                new WotAddressingDescriptor(secondMethod, ImmutableDictionary<string, string>.Empty.Add(
                    "componentOf", "nsu=" + SourceNamespace + ";s=ConditionB")),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.InvokeAction, "invokeaction", "Call"),
                new WotPayloadDescriptor("application/opcua+uabinary", "opcua"), [], true);
            WotProjectedAffordance condition = new WotProjectedAffordance(
                Opc.Ua.WotCon.Bindings.WotAffordanceKind.Event, "other", "/events/other",
                primary.NodeId, primary.OwnerNodeId, conditionTypeId: primary.ConditionTypeId)
                .WithIdentityMode(primary.IdentityMode);
            var acknowledge = new WotProjectedAffordance(
                Opc.Ua.WotCon.Bindings.WotAffordanceKind.Action, "ackB", "/actions/ackB",
                "nsu=" + LocalNamespace + ";s=AckB", primary.OwnerNodeId, "Acknowledge", "other");
            using var stream = new MemoryStream(first.Sources[1].NodeSetXml, writable: false);
            UANodeSet local = UANodeSet.Read(stream) ??
                throw new InvalidOperationException("The two-source local action model was not read.");
            ExportNativeAction(local, "AckB");
            WotBindingPlan plan = new WotBindingPlan(
                original.ResourceXid, [], [.. original.CompiledForms, source, action], [], [])
                .WithProjectedAffordances([.. original.ProjectedAffordances, condition, acknowledge]);
            return new WotProjectionDocument(first.ClosureKey,
                [
                    first.Sources[0],
                    new WotProjectionSource("two-source-local-owner", [LocalNamespace], Serialize(local))
                ],
                [plan]);
        }
    }
}
#endif
