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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Aas.WoT;
using Opc.Ua.Export;

#pragma warning disable CA1307, CA1865 // TODO: remove when all TFMs agree on single-character string overloads.

namespace Opc.Ua.Aas.Tests.WoT
{
    /// <summary>
    /// Tests the Annex F bridge between AAS environments and WoT Thing Descriptions.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasWotBridgeTests
    {
        [TestCase("absent-versus-empty")]
        [TestCase("every-element-type")]
        [TestCase("identifiable-without-idshort")]
        [TestCase("non-canonical-lexical-forms")]
        [TestCase("ordering-and-nesting")]
        public void CorpusProjectionReconstructsObjectGraph(string scenario)
        {
            AasEnvironment environment = CreateCorpusEnvironment(scenario);
            UANodeSet expected = AasEnvironmentMaterializer.Materialize(environment).NodeSet;

            AasWotProjectionBundle bundle = AasWotBridge.Project(environment);
            AasWotReadResult result = AasWotBridge.Read(bundle.Documents);

            Assert.Multiple(() =>
            {
                Assert.That(bundle.Documents.Span.ToArray(), Has.None.Contains("\"uav:nodeSet\""));
                Assert.That(result.Succeeded, Is.True);
                Assert.That(ObjectGraph(result.NodeSet!), Is.EqualTo(ObjectGraph(expected)));
            });
        }

        [TestCase("absent-versus-empty")]
        [TestCase("every-element-type")]
        [TestCase("identifiable-without-idshort")]
        [TestCase("non-canonical-lexical-forms")]
        [TestCase("ordering-and-nesting")]
        public void CorpusProjectionCarriesBothDirections(string scenario)
        {
            AasWotProjectionBundle bundle = AasWotBridge.Project(CreateCorpusEnvironment(scenario));

            Assert.Multiple(() =>
            {
                Assert.That(bundle.Documents.Span.ToArray().Any(HasComponentOf), Is.True);
                Assert.That(bundle.Documents.Span.ToArray().Any(HasHasComponent), Is.True);
            });
        }

        [Test]
        public void CompactTypeNameOnlyResolvesObjectType()
        {
            Assert.That(Resolve(TypeDocument("i4aas:AASPropertyType", null)),
                Is.EqualTo("nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1021"));
        }

        [Test]
        public void TypeDefinitionLinkOnlyResolvesObjectType()
        {
            Assert.That(Resolve(TypeDocument(null, "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1021")),
                Is.EqualTo("nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1021"));
        }

        [Test]
        public void BothTypeBindingFormsAgree()
        {
            Assert.That(Resolve(TypeDocument("i4aas:AASPropertyType", "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1021")),
                Is.EqualTo("nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1021"));
        }

        [Test]
        public void BothTypeBindingFormsDisagreeIsRejected()
        {
            using JsonDocument document = JsonDocument.Parse(
                TypeDocument("i4aas:AASPropertyType", "nsu=http://opcfoundation.org/UA/I4AAS/v3/;i=1023"));

            Assert.That(
                () => AasWotBridge.ResolveTypeBinding(document.RootElement),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void MissingTypeBindingFallsBackToBaseObjectType()
        {
            Assert.That(Resolve(TypeDocument(null, null)), Is.EqualTo("i=58"));
        }

        [Test]
        public void OrderedContainmentCarriesReferenceIdAndIndex()
        {
            AasWotProjectionBundle bundle = AasWotBridge.Project(CreateCorpusEnvironment("ordering-and-nesting"));

            string orderedMember = bundle.Documents.Span.ToArray().First(document =>
                document.Contains("\"uav:index\": 0", StringComparison.Ordinal));

            Assert.Multiple(() =>
            {
                Assert.That(bundle.Documents.Span.ToArray().Any(document =>
                    document.Contains("\"uav:refId\": \"i=49\"", StringComparison.Ordinal)),
                    Is.True);
                Assert.That(bundle.Documents.Span.ToArray().Any(document =>
                    document.Contains("\"uav:refId\": \"i=47\"", StringComparison.Ordinal)),
                    Is.True);
                Assert.That(orderedMember, Does.Contain("\"uav:browseName\": \"nsu=http://opcfoundation.org/UA/I4AAS/v3/;0\""));
            });
        }

        [Test]
        public void ComponentOfIsDirectionalAndComplete()
        {
            AasWotProjectionBundle bundle = AasWotBridge.Project(CreateCorpusEnvironment("ordering-and-nesting"));
            string child = bundle.Documents.Span.ToArray().First(HasComponentOf);

            using JsonDocument document = JsonDocument.Parse(child);
            JsonElement root = document.RootElement;

            Assert.Multiple(() =>
            {
                Assert.That(root.TryGetProperty("uav:componentOf", out JsonElement componentOf), Is.True);
                Assert.That(componentOf.GetArrayLength(), Is.EqualTo(1));
                Assert.That(root.GetProperty("links").EnumerateArray()
                    .Any(link => link.GetProperty("rel").GetString() == "uav:componentOf"), Is.True);
            });
        }

        [Test]
        public void OperationRoleIndexIsNotUsedAsBrowseName()
        {
            AasWotProjectionBundle bundle = AasWotBridge.Project(CreateCorpusEnvironment("every-element-type"));

            Assert.That(bundle.Documents.Span.ToArray().Any(static document => document.Contains(
                "\"uav:browseName\": \"nsu=http://opcfoundation.org/UA/I4AAS/v3/;OperationInput\"",
                StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void ShellsAndConceptDescriptionsProjectBeyondAnnexFOneClaim()
        {
            AasWotProjectionBundle bundle = AasWotBridge.Project(CreateCorpusEnvironment("identifiable-without-idshort"));
            string union = string.Join(Environment.NewLine, bundle.Documents.Span.ToArray());

            Assert.Multiple(() =>
            {
                Assert.That(union, Does.Contain("aas:AssetAdministrationShell"));
                Assert.That(union, Does.Contain("aas:ConceptDescription"));
                Assert.That(ObjectGraph(AasWotBridge.Read(bundle.Documents).NodeSet!).Any(static item =>
                    item.Contains(":ns=1;i=1011:Organizes", StringComparison.Ordinal)), Is.True);
                Assert.That(ObjectGraph(AasWotBridge.Read(bundle.Documents).NodeSet!).Any(static item =>
                    item.Contains(":ns=1;i=1030:Organizes", StringComparison.Ordinal)), Is.True);
            });
        }

        private static string Resolve(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return AasWotBridge.ResolveTypeBinding(document.RootElement);
        }

        private static string TypeDocument(string? type, string? link)
        {
            string typePart = type is null ? string.Empty : ", \"" + type + "\"";
            string linkPart = link is null
                ? string.Empty
                : ", \"links\": [{\"rel\": \"ua:HasTypeDefinition\", \"href\": \"" + link + "\"}]";
            return "{\"@type\": [\"Thing\", \"uav:object\"" + typePart +
                "], \"uav:id\": \"nsu=http://opcfoundation.org/UA/I4AAS/v3/;s=property\"" + linkPart + "}";
        }

        private static AasEnvironment CreateCorpusEnvironment(string scenario)
        {
            switch (scenario)
            {
                case "absent-versus-empty":
                    return EnvironmentWith("absent", new AasSubmodelElement[]
                    {
                        new AasSubmodelElementCollection { IdShort = Present("EmptyCollection"), Value = PresentElements() },
                        new AasSubmodelElementCollection { IdShort = Present("AbsentCollection") },
                        new AasSubmodelElementList
                        {
                            IdShort = Present("EmptyList"),
                            TypeValueListElement = AASSubmodelElementsDataType.Property,
                            Value = PresentElements()
                        },
                        new AasOperation
                        {
                            IdShort = Present("EmptyOperation"),
                            InputVariables = PresentElements()
                        }
                    });
                case "every-element-type":
                    return EnvironmentWith("every", EveryElementType());
                case "identifiable-without-idshort":
                    return EnvironmentWith("without-idshort", new AasSubmodelElement[]
                    {
                        new AasProperty { IdShort = Present("Named"), ValueType = AASDataTypeDefXsdDataType.String }
                    }, omitTopLevelIdShort: true);
                case "non-canonical-lexical-forms":
                    return EnvironmentWith("lexical", new AasSubmodelElement[]
                    {
                        new AasProperty
                        {
                            IdShort = Present("IntegerLexical"),
                            ValueType = AASDataTypeDefXsdDataType.Int,
                            Value = AasOptional<Variant>.Present(new Variant("001"))
                        },
                        new AasRange
                        {
                            IdShort = Present("BooleanBounds"),
                            ValueType = AASDataTypeDefXsdDataType.Boolean,
                            Min = AasOptional<Variant>.Present(new Variant("0")),
                            Max = AasOptional<Variant>.Present(new Variant("1"))
                        }
                    });
                case "ordering-and-nesting":
                    return EnvironmentWith("ordering", new AasSubmodelElement[]
                    {
                        new AasSubmodelElementList
                        {
                            IdShort = Present("OrderMatters"),
                            TypeValueListElement = AASSubmodelElementsDataType.Property,
                            Value = PresentElements(
                                new AasProperty { ValueType = AASDataTypeDefXsdDataType.String },
                                new AasProperty { ValueType = AASDataTypeDefXsdDataType.String })
                        },
                        new AasSubmodelElementList
                        {
                            IdShort = Present("OrderDoesNotMatter"),
                            OrderRelevant = AasOptional<bool>.Present(false),
                            TypeValueListElement = AASSubmodelElementsDataType.Property,
                            Value = PresentElements(new AasProperty { ValueType = AASDataTypeDefXsdDataType.String })
                        },
                        new AasSubmodelElementCollection
                        {
                            IdShort = Present("Nested"),
                            Value = PresentElements(new AasCapability { IdShort = Present("NestedCapability") })
                        }
                    });
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }
        }

        private static AasSubmodelElement[] EveryElementType()
        {
            AASReferenceDataType reference = Reference();
            return
            [
                new AasProperty
                {
                    IdShort = Present("AProperty"),
                    ValueType = AASDataTypeDefXsdDataType.String,
                    Value = AasOptional<Variant>.Present(new Variant("value"))
                },
                new AasMultiLanguageProperty { IdShort = Present("AMultiLanguageProperty") },
                new AasRange { IdShort = Present("ARange"), ValueType = AASDataTypeDefXsdDataType.Int },
                new AasBlob { IdShort = Present("ABlob"), ContentType = "application/octet-stream" },
                new AasFile { IdShort = Present("AFile"), ContentType = "text/plain" },
                new AasReferenceElement { IdShort = Present("AReferenceElement"), Value = AasOptional<AASReferenceDataType>.Present(reference) },
                new AasRelationshipElement { IdShort = Present("ARelationship"), First = reference, Second = reference },
                new AasAnnotatedRelationshipElement
                {
                    IdShort = Present("AnAnnotatedRelationship"),
                    First = reference,
                    Second = reference,
                    Annotations = PresentElements(new AasCapability { IdShort = Present("Annotation") })
                },
                new AasSubmodelElementCollection
                {
                    IdShort = Present("ACollection"),
                    Value = PresentElements(new AasCapability { IdShort = Present("CollectionMember") })
                },
                new AasSubmodelElementList
                {
                    IdShort = Present("AList"),
                    TypeValueListElement = AASSubmodelElementsDataType.Property,
                    Value = PresentElements(new AasProperty { ValueType = AASDataTypeDefXsdDataType.String })
                },
                new AasEntity
                {
                    IdShort = Present("AnEntity"),
                    EntityType = AASEntityTypeDataType.SelfManagedEntity,
                    Statements = PresentElements(new AasCapability { IdShort = Present("Statement") })
                },
                new AasBasicEventElement
                {
                    IdShort = Present("AnEvent"),
                    Observed = reference,
                    Direction = AASDirectionDataType.Input,
                    State = AASStateOfEventDataType.On
                },
                new AasOperation
                {
                    IdShort = Present("AnOperation"),
                    InputVariables = PresentElements(new AasProperty
                    {
                        IdShort = Present("OperationInput"),
                        ValueType = AASDataTypeDefXsdDataType.String
                    })
                },
                new AasCapability { IdShort = Present("ACapability") }
            ];
        }

        private static AasEnvironment EnvironmentWith(
            string id,
            AasSubmodelElement[] elements,
            bool omitTopLevelIdShort = false)
        {
            return new AasEnvironment
            {
                AssetAdministrationShells = AasOptional<ArrayOf<AasShell>>.Present(
                    new ArrayOf<AasShell>(
                        new[]
                        {
                            new AasShell
                            {
                                Id = "https://example.com/shell/" + id,
                                IdShort = omitTopLevelIdShort ? AasOptional<string>.Absent : Present("Shell"),
                                AssetInformation = new AasAssetInformation
                                {
                                    AssetKind = AASAssetKindDataType.Instance
                                }
                            }
                        })),
                Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(
                    new ArrayOf<AasSubmodel>(
                        new[]
                        {
                            new AasSubmodel
                            {
                                Id = "https://example.com/submodel/" + id,
                                IdShort = omitTopLevelIdShort ? AasOptional<string>.Absent : Present("Submodel"),
                                SubmodelElements = AasOptional<ArrayOf<AasSubmodelElement>>.Present(
                                    new ArrayOf<AasSubmodelElement>(elements))
                            }
                        })),
                ConceptDescriptions = AasOptional<ArrayOf<AasConceptDescription>>.Present(
                    new ArrayOf<AasConceptDescription>(
                        new[]
                        {
                            new AasConceptDescription
                            {
                                Id = "https://example.com/concept/" + id,
                                IdShort = omitTopLevelIdShort ? AasOptional<string>.Absent : Present("ConceptDescription")
                            }
                        }))
            };
        }

        private static string[] ObjectGraph(UANodeSet nodeSet)
        {
            Dictionary<string, UANode> nodes = nodeSet.Items!
                .Where(static node => node.NodeId is not null)
                .ToDictionary(static node => node.NodeId!, StringComparer.Ordinal);
            return nodeSet.Items!
                .OfType<UAObject>()
                .Where(static node => node.BrowseName != "1:AASEnvironment")
                .Select(node => BrowseName(node) + ":" + TypeDefinition(node) + ":" + ParentReference(node, nodes))
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static string BrowseName(UANode node)
        {
            int separator = node.BrowseName!.IndexOf(":", StringComparison.Ordinal);
            return separator >= 0 ? node.BrowseName.Substring(separator + 1) : node.BrowseName;
        }

        private static string TypeDefinition(UANode node)
        {
            return node.References!.Single(static reference => reference.IsForward &&
                reference.ReferenceType == "HasTypeDefinition").Value!;
        }

        private static string ParentReference(UANode node, Dictionary<string, UANode> nodes)
        {
            Reference parent = node.References!.Single(reference => !reference.IsForward &&
                (reference.ReferenceType == "HasComponent" ||
                    reference.ReferenceType == "HasOrderedComponent" ||
                    reference.ReferenceType == "Organizes"));
            _ = nodes;
            return parent.ReferenceType!;
        }

        private static bool HasComponentOf(string document)
        {
            return document.Contains("\"uav:componentOf\"", StringComparison.Ordinal) &&
                document.Contains("\"rel\": \"uav:componentOf\"", StringComparison.Ordinal);
        }

        private static bool HasHasComponent(string document)
        {
            return document.Contains("\"uav:hasComponent\"", StringComparison.Ordinal) &&
                (document.Contains("\"rel\": \"ua:HasComponent\"", StringComparison.Ordinal) ||
                    document.Contains("\"rel\": \"ua:HasOrderedComponent\"", StringComparison.Ordinal));
        }

        private static AASReferenceDataType Reference()
        {
            var reference = new AASReferenceDataType();
            var key = new AASKeyDataType();
            key.Type = AASKeyTypesDataType.GlobalReference;
            key.Value = "reference";
            reference.Type = AASReferenceTypesDataType.ExternalReference;
            reference.Keys = new ArrayOf<AASKeyDataType>(new[] { key });
            return reference;
        }

        private static AasOptional<string> Present(string value)
        {
            return AasOptional<string>.Present(value);
        }

        private static AasOptional<ArrayOf<AasSubmodelElement>> PresentElements(params AasSubmodelElement[] values)
        {
            return AasOptional<ArrayOf<AasSubmodelElement>>.Present(new ArrayOf<AasSubmodelElement>(values));
        }
    }

    #pragma warning restore CA1307, CA1865
}
