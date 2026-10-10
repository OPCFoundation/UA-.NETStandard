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
 *
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Tests
{
    public sealed partial class WotLegacyNativeAdmissionLiveTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ProspectiveFileIdentityIsReservedBeforeDiscoveryOrRestore(bool persisted)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            ByteString content = NativeDocument(WotNodeSetPreservationMode.Always, "mapped", modify: model =>
            {
                foreach (XElement element in model.Descendants())
                {
                    XAttribute? id = element.Attribute("NodeId");
                    if (id?.Value == "ns=1;s=Assets/mapped/props/Speed")
                    {
                        id.Value = "ns=1;s=Assets/mapped/File";
                    }
                    if (element.Name == s_nodes + "Reference" &&
                        element.Value == "ns=1;s=Assets/mapped/props/Speed")
                    {
                        element.Value = "ns=1;s=Assets/mapped/File";
                    }
                }
            });
            if (persisted)
            {
                await fixture.RestartManagerAsync(content);
            }
            else
            {
                fixture.Options.Discovery = new Discovery(content);
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.Client.CreateAssetForEndpointAsync("mapped", "sim://opcua.test/wot/mapped"));
            }
            Assert.That(fixture.Provider.Connects, Is.Zero);
            var assets = new List<WotAssetEntry>();
            await foreach (WotAssetEntry asset in fixture.Client.EnumerateAssetsAsync())
            {
                assets.Add(asset);
            }
            Assert.That(assets, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EveryProspectiveFixedChildIdentityIsReservedBeforeDiscoveryOrRestore(bool persisted)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            ArrayOf<NodeId> fixedIds = await PublishedFixedChildIdsAsync(fixture);
            for (int index = 0; index < fixedIds.Count; index++)
            {
                NodeId nodeId = fixedIds[index];
                ByteString content = NativeDocumentWithPropertyIdentity(nodeId.WithNamespaceIndex(1).ToString());
                if (persisted)
                {
                    await fixture.RestartManagerAsync(content);
                    Assert.That(await ReadPersistedDocumentAsync(fixture), Is.EqualTo(content), nodeId.ToString());
                }
                else
                {
                    fixture.Options.Discovery = new Discovery(content);
                    ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await fixture.Client.CreateAssetForEndpointAsync(
                            "mapped", "sim://opcua.test/wot/mapped"), nodeId.ToString())!;
                    Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists), nodeId.ToString());
                }
                Assert.That(fixture.Provider.Connects, Is.Zero, nodeId.ToString());
                Assert.That(fixture.Recorder.ObservedPublishedOwner, Is.False, nodeId.ToString());
                await AssertNoPublishedAssetsAsync(fixture);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeRootAndNonReservedFileIdentitiesRemainAvailable(bool persisted)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            ushort ns = fixture.Session.NamespaceUris.GetIndexOrAppend(fixture.Options.AssetNamespaceUri);
            var ownerId = new NodeId("Assets/mapped", ns);
            var propertyId = new NodeId("Assets/mapped/File/Unreserved", ns);
            ByteString content = NativeDocumentWithPropertyIdentity(propertyId.WithNamespaceIndex(1).ToString());
            WotAssetClient asset;
            if (persisted)
            {
                await fixture.RestartManagerAsync(content);
                asset = await fixture.Client.OpenAssetAsync(ownerId);
            }
            else
            {
                fixture.Options.Discovery = new Discovery(content);
                asset = await fixture.Client.CreateAssetForEndpointAsync("mapped", "sim://opcua.test/wot/mapped");
            }
            ByteString admitted = fixture.Recorder.LastContent;
            if (persisted)
            {
                Assert.That(admitted, Is.EqualTo(content));
            }
            Assert.That(asset.AssetId, Is.EqualTo(ownerId));
            Assert.That(fixture.Provider.Connects, Is.EqualTo(1));
            Assert.That(fixture.Recorder.ObservedPublishedOwner, Is.False);
            Assert.That(await fixture.TypeAsync(asset.AssetId),
                Is.EqualTo(new NodeId(4001u, fixture.ModelNamespaceIndex)));
            ArrayOf<WotAssetVariableEntry> properties = await PropertiesAsync(asset);
            Assert.That(properties.Count, Is.EqualTo(1));
            Assert.That(properties[0].NodeId, Is.EqualTo(propertyId));
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(admitted.Span.ToArray()));
            ArrayOf<ReferenceDescription> interfaces = await ReferencesAsync(
                fixture, asset.AssetId, Ua.ReferenceTypeIds.HasInterface, BrowseDirection.Forward);
            Assert.That(interfaces.Count, Is.EqualTo(1));

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await asset.UploadThingDescriptionAsync(NativeDocumentWithPropertyIdentity(
                    "ns=1;s=Assets/mapped/File/Read/OutputArguments").Span.ToArray()))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
            Assert.That(fixture.Provider.Connects, Is.EqualTo(1));
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(admitted.Span.ToArray()));
            Assert.That(await ReadPersistedDocumentAsync(fixture), Is.EqualTo(admitted));
            Assert.That((await PropertiesAsync(asset)).ToList().Select(property => property.NodeId),
                Is.EqualTo(new[] { propertyId }));
            ArrayOf<DataValue> values = await fixture.ReadAttributesAsync(propertyId, [Attributes.Value]);
            Assert.That(values[0].WrappedValue.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(42.5));
        }

        [TestCase(WotNodeSetPreservationMode.Always)]
        [TestCase(WotNodeSetPreservationMode.Never)]
        public async Task DiscoverySerializationRetainsNativeIdentityMetadata(WotNodeSetPreservationMode mode)
        {
            ByteString content = NativeDocumentWithPropertyIdentity("ns=1;s=Assets/mapped/File", mode);
            ThingDescription discovered = await new Discovery(content).CreateThingDescriptionAsync(
                "mapped", "sim://opcua.test/wot/mapped", CancellationToken.None);
            ByteString serialized = ByteString.From(JsonSerializer.SerializeToUtf8Bytes(
                discovered, ThingDescriptionJsonContext.Default.ThingDescription));
            using WotDocument original = WotDocument.Parse(content.Memory);
            using WotDocument retained = WotDocument.Parse(serialized.Memory);
            string member = mode == WotNodeSetPreservationMode.Always ? "nodeSet" : "nodes";
            Assert.That(original.TryGetUav(member, out JsonElement before), Is.True);
            Assert.That(retained.TryGetUav(member, out JsonElement after), Is.True);
            Assert.That(JsonElement.DeepEquals(before, after), Is.True);
        }

        [TestCase("properties/reading", "uav:id")]
        [TestCase("properties/reading/items", "uav:dataTypeId")]
        [TestCase("actions/reset", "uav:id")]
        [TestCase("actions/reset/input", "uav:allowSubTypes")]
        [TestCase("actions/reset/input/properties/value", "uav:dataTypeId")]
        [TestCase("events/alarm", "@type")]
        public async Task DiscoverySerializationRetainsNestedMappingMetadata(string path, string member)
        {
            ByteString content = Document(properties: """
                {"reading":{"type":"array","uav:id":"nsu=urn:test:discovery;s=reading",
                    "items":{"type":"number","uav:dataTypeId":"i=11"}}}
                """, extra: """
                "actions":{"reset":{"uav:id":"nsu=urn:test:discovery;s=reset",
                    "input":{"type":"object","uav:allowSubTypes":false,
                        "properties":{"value":{"type":"number","uav:dataTypeId":"i=11"}}}}},
                "events":{"alarm":{"@type":["uav:eventType","ua:AlarmConditionType"]}},
                """);
            ThingDescription discovered = await new Discovery(content).CreateThingDescriptionAsync(
                "mapped", "sim://opcua.test/wot/mapped", CancellationToken.None);
            ByteString serialized = ByteString.From(JsonSerializer.SerializeToUtf8Bytes(
                discovered, ThingDescriptionJsonContext.Default.ThingDescription));
            using WotDocument original = WotDocument.Parse(content.Memory);
            using WotDocument retained = WotDocument.Parse(serialized.Memory);
            JsonElement before = original.RootElement;
            JsonElement after = retained.RootElement;
            foreach (string segment in path.Split('/'))
            {
                before = before.GetProperty(segment);
                after = after.GetProperty(segment);
            }
            Assert.That(after.TryGetProperty(member, out JsonElement metadata), Is.True, path);
            Assert.That(JsonElement.DeepEquals(before.GetProperty(member), metadata), Is.True, path);
        }

        private static async Task<ArrayOf<NodeId>> PublishedFixedChildIdsAsync(Fixture fixture)
        {
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            var pending = new Queue<NodeId>();
            var fixedIds = new HashSet<NodeId> { asset.AssetId };
            pending.Enqueue(asset.AssetId);
            while (pending.Count != 0)
            {
                BrowseResponse result = await fixture.Session.BrowseAsync(null, new ViewDescription(), 0,
                    [new BrowseDescription
                    {
                        NodeId = pending.Dequeue(),
                        ReferenceTypeId = Ua.ReferenceTypeIds.HierarchicalReferences,
                        BrowseDirection = BrowseDirection.Forward,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable | NodeClass.Method),
                        ResultMask = (uint)BrowseResultMask.All
                    }], default);
                Assert.That(result.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.Results[0].ContinuationPoint.Length, Is.Zero);
                ArrayOf<ReferenceDescription> references = result.Results[0].References;
                for (int index = 0; index < references.Count; index++)
                {
                    NodeId nodeId = ExpandedNodeId.ToNodeId(references[index].NodeId, fixture.Session.NamespaceUris);
                    if (fixedIds.Add(nodeId))
                    {
                        pending.Enqueue(nodeId);
                    }
                }
            }
            foreach (string child in new[]
            {
                "Size", "Read", "Read/InputArguments", "Read/OutputArguments",
                "CloseAndUpdate", "CloseAndUpdate/InputArguments"
            })
            {
                Assert.That(fixedIds, Does.Contain(new NodeId(
                    $"Assets/mapped/File/{child}", asset.AssetId.NamespaceIndex)), child);
            }
            Assert.That(fixedIds.Remove(asset.AssetId), Is.True);
            Assert.That(fixedIds.Remove(new NodeId("Assets/mapped/File", asset.AssetId.NamespaceIndex)), Is.True);
            await fixture.Client.DeleteAssetAsync(asset.AssetId);
            await AssertNoPublishedAssetsAsync(fixture);
            return fixedIds.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArrayOf();
        }

        private static ByteString NativeDocumentWithPropertyIdentity(
            string nodeId, WotNodeSetPreservationMode mode = WotNodeSetPreservationMode.Always)
        {
            return NativeDocument(mode, "mapped", modify: model =>
            {
                foreach (XElement element in model.Descendants())
                {
                    XAttribute? id = element.Attribute("NodeId");
                    if (id?.Value == "ns=1;s=Assets/mapped/props/Speed")
                    {
                        id.Value = nodeId;
                    }
                    if (element.Name == s_nodes + "Reference" &&
                        element.Value == "ns=1;s=Assets/mapped/props/Speed")
                    {
                        element.Value = nodeId;
                    }
                }
            });
        }

        private static async Task<ByteString> ReadPersistedDocumentAsync(Fixture fixture)
        {
            string path = Path.Combine(fixture.Options.ThingDescriptionStorageFolder!, "mapped.jsonld");
            using var file = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            using var bytes = new MemoryStream();
            await file.CopyToAsync(bytes);
            return ByteString.From(bytes.ToArray());
        }

        private static async Task AssertNoPublishedAssetsAsync(Fixture fixture)
        {
            var assets = new List<WotAssetEntry>();
            await foreach (WotAssetEntry asset in fixture.Client.EnumerateAssetsAsync())
            {
                assets.Add(asset);
            }
            Assert.That(assets, Is.Empty);
        }
    }
}
