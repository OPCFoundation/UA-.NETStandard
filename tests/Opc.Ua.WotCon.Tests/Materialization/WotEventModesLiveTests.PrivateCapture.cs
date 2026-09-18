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
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase("local-re-emission", "ConditionId")]
        [TestCase("transparent-forwarding", "ConditionId")]
        [TestCase("local-re-emission", "Identity")]
        [TestCase("transparent-forwarding", "Identity")]
        [TestCase("local-re-emission", "Core")]
        [TestCase("transparent-forwarding", "Core")]
        public async Task NativeConditionsCaptureRequiredFieldsOutsidePublicSelectionAsync(
            string mode, string omitted)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            destination.Session.Factory.Builder.AddOpcUaWotCon().Commit();
            await source.InstallAsync(ConditionSourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            WotEventSelection selection = PrivatePublicSelection(omitted);
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session)));
            WotProjectionDocument document = ConditionProjection(
                mode, source.EndpointUrl, selection, useDefaultMode: mode == "local-re-emission");
            Assert.That(document.BindingPlans[0].ProjectedAffordances.Count, Is.EqualTo(1));
            Assert.That(document.BindingPlans[0].ProjectedAffordances[0].IdentityMode,
                Is.EqualTo(mode == "transparent-forwarding"
                    ? WoTEventIdentityModeEnum.TransparentForwarding : WoTEventIdentityModeEnum.LocalReEmission));
            WotProjectionHandle handle = await host.AddAsync(document, ct).ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeConditionEvents sourceEvents = await NativeConditionEvents.OpenAsync(
                    source.Session, ct, selection: s_privateCoreSelection).ConfigureAwait(false);
                await using NativeConditionEvents events = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct, selection: s_privateCoreSelection).ConfigureAwait(false);
                WotCompiledForm publicForm = ConditionProjection(mode, destination.EndpointUrl, selection)
                    .BindingPlans[0].CompiledForms[0];
                await using IWotBindingChannel publicChannel = await new NativeChannels(destination.Session)
                    .OpenChannelAsync(publicForm, ct).ConfigureAwait(false);
                var publicEvents = Channel.CreateBounded<WotNotification>(8);
                await using IWotSubscription subscription = await publicChannel.SubscribeEventAsync(
                    notification => CapturePublicCondition(notification, publicEvents.Writer), ct)
                    .ConfigureAwait(false);
                WoTEventBindingTypeClient provenance = await PrivateConditionProvenanceAsync(destination, ct)
                    .ConfigureAwait(false);
                var conditions = new ExpandedNodeId[4];
                var branches = new ExpandedNodeId[4];
                var eventIds = new ByteString[4];
                for (int index = 0; index < 4; index++)
                {
                    string conditionName = index < 2 ? "ConditionA" : "ConditionB";
                    string pumpName = index < 2 ? "PumpA" : "PumpB";
                    bool retained = index % 2 != 0;
                    ByteString originalId = ByteString.From(new byte[] { 0xD3, 0xC1, 0x01, (byte)index });
                    DateTimeUtc boundary = DateTimeUtc.Now;
                    await ReportConditionAsync(source, conditionName, pumpName, retained, true, originalId, ct,
                        ConfigurePrivateCoreFields).ConfigureAwait(false);
                    ArrayOf<Variant> original = await sourceEvents.ReadAsync(ct).ConfigureAwait(false);
                    ArrayOf<Variant> actual = await events.ReadAsync(ct).ConfigureAwait(false);
                    WotNotification publicEvent = await publicEvents.Reader.ReadAsync(ct).ConfigureAwait(false);
                    Assert.That(actual.Count, Is.EqualTo(s_privateCoreSelection.Clauses.Count));
                    Assert.That(actual[0].TryGetValue(out ByteString eventId), Is.True);
                    Assert.That(actual[1].TryGetValue(out NodeId eventType), Is.True);
                    Assert.That(actual[2].TryGetValue(out NodeId sourceNode), Is.True);
                    Assert.That(actual[5].TryGetValue(out DateTimeUtc received), Is.True);
                    Assert.That(actual[8].TryGetValue(out NodeId conditionId), Is.True);
                    Assert.That(actual[9].TryGetValue(out NodeId branchId), Is.True);
                    conditions[index] = NodeId.ToExpandedNodeId(conditionId, destination.Session.NamespaceUris);
                    branches[index] = NodeId.ToExpandedNodeId(branchId, destination.Session.NamespaceUris);
                    eventIds[index] = eventId;
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
                        Assert.That(conditions[index] == new ExpandedNodeId(conditionName, SourceNamespace),
                            Is.EqualTo(transparent));
                        Assert.That(branchId.IsNull, Is.EqualTo(!retained));
                        Assert.That(received, Is.GreaterThanOrEqualTo(boundary));
                        Assert.That(received, Is.Not.EqualTo(SourceReceiveTime));
                    }
                    for (int field = 0; field < original.Count; field++)
                    {
                        if (field is not (0 or 1 or 2 or 5 or 8 or 9))
                        {
                            Assert.That(actual[field], Is.EqualTo(original[field]),
                                s_privateCoreSelection.Clauses[field].BrowsePath);
                        }
                    }
                    AssertPublicConditionShape(publicEvent, selection, actual);
                    Assert.That(publicChannel.Form.EventSelection, Is.SameAs(selection));
                    WoTEventOriginDataType origin = await provenance.GetEventProvenanceAsync(eventId, ct)
                        .ConfigureAwait(false);
                    using (Assert.EnterMultipleScope())
                    {
                        Assert.That(origin.SourceEventId, Is.EqualTo(originalId));
                        Assert.That(origin.SourceConditionId,
                            Is.EqualTo(new ExpandedNodeId(conditionName, SourceNamespace)));
                        Assert.That(origin.SourceBranchId, Is.EqualTo(retained
                            ? new ExpandedNodeId("Retained", SourceNamespace) : ExpandedNodeId.Null));
                        Assert.That(origin.SourceTime, Is.EqualTo(SourceTime));
                        Assert.That(origin.SourceNode, Is.EqualTo(new ExpandedNodeId(pumpName, SourceNamespace)));
                        Assert.That(origin.Generation, Is.EqualTo(checked((uint)handle.Generation)));
                    }
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
                    Assert.That(selection.Clauses.Contains(clause => clause.IsConditionIdSelection), Is.False);
                }
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeConditionActionsUsePrivatelyCapturedIdentityAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            var calls = Channel.CreateBounded<NativeConditionCall>(4);
            NodeId method = await InstallActionSourceAsync(source, calls.Writer, ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            string sourceMethod = NodeId.ToExpandedNodeId(method, source.Session.NamespaceUris).ToString();
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session)));
            WotEventSelection selection = PrivatePublicSelection("Core");
            WotProjectionHandle handle = await host.AddAsync(ActionProjection(
                mode, source.EndpointUrl, sourceMethod, selection, useDefaultMode: mode == "local-re-emission"), ct)
                .ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeConditionEvents events = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct).ConfigureAwait(false);
                NodeId owner = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                NodeId action = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Ack", destination.Session.NamespaceUris);
                var comment = new LocalizedText("Private identity route");
                ByteString sourceEventId = ByteString.From(new byte[] { 0xD3, 0xA1, 0x01, 0x01 });
                await ReportConditionAsync(source, "ConditionA", "PumpA", true, true, sourceEventId, ct)
                    .ConfigureAwait(false);
                ArrayOf<Variant> occurrence = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(occurrence[0].TryGetValue(out ByteString eventId), Is.True);
                Assert.That(occurrence[9].TryGetValue(out NodeId branch), Is.True);
                Assert.That(branch.IsNull, Is.False);
                CallMethodResult accepted = await InvokeNativeActionAsync(
                    destination.Session, owner, action, eventId, comment, ct).ConfigureAwait(false);
                Assert.That(accepted.StatusCode, Is.EqualTo(StatusCodes.Good));
                NativeConditionCall call = await calls.Reader.ReadAsync(ct).ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(call.EventId, Is.EqualTo(sourceEventId));
                    Assert.That(call.ObjectId, Is.EqualTo(new ExpandedNodeId("ConditionA", SourceNamespace)));
                    Assert.That(call.SessionId, Is.EqualTo(source.Session.SessionId));
                    Assert.That(call.Comment, Is.EqualTo(comment));
                }
                await ReportConditionAsync(source, "ConditionB", "PumpB", false, true,
                    ByteString.From(new byte[] { 0xD3, 0xA1, 0x02, 0x01 }), ct).ConfigureAwait(false);
                ArrayOf<Variant> foreign = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(foreign[0].TryGetValue(out ByteString foreignId), Is.True);
                CallMethodResult rejected = await InvokeNativeActionAsync(
                    destination.Session, owner, action, foreignId, comment, ct).ConfigureAwait(false);
                Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadEventIdUnknown));
                source.Session.NamespaceUris.GetIndexOrAppend("urn:wot:d3:private-capture-invalidated");
                CallMethodResult stale = await InvokeNativeActionAsync(
                    destination.Session, owner, action, eventId, comment, ct).ConfigureAwait(false);
                Assert.That(stale.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(calls.Reader.TryRead(out _), Is.False);
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
                calls.Writer.TryComplete();
            }
        }

        private static WotEventSelection PrivatePublicSelection(string omitted)
        {
            return new WotEventSelection(s_conditionSelection.Clauses.ToList().Where(clause => omitted switch
            {
                "ConditionId" => !clause.IsConditionIdSelection,
                "Identity" => clause.BrowsePath is
                    "SourceName" or "Message" or "Severity" or "ConditionName" or "EnabledState/Id" or "Retain",
                "Core" => clause.BrowsePath == "ConditionName",
                _ => throw new ArgumentOutOfRangeException(nameof(omitted))
            }).ToArrayOf(), WotEventSelectionOrigin.Standard);
        }

        private static void ConfigurePrivateCoreFields(ConditionState condition)
        {
            condition.EnabledState!.Value = new LocalizedText("Enabled at source");
            condition.ConditionClassId!.Value = Ua.ObjectTypeIds.ProcessConditionClassType;
            condition.ConditionClassName!.Value = new LocalizedText("Source process class");
            condition.Quality!.Value = StatusCodes.Good;
            condition.Quality.SourceTimestamp!.Value = SourceTime;
            condition.LastSeverity!.Value = 117;
            condition.LastSeverity.SourceTimestamp!.Value = SourceTime;
            condition.Comment!.Value = new LocalizedText("Private source comment");
            condition.Comment.SourceTimestamp!.Value = SourceTime;
            condition.ClientUserId!.Value = "source-operator";
        }

        private static void CapturePublicCondition(
            WotNotification notification, ChannelWriter<WotNotification> writer)
        {
            if (StatusCode.IsBad(notification.Value.StatusCode))
            {
                writer.TryComplete(new ServiceResultException(notification.Value.StatusCode));
            }
            else if (notification.Data.TryGetValue(["ConditionName"], out DataValue name) &&
                name.WrappedValue.TryGetValue(out string? text) &&
                text is "ConditionA" or "ConditionB" && !writer.TryWrite(notification))
            {
                writer.TryComplete(new InvalidOperationException("The public Condition queue is full."));
            }
        }

        private static void AssertPublicConditionShape(
            WotNotification notification, WotEventSelection selection, ArrayOf<Variant> native)
        {
            Assert.That(notification.EventFields.Keys, Is.EqualTo(selection.Clauses.ToList().Select(clause =>
                clause.IsConditionIdSelection ? "ConditionId" : clause.BrowsePath)));
            ArrayOf<ArrayOf<string>> paths = WotEventSelectClauses.GetMaterializedMemberPaths(selection.Clauses);
            Assert.That(CountFields(notification.Data), Is.EqualTo(paths.Count));
            for (int index = 0; index < selection.Clauses.Count; index++)
            {
                WotResolvedEventSelectClause clause = selection.Clauses[index];
                int nativeIndex = s_privateCoreSelection.Clauses.ToList()
                    .FindIndex(candidate => candidate.Equals(clause));
                Assert.That(nativeIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(notification.Data.TryGetValue(paths[index], out DataValue field), Is.True);
                Assert.That(field.WrappedValue, Is.EqualTo(native[nativeIndex]));
            }
            Assert.That(notification.Data.TryGetValue(["ConditionId"], out _), Is.False);

            static int CountFields(WotEventData data)
            {
                return data.HasValue ? 1 : data.Members.Values.Sum(CountFields);
            }
        }

        private static async Task<WoTEventBindingTypeClient> PrivateConditionProvenanceAsync(
            NativeEndpoint destination, CancellationToken ct)
        {
            NodeId owner = ExpandedNodeId.Parse(
                "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
            ushort metadata = checked((ushort)destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon));
            NodeId folder = await FindNativeChildAsync(destination.Session, owner,
                new QualifiedName("EventBindings", metadata), ct).ConfigureAwait(false);
            NodeId descriptor = await FindNativeChildAsync(destination.Session, folder,
                new QualifiedName("condition", metadata), ct).ConfigureAwait(false);
            return new WoTEventBindingTypeClient(destination.Session, descriptor, NUnitTelemetryContext.Create());
        }

        private static readonly WotEventSelection s_privateCoreSelection = new(
            [
                .. s_conditionSelection.Clauses,
                new WotResolvedEventSelectClause("i=2782", "EnabledState"),
                new WotResolvedEventSelectClause("i=2782", "ConditionClassId"),
                new WotResolvedEventSelectClause("i=2782", "ConditionClassName"),
                new WotResolvedEventSelectClause("i=2782", "Quality"),
                new WotResolvedEventSelectClause("i=2782", "Quality/SourceTimestamp"),
                new WotResolvedEventSelectClause("i=2782", "LastSeverity"),
                new WotResolvedEventSelectClause("i=2782", "LastSeverity/SourceTimestamp"),
                new WotResolvedEventSelectClause("i=2782", "Comment"),
                new WotResolvedEventSelectClause("i=2782", "Comment/SourceTimestamp"),
                new WotResolvedEventSelectClause("i=2782", "ClientUserId")
            ],
            WotEventSelectionOrigin.Standard);
    }
}
#endif
