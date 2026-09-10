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
using System.Linq;
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
    public sealed class WotEventDeclarationEvidenceTests
    {
        [TestCase("inherited", true)]
        [TestCase("qualified", true)]
        [TestCase("vendor", false)]
        [TestCase("nested", false)]
        [TestCase("lookalike", false)]
        [TestCase("wrongOwner", false)]
        [TestCase("wrongDataType", false)]
        [TestCase("wrongRank", false)]
        [TestCase("wrongKind", false)]
        [TestCase("wrongDeclarationId", false)]
        [TestCase("wrongNamespace", false)]
        [TestCase("wrongQueryIdentity", false)]
        [TestCase("wrongQueryClass", false)]
        [TestCase("nonEventAncestor", false)]
        [TestCase("missingQuery", false)]
        [TestCase("missingDeclarations", false)]
        [TestCase("incompleteDeclarations", false)]
        [TestCase("substitutedDeclarationSet", false)]
        [TestCase("duplicateDeclarations", false)]
        public async Task OccurrenceSelectionRequiresTheActualBaseEventDeclarationAsync(string evidence, bool accepted)
        {
            string path = evidence switch
            {
                "qualified" => "core:EventId",
                "vendor" => "vendor:EventId",
                "nested" => "Envelope/EventId",
                "lookalike" => "Token",
                _ => "EventId"
            };
            using WotDocument document = SelectionDocument(path);
            Mock<IWotThingResolver> things = DefinitionResolver(QueryIdentity);
            var nodes = new Mock<IWotNodeResolver>();
            nodes.Setup(value => value.ResolveByNodeIdAsync(QueryIdentity, It.IsAny<CancellationToken>()))
                .ReturnsAsync(evidence == "missingQuery"
                    ? null
                    : new WotResolvedNode(
                        evidence == "wrongQueryIdentity" ? "nsu=urn:test:event;i=7999" : QueryIdentity,
                        evidence == "wrongQueryClass"
                            ? WotExpectedNodeClass.VariableType
                            : WotExpectedNodeClass.ObjectType)
                    {
                        SupertypeNodeIds = [evidence == "nonEventAncestor" ? "i=58" : "i=2041"]
                    });
            WotTypeDeclaration declaration = EventIdDeclaration() with
            {
                NamespaceUri = evidence == "wrongNamespace" ? "urn:test:vendor" : Namespaces.OpcUa,
                DeclaringTypeNodeId = evidence == "wrongOwner" ? QueryIdentity : "i=2041",
                NodeId = evidence == "wrongDeclarationId" ? "nsu=urn:test:event;i=7002" : "i=2042",
                Kind = evidence == "wrongKind" ? WotDeclarationKind.Object : WotDeclarationKind.Variable,
                DataType = evidence == "wrongDataType" ? "i=12" : "i=15",
                ValueRank = evidence == "wrongRank" ? 1 : -1
            };
            Mock<IWotTypeDeclarationResolver> declarations = nodes.As<IWotTypeDeclarationResolver>();
            declarations.Setup(value => value.ResolveDeclarationsAsync(
                    QueryIdentity, WotDeclarationScope.Effective, It.IsAny<CancellationToken>()))
                .ReturnsAsync(evidence == "missingDeclarations"
                    ? null
                    : new WotTypeDeclarationSet
                    {
                        TypeNodeId = evidence == "substitutedDeclarationSet"
                            ? "nsu=urn:test:event;i=7999"
                            : QueryIdentity,
                        IsComplete = evidence != "incompleteDeclarations",
                        Detail = evidence == "incompleteDeclarations" ? "An ancestor is unavailable." : null,
                        Supertypes = ["i=2041"],
                        Declarations = evidence == "duplicateDeclarations"
                            ? [declaration, declaration]
                            : [declaration]
                    });

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, things.Object, null, nodes.Object).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(accepted), Errors(result));
            if (accepted)
            {
                Assert.That(result.Value.Items.OfType<UAObjectType>().Single().References
                    .Single(reference => !reference.IsForward && reference.ReferenceType == "HasSubtype").Value,
                    Is.EqualTo("i=2955"));
                nodes.Verify(value => value.ResolveByNodeIdAsync(
                    QueryIdentity, It.IsAny<CancellationToken>()), Times.Once);
                declarations.Verify(value => value.ResolveDeclarationsAsync(
                    QueryIdentity, WotDeclarationScope.Effective, It.IsAny<CancellationToken>()), Times.Once);
            }
            else
            {
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ConditionEventIdMissing &&
                    diagnostic.Location?.JsonPointer == "/events/alarm/uav:eventSelectClauses"),
                    Is.True, Errors(result));
            }
        }

        [TestCase("i=2041")]
        [TestCase("nsu=http://opcfoundation.org/UA/;i=2041")]
        [TestCase("i=2955")]
        public async Task AStandardQueryUsesTheKnownBaseEventDeclarationAsync(string queryIdentity)
        {
            using WotDocument document = SelectionDocument("core:EventId");
            Mock<IWotThingResolver> things = DefinitionResolver(queryIdentity);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, thingResolver: things.Object).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Errors(result));
        }

        [Test]
        public async Task ADocumentDeclaredCustomQueryInheritsTheActualOccurrenceFieldAsync()
        {
            using WotDocument document = SelectionDocument("core:EventId");
            Mock<IWotThingResolver> things = DefinitionResolver(QueryIdentity);
            using var context = new WotEventTypeTestContext(
                QueryIdentity, "i=2955", "nsu=urn:test:event;CustomEventType");

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, things.Object, null, context.Resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Errors(result));
            Assert.That(result.Value.Items.OfType<UAObjectType>().Single().References
                .Single(reference => !reference.IsForward && reference.ReferenceType == "HasSubtype").Value,
                Is.EqualTo("i=2955"));
        }

        [Test]
        public async Task CancellationDuringOccurrenceDeclarationResolutionIsObservedAsync()
        {
            using WotDocument document = SelectionDocument("EventId");
            using var cancellation = new CancellationTokenSource();
            Mock<IWotThingResolver> things = DefinitionResolver(QueryIdentity);
            var nodes = new Mock<IWotNodeResolver>();
            nodes.Setup(value => value.ResolveByNodeIdAsync(QueryIdentity, cancellation.Token))
                .ReturnsAsync(new WotResolvedNode(QueryIdentity, WotExpectedNodeClass.ObjectType)
                {
                    SupertypeNodeIds = ["i=2041"]
                });
            nodes.As<IWotTypeDeclarationResolver>()
                .Setup(value => value.ResolveDeclarationsAsync(
                    QueryIdentity, WotDeclarationScope.Effective, cancellation.Token))
                .Returns(() =>
                {
                    cancellation.Cancel();
                    return new ValueTask<WotTypeDeclarationSet>(new WotTypeDeclarationSet
                    {
                        TypeNodeId = QueryIdentity,
                        Declarations = [EventIdDeclaration()],
                        Supertypes = ["i=2041"]
                    });
                });

            await Assert.ThatAsync(async () =>
                await WotNodeSetConverter.ToNodeSetResultAsync(
                    document, null, things.Object, null, nodes.Object, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        }

        [TestCase("EventId")]
        [TestCase("Token")]
        public void NamespaceDistinctVendorFieldsAreMaterializedWithoutBorrowingStandardIdentity(string name)
        {
            using var document = WotDocument.Parse(WotTestData.Utf8(
                """
                {
                  "@context": {
                    "ua": "http://opcfoundation.org/UA/", "first": "urn:test:first", "second": "urn:test:second"
                  },
                  "@type": "uav:object", "uav:id": "nsu=urn:test:pump;i=5001",
                  "uav:browseName": "nsu=urn:test:pump;Pump",
                  "events": { "event": { "@type": "uav:eventType", "data": {
                    "type": "object", "properties": {
                """ +
                "\"First\":{\"uav:browseName\":\"first:" +
                name +
                "\",\"type\":\"string\",\"contentEncoding\":\"base64\"}," +
                "\"Second\":{\"uav:browseName\":\"second:" +
                name +
                "\",\"type\":\"string\",\"contentEncoding\":\"base64\"}," +
                """
                      "Standard": { "uav:browseName": "ua:EventId", "type": "string" },
                      "Severity": { "uav:browseName": "first:Severity", "type": "integer", "uav:mapToType": "i=6" }
                    }
                  } } }
                }
                """));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.True, Errors(result));
            UAVariable[] fields = [.. result.Value.Items.OfType<UAVariable>()];
            Assert.That(fields.Select(field => field.BrowseName),
                Is.EqualTo(["2:" + name, "3:" + name, "2:Severity"]));
            Assert.That(result.Value.NamespaceUris, Is.EqualTo(s_vendorNamespaces));
            Assert.That(fields.Select(field => field.NodeId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(fields.Select(field => field.DataType), Is.EqualTo(s_vendorDataTypes));
            string owner = result.Value.Items.OfType<UAObjectType>().Single().NodeId;
            Assert.That(fields.All(field => field.ParentNodeId == owner), Is.True);
        }

        [Test]
        public void NamespaceAliasesStillRejectDuplicateEventFieldDeclarations()
        {
            using var document = WotDocument.Parse(WotTestData.Utf8(
                                     /*lang=json,strict*/
                                     """
                {
                  "@context": { "vendor": "urn:test:vendor", "alias": "urn:test:vendor" },
                  "@type": "uav:object", "uav:id": "nsu=urn:test:pump;i=5001",
                  "uav:browseName": "nsu=urn:test:pump;Pump",
                  "events": { "event": { "@type": "uav:eventType", "data": {
                    "type": "object", "properties": {
                      "One": { "uav:browseName": "vendor:Token", "type": "integer" },
                      "Two": { "uav:browseName": "alias:Token", "type": "integer" }
                    }
                  } } }
                }
                """));

            WotConversionResult<UANodeSet> result = WotNodeSetConverter.ToNodeSetResult(document);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.EventFieldInvalid &&
                diagnostic.Location?.JsonPointer == "/events/event/data/properties/Two"), Is.True);
            Assert.That(result.Value.Items.OfType<UAVariable>().Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task ALinkedVendorFieldKeepsItsDefinitionNamespaceWhenMaterializedAsync()
        {
            using var document = WotDocument.Parse(WotTestData.Utf8(
                                     /*lang=json,strict*/
                                     """
                {
                  "@context": { "vendor": "urn:test:wrong" },
                  "@type": "uav:object", "uav:id": "nsu=urn:test:pump;i=5001",
                  "uav:browseName": "nsu=urn:test:pump;Pump",
                  "events": { "event": { "@type": "uav:eventType", "tm:ref": "urn:test:definition" } }
                }
                """));
            var things = new Mock<IWotThingResolver>();
            things.Setup(value => value.ResolveThingAsync(
                    "urn:test:definition", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(WotTestData.Utf8(
                                         /*lang=json,strict*/
                                         """
                    {
                      "@context": { "vendor": "urn:test:vendor" },
                      "@id": "urn:test:definition", "@type": [ "tm:ThingModel", "uav:eventType" ],
                      "uav:id": "nsu=urn:test:event;i=7001",
                      "data": { "type": "object", "properties": {
                        "EventId": { "uav:browseName": "vendor:EventId", "type": "string", "contentEncoding": "base64" }
                      } }
                    }
                    """)));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, thingResolver: things.Object).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Errors(result));
            UAVariable field = result.Value.Items.OfType<UAVariable>().Single();
            Assert.That(field.BrowseName, Is.EqualTo("2:EventId"));
            Assert.That(result.Value.NamespaceUris[1], Is.EqualTo("urn:test:vendor"));
            Assert.That(field.DataType, Is.EqualTo("i=15"));
            Assert.That(field.NodeId, Is.EqualTo(
                "ns=1;s=/nsu=urn%3Atest%3Apump;Pump/nsu=urn%3Atest%3Apump;event/nsu=urn%3Atest%3Avendor;EventId"));
        }

        [TestCase("uav:browseName", "\"vendor:EventId\"")]
        [TestCase("uav:mapToType", "\"i=12\"")]
        [TestCase("uav:valueRank", "1")]
        public async Task ASelectedOccurrenceCannotBeRedefinedAsAnIncompatibleNativeFieldAsync(
            string member, string value)
        {
            using WotDocument original = SelectionDocument("EventId");
            JsonObject root = JsonNode.Parse(original.Utf8Json.Span).AsObject();
            root["events"]["alarm"]["data"]["properties"]["EventId"][member] = JsonNode.Parse(value);
            using var document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));
            Mock<IWotThingResolver> things = DefinitionResolver(QueryIdentity);
            using var context = new WotEventTypeTestContext(QueryIdentity);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, things.Object, null, context.Resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Any(diagnostic =>
                diagnostic.Code == WotDiagnosticCode.ConditionEventIdMissing &&
                diagnostic.Location?.JsonPointer == "/events/alarm"), Is.True);
        }

        [Test]
        public async Task ALinkedConditionSchemaUsesItsVerifiedInheritedOccurrenceAsync()
        {
            using WotDocument original = SelectionDocument("EventId");
            JsonObject root = JsonNode.Parse(original.Utf8Json.Span).AsObject();
            root["events"]["alarm"].AsObject().Remove("data");
            root["events"]["alarm"]["tm:ref"] = "urn:test:definition";
            using var document = WotDocument.Parse(WotTestData.Utf8(root.ToJsonString()));
            Mock<IWotThingResolver> things = DefinitionResolver(QueryIdentity, fieldOrder: true);
            using var context = new WotEventTypeTestContext(QueryIdentity);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, things.Object, null, context.Resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, Errors(result));
            Assert.That(result.Value.Items.OfType<UAVariable>().Select(field => field.BrowseName),
                Is.EqualTo(s_linkedFields));
        }

        private static WotDocument SelectionDocument(string path)
        {
            return WotDocument.Parse(WotTestData.Utf8(
                """
                {
                  "@context": {
                    "core": "http://opcfoundation.org/UA/",
                    "vendor": "urn:test:vendor"
                  },
                  "@type": "uav:object",
                  "uav:id": "nsu=urn:test:pump;i=5001",
                  "uav:browseName": "nsu=urn:test:pump;Pump",
                  "events": {
                    "alarm": {
                      "@type": "uav:eventType",
                      "uav:conditionType": "core:LimitAlarmType",
                      "uav:eventSelectClauses": [
                """ +
                "{\"tm:ref\":\"urn:test:definition\",\"uav:browsePath\":\"" +
                path +
                "\"}" +
                "],\"data\":" +
                DataSchema +
                "}}}"));
        }

        private static Mock<IWotThingResolver> DefinitionResolver(string identity, bool fieldOrder = false)
        {
            JsonObject data = JsonNode.Parse(DataSchema).AsObject();
            if (fieldOrder)
            {
                data["uav:fieldOrder"] = new JsonArray("EventId", "Token", "Envelope");
            }
            var resolver = new Mock<IWotThingResolver>();
            resolver.Setup(value => value.ResolveThingAsync(
                    "urn:test:definition", It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(WotResolverResult.FromBytes(WotTestData.Utf8(
                    "{\"@id\":\"urn:test:definition\",\"@type\":[\"tm:ThingModel\",\"uav:eventType\"]," +
                    "\"uav:id\":\"" +
                    identity +
                    "\",\"data\":" +
                    data.ToJsonString() +
                    "}")));
            return resolver;
        }

        private static WotTypeDeclaration EventIdDeclaration()
        {
            return new WotTypeDeclaration
            {
                NamespaceUri = Namespaces.OpcUa,
                BrowseName = "EventId",
                Kind = WotDeclarationKind.Variable,
                DeclaringTypeNodeId = "i=2041",
                NodeId = "i=2042",
                DataType = "i=15",
                ValueRank = -1,
                IsInherited = true,
                ReferenceTypeName = "HasProperty",
                TypeDefinitionNodeId = "i=68",
                ModellingRule = WotModellingRule.Mandatory
            };
        }

        private static string Errors(WotConversionResult<UANodeSet> result)
        {
            return string.Join("; ", result.Diagnostics
                .Where(diagnostic => diagnostic.Severity == WotDiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Message));
        }

        private const string QueryIdentity = "nsu=urn:test:event;i=7001";
        private static readonly string[] s_vendorNamespaces = ["urn:test:pump", "urn:test:first", "urn:test:second"];
        private static readonly string[] s_vendorDataTypes = ["i=15", "i=15", "i=6"];
        private static readonly string[] s_linkedFields = ["1:Token", "1:Envelope"];

        private const string DataSchema =
            """
            {
              "type": "object",
              "properties": {
                "EventId": { "type": "string", "contentEncoding": "base64" },
                "Token": { "type": "string", "contentEncoding": "base64" },
                "Envelope": {
                  "type": "object",
                  "properties": { "EventId": { "type": "string", "contentEncoding": "base64" } }
                }
              }
            }
            """;
    }
}
