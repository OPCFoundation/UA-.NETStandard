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
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Export;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeRetainedLimitRefreshUpdatesPropertiesWithoutChangingOccurrenceAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            destination.Session.Factory.Builder.AddOpcUaWotCon().Commit();
            UANodeSet sourceNodes = ConditionSourceNodes();
            sourceNodes.Items![2] = ConditionEventType(1, Ua.ObjectTypeIds.LimitAlarmType.ToString());
            await source.InstallAsync(sourceNodes, ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var publisher = new CapacityObservedPublisher();
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(
                    new NativeChannels(source.Session), null, publisher, new WotProjectionConditionFactory(),
                    new WotProjectionBindingRuntimeOptions { MaxEventRoutes = 1 }));
            WotProjectionHandle handle = await host.AddAsync(ConditionProjection(
                mode, source.EndpointUrl, s_refreshPublicSelection, conditionType: Ua.ObjectTypeIds.LimitAlarmType), ct)
                .ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeConditionEvents sourceEvents = await NativeConditionEvents.OpenAsync(
                    source.Session, ct, selection: s_refreshNativeSelection).ConfigureAwait(false);
                await using NativeConditionEvents events = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct, selection: s_refreshNativeSelection).ConfigureAwait(false);
                WotCompiledForm form = ConditionProjection(mode, destination.EndpointUrl, s_refreshPublicSelection,
                    conditionType: Ua.ObjectTypeIds.LimitAlarmType).BindingPlans[0].CompiledForms[0];
                await using IWotBindingChannel publicChannel = await new NativeChannels(destination.Session)
                    .OpenChannelAsync(form, ct).ConfigureAwait(false);
                var publicEvents = Channel.CreateBounded<WotNotification>(8);
                await using IWotSubscription subscription = await publicChannel.SubscribeEventAsync(
                    notification => CapturePublicCondition(notification, publicEvents.Writer), ct)
                    .ConfigureAwait(false);
                var context = source.Server.CurrentInstance.DefaultSystemContext;
                ByteString sourceId = ByteString.From(new byte[] { 0xD3, 0xC4, 0x01 });
                LimitAlarmState alarm = WotRetainedConditionRefreshTests.CreateAlarm(
                    context,
                    ExpandedNodeId.Parse("nsu=" + SourceNamespace + ";s=ConditionEventType", context.NamespaceUris),
                    ExpandedNodeId.Parse("nsu=" + SourceNamespace + ";s=retainedLimitAlarm", context.NamespaceUris),
                    sourceId, SourceTime);
                alarm.ReceiveTime!.Value = SourceReceiveTime;
                await source.Server.CurrentInstance.ReportEventAsync(context, alarm, ct).ConfigureAwait(false);
                Assert.That((await publisher.ReadAsync(ct).ConfigureAwait(false)).Status, Is.EqualTo(StatusCodes.Good));
                ArrayOf<Variant> original = await sourceEvents.ReadAsync(ct).ConfigureAwait(false);
                ArrayOf<Variant> first = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(first[0].TryGetValue(out ByteString localId), Is.True);
                Assert.That(localId == sourceId, Is.EqualTo(mode == "transparent-forwarding"));
                AssertRefreshPublicShape(await publicEvents.Reader.ReadAsync(ct).ConfigureAwait(false), first);
                Assert.That(publicChannel.Form.EventSelection, Is.SameAs(s_refreshPublicSelection));
                NodeId descriptor = await CapacityDescriptorAsync(destination, "condition", ct).ConfigureAwait(false);
                var provenance = new WoTEventBindingTypeClient(
                    destination.Session, descriptor, NUnitTelemetryContext.Create());
                WoTEventOriginDataType before = await provenance.GetEventProvenanceAsync(localId, ct)
                    .ConfigureAwait(false);

                alarm.HighLimit!.Value = 110;
                DateTimeUtc refreshedReceipt = SourceReceiveTime + TimeSpan.FromSeconds(1);
                alarm.ReceiveTime.Value = refreshedReceipt;
                var retained = new List<IFilterTarget>();
                alarm.ConditionRefresh(context, retained, includeChildren: false);
                Assert.That(retained, Has.Count.EqualTo(1));
                Assert.That(retained[0], Is.SameAs(alarm));
                await source.Server.CurrentInstance.ReportEventAsync(context, retained[0], ct).ConfigureAwait(false);
                ArrayOf<Variant> sourceRefresh = await sourceEvents.ReadAsync(ct).ConfigureAwait(false);
                for (int index = 0; index < original.Count; index++)
                {
                    if (s_refreshNativeSelection.Clauses[index].BrowsePath is not ("HighLimit" or "ReceiveTime"))
                    {
                        Assert.That(sourceRefresh[index], Is.EqualTo(original[index]),
                            "Core retained refresh must preserve " +
                                s_refreshNativeSelection.Clauses[index].BrowsePath);
                    }
                }
                Assert.That(sourceRefresh[s_privateCoreSelection.Clauses.Count], Is.EqualTo(new Variant(110d)));
                ProjectionObservation refreshed = await publisher.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(refreshed.Status, Is.EqualTo(StatusCodes.Good),
                    "Core retainedLimitAlarm refresh HighLimit 100 -> 110 is not an occurrence collision.");
                Assert.That(refreshed.EventId, Is.EqualTo(localId));
                ArrayOf<Variant> second = await events.ReadAsync(ct).ConfigureAwait(false);
                AssertRefreshPublicShape(await publicEvents.Reader.ReadAsync(ct).ConfigureAwait(false), second);
                for (int index = 0; index < first.Count; index++)
                {
                    if (s_refreshNativeSelection.Clauses[index].BrowsePath is not ("HighLimit" or "ReceiveTime"))
                    {
                        Assert.That(second[index], Is.EqualTo(first[index]));
                    }
                }
                Assert.That(second[s_privateCoreSelection.Clauses.Count], Is.EqualTo(new Variant(110d)));
                Assert.That(await ReadCapacityAvailabilityAsync(destination, descriptor, ct).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.Good));
                WoTEventOriginDataType after = await provenance.GetEventProvenanceAsync(localId, ct)
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(after.SourceEventId, Is.EqualTo(sourceId));
                    Assert.That(after.SourceConditionId, Is.EqualTo(before.SourceConditionId));
                    Assert.That(after.SourceBranchId, Is.EqualTo(before.SourceBranchId));
                    Assert.That(after.SourceTime, Is.EqualTo(before.SourceTime));
                    Assert.That(after.SourceReceiveTime, Is.EqualTo(refreshedReceipt));
                    Assert.That(after.Generation, Is.EqualTo(checked((uint)handle.Generation)));
                    Assert.That(after.ReceiveTime, Is.GreaterThan(before.ReceiveTime));
                }

                foreach (Subscription nativeSubscription in destination.Session.Subscriptions)
                {
                    Assert.That(await nativeSubscription.ConditionRefreshAsync(ct).ConfigureAwait(false), Is.True);
                }
                ArrayOf<Variant> downstreamRefresh = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(downstreamRefresh[0], Is.EqualTo(new Variant(localId)));
                Assert.That(downstreamRefresh[s_privateCoreSelection.Clauses.Count], Is.EqualTo(new Variant(110d)),
                    "A subsequent native ConditionRefresh must read the updated retained Property.");

                alarm.ReceiveTime.Value = refreshedReceipt + TimeSpan.FromSeconds(1);
                await source.Server.CurrentInstance.ReportEventAsync(context, alarm, ct).ConfigureAwait(false);
                WoTEventOriginDataType replay = after;
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    replay = await provenance.GetEventProvenanceAsync(localId, ct).ConfigureAwait(false);
                    if (replay.SourceReceiveTime == alarm.ReceiveTime.Value)
                    {
                        break;
                    }
                    await Task.Delay(25, ct).ConfigureAwait(false);
                }
                Assert.That(replay.SourceReceiveTime, Is.EqualTo(alarm.ReceiveTime.Value));
                alarm.EventId!.Value = ByteString.From(new byte[] { 0xD3, 0xC4, 0x02 });
                await source.Server.CurrentInstance.ReportEventAsync(context, alarm, ct).ConfigureAwait(false);
                ProjectionObservation capacity = await publisher.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(capacity.Status, Is.EqualTo(StatusCodes.BadTooManyOperations),
                    "Exact replay is suppressed and refresh neither consumes nor resets the occurrence budget.");
                WoTEventOriginDataType owned = await provenance.GetEventProvenanceAsync(localId, ct)
                    .ConfigureAwait(false);
                Assert.That(owned.SourceEventId, Is.EqualTo(sourceId));
                Assert.That(owned.Generation, Is.EqualTo(after.Generation));
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static void AssertRefreshPublicShape(WotNotification notification, ArrayOf<Variant> native)
        {
            Assert.That(notification.EventFields.Keys,
                Is.EqualTo(s_refreshPublicSelection.Clauses.ToList().Select(clause => clause.BrowsePath)));
            ArrayOf<ArrayOf<string>> paths = WotEventSelectClauses.GetMaterializedMemberPaths(
                s_refreshPublicSelection.Clauses);
            for (int index = 0; index < paths.Count; index++)
            {
                int nativeIndex = s_refreshNativeSelection.Clauses.ToList().FindIndex(
                    clause => clause.Equals(s_refreshPublicSelection.Clauses[index]));
                Assert.That(nativeIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(notification.Data.TryGetValue(paths[index], out DataValue value), Is.True);
                Assert.That(value.WrappedValue, Is.EqualTo(native[nativeIndex]));
            }
            Assert.That(notification.Data.Members.Keys,
                Is.EquivalentTo(s_refreshDataKeys));
            Assert.That(notification.Data.TryGetValue(["ConditionId"], out _), Is.False);
            Assert.That(notification.Data.TryGetValue(["EventId"], out _), Is.False);
        }

        private static readonly string[] s_refreshDataKeys =
            ["ConditionName", "HighLimit", "ActiveState", "AckedState"];

        private static readonly WotEventSelection s_refreshPublicSelection = new(
            [
                new WotResolvedEventSelectClause("i=2782", "ConditionName"),
                new WotResolvedEventSelectClause(Ua.ObjectTypeIds.LimitAlarmType.ToString(), "HighLimit"),
                new WotResolvedEventSelectClause(Ua.ObjectTypeIds.AlarmConditionType.ToString(), "ActiveState/Id"),
                new WotResolvedEventSelectClause(
                    Ua.ObjectTypeIds.AcknowledgeableConditionType.ToString(), "AckedState/Id")
            ],
            WotEventSelectionOrigin.Standard);

        private static readonly WotEventSelection s_refreshNativeSelection = new(
            [.. s_privateCoreSelection.Clauses, .. s_refreshPublicSelection.Clauses.ToList().Skip(1)],
            WotEventSelectionOrigin.Standard);
    }
}
#endif
