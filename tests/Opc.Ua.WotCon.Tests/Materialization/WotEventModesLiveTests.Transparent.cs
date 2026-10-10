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
using Opc.Ua.Export;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeTransparentEventModePreservesOccurrenceProvenanceAsync(bool managed)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(managed, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            await source.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            await using NativeEvents sourceEvents = await NativeEvents.OpenAsync(source.Session, ct)
                .ConfigureAwait(false);
            ExpandedNodeId expectedType = new("EventType", SourceNamespace);
            ExpandedNodeId expectedSource = new("Pump", SourceNamespace);
            WotCompiledForm form = EventForm(source.EndpointUrl);
            WotProjectedAffordance declaration = EventDeclaration("transparent-forwarding", expectedType);
            var factory = new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session));
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle, factory);
            WotProjectionHandle handle = await host.AddAsync(
                TransparentProjection(form, declaration), ct).ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeEvents events = await NativeEvents.OpenAsync(destination.Session, ct)
                    .ConfigureAwait(false);
                ByteString originalId = ByteString.From(new byte[] { 0xD3, 0x10, 0x20, 0x30 });
                DateTimeUtc boundary = DateTimeUtc.Now;
                await source.ReportAsync(originalId, ct).ConfigureAwait(false);
                ArrayOf<Variant> sourceFields = await sourceEvents.ReadAsync(ct).ConfigureAwait(false);
                ArrayOf<Variant> fields = await events.ReadAsync(ct).ConfigureAwait(false);

                Assert.That(sourceFields.Count, Is.EqualTo(8));
                Assert.That(sourceFields[0].TryGetValue(out ByteString upstreamEventId), Is.True);
                Assert.That(sourceFields[1].TryGetValue(out NodeId upstreamEventType), Is.True);
                Assert.That(sourceFields[2].TryGetValue(out NodeId upstreamSourceNode), Is.True);
                Assert.That(sourceFields[4].TryGetValue(out DateTimeUtc upstreamTime), Is.True);
                Assert.That(sourceFields[5].TryGetValue(out DateTimeUtc upstreamReceived), Is.True);
                Assert.That(fields.Count, Is.EqualTo(8));
                Assert.That(fields[0].TryGetValue(out ByteString eventId), Is.True);
                Assert.That(fields[1].TryGetValue(out NodeId eventType), Is.True);
                Assert.That(fields[2].TryGetValue(out NodeId sourceNode), Is.True);
                Assert.That(fields[3].TryGetValue(out string? sourceName), Is.True);
                Assert.That(fields[4].TryGetValue(out DateTimeUtc time), Is.True);
                Assert.That(fields[5].TryGetValue(out DateTimeUtc received), Is.True);
                Assert.That(fields[6].TryGetValue(out LocalizedText message), Is.True);
                Assert.That(fields[7].TryGetValue(out ushort severity), Is.True);

                ExpandedNodeId upstreamType = NodeId.ToExpandedNodeId(upstreamEventType, source.Session.NamespaceUris);
                ExpandedNodeId upstreamSource = NodeId.ToExpandedNodeId(
                    upstreamSourceNode, source.Session.NamespaceUris);
                ExpandedNodeId actualType = NodeId.ToExpandedNodeId(eventType, destination.Session.NamespaceUris);
                ExpandedNodeId actualSource = NodeId.ToExpandedNodeId(sourceNode, destination.Session.NamespaceUris);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(upstreamEventId, Is.EqualTo(originalId));
                    Assert.That(upstreamType, Is.EqualTo(expectedType));
                    Assert.That(upstreamSource, Is.EqualTo(expectedSource));
                    Assert.That(upstreamTime, Is.EqualTo(SourceTime));
                    Assert.That(upstreamReceived, Is.EqualTo(SourceReceiveTime));
                    Assert.That(eventId, Is.EqualTo(originalId));
                    Assert.That(actualType, Is.EqualTo(expectedType));
                    Assert.That(actualSource, Is.EqualTo(expectedSource));
                    Assert.That(sourceName, Is.EqualTo("Pump"));
                    Assert.That(time, Is.EqualTo(SourceTime));
                    Assert.That(received, Is.GreaterThanOrEqualTo(boundary));
                    Assert.That(received, Is.Not.EqualTo(SourceReceiveTime));
                    Assert.That(message.Text, Is.EqualTo("Source occurrence"));
                    Assert.That(severity, Is.EqualTo((ushort)612));
                    Assert.That(source.Session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(destination.Session.ConfiguredEndpoint.Description.SecurityMode,
                        Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                }
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static WotProjectionDocument TransparentProjection(
            WotCompiledForm form, WotProjectedAffordance declaration)
        {
            var eventTypes = new UANodeSet
            {
                NamespaceUris = [SourceNamespace],
                Models = [new ModelTableEntry { ModelUri = SourceNamespace, Version = "1.0.0" }],
                Items = [EventType(1)]
            };
            UAObject owner = Object("ns=1;s=Owner", "1:Owner");
            owner.References =
            [
                .. owner.References!,
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
                Items = [owner]
            };
            WotBindingPlan plan = new WotBindingPlan("urn:wot:d3:transparent-document", [], [form], [], [])
                .WithProjectedAffordances([declaration]);
            return new WotProjectionDocument(
                "d3-transparent-owned-events",
                [
                    new WotProjectionSource(
                        "d3-transparent-source-types", [SourceNamespace], Serialize(eventTypes)),
                    new WotProjectionSource(
                        "d3-transparent-local-notifier", [LocalNamespace], Serialize(nodes))
                ],
                [plan]);
        }
    }
}
#endif
