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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeRetransmissionAndIdentityCollisionHaveDifferentOutcomesAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            destination.Session.Factory.Builder.AddOpcUaWotCon().Commit();
            await source.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            bool transparent = mode == "transparent-forwarding";
            ExpandedNodeId type = new("EventType", transparent ? SourceNamespace : LocalNamespace);
            WotCompiledForm form = EventForm(source.EndpointUrl);
            WotProjectedAffordance declaration = EventDeclaration(mode, type);
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session)));
            WotProjectionHandle handle = await host.AddAsync(
                transparent ? TransparentProjection(form, declaration) : Projection(type, form, declaration), ct)
                .ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeEvents events = await NativeEvents.OpenAsync(destination.Session, ct)
                    .ConfigureAwait(false);
                ByteString originalId = ByteString.From(new byte[] { 0xD3, 0x70, 0x80, 0x90 });
                await ReportOccurrenceAsync(source, originalId, SourceReceiveTime, 612, ct).ConfigureAwait(false);
                ArrayOf<Variant> first = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(first[0].TryGetValue(out ByteString observedId), Is.True);
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                ushort metadataIndex = checked((ushort)destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon));
                NodeId owner = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                NodeId folder = await FindNativeChildAsync(destination.Session, owner,
                    new QualifiedName("EventBindings", metadataIndex), ct).ConfigureAwait(false);
                NodeId descriptor = await FindNativeChildAsync(destination.Session, folder,
                    new QualifiedName("event", metadataIndex), ct).ConfigureAwait(false);
                var client = new WoTEventBindingTypeClient(
                    destination.Session, descriptor, NUnitTelemetryContext.Create());
                WoTEventOriginDataType before = await client.GetEventProvenanceAsync(observedId, ct)
                    .ConfigureAwait(false);
                await Task.Delay(30, ct).ConfigureAwait(false);
                DateTimeUtc laterSourceReceipt = new(2026, 8, 1, 1, 2, 5);
                await ReportOccurrenceAsync(source, originalId, laterSourceReceipt, 612, ct).ConfigureAwait(false);
                WoTEventOriginDataType after = before;
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    after = await client.GetEventProvenanceAsync(observedId, ct).ConfigureAwait(false);
                    if (after.SourceReceiveTime == laterSourceReceipt)
                    {
                        break;
                    }
                    await Task.Delay(25, ct).ConfigureAwait(false);
                }
                Assert.That(after.SourceReceiveTime, Is.EqualTo(laterSourceReceipt));
                Assert.That(after.ReceiveTime, Is.GreaterThan(before.ReceiveTime));
                Assert.That(after.SourceEventId, Is.EqualTo(originalId));
                Assert.That(after.SourceTime, Is.EqualTo(SourceTime));
                Assert.That(after.Generation, Is.EqualTo(before.Generation));

                ByteString probeId = ByteString.From(new byte[] { 0xD3, 0x70, 0x80, 0x91 });
                await ReportOccurrenceAsync(source, probeId, laterSourceReceipt, 614, ct).ConfigureAwait(false);
                ArrayOf<Variant> next = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(next[7].TryGetValue(out ushort severity), Is.True);
                Assert.That(severity, Is.EqualTo((ushort)614), "A retransmission must not be re-published.");

                NodeId availabilityId = await FindNativeChildAsync(destination.Session, descriptor,
                    new QualifiedName("Availability", metadataIndex), ct).ConfigureAwait(false);
                await ReportOccurrenceAsync(source, originalId, laterSourceReceipt, 999, ct).ConfigureAwait(false);
                StatusCode availability = StatusCodes.Good;
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    DataValue value = await destination.Session.ReadValueAsync(availabilityId, ct)
                        .ConfigureAwait(false);
                    Assert.That(value.WrappedValue.TryGetValue(out availability), Is.True);
                    if (StatusCode.IsBad(availability))
                    {
                        break;
                    }
                    await Task.Delay(25, ct).ConfigureAwait(false);
                }
                Assert.That(availability, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                WoTEventOriginDataType retained = await client.GetEventProvenanceAsync(observedId, ct)
                    .ConfigureAwait(false);
                Assert.That(retained.ReceiveTime, Is.EqualTo(after.ReceiveTime));
                Assert.That(retained.SourceEventId, Is.EqualTo(originalId));
                Assert.That(retained.SourceReceiveTime, Is.EqualTo(laterSourceReceipt));
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static async Task ReportOccurrenceAsync(
            NativeEndpoint endpoint,
            ByteString eventId,
            DateTimeUtc sourceReceipt,
            ushort severity,
            CancellationToken ct)
        {
            var context = endpoint.Server.CurrentInstance.DefaultSystemContext;
            var state = new BaseEventState(null)
            {
                TypeDefinitionId = ExpandedNodeId.Parse(
                    "nsu=" + SourceNamespace + ";s=EventType", context.NamespaceUris)
            };
            state.EventId = PropertyState<ByteString>.With<VariantBuilder>(state, eventId);
            state.EventType = PropertyState<NodeId>.With<VariantBuilder>(state, state.TypeDefinitionId);
            state.SourceNode = PropertyState<NodeId>.With<VariantBuilder>(state,
                ExpandedNodeId.Parse("nsu=" + SourceNamespace + ";s=Pump", context.NamespaceUris));
            state.SourceName = PropertyState<string>.With<VariantBuilder>(state, "Pump");
            state.Time = PropertyState<DateTimeUtc>.With<VariantBuilder>(state, SourceTime);
            state.ReceiveTime = PropertyState<DateTimeUtc>.With<VariantBuilder>(state, sourceReceipt);
            state.Message = PropertyState<LocalizedText>.With<VariantBuilder>(
                state, new LocalizedText("Source occurrence"));
            state.Severity = PropertyState<ushort>.With<VariantBuilder>(state, severity);
            await endpoint.Server.CurrentInstance.ReportEventAsync(context, state, ct).ConfigureAwait(false);
        }
    }
}
#endif
