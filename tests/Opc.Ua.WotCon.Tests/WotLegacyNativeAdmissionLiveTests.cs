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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Export;
using Opc.Ua.Server;
using Opc.Ua.Server.RuntimeNodeSet;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Client;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Server.ThingDescriptions;
using Opc.Ua.WotCon.Tests.Providers;
using Quickstarts.ReferenceServer;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.WotCon.Tests
{
    [TestFixture]
    [Category("WotCon")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed partial class WotLegacyNativeAdmissionLiveTests
    {
        [TestCase("unknown")]
        [TestCase("wrong-class")]
        [TestCase("multiple")]
        [TestCase("conflict")]
        public async Task InvalidTypeBindingDoesNotConnectOrReplaceLastValid(string invalid)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString original = Document();
            await asset.UploadThingDescriptionAsync(original.Span.ToArray());
            NodeId typeBefore = await fixture.TypeAsync(asset.AssetId);
            ArrayOf<WotAssetVariableEntry> before = await PropertiesAsync(asset);
            int connects = fixture.Provider.Connects;
            string binding = invalid switch
            {
                "unknown" => """
                    "@type":["Thing","uav:object"],
                    "links":[{"rel":"ua:HasTypeDefinition","href":"nsu=urn:test:r42-admission;i=9999"}],
                    """,
                "wrong-class" => """
                    "@type":["Thing","uav:object"],
                    "links":[{"rel":"ua:HasTypeDefinition","href":"nsu=urn:test:r42-admission;i=4002"}],
                    """,
                "multiple" => """
                    "@type":["Thing","uav:object","model:PumpType","model:OtherType"],
                    """,
                _ => """
                    "@type":["Thing","uav:object","model:PumpType"],
                    "links":[{"rel":"ua:HasTypeDefinition","href":"nsu=urn:test:r42-admission;i=4010"}],
                    """
            };

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await asset.UploadThingDescriptionAsync(Document(binding: binding).Span.ToArray()))!;

            Assert.That(StatusCode.IsBad(failure.StatusCode), Is.True);
            Assert.That(fixture.Provider.Connects, Is.EqualTo(connects));
            Assert.That(await fixture.TypeAsync(asset.AssetId), Is.EqualTo(typeBefore));
            Assert.That((await PropertiesAsync(asset)).ToList().Select(property => property.NodeId),
                Is.EqualTo(before.ToList().Select(property => property.NodeId)));
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(original.Span.ToArray()));
        }

        [TestCase("\"uav:dataTypeId\":\"i=12\",")]
        [TestCase("\"uav:valueRank\":1,")]
        public async Task DefinitiveDeclarationMismatchFailsBeforeProvider(string mismatch)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId originalType = await fixture.TypeAsync(asset.AssetId);
            string properties = $$$$"""
                {"Speed":{ {{{{mismatch}}}} "type":"number",
                  "forms":[{"href":"sim://opcua.test/wot/speed"}]}}
                """;

            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await asset.UploadThingDescriptionAsync(Document(properties: properties).Span.ToArray()));

            Assert.That(fixture.Provider.Connects, Is.Zero);
            Assert.That(await fixture.TypeAsync(asset.AssetId), Is.EqualTo(originalType));
            Assert.That((await PropertiesAsync(asset)).Count, Is.Zero);
        }

        [Test]
        public async Task MissingMandatoryPropertyComesFromTheLoadedDeclaration()
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString content = Document(properties: "{}");

            await asset.UploadThingDescriptionAsync(content.Span.ToArray());

            ArrayOf<WotAssetVariableEntry> properties = await PropertiesAsync(asset);
            Assert.That(properties.Count, Is.EqualTo(1));
            Assert.That(properties[0].BrowseName, Is.EqualTo("Speed"));
            Assert.That(await fixture.TypeAsync(properties[0].NodeId), Is.EqualTo(VariableTypeIds.PropertyType));
            ArrayOf<DataValue> values = await fixture.ReadAttributesAsync(properties[0].NodeId,
                [Attributes.BrowseName, Attributes.DataType, Attributes.Value]);
            Assert.That(values[0].WrappedValue.TryGetValue(out QualifiedName name), Is.True);
            Assert.That(name, Is.EqualTo(new QualifiedName("Speed", fixture.ModelNamespaceIndex)));
            Assert.That(values[1].WrappedValue.TryGetValue(out NodeId type), Is.True);
            Assert.That(type, Is.EqualTo(Ua.DataTypeIds.Double));
            Assert.That(values[2].WrappedValue.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.Zero);
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
        }

        [Test]
        public async Task RootIdentityConflictKeepsThePublishedAsset()
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString initial = Document();
            await asset.UploadThingDescriptionAsync(initial.Span.ToArray());
            int connects = fixture.Provider.Connects;

            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await asset.UploadThingDescriptionAsync(Document(extra:
                    "\"uav:id\":\"nsu=urn:test:other-owner;s=other\",").Span.ToArray()));

            Assert.That(fixture.Provider.Connects, Is.EqualTo(connects));
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(initial.Span.ToArray()));
            Assert.That(await fixture.TypeAsync(asset.AssetId),
                Is.EqualTo(new NodeId(4001u, fixture.ModelNamespaceIndex)));
        }

        [Test]
        public async Task AuthoredRootNameConflictFailsBeforeConnecting()
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId originalType = await fixture.TypeAsync(asset.AssetId);

            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await asset.UploadThingDescriptionAsync(Document(extra:
                    "\"uav:browseName\":\"nsu=urn:test:another-owner;Different\",").Span.ToArray()));

            Assert.That(fixture.Provider.Connects, Is.Zero);
            Assert.That(await fixture.TypeAsync(asset.AssetId), Is.EqualTo(originalType));
            Assert.That((await PropertiesAsync(asset)).Count, Is.Zero);
        }

        [Test]
        public async Task NativeActionArgumentsAndIdentityComeFromSharedConversion()
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            const string action = """
                "actions":{"Setpoint":{"input":{"type":"object","properties":{
                    "value":{"type":"integer","uav:dataTypeId":"i=5"}}},
                  "output":{"type":"object","properties":{
                    "value":{"type":"integer","uav:dataTypeId":"i=5"}}},
                  "forms":[{"href":"sim://opcua.test/wot/setpoint"}]}},
                """;
            ByteString content = Document(extra: action);
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            var actions = new List<WotAssetVariableEntry>();
            await foreach (WotAssetVariableEntry entry in asset.EnumerateActionsAsync())
            {
                actions.Add(entry);
            }
            Assert.That(actions, Has.Count.EqualTo(1));
            NodeId methodId = actions[0].NodeId;
            ArrayOf<Argument> input = await fixture.ArgumentsAsync(methodId, Ua.BrowseNames.InputArguments);
            ArrayOf<Argument> output = await fixture.ArgumentsAsync(methodId, Ua.BrowseNames.OutputArguments);
            Assert.Multiple(() =>
            {
                Assert.That(input.Count, Is.EqualTo(1));
                Assert.That(output.Count, Is.EqualTo(1));
                Assert.That(input[0].DataType, Is.EqualTo(Ua.DataTypeIds.UInt16));
                Assert.That(output[0].DataType, Is.EqualTo(Ua.DataTypeIds.UInt16));
                Assert.That(input[0].ValueRank, Is.EqualTo(ValueRanks.Scalar));
            });
            CallResponse call = await fixture.Session.CallAsync(null,
                [new CallMethodRequest
                {
                    ObjectId = asset.AssetId,
                    MethodId = methodId,
                    InputArguments = [new Variant((ushort)17)]
                }], default);
            Assert.That(call.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(call.Results[0].OutputArguments[0].TryGetValue(out ushort value), Is.True);
            Assert.That(value, Is.EqualTo(17));
            await asset.UploadThingDescriptionAsync(content.Span.ToArray());
            var replaced = new List<WotAssetVariableEntry>();
            await foreach (WotAssetVariableEntry entry in asset.EnumerateActionsAsync())
            {
                replaced.Add(entry);
            }
            Assert.That(replaced.Single().NodeId, Is.EqualTo(methodId));
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeActionArgumentIdentitiesDoNotAliasSuffixNamedActions(bool reverse)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            string[] names = reverse ? ["Reset_out", "Reset_in", "Reset"] : ["Reset", "Reset_in", "Reset_out"];
            var actions = new JsonObject();
            foreach (string name in names)
            {
                actions[name] = JsonNode.Parse("""
                    {"input":{"type":"object","properties":{
                       "value":{"type":"integer","uav:dataTypeId":"i=5"}}},
                     "output":{"type":"object","properties":{
                       "value":{"type":"integer","uav:dataTypeId":"i=5"}}},
                     "forms":[{"href":"sim://opcua.test/wot/action"}]}
                    """);
            }
            ByteString content = Document(extra: "\"actions\":" + actions.ToJsonString() + ",");

            await asset.UploadThingDescriptionAsync(content.Span.ToArray());

            var identities = new List<NodeId>();
            ushort ns = fixture.Session.NamespaceUris.GetIndexOrAppend(
                WotConnectivityServerOptions.DefaultAssetNamespaceUri);
            var published = new List<WotAssetVariableEntry>();
            await foreach (WotAssetVariableEntry action in asset.EnumerateActionsAsync())
            {
                published.Add(action);
            }
            Assert.That(published, Has.Count.EqualTo(3));
            foreach (string name in names)
            {
                var method = new NodeId("Assets/mapped/actions/" + name, ns);
                var input = new NodeId("Assets/mapped/actions/" + name + "/InputArguments", ns);
                var output = new NodeId("Assets/mapped/actions/" + name + "/OutputArguments", ns);
                identities.AddRange([method, input, output]);
                Assert.That(published.Single(action => action.BrowseName == name).NodeId, Is.EqualTo(method));
                await fixture.ReadAttributesAsync(input, [Attributes.NodeClass, Attributes.Value]);
                await fixture.ReadAttributesAsync(output, [Attributes.NodeClass, Attributes.Value]);
                CallResponse called = await fixture.Session.CallAsync(null,
                    [new CallMethodRequest
                    {
                        ObjectId = asset.AssetId,
                        MethodId = method,
                        InputArguments = [new Variant((ushort)23)]
                    }], default);
                Assert.That(called.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(called.Results[0].OutputArguments[0].TryGetValue(out ushort value), Is.True);
                Assert.That(value, Is.EqualTo(23));
            }
            Assert.That(identities, Is.Unique);
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));

            await asset.UploadThingDescriptionAsync(Document().Span.ToArray());

            ReadResponse removed = await fixture.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                identities.Select(id => new ReadValueId { NodeId = id, AttributeId = Attributes.NodeClass })
                    .ToArrayOf(), default);
            Assert.That(removed.Results.Count, Is.EqualTo(9));
            foreach (DataValue value in removed.Results)
            {
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
            }
        }

        [Test]
        public async Task UnknownAndLiteralSourceMembersRetainExactBytes()
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString content = Document(extra: """
                "opaque":{"fraction":1.2300,"escaped":"\u0053peed","negativeZero":-0.0,
                  "schema":{"properties":{"literal":{"type":"number"}}}},
                """);

            await asset.UploadThingDescriptionAsync(content.Span.ToArray());

            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
            Assert.That((await PropertiesAsync(asset)).Count, Is.EqualTo(1));
        }

        [TestCase(WotNodeSetPreservationMode.Always)]
        [TestCase(WotNodeSetPreservationMode.Never)]
        public async Task NativeGraphKeepsLiteralMetadataAndUsesTheExistingOwner(WotNodeSetPreservationMode mode)
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString content = NativeDocument(mode, "mapped");

            await asset.UploadThingDescriptionAsync(content.Span.ToArray());

            Assert.That(await fixture.TypeAsync(asset.AssetId),
                Is.EqualTo(new NodeId(4001u, fixture.ModelNamespaceIndex)));
            ArrayOf<WotAssetVariableEntry> properties = await PropertiesAsync(asset);
            Assert.That(properties.Count, Is.EqualTo(1));
            Assert.That(await fixture.TypeAsync(properties[0].NodeId), Is.EqualTo(VariableTypeIds.PropertyType));
            ArrayOf<DataValue> values = await fixture.ReadAttributesAsync(properties[0].NodeId,
                [Attributes.BrowseName, Attributes.DataType, Attributes.Value, Attributes.ValueRank]);
            Assert.That(values[0].WrappedValue.TryGetValue(out QualifiedName name), Is.True);
            Assert.That(name, Is.EqualTo(new QualifiedName("Speed", fixture.ModelNamespaceIndex)));
            Assert.That(values[1].WrappedValue.TryGetValue(out NodeId dataType), Is.True);
            Assert.That(dataType, Is.EqualTo(Ua.DataTypeIds.Double));
            Assert.That(values[2].WrappedValue.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(42.5));
            Assert.That(values[3].WrappedValue.TryGetValue(out int rank), Is.True);
            Assert.That(rank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(await asset.DownloadThingDescriptionAsync(), Is.EqualTo(content.Span.ToArray()));
            var assets = new List<WotAssetEntry>();
            await foreach (WotAssetEntry entry in fixture.Client.EnumerateAssetsAsync())
            {
                assets.Add(entry);
            }
            Assert.That(assets, Has.Count.EqualTo(1));
            Assert.That(assets[0].AssetId, Is.EqualTo(asset.AssetId));
        }

        [Test]
        public async Task NativeArchiveCannotSilentlyRebaseAnotherRoot()
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            NodeId initial = await fixture.TypeAsync(asset.AssetId);

            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await asset.UploadThingDescriptionAsync(
                    NativeDocument(WotNodeSetPreservationMode.Always, "another").Span.ToArray()));

            Assert.That(fixture.Provider.Connects, Is.Zero);
            Assert.That(await fixture.TypeAsync(asset.AssetId), Is.EqualTo(initial));
            Assert.That((await PropertiesAsync(asset)).Count, Is.Zero);
        }

        [Test]
        public async Task NativeAuxiliaryDeclarationsRetireWithTheAssetOwner()
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            await asset.UploadThingDescriptionAsync(
                NativeDocument(WotNodeSetPreservationMode.Always, "mapped", includeAuxiliary: true).Span.ToArray());
            var auxiliary = new NodeId("Assets/mapped/support/LiteralType",
                fixture.Session.NamespaceUris.GetIndexOrAppend(
                    WotConnectivityServerOptions.DefaultAssetNamespaceUri));
            await fixture.ReadAttributesAsync(auxiliary, [Attributes.NodeClass]);

            await fixture.Client.DeleteAssetAsync(asset.AssetId);

            ReadResponse read = await fixture.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = auxiliary, AttributeId = Attributes.NodeClass }], default);
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task DiscoveryPreparesBeforePublishingTheOwner()
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            fixture.Options.Discovery = new Discovery(Document());
            fixture.Recorder.ObservedPublishedOwner = false;

            WotAssetClient asset = await fixture.Client.CreateAssetForEndpointAsync(
                "mapped", "sim://opcua.test/wot/mapped");

            Assert.That(fixture.Recorder.ObservedPublishedOwner, Is.False);
            Assert.That(await fixture.TypeAsync(asset.AssetId),
                Is.EqualTo(new NodeId(4001u, fixture.ModelNamespaceIndex)));
        }

        [Test]
        public async Task PersistedDocumentPreparesBeforePublishingTheOwner()
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString bytes = Document();
            await asset.UploadThingDescriptionAsync(bytes.Span.ToArray());

            await fixture.RestartManagerAsync();

            Assert.That(fixture.Recorder.ObservedPublishedOwner, Is.False);
            WotAssetClient restored = await fixture.Client.OpenAssetAsync(asset.AssetId);
            Assert.That(await restored.DownloadThingDescriptionAsync(), Is.EqualTo(bytes.Span.ToArray()));
            Assert.That(await fixture.TypeAsync(restored.AssetId),
                Is.EqualTo(new NodeId(4001u, fixture.ModelNamespaceIndex)));
        }

        [Test]
        public async Task InvalidPersistedTypePublishesNoOwnerAndKeepsItsSource()
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            ByteString invalid = Document(binding: """
                "@type":["Thing","uav:object"],
                "links":[{"rel":"ua:HasTypeDefinition","href":"nsu=urn:test:r42-admission;i=9999"}],
                """);

            await fixture.RestartManagerAsync(invalid);

            var assets = new List<WotAssetEntry>();
            await foreach (WotAssetEntry asset in fixture.Client.EnumerateAssetsAsync())
            {
                assets.Add(asset);
            }
            Assert.That(assets, Is.Empty);
            Assert.That(fixture.Provider.Connects, Is.Zero);
            string path = Path.Combine(fixture.Options.ThingDescriptionStorageFolder!, "mapped.jsonld");
            Assert.That(File.Exists(path), Is.True);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var bytes = new MemoryStream();
            await file.CopyToAsync(bytes);
            Assert.That(bytes.ToArray(), Is.EqualTo(invalid.Span.ToArray()));
        }

        [Test]
        public async Task DiUsesTheConfiguredSharedConverter()
        {
            await using Fixture fixture = await Fixture.CreateAsync(useDi: true);
            WotAssetClient asset = await fixture.Client.CreateAssetAsync("mapped");
            ByteString content = Document();

            await asset.UploadThingDescriptionAsync(content.Span.ToArray());

            Assert.That(fixture.Recorder.Calls, Is.EqualTo(1));
            Assert.That(fixture.Recorder.LastContent, Is.EqualTo(content));
            Assert.That(await fixture.TypeAsync(asset.AssetId),
                Is.EqualTo(new NodeId(4001u, fixture.ModelNamespaceIndex)));
        }

        private static async Task<ArrayOf<WotAssetVariableEntry>> PropertiesAsync(WotAssetClient asset)
        {
            var result = new List<WotAssetVariableEntry>();
            await foreach (WotAssetVariableEntry entry in asset.EnumeratePropertiesAsync())
            {
                result.Add(entry);
            }
            return result.ToArrayOf();
        }

        private static ByteString Document(
            string binding = "\"@type\":[\"Thing\",\"uav:object\",\"model:PumpType\"],",
            string properties = "{\"Speed\":{\"type\":\"number\",\"forms\":[{\"href\":\"sim://opcua.test/wot/speed\"}]}}",
            string extra = "")
        {
            return ByteString.From(Encoding.UTF8.GetBytes($$$$"""
                {
                  "@context":["https://www.w3.org/2022/wot/td/v1.1",
                    {"ua":"http://opcfoundation.org/UA/","uav":"http://opcfoundation.org/UA/WoT-Binding/",
                     "model":"urn:test:r42-admission"}],
                  {{{{binding}}}}
                  "name":"mapped","title":"Mapped asset","base":"sim://opcua.test/wot/mapped",
                  "security":"none","securityDefinitions":{"none":{"scheme":"nosec"}},
                  {{{{extra}}}}
                  "properties":{{{{properties}}}}
                }
                """));
        }

        private static ByteString NativeDocument(
            WotNodeSetPreservationMode mode, string rootName, bool includeAuxiliary = false,
            Action<XDocument>? modify = null)
        {
            string auxiliary = includeAuxiliary ? $$$$"""
                <UADataType NodeId="ns=1;s=Assets/{{{{rootName}}}}/support/LiteralType" BrowseName="1:LiteralType">
                  <DisplayName>LiteralType</DisplayName>
                  <References><Reference ReferenceType="i=45" IsForward="false">i=6</Reference></References>
                </UADataType>
                """ : string.Empty;
            string xml = $$$$"""
                <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                  xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
                  <NamespaceUris><Uri>{{{{WotConnectivityServerOptions.DefaultAssetNamespaceUri}}}}</Uri>
                    <Uri>{{{{ModelUri}}}}</Uri></NamespaceUris>
                  <Models><Model ModelUri="{{{{WotConnectivityServerOptions.DefaultAssetNamespaceUri}}}}"
                    Version="1.0.0" PublicationDate="2026-01-01T00:00:00Z">
                    <RequiredModel ModelUri="{{{{ModelUri}}}}" Version="1.0.0"
                      PublicationDate="2026-01-01T00:00:00Z" />
                  </Model></Models>
                  <UAObject NodeId="ns=1;s=Assets/{{{{rootName}}}}" BrowseName="1:{{{{rootName}}}}">
                    <DisplayName>{{{{rootName}}}}</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">ns=2;i=4001</Reference>
                      <Reference ReferenceType="i=46">ns=1;s=Assets/{{{{rootName}}}}/props/Speed</Reference>
                      <Reference ReferenceType="i=35">ns=1;s=Assets/{{{{rootName}}}}/view</Reference>
                    </References>
                  </UAObject>
                  <UAVariable NodeId="ns=1;s=Assets/{{{{rootName}}}}/props/Speed" BrowseName="2:Speed"
                    ParentNodeId="ns=1;s=Assets/{{{{rootName}}}}" DataType="i=11" ValueRank="-1" AccessLevel="1">
                    <DisplayName>Native Speed</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">i=68</Reference>
                      <Reference ReferenceType="i=46" IsForward="false">ns=1;s=Assets/{{{{rootName}}}}</Reference>
                    </References>
                    <Value><uax:Double>42.5</uax:Double></Value>
                  </UAVariable>
                  <UAView NodeId="ns=1;s=Assets/{{{{rootName}}}}/view" BrowseName="1:View"
                    ParentNodeId="ns=1;s=Assets/{{{{rootName}}}}" ContainsNoLoops="true">
                    <DisplayName>Native View</DisplayName>
                    <References>
                      <Reference ReferenceType="i=35" IsForward="false">ns=1;s=Assets/{{{{rootName}}}}</Reference>
                    </References>
                  </UAView>
                  {{{{auxiliary}}}}
                </UANodeSet>
                """;
            if (modify is not null)
            {
                XDocument edited = XDocument.Parse(xml);
                modify(edited);
                xml = edited.ToString(SaveOptions.DisableFormatting);
            }
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            UANodeSet nodes = UANodeSet.Read(stream) ??
                throw new InvalidDataException("The native test NodeSet could not be read.");
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                nodes, rootName, new WotNodeSetConverterOptions { PreservationMode = mode });
            JsonObject root = JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
            root["name"] = "mapped";
            root["base"] = "sim://opcua.test/wot/mapped";
            return ByteString.From(Encoding.UTF8.GetBytes(root.ToJsonString()));
        }

        private sealed partial class Fixture : IAsyncDisposable
        {
            public ClientSession Session { get; private set; } = null!;
            public WotConnectivityClient Client { get; private set; } = null!;
            public IServerInternal Server => m_server.CurrentInstance;
            public WotConnectivityServerOptions Options { get; private set; } = null!;
            public CountingProvider Provider { get; } = new();
            public RecordingConverter Recorder { get; private set; } = null!;
            public ushort ModelNamespaceIndex => Session.NamespaceUris.GetIndexOrAppend(ModelUri);

            public static async Task<Fixture> CreateAsync(
                bool useDi = false, string? modelXml = null, ArrayOf<string> modelNamespaces = default)
            {
                var fixture = new Fixture();
                bool started = false;
                try
                {
                    await fixture.StartAsync(useDi, modelXml, modelNamespaces);
                    started = true;
                    return fixture;
                }
                finally
                {
                    if (!started)
                    {
                        await fixture.DisposeAsync();
                    }
                }
            }

            public async Task<NodeId> TypeAsync(NodeId node)
            {
                BrowseResponse result = await Session.BrowseAsync(null, new ViewDescription(), 0,
                    [new BrowseDescription
                    {
                        NodeId = node,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = Ua.ReferenceTypeIds.HasTypeDefinition,
                        IncludeSubtypes = false,
                        ResultMask = (uint)BrowseResultMask.All
                    }], default);
                Assert.That(result.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(result.Results[0].References.Count, Is.EqualTo(1));
                return ExpandedNodeId.ToNodeId(result.Results[0].References[0].NodeId, Session.NamespaceUris);
            }

            public async Task<ArrayOf<DataValue>> ReadAttributesAsync(NodeId node, ArrayOf<uint> attributes)
            {
                ReadResponse result = await Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    attributes.ConvertAll(attribute => new ReadValueId { NodeId = node, AttributeId = attribute }),
                    default);
                foreach (DataValue value in result.Results)
                {
                    Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                }
                return result.Results;
            }

            public async Task<ArrayOf<Argument>> ArgumentsAsync(NodeId method, string name)
            {
                TranslateBrowsePathsToNodeIdsResponse paths = await Session.TranslateBrowsePathsToNodeIdsAsync(
                    null, [new BrowsePath
                    {
                        StartingNode = method,
                        RelativePath = new RelativePath
                        {
                            Elements = [new RelativePathElement
                            {
                                ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty,
                                TargetName = new QualifiedName(name)
                            }]
                        }
                    }], default);
                Assert.That(paths.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                NodeId id = ExpandedNodeId.ToNodeId(paths.Results[0].Targets[0].TargetId, Session.NamespaceUris);
                DataValue value = (await ReadAttributesAsync(id, [Attributes.Value]))[0];
                Assert.That(value.WrappedValue.TryGetStructure(out ArrayOf<Argument> arguments), Is.True);
                return arguments;
            }

            public async Task RestartManagerAsync(ByteString replacement = default)
            {
                await m_server.NodeManagerLifecycle.RemoveAsync(m_registration, null);
                if (!replacement.IsNull)
                {
                    Directory.CreateDirectory(Options.ThingDescriptionStorageFolder!);
                    string path = Path.Combine(Options.ThingDescriptionStorageFolder!, "mapped.jsonld");
                    using var file = new FileStream(
                        path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
#if NET8_0_OR_GREATER
                    await file.WriteAsync(replacement.Memory, CancellationToken.None);
#else
                    byte[] bytes = replacement.Span.ToArray();
                    await file.WriteAsync(bytes, 0, bytes.Length, CancellationToken.None);
#endif
                }
                Recorder = new RecordingConverter(this);
                Options.DocumentConverter = Recorder;
                m_registration = await m_server.NodeManagerLifecycle.AddAsync(
                    new WotConnectivityNodeManagerFactory(Options), null);
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (Session is not null)
                    {
                        await Session.CloseAsync();
                    }
                }
                finally
                {
                    try
                    {
                        Session?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            if (m_clientFixture is not null)
                            {
                                await m_clientFixture.DisposeAsync();
                            }
                        }
                        finally
                        {
                            try
                            {
                                if (m_serverFixture is not null)
                                {
                                    await m_serverFixture.StopAsync();
                                }
                            }
                            finally
                            {
                                try
                                {
                                    m_server?.Dispose();
                                    if (m_services is not null)
                                    {
                                        await m_services.DisposeAsync();
                                    }
                                }
                                finally
                                {
                                    if (Directory.Exists(m_directory))
                                    {
                                        Directory.Delete(m_directory, recursive: true);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            private async Task StartAsync(bool useDi, string? modelXml, ArrayOf<string> modelNamespaces)
            {
                string? expected = Environment.GetEnvironmentVariable("WOT_R42_EXPECTED_RUNTIME");
                if (!string.IsNullOrEmpty(expected))
                {
                    Assert.That(Environment.Version.ToString(), Is.EqualTo(expected));
                }
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                m_directory = Path.Combine(Path.GetTempPath(), "r42-admission", Guid.NewGuid().ToString("N"));
                m_serverFixture = new ServerFixture<ReferenceServer>(context => new ReferenceServer(context))
                {
                    UriScheme = Utils.UriSchemeOpcTcp, AutoAccept = true, SecurityNone = false
                };
                m_server = await m_serverFixture.StartAsync(m_directory);
                await m_server.NodeManagerLifecycle.AddRuntimeNodeSetAsync(new RuntimeNodeSetOptions
                {
                    Sources = [RuntimeNodeSetSource.FromStream("R42Model",
                        _ => new ValueTask<Stream>(new MemoryStream(
                            Encoding.UTF8.GetBytes(modelXml ?? s_model), false)),
                        modelNamespaces.IsNull ? [ModelUri] : modelNamespaces)]
                }, null);
                Recorder = new RecordingConverter(this);
                Options = new WotConnectivityServerOptions
                {
                    ThingDescriptionStorageFolder = Path.Combine(m_directory, "documents"),
                    DocumentConverter = useDi ? null : Recorder,
                    ManagementAccess = new WotManagementAccessPolicy
                    {
                        MinimumSecurityMode = MessageSecurityMode.SignAndEncrypt,
                        AllowAnonymous = true,
                        RequiredRoleId = Ua.ObjectIds.WellKnownRole_Anonymous
                    }
                };
                Options.AssetEndpointPolicy.AllowedSchemes.Add("sim");
                Options.Bindings.Add(Provider);
                WotConnectivityNodeManagerFactory factory;
                if (useDi)
                {
                    var services = new ServiceCollection();
                    services.AddSingleton(telemetry);
                    services.AddSingleton<IWotDocumentConverter>(Recorder);
                    services.AddOpcUa().AddWotConServer(options =>
                    {
                        options.ThingDescriptionStorageFolder = Options.ThingDescriptionStorageFolder;
                        options.ManagementAccess = Options.ManagementAccess;
                        options.Bindings.Add(Provider);
                    });
                    m_services = services.BuildServiceProvider();
                    factory = m_services.GetRequiredService<WotConnectivityNodeManagerFactory>();
                }
                else
                {
                    factory = new WotConnectivityNodeManagerFactory(Options);
                }
                m_registration = await m_server.NodeManagerLifecycle.AddAsync(factory, null);
                m_clientFixture = new ClientFixture(false, false, telemetry);
                await m_clientFixture.LoadClientConfigurationAsync(m_directory);
                Session = await m_clientFixture.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{m_serverFixture.Port}"),
                    SecurityPolicies.Basic256Sha256);
                Assert.That(Session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                Client = await WotConnectivityClient.ForServerAsync(Session, telemetry);
            }

            private string m_directory = string.Empty;
            private ServerFixture<ReferenceServer>? m_serverFixture;
            private ReferenceServer m_server = null!;
            private ClientFixture? m_clientFixture;
            private NodeManagerRegistration m_registration = null!;
            private ServiceProvider? m_services;
        }

        private sealed class RecordingConverter : IWotDocumentConverter
        {
            public RecordingConverter(Fixture fixture)
            {
                m_fixture = fixture;
            }

            public int Calls { get; private set; }
            public bool ObservedPublishedOwner { get; set; }
            public ByteString LastContent { get; private set; }

            public async ValueTask<WotConversionOutput> ConvertAsync(
                WotResource resource, ByteString content, WotRegistrySnapshot snapshot,
                IReadOnlyDictionary<string, ByteString> contents, CancellationToken cancellationToken)
            {
                Calls++;
                LastContent = content;
                var id = new NodeId($"Assets/{resource.ResourceId}",
                    m_fixture.Server.NamespaceUris.GetIndexOrAppend(m_fixture.Options.AssetNamespaceUri));
                var owner = await m_fixture.Server.NodeManager.GetManagerHandleAsync(id, cancellationToken);
                ObservedPublishedOwner |= owner.handle is not null;
                var converter = new WotNodeSetDocumentConverter(
                    addressSpace: new AddressSpaceWotNodeResolver(m_fixture.Server));
                return await converter.ConvertAsync(resource, content, snapshot, contents, cancellationToken);
            }

            private readonly Fixture m_fixture;
        }

        private sealed class CountingProvider : IWotAssetProviderFactory
        {
            public int Connects { get; private set; }
            public IReadOnlyCollection<string> SupportedBindings => m_inner.SupportedBindings;
            public bool CanHandle(ThingDescription thingDescription) => m_inner.CanHandle(thingDescription);

            public ValueTask<IWotAssetProvider> ConnectAsync(ThingDescription thingDescription, CancellationToken ct)
            {
                Connects++;
                return m_inner.ConnectAsync(thingDescription, ct);
            }

            private readonly SimulatedWotAssetProviderFactory m_inner = new();
        }

        private sealed class Discovery : IWotAssetDiscoveryProvider
        {
            public Discovery(ByteString content)
            {
                m_content = content;
            }

            public ValueTask<IReadOnlyList<string>> DiscoverAsync(CancellationToken ct)
            {
                return new ValueTask<IReadOnlyList<string>>(s_endpoints);
            }

            public ValueTask<(bool Success, string Status)> TestAsync(string assetEndpoint, CancellationToken ct)
            {
                return new ValueTask<(bool, string)>((true, string.Empty));
            }

            public ValueTask<ThingDescription> CreateThingDescriptionAsync(
                string assetName, string assetEndpoint, CancellationToken ct)
            {
                ThingDescription result = JsonSerializer.Deserialize(
                    m_content.Span, ThingDescriptionJsonContext.Default.ThingDescription)!;
                return new ValueTask<ThingDescription>(result);
            }

            private readonly ByteString m_content;
            private static readonly string[] s_endpoints = ["sim://opcua.test/wot/mapped"];
        }

        private const string ModelUri = "urn:test:r42-admission";
        private const string s_model = """
            <?xml version="1.0" encoding="utf-8"?>
            <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
              <NamespaceUris><Uri>urn:test:r42-admission</Uri></NamespaceUris>
              <Models><Model ModelUri="urn:test:r42-admission" Version="1.0.0"
                PublicationDate="2026-01-01T00:00:00Z">
                <RequiredModel ModelUri="http://opcfoundation.org/UA/" Version="1.05.04"
                  PublicationDate="2025-01-08T00:00:00Z" />
              </Model></Models>
              <UAObjectType NodeId="ns=1;i=4001" BrowseName="1:PumpType">
                <DisplayName>PumpType</DisplayName>
                <References>
                  <Reference ReferenceType="i=45" IsForward="false">i=58</Reference>
                  <Reference ReferenceType="i=46">ns=1;i=4002</Reference>
                </References>
              </UAObjectType>
              <UAVariable NodeId="ns=1;i=4002" BrowseName="1:Speed" ParentNodeId="ns=1;i=4001"
                DataType="i=11" ValueRank="-1" AccessLevel="3">
                <DisplayName>Speed</DisplayName>
                <References>
                  <Reference ReferenceType="i=46" IsForward="false">ns=1;i=4001</Reference>
                  <Reference ReferenceType="i=40">i=68</Reference>
                  <Reference ReferenceType="i=37">i=78</Reference>
                </References>
              </UAVariable>
              <UAObjectType NodeId="ns=1;i=4010" BrowseName="1:OtherType">
                <DisplayName>OtherType</DisplayName>
                <References><Reference ReferenceType="i=45" IsForward="false">i=58</Reference></References>
              </UAObjectType>
            </UANodeSet>
            """;
    }
}
