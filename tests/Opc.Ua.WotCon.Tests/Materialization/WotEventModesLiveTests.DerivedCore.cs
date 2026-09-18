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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
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
        public async Task NativeDerivedConditionFieldsArePrivateToProjectionAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            UANodeSet nodes = ConditionSourceNodes();
            nodes.Items![2] = ConditionEventType(1, Ua.ObjectTypeIds.LimitAlarmType.ToString());
            await source.InstallAsync(nodes, ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            WotEventSelection selection = PrivatePublicSelection("Core");
            var nativeSelection = new WotEventSelection(
                [
                    .. s_privateCoreSelection.Clauses,
                    new WotResolvedEventSelectClause(
                        Ua.ObjectTypeIds.AcknowledgeableConditionType.ToString(), "AckedState"),
                    new WotResolvedEventSelectClause(
                        Ua.ObjectTypeIds.AcknowledgeableConditionType.ToString(), "AckedState/Id"),
                    new WotResolvedEventSelectClause(Ua.ObjectTypeIds.AlarmConditionType.ToString(), "ActiveState"),
                    new WotResolvedEventSelectClause(Ua.ObjectTypeIds.AlarmConditionType.ToString(), "ActiveState/Id"),
                    new WotResolvedEventSelectClause(Ua.ObjectTypeIds.AlarmConditionType.ToString(), "InputNode"),
                    new WotResolvedEventSelectClause(
                        Ua.ObjectTypeIds.AlarmConditionType.ToString(), "SuppressedOrShelved")
                ],
                WotEventSelectionOrigin.Standard);
            var publisher = new CapacityObservedPublisher();
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session), null, publisher,
                    new WotProjectionConditionFactory(), new WotProjectionBindingRuntimeOptions()));
            WotProjectionHandle handle = await host.AddAsync(ConditionProjection(
                mode, source.EndpointUrl, selection, conditionType: Ua.ObjectTypeIds.LimitAlarmType), ct)
                .ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeConditionEvents originalEvents = await NativeConditionEvents.OpenAsync(
                    source.Session, ct, selection: nativeSelection).ConfigureAwait(false);
                await using NativeConditionEvents events = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct, selection: nativeSelection).ConfigureAwait(false);
                WotCompiledForm form = ConditionProjection(mode, destination.EndpointUrl, selection,
                    conditionType: Ua.ObjectTypeIds.LimitAlarmType).BindingPlans[0].CompiledForms[0];
                await using IWotBindingChannel publicChannel = await new NativeChannels(destination.Session)
                    .OpenChannelAsync(form, ct).ConfigureAwait(false);
                var publicEvents = Channel.CreateBounded<WotNotification>(4);
                await using IWotSubscription subscription = await publicChannel.SubscribeEventAsync(
                    notification => CapturePublicCondition(notification, publicEvents.Writer), ct)
                    .ConfigureAwait(false);

                var context = source.Server.CurrentInstance.DefaultSystemContext;
                ByteString sourceId = ByteString.From(new byte[] { 0xD3, 0xC6, 0x01 });
                LimitAlarmState alarm = WotRetainedConditionRefreshTests.CreateAlarm(
                    context,
                    ExpandedNodeId.Parse("nsu=" + SourceNamespace + ";s=ConditionEventType", context.NamespaceUris),
                    ExpandedNodeId.Parse("nsu=" + SourceNamespace + ";s=ConditionA", context.NamespaceUris),
                    sourceId, SourceTime);
                alarm.ReceiveTime!.Value = SourceReceiveTime;
                alarm.AckedState!.Value = new LocalizedText("Acknowledged at source");
                alarm.AckedState.Id!.Value = true;
                alarm.ActiveState!.Value = new LocalizedText("Active at source");
                alarm.ActiveState.Id!.Value = true;
                alarm.InputNode!.Value = ExpandedNodeId.Parse(
                    "nsu=" + SourceNamespace + ";s=PumpA", context.NamespaceUris);
                alarm.SuppressedOrShelved!.Value = false;
                await source.Server.CurrentInstance.ReportEventAsync(context, alarm, ct).ConfigureAwait(false);
                Assert.That((await publisher.ReadAsync(ct).ConfigureAwait(false)).Status, Is.EqualTo(StatusCodes.Good));
                ArrayOf<Variant> original = await originalEvents.ReadAsync(ct).ConfigureAwait(false);
                ArrayOf<Variant> actual = await events.ReadAsync(ct).ConfigureAwait(false);
                WotNotification publicEvent = await publicEvents.Reader.ReadAsync(ct).ConfigureAwait(false);
                int firstDerived = s_privateCoreSelection.Clauses.Count;
                Assert.That(original[firstDerived], Is.EqualTo(new Variant(alarm.AckedState.Value)));
                Assert.That(original[firstDerived + 1], Is.EqualTo(new Variant(true)));
                Assert.That(original[firstDerived + 2], Is.EqualTo(new Variant(alarm.ActiveState.Value)));
                Assert.That(original[firstDerived + 3], Is.EqualTo(new Variant(true)));
                Assert.That(original[firstDerived + 4], Is.EqualTo(new Variant(alarm.InputNode.Value)));
                Assert.That(original[firstDerived + 5], Is.EqualTo(new Variant(false)));
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(actual.Count, Is.EqualTo(nativeSelection.Clauses.Count));
                    Assert.That(actual[0].TryGetValue(out ByteString eventId), Is.True);
                    Assert.That(eventId.IsEmpty, Is.False);
                    Assert.That(eventId == sourceId, Is.EqualTo(mode == "transparent-forwarding"));
                    for (int index = firstDerived; index < actual.Count; index++)
                    {
                        if (nativeSelection.Clauses[index].BrowsePath == "InputNode")
                        {
                            Assert.That(actual[index].TryGetValue(out NodeId inputNode), Is.True);
                            Assert.That(NodeId.ToExpandedNodeId(inputNode, destination.Session.NamespaceUris),
                                Is.EqualTo(new ExpandedNodeId("PumpA", SourceNamespace)));
                        }
                        else
                        {
                            Assert.That(actual[index], Is.EqualTo(original[index]),
                                nativeSelection.Clauses[index].BrowsePath);
                        }
                    }
                    Assert.That(selection.Clauses.Count, Is.EqualTo(1));
                    Assert.That(selection.Clauses[0].BrowsePath, Is.EqualTo("ConditionName"));
                    Assert.That(publicChannel.Form.EventSelection, Is.SameAs(selection));
                }
                AssertPublicConditionShape(publicEvent, selection, actual);
                Assert.That(publicEvent.Data.TryGetValue(["ActiveState"], out _), Is.False);
                Assert.That(publicEvent.Data.TryGetValue(["AckedState"], out _), Is.False);
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

    }
}
#endif
