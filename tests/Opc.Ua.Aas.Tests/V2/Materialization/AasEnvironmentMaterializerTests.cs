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
using NUnit.Framework;
using Opc.Ua.Aas.V2;
using Opc.Ua.Export;

namespace Opc.Ua.Aas.Tests.V2.Materialization
{
    /// <summary>
    /// Tests OPC 30270 instance materialization.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasEnvironmentMaterializerTests
    {
        [Test]
        public void MaterializedNodeSetImportsIntoAnAddressSpace()
        {
            AasMaterializationResult result = AasEnvironmentMaterializer.Materialize(
                Environment(Submodel(
                    "importable",
                    new AasOperation
                    {
                        IdShort = "callable",
                        Category = "PARAMETER",
                        ModelingKind = AASModelingKindDataType.Instance
                    },
                    new AasFile
                    {
                        IdShort = "document",
                        Category = "VARIABLE",
                        ModelingKind = AASModelingKindDataType.Instance,
                        MimeType = "text/plain",
                        Value = "readme.txt",
                        File = AasOptional<AasFileObject>.Present(new AasFileObject())
                    })));

            var namespaces = new NamespaceTable();
            namespaces.Append(Opc.Ua.Aas.V2.Namespaces.AasV2);
            var context = new SystemContext(telemetry: null!) { NamespaceUris = namespaces };
            var nodes = new NodeStateCollection();

            Assert.Multiple(() =>
            {
                Assert.That(result.Diagnostics, Is.Empty);
                Assert.DoesNotThrow(() => result.NodeSet.Import(context, nodes, linkParentChild: true));
                Assert.That(nodes, Is.Not.Empty);
            });

            NodeState operation = nodes.Single(node => node.BrowseName.Name == "callable");
            MethodState? method = nodes.OfType<MethodState>()
                .FirstOrDefault(node => node.BrowseName.Name == "Operation");

            Assert.Multiple(() =>
            {
                Assert.That(method, Is.Not.Null);
                Assert.That(operation.FindMethod(context, method!.NodeId), Is.Not.Null);
                Assert.That(method.MethodDeclarationId.IsNull, Is.False);
                Assert.That(operation.FindMethod(context, method.MethodDeclarationId), Is.Not.Null);
                Assert.That(method.MethodDeclarationId.NamespaceIndex,
                    Is.EqualTo(namespaces.GetIndex(Opc.Ua.Aas.V2.Namespaces.AasV2)));
                ArgumentsOf(nodes, method.NodeId, "InputArguments");
                ArgumentsOf(nodes, method.NodeId, "OutputArguments");
            });

            MethodState open = nodes.OfType<MethodState>().Single(node => node.BrowseName.Name == "Open");
            MethodState read = nodes.OfType<MethodState>().Single(node => node.BrowseName.Name == "Read");
            MethodState close = nodes.OfType<MethodState>().Single(node => node.BrowseName.Name == "Close");

            Assert.Multiple(() =>
            {
                ArgumentsOf(nodes, open.NodeId, "InputArguments");
                ArgumentsOf(nodes, open.NodeId, "OutputArguments");
                ArgumentsOf(nodes, read.NodeId, "InputArguments");
                ArgumentsOf(nodes, read.NodeId, "OutputArguments");
                ArgumentsOf(nodes, close.NodeId, "InputArguments");
                AssertAliasesResolve(result.NodeSet);
            });
        }

        /// <summary>
        /// OPC 30270 uses the standard FileType unmodified, and every one of
        /// its six Methods and four Properties carries the Mandatory modelling
        /// rule in the pinned NodeSet. OPC 10000-3 6.4.4 requires an instance
        /// of a type to contain all of them, and a Client cannot size a read
        /// or notice an abandoned handle without Size and OpenCount.
        /// </summary>
        [Test]
        public void TheEmbeddedFileCarriesEveryMandatoryFileTypeMember()
        {
            AasMaterializationResult result = AasEnvironmentMaterializer.Materialize(
                Environment(Submodel(
                    "files",
                    new AasFile
                    {
                        IdShort = "document",
                        Category = "VARIABLE",
                        ModelingKind = AASModelingKindDataType.Instance,
                        MimeType = "text/plain",
                        Value = "readme.txt",
                        File = AasOptional<AasFileObject>.Present(new AasFileObject())
                    })));

            var namespaces = new NamespaceTable();
            namespaces.Append(Opc.Ua.Aas.V2.Namespaces.AasV2);
            var context = new SystemContext(telemetry: null!) { NamespaceUris = namespaces };
            var nodes = new NodeStateCollection();
            result.NodeSet.Import(context, nodes, linkParentChild: true);

            NodeState file = nodes.Single(node =>
                node.BrowseName.Name == "File" && node is BaseObjectState);
            var children = new List<BaseInstanceState>();
            file.GetChildren(context, children);
            var names = children.ConvertAll(child => child.BrowseName.Name);

            Assert.That(names, Is.SupersetOf(new[]
            {
                "Open", "Close", "Read", "Write", "GetPosition", "SetPosition",
                "Size", "Writable", "UserWritable", "OpenCount"
            }));
        }

        [Test]
        public void EverySubmodelElementTypeMaterializesWithItsMembers()
        {
            AasReference reference = Reference();
            AasSubmodel submodel = Submodel(
                "elements",
                new AasBlob
                {
                    IdShort = "blob",
                    Category = "VARIABLE",
                    ModelingKind = AASModelingKindDataType.Instance,
                    File = AasOptional<AasFileObject>.Present(new AasFileObject())
                },
                new AasCapability
                {
                    IdShort = "capability",
                    Category = "PARAMETER",
                    ModelingKind = AASModelingKindDataType.Instance
                },
                new AasEntity
                {
                    IdShort = "entity",
                    Category = "PARAMETER",
                    ModelingKind = AASModelingKindDataType.Instance,
                    EntityType = AASEntityTypeDataType.SelfManagedEntity,
                    Asset = AasOptional<AasReference>.Present(reference)
                },
                new AasEvent { IdShort = "event", Category = "EVENT", ModelingKind = AASModelingKindDataType.Instance },
                new AasFile
                {
                    IdShort = "file",
                    Category = "VARIABLE",
                    ModelingKind = AASModelingKindDataType.Instance,
                    MimeType = "text/plain",
                    Value = "file.txt",
                    File = AasOptional<AasFileObject>.Present(new AasFileObject())
                },
                new AasMultiLanguageProperty
                {
                    IdShort = "multi",
                    Category = "VARIABLE",
                    ModelingKind = AASModelingKindDataType.Instance,
                    ValueId = AasOptional<AasReference>.Present(reference)
                },
                new AasOperation
                {
                    IdShort = "operation",
                    Category = "PARAMETER",
                    ModelingKind = AASModelingKindDataType.Instance
                },
                new AasProperty
                {
                    IdShort = "property",
                    Category = "VARIABLE",
                    ModelingKind = AASModelingKindDataType.Instance,
                    ValueType = AASValueTypeDataType.String,
                    Value = AasOptional<Variant>.Present(new Variant("value")),
                    ValueId = AasOptional<AasReference>.Present(reference)
                },
                new AasRange
                {
                    IdShort = "range",
                    Category = "VARIABLE",
                    ModelingKind = AASModelingKindDataType.Instance,
                    ValueType = AASValueTypeDataType.Int32,
                    Min = AasOptional<Variant>.Present(new Variant(1)),
                    Max = AasOptional<Variant>.Present(new Variant(2))
                },
                new AasReferenceElement
                {
                    IdShort = "reference",
                    Category = "PARAMETER",
                    ModelingKind = AASModelingKindDataType.Instance,
                    Value = reference
                },
                new AasRelationshipElement
                {
                    IdShort = "relationship",
                    Category = "RELATIONSHIP",
                    ModelingKind = AASModelingKindDataType.Instance,
                    First = reference,
                    Second = reference
                },
                new AasAnnotatedRelationshipElement
                {
                    IdShort = "annotated",
                    Category = "RELATIONSHIP",
                    ModelingKind = AASModelingKindDataType.Instance,
                    First = reference,
                    Second = reference,
                    DataElements = PresentElements(new AasCapability
                    {
                        IdShort = "annotation",
                        Category = "PARAMETER",
                        ModelingKind = AASModelingKindDataType.Instance
                    })
                },
                new AasSubmodelElementCollection
                {
                    IdShort = "collection",
                    Category = "PARAMETER",
                    ModelingKind = AASModelingKindDataType.Instance,
                    AllowDuplicates = AasOptional<bool>.Present(false),
                    SubmodelElements = PresentElements(new AasCapability
                    {
                        IdShort = "member",
                        Category = "PARAMETER",
                        ModelingKind = AASModelingKindDataType.Instance
                    })
                },
                new AasOrderedSubmodelElementCollection
                {
                    IdShort = "ordered",
                    Category = "PARAMETER",
                    ModelingKind = AASModelingKindDataType.Instance,
                    SubmodelElements = PresentElements(new AasProperty
                    {
                        IdShort = "orderedMember",
                        Category = "VARIABLE",
                        ModelingKind = AASModelingKindDataType.Instance,
                        ValueType = AASValueTypeDataType.String
                    })
                });

            UANodeSet nodeSet = AasEnvironmentMaterializer.Materialize(Environment(submodel)).NodeSet;

            Assert.Multiple(() =>
            {
                Assert.That(NodesWithBrowseName(nodeSet, "1:blob"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:File"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:EntityType"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:MimeType"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:ValueId"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:Operation"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:ValueType"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:First"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:AllowDuplicates"), Is.Not.Empty);
            });
        }

        [Test]
        public void OrderedCollectionUsesHasOrderedComponentAndUnorderedCollectionUsesHasComponent()
        {
            var ordered = new AasOrderedSubmodelElementCollection
            {
                IdShort = "ordered",
                Category = "PARAMETER",
                ModelingKind = AASModelingKindDataType.Instance,
                SubmodelElements = PresentElements(Element("orderedMember"))
            };
            var unordered = new AasSubmodelElementCollection
            {
                IdShort = "unordered",
                Category = "PARAMETER",
                ModelingKind = AASModelingKindDataType.Instance,
                SubmodelElements = PresentElements(Element("unorderedMember"))
            };

            UANodeSet nodeSet = AasEnvironmentMaterializer
                .Materialize(Environment(Submodel("collections", ordered, unordered)))
                .NodeSet;

            Assert.Multiple(() =>
            {
                Assert.That(HasForwardReference(SingleNode(nodeSet, "1:ordered"), "HasOrderedComponent"), Is.True);
                Assert.That(HasForwardReference(SingleNode(nodeSet, "1:unordered"), "HasOrderedComponent"), Is.False);
                Assert.That(HasForwardReference(SingleNode(nodeSet, "1:unordered"), "HasComponent"), Is.True);
            });
        }

        [Test]
        public void ShellMaterializesAssetViewsInterfacesAndAasReferences()
        {
            var shell = new AasShell
            {
                IdShort = "shell",
                Category = "CONSTANT",
                Identification = Identifier("shell"),
                Administration = Administration(),
                Asset = Asset("asset"),
                Views = AasOptional<ArrayOf<AasView>>.Present(new ArrayOf<AasView>(new[]
                {
                    new AasView { Referables = PresentReferences(Reference()) }
                })),
                SubmodelReferences = PresentReferences(Reference())
            };

            UANodeSet nodeSet = AasEnvironmentMaterializer.Materialize(new AasEnvironment
            {
                AssetAdministrationShells = AasOptional<ArrayOf<AasShell>>.Present(
                    new ArrayOf<AasShell>(new[] { shell }))
            }).NodeSet;
            UANode shellNode = SingleNode(nodeSet, "1:shell");

            Assert.Multiple(() =>
            {
                Assert.That(NodesWithBrowseName(nodeSet, "1:Asset"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:View0"), Is.Not.Empty);
                Assert.That(ForwardReferences(shellNode, "HasInterface").Select(reference => reference.Value),
                    Does.Contain("ns=1;i=1033"));
                Assert.That(ForwardReferences(shellNode, "HasInterface").Select(reference => reference.Value),
                    Does.Contain("ns=1;i=1034"));
                Assert.That((nodeSet.Items ?? []).SelectMany(node => node.References ?? [])
                    .Any(reference => string.Equals(reference.ReferenceType, "AASReference", StringComparison.Ordinal)),
                    Is.True);
            });
        }

        [Test]
        public void MaterializingTheSameEnvironmentTwiceProducesByteIdenticalNodeSet()
        {
            AasEnvironment environment = Environment(Submodel("deterministic", new AasProperty
            {
                IdShort = "property",
                Category = "VARIABLE",
                ModelingKind = AASModelingKindDataType.Instance,
                ValueType = AASValueTypeDataType.String,
                Value = AasOptional<Variant>.Present(new Variant("value"))
            }));

            byte[] first = Write(AasEnvironmentMaterializer.Materialize(environment).NodeSet);
            byte[] second = Write(AasEnvironmentMaterializer.Materialize(environment).NodeSet);

            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void OverlongNodeIdRejectsWholeIdentifiableWithoutPartialSubtree()
        {
            string idShort = new string('a', AasNodeIdEncoding.MaxIdentifierLength);
            AasMaterializationResult result = AasEnvironmentMaterializer.Materialize(
                Environment(Submodel("long", Element(idShort))));

            Assert.Multiple(() =>
            {
                Assert.That(result.HasErrors, Is.True);
                Assert.That(result.Diagnostics[0].Code, Is.EqualTo(AasMaterializationDiagnosticCode.NodeIdTooLong));
                Assert.That(result.NodeSet.Items, Has.Length.EqualTo(1));
            });
        }

        [Test]
        public void MissingIdShortProducesDiagnostic()
        {
            AasMaterializationResult result = AasEnvironmentMaterializer.Materialize(
                Environment(Submodel("missing", new AasProperty
                {
                    IdShort = null!,
                    Category = "VARIABLE",
                    ModelingKind = AASModelingKindDataType.Instance,
                    ValueType = AASValueTypeDataType.String
                })));

            Assert.Multiple(() =>
            {
                Assert.That(result.HasErrors, Is.True);
                Assert.That(result.Diagnostics[0].Code, Is.EqualTo(AasMaterializationDiagnosticCode.MissingIdShort));
            });
        }

        private static void AssertAliasesResolve(UANodeSet nodeSet)
        {
            Dictionary<string, string> declared = (nodeSet.Aliases ?? [])
                .ToDictionary(
                    alias => alias.Alias ?? string.Empty,
                    alias => alias.Value ?? string.Empty,
                    StringComparer.Ordinal);
            string[] used = [.. (nodeSet.Items ?? [])
                .SelectMany(node => node.References ?? [])
                .Select(reference => reference.ReferenceType)
                .Where(referenceType => referenceType is not null &&
                    referenceType.IndexOf('=', StringComparison.Ordinal) < 0)
                .Select(referenceType => referenceType!)
                .Distinct(StringComparer.Ordinal)];

            Assert.Multiple(() =>
            {
                foreach (string alias in used)
                {
                    Assert.That(declared.ContainsKey(alias), Is.True);
                }
                Assert.That(declared["HasComponent"], Is.EqualTo(ReferenceTypeIds.HasComponent.ToString()));
                Assert.That(declared["HasOrderedComponent"],
                    Is.EqualTo(ReferenceTypeIds.HasOrderedComponent.ToString()));
                Assert.That(declared["HasProperty"], Is.EqualTo(ReferenceTypeIds.HasProperty.ToString()));
                Assert.That(declared["HasTypeDefinition"], Is.EqualTo(ReferenceTypeIds.HasTypeDefinition.ToString()));
                Assert.That(declared["Organizes"], Is.EqualTo(ReferenceTypeIds.Organizes.ToString()));
                Assert.That(declared["HasInterface"], Is.EqualTo(ReferenceTypeIds.HasInterface.ToString()));
                Assert.That(declared["AASReference"], Is.EqualTo("ns=1;i=4003"));
            });
        }

        private static BaseVariableState ArgumentsOf(NodeStateCollection nodes, NodeId methodNodeId, string browseName)
        {
            BaseVariableState? arguments = nodes.OfType<BaseVariableState>()
                .FirstOrDefault(node =>
                    node.BrowseName.Name == browseName &&
                    node.BrowseName.NamespaceIndex == 0 &&
                    node is BaseInstanceState instance &&
                    (instance.Parent?.NodeId == methodNodeId ||
                        UANodeSet.GetUnresolvedParentNodeId(instance) == methodNodeId));

            Assert.That(arguments, Is.Not.Null, $"{browseName} is required.");
            return arguments!;
        }

        private static AasEnvironment Environment(AasSubmodel submodel)
        {
            return new AasEnvironment
            {
                Submodels = AasOptional<ArrayOf<AasSubmodel>>.Present(
                    new ArrayOf<AasSubmodel>(new[] { submodel }))
            };
        }

        private static AasSubmodel Submodel(string id, params AasSubmodelElement[] elements)
        {
            return new AasSubmodel
            {
                IdShort = id,
                Category = "CONSTANT",
                Identification = Identifier(id),
                Administration = Administration(),
                ModelingKind = AASModelingKindDataType.Instance,
                SubmodelElements = PresentElements(elements)
            };
        }

        private static AasAsset Asset(string id)
        {
            return new AasAsset
            {
                IdShort = id,
                Category = "CONSTANT",
                Identification = Identifier(id),
                Administration = Administration(),
                AssetKind = AASAssetKindDataType.Instance
            };
        }

        private static AasProperty Element(string idShort)
        {
            return new AasProperty
            {
                IdShort = idShort,
                Category = "VARIABLE",
                ModelingKind = AASModelingKindDataType.Instance,
                ValueType = AASValueTypeDataType.String
            };
        }

        private static AasIdentifier Identifier(string id)
        {
            return new AasIdentifier { Id = id, IdType = AASIdentifierTypeDataType.IRI };
        }

        private static AasAdministrativeInformation Administration()
        {
            return new AasAdministrativeInformation { Revision = "0", Version = "1" };
        }

        private static AasReference Reference()
        {
            return new AasReference
            {
                Keys = new ArrayOf<AASKeyDataType>(new[]
                {
                    new AASKeyDataType
                    {
                        Type = AASKeyElementsDataType.GlobalReference,
                        IdType = AASKeyTypeDataType.IRI,
                        Value = "reference"
                    }
                })
            };
        }

        private static AasOptional<ArrayOf<AasSubmodelElement>> PresentElements(params AasSubmodelElement[] values)
        {
            return AasOptional<ArrayOf<AasSubmodelElement>>.Present(new ArrayOf<AasSubmodelElement>(values));
        }

        private static AasOptional<ArrayOf<AasReference>> PresentReferences(params AasReference[] values)
        {
            return AasOptional<ArrayOf<AasReference>>.Present(new ArrayOf<AasReference>(values));
        }

        private static UANode SingleNode(UANodeSet nodeSet, string browseName)
        {
            return NodesWithBrowseName(nodeSet, browseName).Single();
        }

        private static UANode[] NodesWithBrowseName(UANodeSet nodeSet, string browseName)
        {
            return (nodeSet.Items ?? [])
                .Where(node => string.Equals(node.BrowseName, browseName, StringComparison.Ordinal))
                .ToArray();
        }

        private static Reference[] ForwardReferences(UANode node, string referenceType)
        {
            return (node.References ?? [])
                .Where(reference => reference.IsForward &&
                    string.Equals(reference.ReferenceType, referenceType, StringComparison.Ordinal))
                .ToArray();
        }

        private static bool HasForwardReference(UANode node, string referenceType)
        {
            return ForwardReferences(node, referenceType).Length > 0;
        }

        private static byte[] Write(UANodeSet nodeSet)
        {
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            return stream.ToArray();
        }
    }
}
