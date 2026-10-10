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
using System.Threading;
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
        [Test]
        public async Task NativeLocalOccurrenceRejectsDifferentForwardedOwnerAsync()
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
            second.Server.CurrentInstance.NamespaceUris.GetIndexOrAppend("urn:wot:d3:identity:asymmetric");
            await first.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await second.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await first.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            await second.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var channels = new EndpointNativeChannels(new Dictionary<string, ISession>(StringComparer.Ordinal)
            {
                [first.EndpointUrl] = first.Session,
                [second.EndpointUrl] = second.Session
            });
            var publisher = new CapacityObservedPublisher();
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(channels, null, publisher,
                    new WotProjectionConditionFactory(), new WotProjectionBindingRuntimeOptions()));
            WotProjectionHandle handle = await host.AddAsync(
                MixedIdentityProjection(first.EndpointUrl, second.EndpointUrl), ct).ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeEvents events = await NativeEvents.OpenAsync(destination.Session, ct)
                    .ConfigureAwait(false);
                ByteString sourceId = ByteString.From(new byte[] { 0xD3, 0xC3, 0x01, 0x01 });
                await first.ReportAsync(sourceId, ct).ConfigureAwait(false);
                ProjectionObservation accepted = await publisher.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(accepted.Status, Is.EqualTo(StatusCodes.Good));
                ArrayOf<Variant> local = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(local[0].TryGetValue(out ByteString localId), Is.True);
                Assert.That(localId, Is.Not.EqualTo(sourceId));
                NodeId descriptor = await CapacityDescriptorAsync(destination, "event", ct).ConfigureAwait(false);
                var provenance = new WoTEventBindingTypeClient(
                    destination.Session, descriptor, NUnitTelemetryContext.Create());
                WoTEventOriginDataType before = await provenance.GetEventProvenanceAsync(localId, ct)
                    .ConfigureAwait(false);

                await second.ReportAsync(localId, ct).ConfigureAwait(false);
                ProjectionObservation conflicting = await publisher.ReadAsync(ct).ConfigureAwait(false);
                int published = 1;
                if (StatusCode.IsGood(conflicting.Status))
                {
                    ArrayOf<Variant> duplicate = await events.ReadAsync(ct).ConfigureAwait(false);
                    Assert.That(duplicate[0].TryGetValue(out ByteString duplicateId), Is.True);
                    Assert.That(duplicateId, Is.EqualTo(localId), "The original counterexample must be preserved.");
                    published++;
                }
                NodeId rejectedDescriptor = await IdentityDescriptorAsync(destination, ct).ConfigureAwait(false);
                StatusCode availability = await ReadCapacityAvailabilityAsync(destination, rejectedDescriptor, ct)
                    .ConfigureAwait(false);
                WoTEventOriginDataType after = await provenance.GetEventProvenanceAsync(localId, ct)
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(conflicting.Status, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                    Assert.That(availability, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                    Assert.That(published, Is.EqualTo(1), "Different physical sources must not publish the same ID.");
                    Assert.That(after.SourceEventId, Is.EqualTo(sourceId));
                    Assert.That(after.ReceiveTime, Is.EqualTo(before.ReceiveTime));
                    Assert.That(after.Generation, Is.EqualTo(checked((uint)handle.Generation)));
                    Assert.That(first.Session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(second.Session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(destination.Session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(first.Session.NamespaceUris.GetIndex(SourceNamespace),
                        Is.Not.EqualTo(second.Session.NamespaceUris.GetIndex(SourceNamespace)));
                }
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativePublicationAndForwardingCannotShareAnEventIdAsync(bool forwardedFirst)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            await source.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var publisher = new CapacityObservedPublisher();
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle,
                new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session), null, publisher,
                    new WotProjectionConditionFactory(), new WotProjectionBindingRuntimeOptions()));
            ExpandedNodeId type = new("EventType", SourceNamespace);
            WotProjectionDocument original = TransparentProjection(
                EventForm(source.EndpointUrl), EventDeclaration("transparent-forwarding", type));
            var document = new WotProjectionDocument(original.ClosureKey,
                [
                    new WotProjectionSource("native-identity-source", [SourceNamespace], Serialize(SourceNodes())),
                    original.Sources[1]
                ], original.BindingPlans);
            WotProjectionHandle handle = await host.AddAsync(document, ct).ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeEvents events = await NativeEvents.OpenAsync(destination.Session, ct)
                    .ConfigureAwait(false);
                ByteString eventId = ByteString.From(new byte[] { 0xD3, 0xC3, 0x02, 0x01 });
                if (forwardedFirst)
                {
                    await source.ReportAsync(eventId, ct).ConfigureAwait(false);
                    Assert.That((await publisher.ReadAsync(ct).ConfigureAwait(false)).Status,
                        Is.EqualTo(StatusCodes.Good));
                }
                else
                {
                    await destination.ReportAsync(eventId, ct).ConfigureAwait(false);
                }
                ArrayOf<Variant> first = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(first[0].TryGetValue(out ByteString firstId), Is.True);
                Assert.That(firstId, Is.EqualTo(eventId));
                StatusCode rejection = StatusCodes.Good;
                if (forwardedFirst)
                {
                    try
                    {
                        await destination.ReportAsync(eventId, ct).ConfigureAwait(false);
                    }
                    catch (ServiceResultException exception)
                    {
                        rejection = exception.StatusCode;
                    }
                }
                else
                {
                    await source.ReportAsync(eventId, ct).ConfigureAwait(false);
                    rejection = (await publisher.ReadAsync(ct).ConfigureAwait(false)).Status;
                }
                int published = 1;
                if (StatusCode.IsGood(rejection))
                {
                    ArrayOf<Variant> duplicate = await events.ReadAsync(ct).ConfigureAwait(false);
                    Assert.That(duplicate[0].TryGetValue(out ByteString duplicateId), Is.True);
                    Assert.That(duplicateId, Is.EqualTo(eventId));
                    published++;
                }
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(rejection, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                    Assert.That(published, Is.EqualTo(1),
                        "Equal fields from a native publisher and a remote authority are not shared ownership.");
                    Assert.That(source.Session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(destination.Session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                }
                if (!forwardedFirst)
                {
                    NodeId descriptor = await CapacityDescriptorAsync(destination, "event", ct).ConfigureAwait(false);
                    Assert.That(await ReadCapacityAvailabilityAsync(destination, descriptor, ct).ConfigureAwait(false),
                        Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                }
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static async Task<NodeId> IdentityDescriptorAsync(NativeEndpoint destination, CancellationToken ct)
        {
            NodeId owner = ExpandedNodeId.Parse(
                "nsu=" + LocalNamespace + ";s=OtherOwner", destination.Session.NamespaceUris);
            ushort metadata = checked((ushort)destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon));
            NodeId folder = await FindNativeChildAsync(destination.Session, owner,
                new QualifiedName("EventBindings", metadata), ct).ConfigureAwait(false);
            return await FindNativeChildAsync(destination.Session, folder, new QualifiedName("other", metadata), ct)
                .ConfigureAwait(false);
        }

        private static WotProjectionDocument MixedIdentityProjection(string firstEndpoint, string secondEndpoint)
        {
            WotCompiledForm first = EventForm(firstEndpoint);
            var second = new WotCompiledForm(
                first.Binding, Opc.Ua.WotCon.Bindings.WotAffordanceKind.Event, "other", "/events/other/forms/0",
                WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent",
                new WotEndpointDescriptor("opc.tcp", null, -1, secondEndpoint),
                new WotAddressingDescriptor("i=2253"),
                new WotOperationDescriptor(WoTBindingCapabilityEnum.SubscribeEvent, "subscribeevent", "Monitor"),
                new WotPayloadDescriptor("application/opcua+uabinary", "opcua"), [], true,
                null, WotEventSelection.Default, null);
            ExpandedNodeId localType = new("EventType", LocalNamespace);
            ExpandedNodeId sourceType = new("EventType", SourceNamespace);
            WotProjectedAffordance local = EventDeclaration("local-re-emission", localType);
            WotProjectedAffordance forwarded = new WotProjectedAffordance(
                Opc.Ua.WotCon.Bindings.WotAffordanceKind.Event, "other", "/events/other", sourceType.ToString(),
                new ExpandedNodeId("OtherOwner", LocalNamespace).ToString())
                .WithIdentityMode(WoTEventIdentityModeEnum.TransparentForwarding);
            UAObject other = Object("ns=1;s=OtherOwner", "1:OtherOwner");
            other.References =
            [
                .. other.References!,
                new Reference
                {
                    ReferenceType = Ua.ReferenceTypeIds.GeneratesEvent.ToString(),
                    Value = "ns=2;s=EventType"
                }
            ];
            var nodes = new UANodeSet
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
                Items = [Object("ns=1;s=Owner", "1:Owner"), other, EventType(1)]
            };
            WotBindingPlan plan = new WotBindingPlan("urn:wot:d3:mixed-identity", [], [first, second], [], [])
                .WithProjectedAffordances([local, forwarded]);
            return new WotProjectionDocument("mixed-identity",
                [
                    new WotProjectionSource("mixed-source", [SourceNamespace], Serialize(SourceNodes())),
                    new WotProjectionSource("mixed-notifiers", [LocalNamespace], Serialize(nodes))
                ], [plan]);
        }
    }
}
#endif
