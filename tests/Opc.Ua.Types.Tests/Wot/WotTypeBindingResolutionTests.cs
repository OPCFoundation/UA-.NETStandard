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

#nullable enable
using System;
using System.Collections.Generic;
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
    /// <summary>
    /// WoT Binding Section 5.2.1 names a type in either or both of two forms
    /// and defines a table of outcomes for the combinations. Section 5.1.5
    /// resolves both against a local context. These pin the table.
    /// </summary>
    [TestFixture]
    public sealed class WotTypeBindingResolutionTests
    {
        private const string PumpNamespace = "urn:test:pump";
        private const string TankTypeId = "nsu=urn:test:pump;i=1042";
        private const string OtherTypeId = "nsu=urn:test:pump;i=9999";

        [TestCase("urn:test:pump")]
        [TestCase("urn%3Atest%3Apump")]
        public async Task UriQualifiedBrowseNamesResolveTypesAndReferencesAsync(string namespaceForm)
        {
            using WotDocument type = WotDocument.Parse(WotTestData.Utf8(
                $$"""
                {
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "uav:id": "nsu=urn:test:pump;i=1042",
                  "uav:browseName": "nsu={{namespaceForm}};TankType"
                }
                """));
            using WotDocument referenceType = WotDocument.Parse(WotTestData.Utf8(
                $$"""
                {
                  "@type": ["tm:ThingModel", "uav:referenceType"],
                  "uav:id": "nsu=urn:test:pump;i=5001",
                  "uav:browseName": "nsu={{namespaceForm}};Feeds",
                  "uav:inverseName": "FedBy"
                }
                """));
            var resolver = new WotDocumentNodeResolver([type, referenceType]);

            ArrayOf<WotResolvedNode> types = await resolver.ResolveByBrowseNameAsync(
                PumpNamespace, "TankType", WotExpectedNodeClass.ObjectType).ConfigureAwait(false);
            ArrayOf<WotResolvedReferenceType> references = await resolver.ResolveReferenceTypesAsync(
                PumpNamespace, "Feeds").ConfigureAwait(false);
            ArrayOf<WotResolvedReferenceType> inverse = await resolver.ResolveReferenceTypesAsync(
                PumpNamespace, "FedBy").ConfigureAwait(false);

            Assert.That(types.Count, Is.EqualTo(1));
            Assert.That(references.Count, Is.EqualTo(1));
            Assert.That(inverse.Count, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(types[0].NodeId, Is.EqualTo(TankTypeId));
                Assert.That(references[0].NodeId, Is.EqualTo("nsu=urn:test:pump;i=5001"));
                Assert.That(inverse[0].NodeId, Is.EqualTo("nsu=urn:test:pump;i=5001"));
                Assert.That(inverse[0].IsForward, Is.False);
            });
        }

        [TestCase("uav:variableType", true, true)]
        [TestCase("uav:objectType", true, false)]
        [TestCase("uav:variableType", false, false)]
        public async Task AnAffordanceResolvesItsOwnVariableTypeBindingAsync(
            string nodeKind,
            bool available,
            bool binds)
        {
            const string typeId = "nsu=urn:test:variable-type;i=200";
            using WotDocument type = WotDocument.Parse(WotTestData.Utf8(
                $$"""
                {
                  "@type": ["tm:ThingModel", "{{nodeKind}}"],
                  "uav:id": "nsu=urn:test:variable-type;i=200",
                  "uav:browseName": "nsu=urn:test:variable-type;CustomType"
                }
                """));
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(
                """
                {
                  "@context": { "v": "urn:outer:" },
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "title": "Root",
                  "uav:id": "nsu=urn:test:binding;i=1",
                  "properties": {
                    "Value": {
                      "@context": { "v": "urn:test:variable-type" },
                      "@type": "v:CustomType",
                      "type": "number",
                      "links": [{
                        "@context": { "a": "http://opcfoundation.org/UA/" },
                        "rel": "a:HasTypeDefinition",
                        "href": "nsu=urn:test:variable-type;i=200"
                      }]
                    }
                  }
                }
                """));
            var resolver = new WotDocumentNodeResolver(available ? new[] { type } : []);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.EqualTo(binds),
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            if (binds)
            {
                UAVariable variable = result.Value!.Items!.OfType<UAVariable>().Single();
                Assert.That(variable.References!.Single(r => r.ReferenceType == "HasTypeDefinition").Value,
                    Is.EqualTo(WotTestData.LocalNodeId(result.Value!, typeId)));
            }
            else
            {
                Assert.That(result.Diagnostics.Any(d =>
                    d.Severity == WotDiagnosticSeverity.Error &&
                    d.Code is WotDiagnosticCode.InvalidTypeBinding or WotDiagnosticCode.UnresolvedTypeBinding),
                    Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task TwoComponentTemplatesCreateDistinctObjectDeclarationsAsync(bool documentReference)
        {
            const string typeId = "nsu=urn:test:parts;i=1000";
            const string typeUri = "https://models.example/MotorType.json";
            string href = documentReference ? typeUri : typeId;
            using WotDocument type = WotDocument.Parse(WotTestData.Utf8(
                """
                {
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "uav:id": "nsu=urn:test:parts;i=1000",
                  "uav:browseName": "nsu=urn:test:parts;MotorType"
                }
                """));
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(
                $$"""
                {
                  "@context": { "m": "urn:test:assembly" },
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "title": "Assembly",
                  "uav:id": "nsu=urn:test:assembly;i=1",
                  "uav:browseName": "m:Assembly",
                  "links": [
                    {
                      "rel": "ua:HasComponent",
                      "href": "{{href}}",
                      "uav:refName": "left-link",
                      "uav:declaration": {
                        "uav:id": "nsu=urn:test:assembly;i=101",
                        "uav:browseName": "m:Left",
                        "uav:modellingRule": "Mandatory"
                      }
                    },
                    {
                      "rel": "ua:HasComponent",
                      "href": "{{href}}",
                      "uav:refName": "right-link",
                      "uav:declaration": { "uav:browseName": "m:Right" }
                    }
                  ]
                }
                """));
            var resolver = new WotDocumentNodeResolver([type]);
            var things = new Mock<IWotThingResolver>();
            things.Setup(r => r.ResolveThingAsync(
                    typeUri, It.IsAny<WotResolutionContext>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WotResolverResult>(WotResolverResult.FromBytes(type.Utf8Json.ToArray())));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, documentReference ? things.Object : null, null,
                documentReference ? null : resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            UAObject[] declarations = result.Value!.Items!.OfType<UAObject>().ToArray();
            Assert.That(declarations, Has.Length.EqualTo(2));
            Assert.Multiple(() =>
            {
                Assert.That(declarations.Single(n => n.BrowseName == "1:Left").NodeId, Is.EqualTo("ns=1;i=101"));
                Assert.That(declarations.Single(n => n.BrowseName == "1:Right").NodeId,
                    Is.EqualTo("ns=1;s=/nsu=urn%3Atest%3Aassembly;Assembly/nsu=urn%3Atest%3Aassembly;Right"));
            });
            foreach (UAObject declaration in declarations)
            {
                Assert.That(declaration.References!.Single(r => r.ReferenceType == "HasTypeDefinition").Value,
                    Is.EqualTo(WotTestData.LocalNodeId(result.Value!, typeId)));
                Assert.That(declaration.References!.Any(r =>
                    !r.IsForward && r.ReferenceType is "HasComponent" or "i=47" && r.Value == "ns=1;i=1"), Is.True);
            }
            Assert.That(declarations.Single(n => n.BrowseName == "1:Left").References!.Any(r =>
                r.ReferenceType == "HasModellingRule" && r.Value == "i=78"), Is.True);
            UANodeSet unchangedType = WotNodeSetConverter.ToNodeSet(type);
            Assert.That(unchangedType.Items!.Single(), Is.TypeOf<UAObjectType>());
            Assert.That(unchangedType.Items![0].BrowseName, Is.EqualTo("1:MotorType"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AVariableTemplateCreatesAVariableWithTheTypesValueShapeAsync(bool nativeDefinition)
        {
            using WotDocument type = WotDocument.Parse(WotTestData.Utf8(
                """
                {
                  "@type": ["tm:ThingModel", "uav:variableType"],
                  "uav:id": "nsu=urn:test:parts;i=2000",
                  "uav:browseName": "nsu=urn:test:parts;MatrixType",
                  "uav:mapToType": "i=11",
                  "uav:valueRank": 2,
                  "uav:arrayDimensions": [2,3]
                }
                """));
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(
                """
                {
                  "@context": { "m": "urn:test:assembly" },
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "uav:id": "nsu=urn:test:assembly;i=1",
                  "uav:browseName": "m:Assembly",
                  "links": [{
                    "rel": "ua:HasComponent",
                    "href": "nsu=urn:test:parts;i=2000",
                    "uav:declaration": { "uav:browseName": "m:Samples" }
                  }]
                }
                """));
            using WotDocument nativeType = WotNodeSetConverter.FromNodeSet(WotNodeSetConverter.ToNodeSet(type));
            if (nativeDefinition)
            {
                Assert.That(nativeType.TryGetNativeProjection(out _), Is.True);
            }
            var resolver = new WotDocumentNodeResolver([nativeDefinition ? nativeType : type]);

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            UAVariable declaration = result.Value!.Items!.OfType<UAVariable>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(declaration.BrowseName, Is.EqualTo("1:Samples"));
                Assert.That(declaration.DataType, Is.EqualTo("i=11"));
                Assert.That(declaration.ValueRank, Is.EqualTo(2));
                Assert.That(declaration.ArrayDimensions, Is.EqualTo("2,3"));
                Assert.That(result.Value.Items!.OfType<UAVariableType>(), Is.Empty);
            });
            UANodeSet typeNodes = WotNodeSetConverter.ToNodeSet(type);
            UAVariableType definition = typeNodes.Items!.OfType<UAVariableType>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(definition.DataType, Is.EqualTo("i=11"));
                Assert.That(definition.ValueRank, Is.EqualTo(2));
                Assert.That(definition.ArrayDimensions, Is.EqualTo("2,3"));
            });
        }

        [Test]
        public async Task ObjectDeclarationsExportAsDistinctComponentTemplatesAsync()
        {
            var source = new UANodeSet
            {
                NamespaceUris = ["urn:test:assembly", "urn:test:parts"],
                Models = [new ModelTableEntry { ModelUri = "urn:test:assembly" }],
                Items =
                [
                    new UAObjectType
                    {
                        NodeId = "ns=1;i=1",
                        BrowseName = "1:Assembly",
                        References =
                        [
                            new Reference { ReferenceType = "HasSubtype", IsForward = false, Value = "i=58" },
                            new Reference { ReferenceType = "HasComponent", Value = "ns=1;i=2" },
                            new Reference { ReferenceType = "HasComponent", Value = "ns=1;i=3" }
                        ]
                    },
                    new UAObject
                    {
                        NodeId = "ns=1;i=2",
                        BrowseName = "1:Left",
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", Value = "ns=2;i=1000" },
                            new Reference { ReferenceType = "HasModellingRule", Value = "i=78" }
                        ]
                    },
                    new UAObject
                    {
                        NodeId = "ns=1;i=3",
                        BrowseName = "1:Right",
                        References =
                        [
                            new Reference { ReferenceType = "HasTypeDefinition", Value = "ns=2;i=1000" }
                        ]
                    }
                ]
            };
            using WotDocument type = WotDocument.Parse(WotTestData.Utf8(
                """
                {
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "uav:id": "nsu=urn:test:parts;i=1000",
                  "uav:browseName": "nsu=urn:test:parts;MotorType"
                }
                """));
            using WotDocument document = WotNodeSetConverter.FromNodeSet(source);
            JsonElement[] links = document.Links.Where(l => l.TryGetProperty("uav:declaration", out _)).ToArray();

            Assert.That(links, Has.Length.EqualTo(2));
            Assert.That(links.All(l => l.GetProperty("href").GetString() == "nsu=urn:test:parts;i=1000"), Is.True);
            Assert.That(links[0].GetProperty("uav:declaration").GetProperty("uav:id").GetString(),
                Is.EqualTo("nsu=urn:test:assembly;i=2"));
            Assert.That(links[1].GetProperty("uav:declaration").GetProperty("uav:id").GetString(),
                Is.EqualTo("nsu=urn:test:assembly;i=3"));
            Assert.That(links[0].GetProperty("uav:declaration").GetProperty("uav:modellingRule").GetString(),
                Is.EqualTo("Mandatory"));

            JsonObject readable = JsonNode.Parse(document.Utf8Json.Span)!.AsObject();
            readable.Remove("uav:nodes");
            readable.Remove("uav:nodeSet");
            using WotDocument readableDocument = WotDocument.Parse(WotTestData.Utf8(readable.ToJsonString()));
            WotConversionResult<UANodeSet> restored = await WotNodeSetConverter.ToNodeSetResultAsync(
                readableDocument, null, null, null, new WotDocumentNodeResolver([type])).ConfigureAwait(false);

            Assert.That(restored.Success, Is.True, string.Join("; ", restored.Diagnostics.Select(d => d.Message)));
            UAObject[] declarations = restored.Value!.Items!.OfType<UAObject>().ToArray();
            Assert.That(declarations, Has.Length.EqualTo(2));
            Assert.That(declarations.Single(n => n.NodeId == "ns=1;i=2").BrowseName, Is.EqualTo("1:Left"));
            Assert.That(declarations.Single(n => n.NodeId == "ns=1;i=3").BrowseName, Is.EqualTo("1:Right"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AComponentTypeNeedsAnExplicitlyNamedInstanceDeclarationAsync(bool unnamedDeclaration)
        {
            using WotDocument type = WotDocument.Parse(WotTestData.Utf8(
                """
                {
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "uav:id": "nsu=urn:test:parts;i=1000",
                  "uav:browseName": "nsu=urn:test:parts;MotorType"
                }
                """));
            string declaration = unnamedDeclaration ? ",\"uav:declaration\":{}" : string.Empty;
            using WotDocument document = WotDocument.Parse(WotTestData.Utf8(
                $$"""
                {
                  "@type": ["tm:ThingModel", "uav:objectType"],
                  "uav:id": "nsu=urn:test:assembly;i=1",
                  "links": [{
                    "rel": "ua:HasComponent",
                    "href": "nsu=urn:test:parts;i=1000",
                    "uav:refName": "NotTheNodesBrowseName"
                    {{declaration}}
                  }]
                }
                """));

            WotConversionResult<UANodeSet> result = await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, new WotDocumentNodeResolver([type])).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value!.Items![0].References!.Any(r =>
                r.IsForward && r.Value == WotTestData.LocalNodeId(result.Value, "nsu=urn:test:parts;i=1000")),
                Is.False);
        }

        [Test]
        public async Task ANameThatResolvesUniquelyBindsToThatTypeAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(TankTypeId)] }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", link: null, resolver).ConfigureAwait(false);

            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo("ns=1;i=1042"));
        }

        /// <summary>
        /// An unresolved binding fails rather than falling back, because a
        /// silently mistyped node is worse than a reported failure.
        /// </summary>
        [Test]
        public async Task ANameInAHeldNamespaceThatResolvesToNothingIsReportedAsync()
        {
            var resolver = new StubResolver(PumpNamespace);

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", link: null, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnresolvedTypeBinding),
                Is.True);
            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo(WotVocabulary.BaseObjectType),
                "The node must not be bound to a guess.");
        }

        /// <summary>
        /// A binding is told from an annotation by namespace, not by whether
        /// the lookup succeeds, so a name in a namespace nothing holds stays an
        /// annotation and is not reported.
        /// </summary>
        [Test]
        public async Task ANameInAnUnheldNamespaceIsAnAnnotationNotABindingAsync()
        {
            var resolver = new StubResolver(PumpNamespace);

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"saref:TemperatureSensor\"", link: null, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.UnresolvedTypeBinding),
                Is.False,
                "A namespace the local context does not hold cannot have been meant as a type.");
            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo(WotVocabulary.BaseObjectType));
        }

        [Test]
        public async Task AnAmbiguousNameWithNothingToSettleItIsInvalidAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(TankTypeId), Node(OtherTypeId)] }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", link: null, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.AmbiguousTypeBinding),
                Is.True);
        }

        /// <summary>
        /// The link settles an ambiguous name, exactly as `uav:refId` settles
        /// an ambiguous `rel`.
        /// </summary>
        [Test]
        public async Task TheLinkSettlesAnAmbiguousNameAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(TankTypeId), Node(OtherTypeId)] },
                ByNodeId = { [TankTypeId] = Node(TankTypeId) }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", TankTypeId, resolver).ConfigureAwait(false);

            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo("ns=1;i=1042"));
            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.AmbiguousTypeBinding),
                Is.False);
        }

        /// <summary>
        /// A name that resolves to nothing while the identifier resolves is a
        /// mistake in the name, not a shorthand for the identifier.
        /// </summary>
        [Test]
        public async Task ANameResolvingToNothingWhileTheLinkResolvesIsInvalidAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByNodeId = { [TankTypeId] = Node(TankTypeId) }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", TankTypeId, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidTypeBinding),
                Is.True);
            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo(WotVocabulary.BaseObjectType));
        }

        [Test]
        public async Task TwoFormsResolvingToDifferentNodesAreInvalidAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(OtherTypeId)] },
                ByNodeId = { [TankTypeId] = Node(TankTypeId) }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", TankTypeId, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidTypeBinding),
                Is.True);
        }

        [Test]
        public async Task TwoFormsAgreeingBindToThatTypeAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(TankTypeId)] },
                ByNodeId = { [TankTypeId] = Node(TankTypeId) }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", TankTypeId, resolver).ConfigureAwait(false);

            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo("ns=1;i=1042"));
        }

        /// <summary>
        /// A Thing Description projects an Object, so a VariableType is the
        /// wrong NodeClass for it.
        /// </summary>
        [Test]
        public async Task AResolvedTypeOfTheWrongNodeClassIsInvalidAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName =
                {
                    ["TankType"] =
                        [new WotResolvedNode(TankTypeId, WotExpectedNodeClass.VariableType)]
                }
            };

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", link: null, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.InvalidTypeBinding),
                Is.True);
        }

        /// <summary>
        /// The sibling documents of the conversion are consulted before the
        /// AddressSpace, so a set of documents authored together resolves to
        /// itself.
        /// </summary>
        [Test]
        public async Task SiblingsWinOverTheAddressSpaceAsync()
        {
            var siblings = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(TankTypeId)] }
            };
            var addressSpace = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(OtherTypeId)] }
            };
            var composite = new WotCompositeNodeResolver(siblings, addressSpace);

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", link: null, composite).ConfigureAwait(false);

            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo("ns=1;i=1042"),
                "The sibling documents are the first part of the local context.");
        }

        [Test]
        public async Task TheAddressSpaceIsTheFallbackAsync()
        {
            var siblings = new StubResolver(PumpNamespace);
            var addressSpace = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(OtherTypeId)] }
            };
            var composite = new WotCompositeNodeResolver(siblings, addressSpace);

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\"", link: null, composite).ConfigureAwait(false);

            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo("ns=1;i=9999"));
        }

        [Test]
        public async Task ThingModelWithTwoBindingNamesIsReportedAsync()
        {
            var resolver = new StubResolver(PumpNamespace);

            WotConversionResult<UANodeSet> result = await ConvertThingModelAsync(
                "\"pump:TankType\",\"pump:OtherType\"",
                isEventType: false,
                resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.AmbiguousTypeBinding),
                Is.True);
            Assert.That(SuperTypeOf(result.Value!), Is.EqualTo(WotVocabulary.BaseObjectType));
            Assert.That(HasTypeDefinition(result.Value!), Is.False);
        }

        [Test]
        public async Task ThingModelWithResolvableBindingKeepsEventSubtypeAsync()
        {
            var resolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(TankTypeId)] }
            };

            WotConversionResult<UANodeSet> result = await ConvertThingModelAsync(
                "\"pump:TankType\"",
                isEventType: true,
                resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d =>
                    d.Code is WotDiagnosticCode.AmbiguousTypeBinding or
                        WotDiagnosticCode.InvalidTypeBinding or
                        WotDiagnosticCode.UnresolvedTypeBinding),
                Is.False);
            Assert.That(SuperTypeOf(result.Value!), Is.EqualTo(WotVocabulary.BaseEventType));
            Assert.That(HasTypeDefinition(result.Value!), Is.False);
        }

        [Test]
        public async Task ThingDescriptionWithTwoBindingNamesIsStillReportedAsync()
        {
            var resolver = new StubResolver(PumpNamespace);

            WotConversionResult<UANodeSet> result = await ConvertAsync(
                "\"pump:TankType\",\"pump:OtherType\"", link: null, resolver).ConfigureAwait(false);

            Assert.That(
                result.Diagnostics.Any(d => d.Code == WotDiagnosticCode.AmbiguousTypeBinding),
                Is.True);
            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo(WotVocabulary.BaseObjectType));
        }

        [Test]
        public async Task ThingDescriptionExtendingThingModelAndBindingDifferentTypeIsInvalidAsync()
        {
            var nodeResolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["OtherType"] = [Node(OtherTypeId)] }
            };
            var thingResolver = new StubThingResolver(TankTypeId);

            WotConversionResult<UANodeSet> result = await ConvertExtendingThingModelAsync(
                "\"pump:OtherType\"",
                nodeResolver,
                thingResolver).ConfigureAwait(false);

            Assert.That(HasThingModelTypeMismatchDiagnostic(result), Is.True);
        }

        [Test]
        public async Task ThingDescriptionExtendingThingModelAndBindingSameTypeIsValidAsync()
        {
            var nodeResolver = new StubResolver(PumpNamespace)
            {
                ByName = { ["TankType"] = [Node(TankTypeId)] }
            };
            var thingResolver = new StubThingResolver(TankTypeId);

            WotConversionResult<UANodeSet> result = await ConvertExtendingThingModelAsync(
                "\"pump:TankType\"",
                nodeResolver,
                thingResolver).ConfigureAwait(false);

            Assert.That(HasThingModelTypeMismatchDiagnostic(result), Is.False);
            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo("ns=1;i=1042"));
            Assert.That(ExtendsTargetOf(result.Value!), Is.EqualTo("ns=1;i=1042"));
        }

        [Test]
        public async Task ThingDescriptionExtendingThingModelWithoutBindingIsUnchangedAsync()
        {
            var nodeResolver = new StubResolver(PumpNamespace);
            var thingResolver = new StubThingResolver(TankTypeId);

            WotConversionResult<UANodeSet> result = await ConvertExtendingThingModelAsync(
                typeToken: null,
                nodeResolver,
                thingResolver).ConfigureAwait(false);

            Assert.That(HasThingModelTypeMismatchDiagnostic(result), Is.False);
            Assert.That(TypeDefinitionOf(result.Value!), Is.EqualTo(WotVocabulary.BaseObjectType));
            Assert.That(ExtendsTargetOf(result.Value!), Is.EqualTo("ns=1;i=1042"));
        }

        private static WotResolvedNode Node(string nodeId)
        {
            return new WotResolvedNode(nodeId, WotExpectedNodeClass.ObjectType);
        }

        private static string TypeDefinitionOf(UANodeSet nodeSet)
        {
            UANode root = nodeSet.Items!.First(i => i is UAObject);
            return root.References!.First(r =>
                string.Equals(r.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal)).Value!;
        }

        private static string SuperTypeOf(UANodeSet nodeSet)
        {
            UANode root = nodeSet.Items!.First(i => i is UAObjectType);
            return root.References!.First(r =>
                string.Equals(r.ReferenceType, "HasSubtype", StringComparison.Ordinal) &&
                !r.IsForward).Value!;
        }

        private static string ExtendsTargetOf(UANodeSet nodeSet)
        {
            UANode root = nodeSet.Items!.First(i => i is UAObject);
            return root.References!.First(r =>
                string.Equals(r.ReferenceType, "HasSubtype", StringComparison.Ordinal) &&
                !r.IsForward).Value!;
        }

        private static bool HasTypeDefinition(UANodeSet nodeSet)
        {
            UANode root = nodeSet.Items!.First(i => i is UAObjectType);
            return root.References!.Any(r =>
                string.Equals(r.ReferenceType, "HasTypeDefinition", StringComparison.Ordinal));
        }

        private static bool HasThingModelTypeMismatchDiagnostic(
            WotConversionResult<UANodeSet> result)
        {
            return result.Diagnostics.Any(d =>
                d.Code == WotDiagnosticCode.InvalidTypeBinding &&
                d.Message.Contains("instantiates a Thing Model", StringComparison.Ordinal));
        }

        private static async Task<WotConversionResult<UANodeSet>> ConvertAsync(
            string typeToken,
            string? link,
            IWotNodeResolver resolver)
        {
            string links = link is null
                ? string.Empty
                : ",\"links\":[{\"rel\":\"ua:HasTypeDefinition\",\"href\":\"" + link + "\"}]";

            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"saref\":\"https://saref.etsi.org/core/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"Thing\",\"uav:object\"," + typeToken + "]," +
                "\"title\":\"Tank\",\"uav:browseName\":\"pump:Tank\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=5001\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}" +
                links + "}");

            using WotDocument document = WotDocument.Parse(json);
            return await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver).ConfigureAwait(false);
        }

        private static async Task<WotConversionResult<UANodeSet>> ConvertExtendingThingModelAsync(
            string? typeToken,
            IWotNodeResolver nodeResolver,
            IWotThingResolver thingResolver)
        {
            string typeTokens = typeToken is null
                ? "\"Thing\",\"uav:object\""
                : "\"Thing\",\"uav:object\"," + typeToken;
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[" + typeTokens + "]," +
                "\"title\":\"Tank\",\"uav:browseName\":\"pump:Tank\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=5001\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}," +
                "\"links\":[{\"rel\":\"tm:extends\",\"href\":\"thing-model.json\"}]}");

            using WotDocument document = WotDocument.Parse(json);
            return await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, thingResolver, null, nodeResolver).ConfigureAwait(false);
        }

        private static async Task<WotConversionResult<UANodeSet>> ConvertThingModelAsync(
            string typeToken,
            bool isEventType,
            IWotNodeResolver resolver)
        {
            string projectedType = isEventType ? "uav:eventType" : "uav:objectType";
            byte[] json = WotTestData.Utf8(
                "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                "\"ua\":\"http://opcfoundation.org/UA/\"," +
                "\"pump\":\"" + PumpNamespace + "\"}]," +
                "\"@type\":[\"tm:ThingModel\",\"" + projectedType + "\"," + typeToken + "]," +
                "\"title\":\"TankType\",\"uav:browseName\":\"pump:TankType\"," +
                "\"uav:id\":\"nsu=urn:test:pump;i=5002\"," +
                "\"security\":\"nosec_sc\"," +
                "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}}");

            using WotDocument document = WotDocument.Parse(json);
            return await WotNodeSetConverter.ToNodeSetResultAsync(
                document, null, null, null, resolver).ConfigureAwait(false);
        }

        /// <summary>
        /// Stands in for one part of the Section 5.1.5 local context.
        /// </summary>
        private sealed class StubResolver(string heldNamespace) : IWotNodeResolver
        {
            public Dictionary<string, List<WotResolvedNode>> ByName { get; } = [];

            public Dictionary<string, WotResolvedNode> ByNodeId { get; } = [];

            public ValueTask<bool> HoldsNamespaceAsync(
                string namespaceUri, CancellationToken cancellationToken = default)
            {
                return new ValueTask<bool>(
                    string.Equals(namespaceUri, heldNamespace, StringComparison.Ordinal));
            }

            public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
                string namespaceUri,
                string browseName,
                WotExpectedNodeClass expected,
                CancellationToken cancellationToken = default)
            {
                ArrayOf<WotResolvedNode> matches =
                    ByName.TryGetValue(browseName, out List<WotResolvedNode>? found)
                        ? new ArrayOf<WotResolvedNode>(found.ToArray())
                        : ArrayOf<WotResolvedNode>.Empty;
                return new ValueTask<ArrayOf<WotResolvedNode>>(matches);
            }

            public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
                string expandedNodeId, CancellationToken cancellationToken = default)
            {
                return new ValueTask<WotResolvedNode?>(
                    ByNodeId.TryGetValue(expandedNodeId, out WotResolvedNode found)
                        ? found
                        : null);
            }
        }

        private sealed class StubThingResolver(string projectedTypeId) : IWotThingResolver
        {
            public ValueTask<WotResolverResult> ResolveThingAsync(
                string reference,
                WotResolutionContext context,
                CancellationToken cancellationToken)
            {
                byte[] json = WotTestData.Utf8(
                    "{\"@context\":[\"https://www.w3.org/2022/wot/td/v1.1\"," +
                    "{\"tm\":\"https://www.w3.org/2019/wot/tm#\"," +
                    "\"uav\":\"http://opcfoundation.org/UA/WoT-Binding/\"," +
                    "\"pump\":\"" + PumpNamespace + "\"}]," +
                    "\"@type\":[\"tm:ThingModel\",\"uav:objectType\"]," +
                    "\"title\":\"TankType\",\"uav:browseName\":\"pump:TankType\"," +
                    "\"uav:id\":\"" + projectedTypeId + "\"," +
                    "\"security\":\"nosec_sc\"," +
                    "\"securityDefinitions\":{\"nosec_sc\":{\"scheme\":\"nosec\"}}}");
                return new ValueTask<WotResolverResult>(WotResolverResult.FromBytes(json));
            }
        }
    }
}
