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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotAtomicPublicationNativeTests
    {
        [TestCase(0, false, false, false, 0, false)]
        [TestCase(1, false, false, false, 0, false)]
        [TestCase(2, false, false, false, 0, false)]
        [TestCase(0, true, false, false, 0, false)]
        [TestCase(1, true, false, false, 0, false)]
        [TestCase(2, true, false, false, 0, false)]
        [TestCase(0, false, true, false, 0, false)]
        [TestCase(0, true, true, false, 0, false)]
        [TestCase(0, false, true, true, 0, false)]
        [TestCase(0, false, true, true, 1, false)]
        [TestCase(0, false, true, true, 2, false)]
        [TestCase(0, false, true, true, 0, true)]
        [TestCase(0, true, true, true, 0, true)]
        public async Task SelectedDefinitionOwnerMaterializesThroughTheStockNativePath(
            int referenceForm, bool activeDefinition, bool sharedDefinition, bool perResource, int restart, bool decorated)
        {
            m_coordinator.Dispose();
            var conversionOptions = new WotNodeSetConverterOptions();
            var stock = new WotNodeSetDocumentConverter(conversionOptions);
            var borrowedDocuments = new List<WotDocument>();
            var emissionFlags = new List<bool>();
            IWotDocumentConverter converter = stock;
            if (decorated)
            {
                var decorator = new Mock<IWotDocumentConverter>(MockBehavior.Strict);
                decorator.Setup(value => value.ConvertAsync(
                    It.IsAny<WotResource>(), It.IsAny<ByteString>(), It.IsAny<WotRegistrySnapshot>(),
                    It.IsAny<IReadOnlyDictionary<string, ByteString>>(), It.IsAny<CancellationToken>()))
                    .Returns((WotResource resource, ByteString bytes, WotRegistrySnapshot snapshot,
                        IReadOnlyDictionary<string, ByteString> contents, CancellationToken token) =>
                    {
                        Assert.That(contents, Is.InstanceOf<IWotDocumentConversionContext>());
                        if (borrowedDocuments.Count != 0)
                        {
                            Assert.That(() => borrowedDocuments[0].RootElement.GetRawText(), Throws.Nothing,
                                "A completed publication unit must not release the root capture's declaration image.");
                        }
                        ArrayOf<WotDataTypeDefinitionSource> definitions =
                            ((IWotDocumentConversionContext)contents).GetDataTypeDefinitions(conversionOptions, token);
                        foreach (WotDataTypeDefinitionSource definition in definitions)
                        {
                            borrowedDocuments.Add(definition.Document);
                            emissionFlags.Add(definition.ProjectedSeparately);
                        }
                        return stock.ConvertAsync(resource, bytes, snapshot, contents, token);
                    });
                converter = decorator.Object;
            }
            m_coordinator = new WotMaterializationCoordinator(
                m_registry, new LifecycleWotProjectionHost(m_server.NodeManagerLifecycle),
                documentConverter: converter)
            {
                ServerNamespaceUris = m_server.CurrentInstance.NamespaceUris
            };
            m_coordinator.Event += (_, change) => m_events.Add(change);
            await m_server.NodeManagerLifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, m_registry, m_coordinator),
                callerContext: null).ConfigureAwait(false);
            WotRegistryMutationResult definition = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingModels, ResourceId = "native-types", VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingModel,
                Content = ByteString.From(Encoding.UTF8.GetBytes("""
                    {
                      "@context": {
                        "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                        "types": "urn:r30:native-types"
                      },
                      "@type": ["tm:ThingModel","uav:dataType"],
                      "id": "urn:r30:native-types-document",
                      "title": "Reading",
                      "uav:id": "nsu=urn:r30:native-types;i=3000",
                      "uav:browseName": "types:Reading",
                      "uav:dataTypeDefinitions": [{
                        "@id": "urn:r30:native-reading-definition",
                        "@type": "uav:SimpleDataType",
                        "uav:dataTypeName": "types:Reading",
                        "uav:dataTypeId": "nsu=urn:r30:native-types;i=3000",
                        "uav:dataTypeSubtypeOf": {"uav:dataTypeId":"i=6"}
                      }]
                    }
                    """))
            }).ConfigureAwait(false);
            Assert.That(definition.Changed, Is.True, definition.Message);
            WotResource model = definition.Resource!;
            if (!activeDefinition)
            {
                await m_registry.SetEnabledAsync(model.GroupId, model.ResourceId, false).ConfigureAwait(false);
            }
            string reference = referenceForm switch
            {
                0 => "\"uav:dataTypeDefinition\":{\"@id\":\"urn:r30:native-reading-definition\"}",
                1 => "\"uav:dataTypeName\":\"types:Reading\"",
                _ => "\"uav:dataTypeId\":\"nsu=urn:r30:native-types;i=3000\""
            };
            WotRegistryMutationResult source = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = WotRegistryGroups.ThingDescriptions, ResourceId = "native-sensor", VersionId = "v1",
                Kind = WoTDocumentKindEnum.ThingDescription,
                Content = ByteString.From(Encoding.UTF8.GetBytes($$$"""
                    {
                      "@context": {
                        "uav": "http://opcfoundation.org/UA/WoT-Binding/",
                        "sensor": "urn:r30:native-sensor",
                        "types": "urn:r30:native-types"
                      },
                      "@type": "uav:object",
                      "id": "urn:r30:native-sensor-document",
                      "title": "Sensor",
                      "uav:id": "nsu=urn:r30:native-sensor;s=Sensor",
                      "uav:browseName": "sensor:Sensor",
                      "properties": {
                        "Reading": {
                          "uav:id": "nsu=urn:r30:native-sensor;s=Reading",
                          {{{reference}}}
                        }
                      }
                    }
                    """))
            }).ConfigureAwait(false);
            Assert.That(source.Changed, Is.True, source.Message);
            WotResource consumer = source.Resource!;
            WotRefreshRequest request = HandoffRequest("native-definition-owner");
            if (perResource)
            {
                request.Options.Atomicity = WoTAtomicityEnum.PerResource;
            }
            request.Selection =
            [
                new WoTResourceSelectorDataType
                {
                    Kind = WoTDocumentKindEnum.ThingDescription, Xid = consumer.Xid
                }
            ];
            WotResource? peer = null;
            if (sharedDefinition)
            {
                ByteString original = await m_registry.ReadContentAsync(consumer.DefaultVersion!).ConfigureAwait(false);
                WotRegistryMutationResult added = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
                {
                    GroupId = consumer.GroupId, ResourceId = "native-peer", VersionId = "v1", Kind = consumer.Kind,
                    Content = ByteString.From(Encoding.UTF8.GetBytes(
                        Encoding.UTF8.GetString(original.Span.ToArray())
                            .Replace("native-sensor", "native-peer", StringComparison.Ordinal)))
                }).ConfigureAwait(false);
                Assert.That(added.Changed, Is.True, added.Message);
                peer = added.Resource!;
                request.Selection = request.Selection.Add(new WoTResourceSelectorDataType
                {
                    Kind = peer.Kind, Xid = peer.Xid
                });
            }

            WotRefreshResult result = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);

            Assert.That(result.Summary.Failed, Is.Zero, string.Join("; ", result.Results.Select(row => row.Message)));
            uint generation = perResource && activeDefinition && sharedDefinition ? 3u : 1u;
            Assert.That(result.NewGeneration, Is.EqualTo(generation));
            if (decorated)
            {
                Assert.That(borrowedDocuments, Has.Count.EqualTo(2));
                Assert.That(borrowedDocuments[1], Is.SameAs(borrowedDocuments[0]),
                    "Each captured declaration document is parsed once and shared by its consumers.");
                Assert.That(emissionFlags.Count(flag => !flag), Is.EqualTo(activeDefinition ? 0 : 1));
                Assert.That(() => borrowedDocuments[0].RootElement.GetRawText(),
                    Throws.TypeOf<ObjectDisposedException>(),
                    "The completed capture must release its borrowed declaration documents.");
            }
            if (perResource)
            {
                Assert.That(result.Summary.Atomicity, Is.EqualTo(
                    activeDefinition ? WoTAtomicityEnum.PerResource : WoTAtomicityEnum.PerClosure));
            }
            WotResource active = m_registry.Current.FindResourceByXid(consumer.Xid)!;
            WotResource retained = m_registry.Current.FindResourceByXid(model.Xid)!;
            Assert.That(retained.Enabled, Is.EqualTo(activeDefinition));
            Assert.That(retained.ActiveVersionId, activeDefinition ? Is.EqualTo("v1") : Is.Null);
            Assert.That(retained.RootNodeId.IsNull, Is.EqualTo(!activeDefinition));
            WotResourceVersion captured = activeDefinition ? retained.ActiveVersion! :
                active.CommittedInputs.ToList().Single(input => input.Xid == model.Xid).DefaultVersion!;
            Assert.That(captured.Digest, Is.EqualTo(model.DefaultVersion!.Digest));
            Assert.That(active.DefaultVersion!.DependencySnapshot!.Edges.ToList()
                .Single(edge => edge.SourceXid == consumer.Xid).TargetXid, Is.EqualTo(model.Xid));
            await m_session.FetchNamespaceTablesAsync().ConfigureAwait(false);
            NodeId property = ExpandedNodeId.ToNodeId(
                ExpandedNodeId.Parse("nsu=urn:r30:native-sensor;s=Reading"), m_session.NamespaceUris);
            NodeId dataType = ExpandedNodeId.ToNodeId(
                ExpandedNodeId.Parse("nsu=urn:r30:native-types;i=3000"), m_session.NamespaceUris);
            ReadResponse read = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = property, AttributeId = Attributes.DataType },
                    new ReadValueId { NodeId = dataType, AttributeId = Attributes.NodeClass }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out NodeId actualType), Is.True);
            Assert.That(actualType, Is.EqualTo(dataType));
            Assert.That(read.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[1].WrappedValue.TryGetValue(out int nodeClass), Is.True);
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.DataType));
            if (peer is not null)
            {
                Assert.That(m_registry.Current.FindResourceByXid(peer.Xid)!.ActiveVersionId, Is.EqualTo("v1"));
                NodeId peerProperty = ExpandedNodeId.ToNodeId(
                    ExpandedNodeId.Parse("nsu=urn:r30:native-peer;s=Reading"), m_session.NamespaceUris);
                ReadResponse peerRead = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = peerProperty, AttributeId = Attributes.DataType }],
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(peerRead.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(peerRead.Results[0].WrappedValue.TryGetValue(out NodeId sharedType), Is.True);
                Assert.That(sharedType, Is.EqualTo(dataType));
            }
            WotRegistryMutationResult refused = await m_registry.DeleteResourceAsync(
                model.GroupId, model.ResourceId).ConfigureAwait(false);
            Assert.That(refused.Outcome, Is.EqualTo(WoTOutcomeEnum.Rejected));
            if (decorated)
            {
                WotDocument previousBorrow = borrowedDocuments[0];
                borrowedDocuments.Clear();
                emissionFlags.Clear();
                request.ExpectedGeneration = generation;
                request.Options.Force = true;
                WotRefreshResult refreshed = await m_coordinator.RefreshAsync(request).ConfigureAwait(false);
                Assert.That(refreshed.Summary.Failed, Is.Zero);
                Assert.That(refreshed.NewGeneration, Is.EqualTo(generation + 1),
                    "Replacing the existing overlapping footprint coarsens the next publication to one unit.");
                Assert.That(refreshed.Summary.Atomicity, Is.EqualTo(WoTAtomicityEnum.PerClosure));
                Assert.That(borrowedDocuments, Has.Count.EqualTo(2));
                Assert.That(borrowedDocuments[0], Is.Not.SameAs(previousBorrow));
                Assert.That(borrowedDocuments[1], Is.SameAs(borrowedDocuments[0]));
                Assert.That(() => borrowedDocuments[0].RootElement.GetRawText(), Throws.TypeOf<ObjectDisposedException>());
            }
            if (restart != 0)
            {
                await VerifyCapturedDefinitionRecoveryAsync(model, consumer, restart == 2).ConfigureAwait(false);
            }
        }

        private async Task VerifyCapturedDefinitionRecoveryAsync(
            WotResource model, WotResource consumer, bool legacyIndex)
        {
            ByteString original = await m_registry.ReadContentAsync(model.DefaultVersion!).ConfigureAwait(false);
            WotRegistryMutationResult updated = await m_registry.UpsertResourceAsync(new WotUpsertResourceRequest
            {
                GroupId = model.GroupId, ResourceId = model.ResourceId, Kind = model.Kind, VersionId = "v2",
                Content = ByteString.From(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original.Span.ToArray())
                    .Replace("i=3000", "i=3001", StringComparison.Ordinal)))
            }).ConfigureAwait(false);
            Assert.That(updated.Changed, Is.True, updated.Message);
            WotRegistrySnapshot previous = m_registry.Current;
            string path = Path.Combine(m_root, "registry", "manifest.json");
            if (legacyIndex)
            {
                JsonNode manifest = JsonNode.Parse(File.ReadAllText(path))!;
                void Downgrade(JsonNode? node)
                {
                    if (node is JsonObject value)
                    {
                        if (value["Dependencies"] is JsonObject index)
                        {
                            index.Remove("IndexVersion");
                            index.Remove("DataTypeDefinitionIds");
                            index.Remove("DataTypeDefinitionNames");
                            index["References"] = new JsonArray();
                        }
                        foreach (JsonNode? child in value.Select(property => property.Value))
                        {
                            Downgrade(child);
                        }
                    }
                    else if (node is JsonArray array)
                    {
                        foreach (JsonNode? child in array)
                        {
                            Downgrade(child);
                        }
                    }
                }
                Downgrade(manifest);
                File.WriteAllText(path, manifest.ToJsonString());
            }
            await using PreparedWotTestRuntime runtime = await PreparedWotTestRuntime.StartAsync().ConfigureAwait(false);
            runtime.Namespaces.GetIndexOrAppend("urn:r30:namespace-padding");
            using var store = new FileWotRegistryStore(Path.Combine(m_root, "registry"));
            using var registry = new WotRegistryService(store);
            using var recovered = new WotMaterializationCoordinator(
                registry, runtime.Host, documentConverter: new WotNodeSetDocumentConverter());
            var events = new List<WotMaterializationEventArgs>();
            recovered.Event += (_, change) => events.Add(change);

            await runtime.Lifecycle.AddAsync(new WotRegistryNodeManagerFactory(
                new WotRegistryServerOptions { AutoRefresh = false }, registry, recovered), callerContext: null)
                .ConfigureAwait(false);

            Assert.That(registry.Current.Generation, Is.EqualTo(previous.Generation + (legacyIndex ? 1 : 0)));
            Assert.That(registry.Current.RefreshGeneration, Is.EqualTo(1u));
            Assert.That(registry.Current.FindResourceByXid(model.Xid)!.DefaultVersionId, Is.EqualTo("v2"));
            Assert.That(registry.Current.FindResourceByXid(consumer.Xid)!.CommittedInputs.ToList()
                .Single(input => input.Xid == model.Xid).DefaultVersion!.Digest, Is.EqualTo(model.DefaultVersion!.Digest));
            Assert.That(events, Is.Empty);
            using ISession session = await m_client.ConnectAsync(
                new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{runtime.Port}"), SecurityPolicies.None)
                .ConfigureAwait(false);
            try
            {
                NodeId property = ExpandedNodeId.ToNodeId(
                    ExpandedNodeId.Parse("nsu=urn:r30:native-sensor;s=Reading"), session.NamespaceUris);
                NodeId originalType = ExpandedNodeId.ToNodeId(
                    ExpandedNodeId.Parse("nsu=urn:r30:native-types;i=3000"), session.NamespaceUris);
                NodeId uncommittedType = ExpandedNodeId.ToNodeId(
                    ExpandedNodeId.Parse("nsu=urn:r30:native-types;i=3001"), session.NamespaceUris);
                ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [
                        new ReadValueId { NodeId = property, AttributeId = Attributes.DataType },
                        new ReadValueId { NodeId = originalType, AttributeId = Attributes.NodeClass },
                        new ReadValueId { NodeId = uncommittedType, AttributeId = Attributes.NodeClass }
                    ], CancellationToken.None).ConfigureAwait(false);
                Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Results[0].WrappedValue.TryGetValue(out NodeId actual), Is.True);
                Assert.That(actual, Is.EqualTo(originalType));
                Assert.That(read.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Results[2].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
            finally
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
        }
    }
}
