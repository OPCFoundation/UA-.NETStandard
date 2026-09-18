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
        public async Task NativeEventProvenanceUsesPublishedGenerationAsync(string mode)
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
                ByteString originalId = ByteString.From(new byte[] { 0xD3, 0x50, 0x60, 0x70 });
                await source.ReportAsync(originalId, ct).ConfigureAwait(false);
                ArrayOf<Variant> fields = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(fields[0].TryGetValue(out ByteString observedId), Is.True);
                Assert.That(fields[5].TryGetValue(out DateTimeUtc received), Is.True);
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                int metadataIndex = destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon);
                Assert.That(metadataIndex, Is.GreaterThan(0).And.LessThan(ushort.MaxValue),
                    "The published runtime must expose the generated event-binding metadata model.");
                NodeId owner = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                NodeId folder = await FindNativeChildAsync(destination.Session, owner,
                    new QualifiedName("EventBindings", (ushort)metadataIndex), ct).ConfigureAwait(false);
                NodeId descriptor = await FindNativeChildAsync(destination.Session, folder,
                    new QualifiedName("event", (ushort)metadataIndex), ct).ConfigureAwait(false);
                var client = new WoTEventBindingTypeClient(
                    destination.Session, descriptor, NUnitTelemetryContext.Create());
                WoTEventOriginDataType origin = await client.GetEventProvenanceAsync(observedId, ct)
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(origin.IdentityMode, Is.EqualTo(transparent
                        ? WoTEventIdentityModeEnum.TransparentForwarding
                        : WoTEventIdentityModeEnum.LocalReEmission));
                    Assert.That(origin.BindingId, Is.EqualTo("/events/event"));
                    Assert.That(origin.Generation, Is.EqualTo(checked((uint)handle.Generation)));
                    Assert.That(origin.SourceDocumentId, Is.EqualTo(transparent
                        ? "urn:wot:d3:transparent-document" : "urn:wot:d3:document"));
                    Assert.That(origin.SourceServerUri,
                        Is.EqualTo(source.Session.ConfiguredEndpoint.Description.Server.ApplicationUri));
                    Assert.That(origin.SourceEventId, Is.EqualTo(originalId));
                    Assert.That(origin.SourceEventType, Is.EqualTo(new ExpandedNodeId("EventType", SourceNamespace)));
                    Assert.That(origin.SourceNode, Is.EqualTo(new ExpandedNodeId("Pump", SourceNamespace)));
                    Assert.That(origin.SourceTime, Is.EqualTo(SourceTime));
                    Assert.That(origin.SourceReceiveTime, Is.EqualTo(SourceReceiveTime));
                    Assert.That(origin.ReceiveTime, Is.EqualTo(received));
                    Assert.That(origin.EncodingMask, Is.EqualTo(0xCFU));
                }
                ServiceResultException? missing = Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    _ = await client.GetEventProvenanceAsync(
                        ByteString.From(new byte[] { 0x00, 0x50, 0x60, 0x70 }), ct).ConfigureAwait(false);
                });
                Assert.That(missing!.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
                TestContext.Out.WriteLine(
                    $"D3 provenance mode={mode}; generation={origin.Generation}; binding={origin.BindingId}; " +
                    $"sourceEvent={origin.SourceEventId}; sourceReceipt={origin.SourceReceiveTime}; " +
                    $"localReceipt={origin.ReceiveTime}; runtime={Environment.Version}");
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static async Task<NodeId> FindNativeChildAsync(
            ISession session, NodeId parent, QualifiedName name, CancellationToken ct)
        {
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                [
                    new BrowsePath
                    {
                        StartingNode = parent,
                        RelativePath = new RelativePath
                        {
                            Elements =
                            [
                                new RelativePathElement
                                {
                                    ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences,
                                    IncludeSubtypes = true,
                                    TargetName = name
                                }
                            ]
                        }
                    }
                ],
                ct).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            BrowsePathResult result = response.Results[0];
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good), name.ToString());
            Assert.That(result.Targets.Count, Is.EqualTo(1));
            Assert.That(result.Targets[0].RemainingPathIndex, Is.EqualTo(uint.MaxValue));
            return ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, session.NamespaceUris);
        }
    }
}
#endif
