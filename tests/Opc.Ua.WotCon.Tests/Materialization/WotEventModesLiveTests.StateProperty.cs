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
 * OF MERCHANTABILITY, FITNESS FOR ANY PARTICULAR PURPOSE AND
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
using System.Threading;
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
        public async Task NativeStatePropertyChangePreservesLastRetainedOccurrenceAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            destination.Session.Factory.Builder.AddOpcUaWotCon().Commit();
            UANodeSet nodes = ConditionSourceNodes();
            nodes.Items![2] = ConditionEventType(1, Ua.ObjectTypeIds.LimitAlarmType.ToString());
            await source.InstallAsync(nodes, ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var stateClause = new WotResolvedEventSelectClause(
                Ua.ObjectTypeIds.AlarmConditionType.ToString(), "SuppressedOrShelved");
            var publicSelection = new WotEventSelection(
                [new WotResolvedEventSelectClause("i=2782", "ConditionName"), stateClause],
                WotEventSelectionOrigin.Standard);
            var nativeSelection = new WotEventSelection(
                [.. s_conditionSelection.Clauses, stateClause], WotEventSelectionOrigin.Standard);
            var publisher = new CapacityObservedPublisher();
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session), null, publisher,
                    new WotProjectionConditionFactory(), new WotProjectionBindingRuntimeOptions()));
            WotProjectionHandle handle = await host.AddAsync(ConditionProjection(
                mode, source.EndpointUrl, publicSelection, conditionType: Ua.ObjectTypeIds.LimitAlarmType), ct)
                .ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeConditionEvents events = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct, selection: nativeSelection).ConfigureAwait(false);
                var context = source.Server.CurrentInstance.DefaultSystemContext;
                ByteString sourceId = ByteString.From(new byte[] { 0xD3, 0xC7, 0x01 });
                LimitAlarmState alarm = WotRetainedConditionRefreshTests.CreateAlarm(
                    context,
                    ExpandedNodeId.Parse("nsu=" + SourceNamespace + ";s=ConditionEventType", context.NamespaceUris),
                    ExpandedNodeId.Parse("nsu=" + SourceNamespace + ";s=ConditionA", context.NamespaceUris),
                    sourceId, SourceTime);
                alarm.SuppressedOrShelved!.Value = false;
                await source.Server.CurrentInstance.ReportEventAsync(context, alarm, ct).ConfigureAwait(false);
                Assert.That((await publisher.ReadAsync(ct).ConfigureAwait(false)).Status, Is.EqualTo(StatusCodes.Good));
                ArrayOf<Variant> first = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(first[0].TryGetValue(out ByteString localId), Is.True);
                Assert.That(first[^1], Is.EqualTo(new Variant(false)));
                NodeId descriptor = await CapacityDescriptorAsync(destination, "condition", ct).ConfigureAwait(false);
                var provenance = new WoTEventBindingTypeClient(
                    destination.Session, descriptor, NUnitTelemetryContext.Create());
                WoTEventOriginDataType before = await provenance.GetEventProvenanceAsync(localId, ct)
                    .ConfigureAwait(false);

                alarm.SuppressedOrShelved.Value = true;
                alarm.ReceiveTime!.Value += TimeSpan.FromSeconds(1);
                await source.Server.CurrentInstance.ReportEventAsync(context, alarm, ct).ConfigureAwait(false);
                Assert.That((await publisher.ReadAsync(ct).ConfigureAwait(false)).Status,
                    Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                Assert.That(await ReadCapacityAvailabilityAsync(destination, descriptor, ct).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                foreach (Subscription subscription in destination.Session.Subscriptions)
                {
                    Assert.That(await subscription.ConditionRefreshAsync(ct).ConfigureAwait(false), Is.True);
                }
                ArrayOf<Variant> retained = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(retained[0], Is.EqualTo(new Variant(localId)));
                Assert.That(retained[^1], Is.EqualTo(new Variant(false)));
                WoTEventOriginDataType after = await provenance.GetEventProvenanceAsync(localId, ct)
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(after.SourceEventId, Is.EqualTo(sourceId));
                    Assert.That(after.SourceReceiveTime, Is.EqualTo(before.SourceReceiveTime));
                    Assert.That(after.Generation, Is.EqualTo(before.Generation));
                }
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
#endif
