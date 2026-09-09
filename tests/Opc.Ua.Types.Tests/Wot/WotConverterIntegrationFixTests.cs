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

#nullable enable

using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Export;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public sealed class WotConverterIntegrationFixTests
    {
        [TestCase(-1, null, "number")]
        [TestCase(1, "3", "array")]
        [TestCase(2, "2,3", "array")]
        public void ArchivedValueShapesRoundTripExactly(int rank, string? dimensions, string jsonType)
        {
            UANodeSet source = CreateRankedNodeSet(rank, dimensions);
            byte[] expected = WotTestData.Serialize(source);
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Always });

            Assert.That(document.RootElement.TryGetProperty("uav:nodeSet", out _), Is.True);
            JsonElement schema = document.Properties["Samples"];
            Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo(jsonType));
            for (int dimension = 0; dimension < rank; dimension++)
            {
                Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("array"));
                schema = schema.GetProperty("items");
            }
            Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("number"));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(WotTestData.Serialize(result.Value!), Is.EqualTo(expected));
            Assert.That(WotTestData.Serialize(source), Is.EqualTo(expected));
        }

        [TestCase("uav:mapToType", "\"i=12\"")]
        [TestCase("uav:dataTypeId", "\"i=12\"")]
        [TestCase("uav:valueRank", "1")]
        [TestCase("uav:arrayDimensions", "[2,4]")]
        [TestCase("type", "\"number\"")]
        public void ArchivedMatrixRejectsContradictoryValueMetadata(string member, string json)
        {
            UANodeSet source = CreateRankedNodeSet(2, "2,3");
            JsonObject root = Archive(source);
            root["properties"]!["Samples"]![member] = JsonNode.Parse(json);

            AssertArchiveConflict(source, root, "/properties/Samples/" + member);
        }

        [TestCase(-3)]
        [TestCase(-2)]
        public void ArchivedIndeterminateRanksRejectConflictingScalarTypes(int rank)
        {
            UANodeSet source = CreateRankedNodeSet(rank, null);
            JsonObject root = Archive(source);
            root["properties"]!["Samples"]!["type"] = "string";

            AssertArchiveConflict(source, root, "/properties/Samples/type");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ArchivedComponentTemplatesPreserveDeclarationsAndSourceTypes(bool inverse)
        {
            UANodeSet source = CreateComponentNodeSet();
            if (inverse)
            {
                source.Items![0].References =
                    source.Items[0].References!.Where(reference => reference.ReferenceType != "i=47").ToArray();
                foreach (UAObject declaration in source.Items.OfType<UAObject>())
                {
                    declaration.References =
                    [
                        .. declaration.References!,
                        new Reference { ReferenceType = "i=47", IsForward = false, Value = "ns=1;i=1000" }
                    ];
                }
            }
            byte[] expected = WotTestData.Serialize(source);
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Always });
            JsonElement[] templates = document.Links
                .Where(link => link.TryGetProperty("uav:declaration", out _)).ToArray();
            Assert.That(templates, Has.Length.EqualTo(2));
            Assert.That(templates.Select(link => link.GetProperty("href").GetString()),
                Is.All.EqualTo("nsu=urn:test:parts;i=2000"));
            Assert.That(templates.Select(link =>
                link.GetProperty("uav:declaration").GetProperty("uav:id").GetString()),
                Is.EquivalentTo(s_componentDeclarationIds));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items!.OfType<UAObjectType>().Select(type => type.NodeId),
                Is.EquivalentTo(s_componentTypeIds));
            Assert.That(WotTestData.Serialize(result.Value), Is.EqualTo(expected));
            Assert.That(WotTestData.Serialize(source), Is.EqualTo(expected));
        }

        [TestCase("uav:browseName", "\"nsu=urn:test:integration;Other\"", "/uav:declaration/uav:browseName")]
        [TestCase("uav:modellingRule", "\"Optional\"", "/uav:declaration/uav:modellingRule")]
        [TestCase("uav:id", "\"nsu=urn:test:integration;i=9999\"", "/uav:declaration/uav:id")]
        [TestCase("uav:id", "\"nsu=urn:test:integration;i=1101\"", "/uav:declaration/uav:browseName")]
        [TestCase("uav:id", "\"nsu=urn:test:parts;i=2000\"", "")]
        [TestCase("href", "\"i=58\"", "")]
        [TestCase("href", "\"nsu=urn:test:integration;i=1100\"", "")]
        [TestCase("uav:declaration", "null", "")]
        public void ArchivedComponentTemplateRejectsContradictoryFacts(
            string member,
            string json,
            string conflictSuffix)
        {
            UANodeSet source = CreateComponentNodeSet();
            JsonObject root = Archive(source);
            JsonArray links = root["links"]!.AsArray();
            int index = links.ToList().FindIndex(link =>
                link?["uav:declaration"]?["uav:id"]?.GetValue<string>() == "nsu=urn:test:integration;i=1100");
            JsonObject template = links[index]!.AsObject();
            JsonObject target = member is "href" or "uav:declaration"
                ? template
                : template["uav:declaration"]!.AsObject();
            target[member] = JsonNode.Parse(json);

            AssertArchiveConflict(
                source, root, "/links/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                conflictSuffix);
        }

        [Test]
        public void ArchivedComponentTemplateCannotChangeItsOwnershipReference()
        {
            UANodeSet source = CreateComponentNodeSet();
            JsonObject root = Archive(source);
            JsonArray links = root["links"]!.AsArray();
            int index = links.ToList().FindIndex(link => link?["uav:declaration"] is not null);
            links[index]!["rel"] = "ua:HasOrderedComponent";
            links[index]!["uav:refId"] = "i=49";

            AssertArchiveConflict(
                source, root, "/links/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExternalReferenceAliasesKeepTheirForwardIdentity(bool inferredForward)
        {
            UANodeSet source = CreateAliasReferenceNodeSet();
            if (inferredForward)
            {
                source.Items![0].References =
                    source.Items[0].References!.Where(reference => reference.ReferenceType != "Feeds").ToArray();
                source.Items[1].References =
                [
                    .. source.Items[1].References!,
                    new Reference { ReferenceType = "Feeds", IsForward = false, Value = "ns=1;i=1000" }
                ];
            }
            byte[] expected = WotTestData.Serialize(source);

            WotConversionResult<WotDocument> exported = WotNodeSetConverter.FromNodeSetResult(
                source,
                options: new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Always });

            using WotDocument document = exported.Value!;
            Assert.That(source.Items!.OfType<UAReferenceType>(), Is.Empty);
            Assert.That(exported.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ModelConceptUnresolved),
                Is.False, string.Join("; ", exported.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            JsonElement feeds = document.Links.Single(link => link.GetProperty("rel").GetString() == "ns2:Feeds");
            Assert.That(feeds.GetProperty("uav:refId").GetString(), Is.EqualTo("nsu=urn:test:relations;i=5001"));
            Assert.That(feeds.GetProperty("href").GetString(), Is.EqualTo("nsu=urn:test:integration;i=1200"));
            JsonElement organizes = document.Links.Single(link =>
                link.GetProperty("rel").GetString() == "ua:Organizes");
            Assert.That(organizes.GetProperty("uav:refId").GetString(), Is.EqualTo("i=35"));
            Assert.That(organizes.GetProperty("href").GetString(), Is.EqualTo("i=85"));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(WotTestData.Serialize(result.Value!), Is.EqualTo(expected));
            Assert.That(WotTestData.Serialize(source), Is.EqualTo(expected));
        }

        [Test]
        public void AnExternalReferenceAliasDoesNotInventAnInverseName()
        {
            UANodeSet source = CreateAliasReferenceNodeSet();
            source.Items![0].References!.Single(reference => reference.ReferenceType == "Feeds").IsForward = false;
            byte[] expected = WotTestData.Serialize(source);

            WotConversionResult<WotDocument> exported = WotNodeSetConverter.FromNodeSetResult(
                source,
                options: new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Always });

            using WotDocument document = exported.Value!;
            Assert.That(exported.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ModelConceptUnresolved &&
                diagnostic.Location?.NodeId == "ns=1;i=1000"), Is.True);
            Assert.That(document.Links.Any(link =>
                link.TryGetProperty("uav:refId", out JsonElement id) &&
                id.GetString() == "nsu=urn:test:relations;i=5001"), Is.False);
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(WotTestData.Serialize(result.Value!), Is.EqualTo(expected));
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void MultipleOwnershipEdgesEmitOneAffordanceAndRetainBothRelations(
            bool method,
            bool inverseComponent,
            bool inverseOrdered)
        {
            UANodeSet source = CreateRankedNodeSet(-1, null);
            if (method)
            {
                source.Items![1] = new UAMethod
                {
                    NodeId = "ns=1;i=1100",
                    BrowseName = "1:Reset",
                    References = [new Reference { ReferenceType = "i=37", Value = "i=78" }]
                };
            }
            UANode child = source.Items![1];
            UANode owner = source.Items[0];
            owner.References = owner.References!.Where(reference => reference.ReferenceType != "i=47").ToArray();
            foreach ((string type, bool inverse) in new[] { ("i=47", inverseComponent), ("i=49", inverseOrdered) })
            {
                UANode carryingNode = inverse ? child : owner;
                carryingNode.References =
                [
                    .. carryingNode.References!,
                    new Reference
                    {
                        ReferenceType = type,
                        IsForward = !inverse,
                        Value = inverse ? owner.NodeId : child.NodeId
                    }
                ];
            }
            byte[] expected = WotTestData.Serialize(source);
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Always });

            WotConversionResult<UANodeSet> archived = WotNodeSetConverter.ToNodeSetResult(document);
            Assert.That(archived.Success, Is.True, Describe(archived));
            Assert.That(WotTestData.Serialize(archived.Value!), Is.EqualTo(expected));
            Assert.That(method ? document.Actions.Count : document.Properties.Count, Is.EqualTo(1));
            Assert.That(document.Links.Any(link =>
                link.GetProperty("rel").GetString() == "ua:HasOrderedComponent" &&
                link.GetProperty("href").GetString() == "nsu=urn:test:integration;i=1100"), Is.True);
            JsonObject readable = JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
            readable.Remove("uav:nodes");
            readable.Remove("uav:nodeSet");
            using WotDocument readableDocument = WotDocument.Parse(WotTestData.Utf8(readable.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(readableDocument);

            Assert.That(result.Success, Is.True, Describe(result));
            UANode[] restored = result.Value!.Items!;
            Assert.That(restored.Count(node => node.NodeId == "ns=1;i=1100"), Is.EqualTo(1));
            Reference[] ownership = restored.Single(node => node.NodeId == "ns=1;i=1000").References!
                .Where(reference => reference.IsForward && reference.Value == "ns=1;i=1100").ToArray();
            Assert.That(ownership, Has.Length.EqualTo(2));
            Assert.That(ownership.Any(reference => reference.ReferenceType is "HasComponent" or "i=47"), Is.True);
            Assert.That(ownership.Any(reference =>
                reference.ReferenceType is "HasOrderedComponent" or "i=49"), Is.True);
            Assert.That(WotTestData.Serialize(source), Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DifferentNodeIdsWithTheSameBrowseNameKeepTheirOwnAffordances(bool method)
        {
            UANodeSet source = CreateRankedNodeSet(-1, null);
            if (method)
            {
                source.Items![1] = new UAMethod { NodeId = "ns=1;i=1100", BrowseName = "1:Reset" };
            }
            UANode second = method
                ? new UAMethod { NodeId = "ns=1;i=1101", BrowseName = "1:Reset" }
                : new UAVariable { NodeId = "ns=1;i=1101", BrowseName = "1:Samples", DataType = "i=11" };
            source.Items = [.. source.Items!, second];
            source.Items[0].References =
            [
                .. source.Items[0].References!,
                new Reference
                {
                    ReferenceType = "i=47",
                    Value = "ns=1;i=1101"
                }
            ];
            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);

            Assert.That(method ? document.Actions.Count : document.Properties.Count, Is.EqualTo(2));
            JsonObject readable = JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
            readable.Remove("uav:nodes");
            readable.Remove("uav:nodeSet");
            using WotDocument readableDocument = WotDocument.Parse(WotTestData.Utf8(readable.ToJsonString()));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(readableDocument);
            Assert.That(result.Success, Is.True, Describe(result));
            UANode[] restored = result.Value!.Items!;
            Assert.That(restored.Count(node => node.NodeId == "ns=1;i=1100"), Is.EqualTo(1));
            Assert.That(restored.Count(node => node.NodeId == "ns=1;i=1101"), Is.EqualTo(1));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ArchivedBrowseNamesUseTheirCarryingAffordanceContext(bool objectTerm, bool identifyByName)
        {
            UANodeSet source = CreateRankedNodeSet(-1, null);
            JsonObject root = Archive(source);
            root["@context"]!.AsArray().Add(new JsonObject { ["p"] = "urn:test:other" });
            JsonObject property = root["properties"]!["Samples"]!.AsObject();
            property["@context"] = JsonNode.Parse(objectTerm
                ? """{ "p": { "@id": "urn:test:integration", "@prefix": true } }"""
                : """{ "p": "urn:test:integration" }""");
            property["uav:browseName"] = "p:Samples";
            if (identifyByName)
            {
                property.Remove("uav:id");
            }
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items, Has.Length.EqualTo(2));
            UAVariable variable = result.Value.Items!.OfType<UAVariable>().Single();
            Assert.That(variable.NodeId, Is.EqualTo("ns=1;i=1100"));
            Assert.That(variable.BrowseName, Is.EqualTo("1:Samples"));
            Assert.That(variable.DataType, Is.EqualTo("i=11"));
            Assert.That(result.Value.NamespaceUris, Is.EqualTo(source.NamespaceUris));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ArchivedBrowseNamesRejectAContradictoryLocalNamespace(bool objectTerm)
        {
            UANodeSet source = CreateRankedNodeSet(-1, null);
            JsonObject root = Archive(source);
            root["@context"]!.AsArray().Add(new JsonObject { ["p"] = "urn:test:integration" });
            JsonObject property = root["properties"]!["Samples"]!.AsObject();
            property["@context"] = JsonNode.Parse(objectTerm
                ? """{ "p": { "@id": "urn:test:other", "@prefix": true } }"""
                : """{ "p": "urn:test:other" }""");
            property["uav:browseName"] = "p:Samples";

            AssertArchiveConflict(source, root, "/properties/Samples/uav:browseName");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ArchivedReferenceNamesUseTheirCarryingLinkContext(bool companion)
        {
            UANodeSet source = companion ? CreateAliasReferenceNodeSet() : CreateRankedNodeSet(-1, null);
            if (companion)
            {
                source.Items![0].References!.Single(reference => reference.ReferenceType == "Feeds").IsForward = false;
                source.Items =
                [
                    .. source.Items,
                    new UAReferenceType
                    {
                        NodeId = "ns=2;i=5001",
                        BrowseName = "2:Feeds",
                        InverseName = [new Opc.Ua.Export.LocalizedText { Value = "FedBy" }],
                        References = [new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=32" }]
                    }
                ];
            }
            JsonObject root = Archive(source);
            root["@context"]!.AsArray().Add(new JsonObject { ["r"] = "urn:test:other" });
            string referenceId = companion ? "nsu=urn:test:relations;i=5001" : "i=45";
            JsonNode link = root["links"]!.AsArray().Single(candidate =>
                candidate?["uav:refId"]?.GetValue<string>() == referenceId)!;
            link["@context"] = new JsonObject
            {
                ["r"] = companion ? "urn:test:relations" : "http://opcfoundation.org/UA/"
            };
            link["rel"] = companion ? "r:FedBy" : "r:HasSupertype";
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items, Has.Length.EqualTo(source.Items!.Length));
            Assert.That(result.Value.Items![0].References!.Select(reference =>
                (reference.ReferenceType, reference.IsForward, reference.Value)),
                Is.EqualTo(source.Items[0].References!.Select(reference =>
                    (reference.ReferenceType, reference.IsForward, reference.Value))));
            Assert.That(result.Value.NamespaceUris, Is.EqualTo(source.NamespaceUris));
        }

        [Test]
        public void ArchivedDataTypeTermsUseTheirCarryingAffordanceContext()
        {
            UANodeSet source = CreateRankedNodeSet(-1, null);
            JsonObject root = Archive(source);
            root["@context"]!.AsArray().Add(new JsonObject { ["a"] = "urn:test:other" });
            JsonObject property = root["properties"]!["Samples"]!.AsObject();
            property["@context"] = new JsonObject { ["a"] = "http://opcfoundation.org/UA/" };
            property["uav:mapToType"] = "a:Double";
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items!.OfType<UAVariable>().Single().DataType, Is.EqualTo("i=11"));
            Assert.That(result.Value.NamespaceUris, Is.EqualTo(source.NamespaceUris));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ArchivedComponentDeclarationBrowseNamesUseTheirLexicalScope(bool declarationContext)
        {
            UANodeSet source = CreateComponentNodeSet();
            JsonObject root = Archive(source);
            root["@context"]!.AsArray().Add(new JsonObject { ["p"] = "urn:test:other" });
            JsonNode link = root["links"]!.AsArray().First(candidate => candidate?["uav:declaration"] is not null)!;
            JsonNode declaration = link["uav:declaration"]!;
            JsonNode carryingNode = declarationContext ? declaration : link;
            carryingNode["@context"] = new JsonObject { ["p"] = "urn:test:integration" };
            declaration["uav:browseName"] = "p:Left";
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items!.Single(node => node.NodeId == "ns=1;i=1100").BrowseName,
                Is.EqualTo("1:Left"));
            Assert.That(result.Value.Items!.OfType<UAObjectType>().Select(type => type.NodeId),
                Is.EquivalentTo(s_componentTypeIds));
            Assert.That(result.Value.NamespaceUris, Is.EqualTo(source.NamespaceUris));
        }

        [Test]
        public void ArchivedDataTypeNamesUseTheDefinitionContext()
        {
            UANodeSet source = CreateRankedNodeSet(-1, null);
            source.Items =
            [
                .. source.Items!,
                new UADataType
                {
                    NodeId = "ns=1;i=3000",
                    BrowseName = "1:Measurement",
                    References = [new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=11" }]
                }
            ];
            JsonObject root = Archive(source);
            root["@context"]!.AsArray().Add(new JsonObject { ["p"] = "urn:test:other" });
            JsonNode definition = root["uav:dataTypeDefinitions"]!.AsArray().Single(candidate =>
                candidate?["uav:dataTypeId"]?.GetValue<string>() == "nsu=urn:test:integration;i=3000")!;
            definition["@context"] = new JsonObject { ["p"] = "urn:test:integration" };
            definition["uav:dataTypeName"] = "p:Measurement";
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Describe(result));
            Assert.That(result.Value!.Items!.OfType<UADataType>().Single().BrowseName, Is.EqualTo("1:Measurement"));
            Assert.That(result.Value.NamespaceUris, Is.EqualTo(source.NamespaceUris));
        }

        [TestCase("i=58", "i=58", false)]
        [TestCase("i=58", "i=58", true)]
        [TestCase("nsu=http://opcfoundation.org/UA/;i=58", "i=58", false)]
        [TestCase("nsu=http://opcfoundation.org/UA/;i=58", "i=58", true)]
        [TestCase("i=4294967294", "i=4294967294", false)]
        [TestCase("i=4294967294", "i=4294967294", true)]
        [TestCase("s=UnknownVariableType", "s=UnknownVariableType", false)]
        [TestCase("s=UnknownVariableType", "s=UnknownVariableType", true)]
        public async Task NamespaceZeroDoesNotProveAVariableTypeAsync(
            string typeId,
            string forbiddenTypeId,
            bool asynchronous)
        {
            using WotDocument document = PropertyTypeBinding(typeId);

            WotConversionResult<UANodeSet> result = asynchronous
                ? await WotNodeSetConverter.ToNodeSetResultAsync(document).ConfigureAwait(false)
                : WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False, Describe(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == WotDiagnosticSeverity.Error &&
                diagnostic.Code is WotDiagnosticCode.InvalidTypeBinding or WotDiagnosticCode.UnresolvedTypeBinding),
                Is.True, Describe(result));
            Assert.That(result.Value!.Items!.OfType<UAVariable>().Single().References!.Any(reference =>
                reference.ReferenceType == "HasTypeDefinition" && reference.Value == forbiddenTypeId), Is.False);
        }

        [TestCase("i=63", "i=63")]
        [TestCase("i=68", "i=68")]
        [TestCase("nsu=http://opcfoundation.org/UA/;i=63", "i=63")]
        [TestCase("nsu=http://opcfoundation.org/UA/;i=68", "i=68")]
        [TestCase("nsu=http%3A%2F%2Fopcfoundation.org%2FUA%2F;i=68", "i=68")]
        [TestCase("i=72", "i=72")]
        [TestCase("i=2368", "i=2368")]
        [TestCase("i=2373", "i=2373")]
        [TestCase("i=17497", "i=17497")]
        [TestCase("i=12021", "i=12021")]
        public async Task StandardVariableTypeFactsPreserveTheAuthoredBindingAsync(
            string typeId,
            string expectedTypeId)
        {
            using WotDocument document = PropertyTypeBinding(typeId);
            WotConversionResult<UANodeSet> synchronous = WotNodeSetConverter.ToNodeSetResult(document);
            WotConversionResult<UANodeSet> asynchronous =
                await WotNodeSetConverter.ToNodeSetResultAsync(document).ConfigureAwait(false);

            foreach (WotConversionResult<UANodeSet> result in new[] { synchronous, asynchronous })
            {
                Assert.That(result.Success, Is.True, Describe(result));
                UAVariable property = result.Value!.Items!.OfType<UAVariable>().Single();
                Assert.That(property.References!.Single(reference =>
                    reference.ReferenceType == "HasTypeDefinition").Value, Is.EqualTo(expectedTypeId));
                Assert.That(property.References!.Any(reference =>
                    !reference.IsForward &&
                    reference.ReferenceType == (expectedTypeId == "i=68" ? "HasProperty" : "HasComponent")), Is.True);
            }
        }

        [TestCase(WotExpectedNodeClass.Any, false)]
        [TestCase(WotExpectedNodeClass.ObjectType, false)]
        [TestCase(WotExpectedNodeClass.ReferenceType, false)]
        [TestCase(WotExpectedNodeClass.VariableType, true)]
        public async Task APropertyResolverMustSupplyPositiveVariableTypeClassEvidenceAsync(
            WotExpectedNodeClass nodeClass,
            bool binds)
        {
            const string typeId = "nsu=urn:test:resolved;i=5000";
            using WotDocument document = PropertyTypeBinding(typeId);
            var resolver = new Mock<IWotNodeResolver>();
            resolver.Setup(instance => instance.ResolveByNodeIdAsync(typeId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotResolvedNode?>(new WotResolvedNode(typeId, nodeClass)));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver.Object).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(binds), Describe(result));
            if (binds)
            {
                Assert.That(result.Value!.Items!.OfType<UAVariable>().Single().References!.Single(reference =>
                    reference.ReferenceType == "HasTypeDefinition").Value,
                    Is.EqualTo(WotTestData.LocalNodeId(result.Value, typeId)));
            }
            else
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.InvalidTypeBinding), Is.True, Describe(result));
            }
        }

        [TestCase("uav:variableType", true)]
        [TestCase("uav:objectType", false)]
        public async Task NamespaceZeroTypeBindingsUseAnAvailableDocumentClassAsync(string annotation, bool binds)
        {
            using WotDocument type = WotDocument.Parse(WotTestData.Utf8(
                $$"""
                {
                  "@type": ["tm:ThingModel", "{{annotation}}"],
                  "uav:id": "nsu=http://opcfoundation.org/UA/;i=4294967294",
                  "uav:browseName": "nsu=http://opcfoundation.org/UA/;ResolvedType"
                }
                """));
            using WotDocument document = PropertyTypeBinding("nsu=http://opcfoundation.org/UA/;i=4294967294");
            var resolver = new WotDocumentNodeResolver([type]);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(binds), Describe(result));
            UAVariable property = result.Value!.Items!.OfType<UAVariable>().Single();
            Assert.That(property.References!.Any(reference =>
                reference.ReferenceType == "HasTypeDefinition" && reference.Value == "i=4294967294"),
                Is.EqualTo(binds));
        }

        private static UANodeSet CreateRankedNodeSet(int rank, string? dimensions)
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:integration"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:integration" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1000",
                        BrowseName = "1:RootType",
                        References =
                        [
                            new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=58" },
                            new Reference { ReferenceType = "i=47", Value = "ns=1;i=1100" }
                        ]
                    },
                    new UAVariable
                    {
                        NodeId = "ns=1;i=1100",
                        BrowseName = "1:Samples",
                        ParentNodeId = "ns=1;i=1000",
                        DataType = "i=11",
                        ValueRank = rank,
                        ArrayDimensions = dimensions,
                        References =
                        [
                            new Reference { ReferenceType = "i=40", Value = "i=63" },
                            new Reference { ReferenceType = "i=37", Value = "i=78" }
                        ]
                    }
                ]
            };
        }

        private static UANodeSet CreateComponentNodeSet()
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:integration", "urn:test:parts"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:integration" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1000",
                        BrowseName = "1:AssemblyType",
                        References =
                        [
                            new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=58" },
                            new Reference { ReferenceType = "i=47", Value = "ns=1;i=1100" },
                            new Reference { ReferenceType = "i=47", Value = "ns=1;i=1101" }
                        ]
                    },
                    new UAObject
                    {
                        NodeId = "ns=1;i=1100",
                        BrowseName = "1:Left",
                        References =
                        [
                            new Reference { ReferenceType = "i=40", Value = "ns=2;i=2000" },
                            new Reference { ReferenceType = "i=37", Value = "i=78" }
                        ]
                    },
                    new UAObject
                    {
                        NodeId = "ns=1;i=1101",
                        BrowseName = "1:Right",
                        References = [new Reference { ReferenceType = "i=40", Value = "ns=2;i=2000" }]
                    },
                    new UAObjectType
                    {
                        NodeId = "ns=2;i=2000",
                        BrowseName = "2:MotorType",
                        Description = [new Opc.Ua.Export.LocalizedText { Value = "The original component type." }],
                        References = [new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=58" }]
                    }
                ]
            };
        }

        private static UANodeSet CreateAliasReferenceNodeSet()
        {
            return new UANodeSet
            {
                NamespaceUris = ["urn:test:integration", "urn:test:relations"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:integration" }],
                Aliases =
                [
                    new NodeIdAlias { Alias = "Feeds", Value = "ns=2;i=5001" },
                    new NodeIdAlias { Alias = "Groups", Value = "i=35" }
                ],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1000",
                        BrowseName = "1:RootType",
                        References =
                        [
                            new Reference { ReferenceType = "i=45", IsForward = false, Value = "i=58" },
                            new Reference { ReferenceType = "Feeds", Value = "ns=1;i=1200" },
                            new Reference { ReferenceType = "Groups", Value = "i=85" }
                        ]
                    },
                    new UAObject
                    {
                        NodeId = "ns=1;i=1200",
                        BrowseName = "1:Target",
                        References = [new Reference { ReferenceType = "i=40", Value = "i=58" }]
                    }
                ]
            };
        }

        private static JsonObject Archive(UANodeSet source)
        {
            using WotDocument document = WotNodeSetConverter.FromNodeSet(
                source,
                options: new WotNodeSetConverterOptions { PreservationMode = WotNodeSetPreservationMode.Always });
            Assert.That(document.RootElement.TryGetProperty("uav:nodeSet", out _), Is.True);
            return JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
        }

        private static WotDocument PropertyTypeBinding(string typeId)
        {
            return WotDocument.Parse(WotTestData.Utf8(
                $$"""
                {
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "uav:id": "nsu=urn:test:integration;i=1000",
                  "uav:browseName": "nsu=urn:test:integration;RootType",
                  "properties": {
                    "Value": {
                      "@type": "uav:variable",
                      "type": "number",
                      "links": [{ "rel": "ua:HasTypeDefinition", "href": "{{typeId}}" }]
                    }
                  }
                }
                """));
        }

        private static void AssertArchiveConflict(UANodeSet source, JsonObject root, string pointer)
        {
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));
            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False, Describe(result));
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.NativeProjectionConflict &&
                diagnostic.Location?.JsonPointer == pointer), Is.True, Describe(result));
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(WotTestData.Serialize(result.Value!), Is.EqualTo(WotTestData.Serialize(source)));
        }

        private static string Describe(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.ToString()));
        }

        private static readonly string[] s_componentDeclarationIds =
            ["nsu=urn:test:integration;i=1100", "nsu=urn:test:integration;i=1101"];
        private static readonly string[] s_componentTypeIds = ["ns=1;i=1000", "ns=2;i=2000"];
    }
}
