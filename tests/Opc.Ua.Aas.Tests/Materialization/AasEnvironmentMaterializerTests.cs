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
using System.IO;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Export;

namespace Opc.Ua.Aas.Tests.Materialization
{
    /// <summary>
    /// Tests clause 6.1.6 instance materialization.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasEnvironmentMaterializerTests
    {
        /// <summary>
        /// A materialized NodeSet is only useful if a loader accepts it. The
        /// other tests here inspect the object graph, which cannot see that a
        /// reference type named by alias was never declared, so this one puts
        /// the document through the same import a runtime NodeSet uses.
        /// </summary>
        [Test]
        public void MaterializedNodeSetImportsIntoAnAddressSpace()
        {
            AasMaterializationResult result = AasEnvironmentMaterializer.Materialize(
                Environment(Submodel(
                    "importable",
                    new AasProperty
                    {
                        IdShort = Present("property"),
                        ValueType = AASDataTypeDefXsdDataType.String,
                        Value = AasOptional<Variant>.Present(new Variant("value"))
                    },
                    new AasSubmodelElementList
                    {
                        IdShort = Present("ordered"),
                        OrderRelevant = AasOptional<bool>.Present(true),
                        TypeValueListElement = AASSubmodelElementsDataType.Property,
                        Value = PresentElements(
                        [
                            new AasProperty
                            {
                                IdShort = Present("member"),
                                ValueType = AASDataTypeDefXsdDataType.Int,
                                Value = AasOptional<Variant>.Present(new Variant(1))
                            }
                        ])
                    },
                    new AasOperation
                    {
                        IdShort = Present("callable"),
                        InputVariables = PresentElements(new AasProperty
                        {
                            IdShort = Present("input"),
                            ValueType = AASDataTypeDefXsdDataType.String
                        })
                    })));

            var namespaces = new NamespaceTable();
            namespaces.Append(Opc.Ua.Aas.V3.Namespaces.AasV3);
            var context = new SystemContext(telemetry: null!) { NamespaceUris = namespaces };
            var nodes = new NodeStateCollection();

            Assert.Multiple(() =>
            {
                Assert.That(result.Diagnostics, Is.Empty);
                Assert.DoesNotThrow(() => result.NodeSet.Import(context, nodes, linkParentChild: true),
                    "The emitted NodeSet has to import, or the metamodel half cannot publish it.");
                Assert.That(nodes, Is.Not.Empty);
            });

            // A Call resolves the Method from the Object it was invoked on, so
            // an Operation whose Invoke is not reachable that way answers
            // BadMethodInvalid however well formed the NodeSet is.
            NodeState operation = nodes.Single(node =>
                node.BrowseName.Name == "callable");
            MethodState? invoke = nodes.OfType<MethodState>()
                .FirstOrDefault(method => method.BrowseName.Name == "Invoke");

            Assert.Multiple(() =>
            {
                Assert.That(invoke, Is.Not.Null);
                Assert.That(operation.FindMethod(context, invoke!.NodeId), Is.Not.Null,
                    "The Operation has to resolve its own Invoke Method.");

                // A generated proxy calls with the declaration id, which
                // FindMethod matches against MethodDeclarationId. An index that
                // does not exist in the emitted NodeSet resolves to nothing and
                // the Call answers BadMethodInvalid.
                Assert.That(invoke!.MethodDeclarationId.IsNull, Is.False);
                Assert.That(operation.FindMethod(context, invoke.MethodDeclarationId), Is.Not.Null,
                    "The Operation has to resolve Invoke by its declaration id too.");
                Assert.That(invoke.MethodDeclarationId.NamespaceIndex,
                    Is.EqualTo(namespaces.GetIndex(Opc.Ua.Aas.V3.Namespaces.AasV3)),
                    "The declaration lives in the AAS namespace.");

                // The argument definitions are emitted with the standard
                // BrowseNames in namespace zero, which is what a Client reads
                // to build the Call.
                ArgumentsOf(nodes, invoke!.NodeId, "InputArguments");
                ArgumentsOf(nodes, invoke.NodeId, "OutputArguments");
            });
        }

        private static BaseVariableState ArgumentsOf(
            NodeStateCollection nodes,
            NodeId methodNodeId,
            string browseName)
        {
            BaseVariableState? arguments = nodes.OfType<BaseVariableState>()
                .FirstOrDefault(node =>
                    node.BrowseName.Name == browseName &&
                    node.BrowseName.NamespaceIndex == 0 &&
                    node is BaseInstanceState instance &&
                    (instance.Parent?.NodeId == methodNodeId ||
                        (UANodeSet.TryGetUnresolvedParentNodeId(instance, out NodeId parentNodeId) &&
                            parentNodeId == methodNodeId)));

            Assert.That(arguments, Is.Not.Null, $"Invoke has to carry its {browseName}.");
            return arguments!;
        }

        /// <summary>
        /// Every reference type the emitted NodeSet names by alias has to be
        /// declared, and has to resolve to the standard NodeId of that
        /// reference type.
        /// </summary>
        [Test]
        public void EveryReferenceTypeUsedIsADeclaredAlias()
        {
            UANodeSet nodeSet = AasEnvironmentMaterializer.Materialize(
                Environment(Submodel(
                    "aliases",
                    new AasSubmodelElementList
                    {
                        IdShort = Present("ordered"),
                        OrderRelevant = AasOptional<bool>.Present(true),
                        TypeValueListElement = AASSubmodelElementsDataType.Property,
                        Value = PresentElements(
                        [
                            new AasProperty
                            {
                                IdShort = Present("member"),
                                ValueType = AASDataTypeDefXsdDataType.String,
                                Value = AasOptional<Variant>.Present(new Variant("v"))
                            }
                        ])
                    }))).NodeSet;

            Dictionary<string, string> declared = (nodeSet.Aliases ?? [])
                .ToDictionary(alias => alias.Alias ?? string.Empty, alias => alias.Value ?? string.Empty, StringComparer.Ordinal);
            List<string> used = [.. (nodeSet.Items ?? [])
                .SelectMany(node => node.References ?? [])
                .Select(reference => reference.ReferenceType)
                .Where(name => name is not null && name.IndexOf('=', StringComparison.Ordinal) < 0)
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)];

            Assert.Multiple(() =>
            {
                Assert.That(used, Is.Not.Empty);
                foreach (string name in used)
                {
                    Assert.That(declared.ContainsKey(name), Is.True,
                        $"'{name}' is used as a reference type but never declared as an alias.");
                }
                Assert.That(declared["HasComponent"],
                    Is.EqualTo(ReferenceTypeIds.HasComponent.ToString()));
                Assert.That(declared["HasOrderedComponent"],
                    Is.EqualTo(ReferenceTypeIds.HasOrderedComponent.ToString()));
                Assert.That(declared["HasProperty"],
                    Is.EqualTo(ReferenceTypeIds.HasProperty.ToString()));
                Assert.That(declared["HasTypeDefinition"],
                    Is.EqualTo(ReferenceTypeIds.HasTypeDefinition.ToString()));
                Assert.That(declared["Organizes"],
                    Is.EqualTo(ReferenceTypeIds.Organizes.ToString()));
            });
        }

        [Test]
        public void EverySubmodelElementTypeMaterializesWithItsAnnexBMembers()
        {
            AASReferenceDataType reference = Reference();
            var submodel = Submodel(
                "elements",
                new AasProperty
                {
                    IdShort = Present("property"),
                    ValueType = AASDataTypeDefXsdDataType.String,
                    Value = AasOptional<Variant>.Present(new Variant("value")),
                    ValueId = AasOptional<AASReferenceDataType>.Present(reference)
                },
                new AasMultiLanguageProperty { IdShort = Present("multi"), ValueId = Present(reference) },
                new AasRange
                {
                    IdShort = Present("range"),
                    ValueType = AASDataTypeDefXsdDataType.Int,
                    Min = AasOptional<Variant>.Present(new Variant(1)),
                    Max = AasOptional<Variant>.Present(new Variant(2))
                },
                new AasBlob { IdShort = Present("blob"), ContentType = "application/octet-stream" },
                new AasFile { IdShort = Present("file"), ContentType = "text/plain", Value = Present("file.txt") },
                new AasReferenceElement { IdShort = Present("reference"), Value = Present(reference) },
                new AasRelationshipElement { IdShort = Present("relationship"), First = reference, Second = reference },
                new AasAnnotatedRelationshipElement
                {
                    IdShort = Present("annotated"),
                    First = reference,
                    Second = reference,
                    Annotations = PresentElements(new AasCapability { IdShort = Present("annotation") })
                },
                new AasSubmodelElementCollection
                {
                    IdShort = Present("collection"),
                    Value = PresentElements(new AasCapability { IdShort = Present("member") })
                },
                new AasSubmodelElementList
                {
                    IdShort = Present("list"),
                    TypeValueListElement = AASSubmodelElementsDataType.Property,
                    Value = PresentElements(new AasProperty { ValueType = AASDataTypeDefXsdDataType.String })
                },
                new AasEntity
                {
                    IdShort = Present("entity"),
                    EntityType = AASEntityTypeDataType.SelfManagedEntity,
                    Statements = PresentElements(new AasCapability { IdShort = Present("statement") })
                },
                new AasBasicEventElement
                {
                    IdShort = Present("event"),
                    Observed = reference,
                    Direction = AASDirectionDataType.Input,
                    State = AASStateOfEventDataType.On
                },
                new AasOperation { IdShort = Present("operation") },
                new AasCapability { IdShort = Present("capability") });

            UANodeSet nodeSet = AasEnvironmentMaterializer.Materialize(Environment(submodel)).NodeSet;

            Assert.Multiple(() =>
            {
                Assert.That(NodesWithBrowseName(nodeSet, "1:property"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:ValueType"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:Value"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:First"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:TypeValueListElement"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:EntityType"), Is.Not.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:Observed"), Is.Not.Empty);
            });
        }

        [Test]
        public void OrderedListUsesHasOrderedComponentAndUnorderedListUsesHasComponent()
        {
            var ordered = new AasSubmodelElementList
            {
                IdShort = Present("ordered"),
                TypeValueListElement = AASSubmodelElementsDataType.Property,
                Value = PresentElements(new AasProperty { ValueType = AASDataTypeDefXsdDataType.String })
            };
            var unordered = new AasSubmodelElementList
            {
                IdShort = Present("unordered"),
                OrderRelevant = AasOptional<bool>.Present(false),
                TypeValueListElement = AASSubmodelElementsDataType.Property,
                Value = PresentElements(new AasProperty { ValueType = AASDataTypeDefXsdDataType.String })
            };

            UANodeSet nodeSet = AasEnvironmentMaterializer.Materialize(Environment(Submodel("lists", ordered, unordered)))
                .NodeSet;
            UANode orderedNode = SingleNode(nodeSet, "1:ordered");
            UANode unorderedNode = SingleNode(nodeSet, "1:unordered");

            Assert.Multiple(() =>
            {
                Assert.That(HasForwardReference(orderedNode, "HasOrderedComponent"), Is.True);
                Assert.That(HasForwardReference(unorderedNode, "HasOrderedComponent"), Is.False);
                Assert.That(HasForwardReference(unorderedNode, "HasComponent"), Is.True);
            });
        }

        [Test]
        public void ListMembersAreNamedByIndexAndCarrySequentialIndex()
        {
            var list = new AasSubmodelElementList
            {
                IdShort = Present("list"),
                TypeValueListElement = AASSubmodelElementsDataType.Property,
                Value = PresentElements(
                    new AasProperty { ValueType = AASDataTypeDefXsdDataType.String },
                    new AasProperty { ValueType = AASDataTypeDefXsdDataType.String })
            };

            UANodeSet nodeSet = AasEnvironmentMaterializer.Materialize(Environment(Submodel("indices", list))).NodeSet;

            Assert.Multiple(() =>
            {
                Assert.That(NodesWithBrowseName(nodeSet, "1:0"), Has.Length.EqualTo(1));
                Assert.That(NodesWithBrowseName(nodeSet, "1:1"), Has.Length.EqualTo(1));
                Assert.That(HasProperty(nodeSet, SingleNode(nodeSet, "1:0"), "1:IdShort"), Is.False);
                Assert.That(HasProperty(nodeSet, SingleNode(nodeSet, "1:1"), "1:IdShort"), Is.False);
                Assert.That(NodesWithBrowseName(nodeSet, "1:Index"), Has.Length.EqualTo(2));
            });
        }

        [Test]
        public void AbsentAndPresentEmptyFieldsMaterializeDifferently()
        {
            var absent = new AasSubmodelElementCollection { IdShort = Present("absent") };
            var empty = new AasSubmodelElementCollection
            {
                IdShort = Present("empty"),
                Value = AasOptional<ArrayOf<AasSubmodelElement>>.Present(ArrayOf<AasSubmodelElement>.Empty),
                Qualifiers = AasOptional<ArrayOf<AASQualifierDataType>>.Present(ArrayOf<AASQualifierDataType>.Empty)
            };

            UANodeSet nodeSet = AasEnvironmentMaterializer.Materialize(Environment(Submodel("optional", absent, empty)))
                .NodeSet;

            Assert.Multiple(() =>
            {
                Assert.That(ForwardReferences(SingleNode(nodeSet, "1:absent"), "HasComponent"), Is.Empty);
                Assert.That(ForwardReferences(SingleNode(nodeSet, "1:empty"), "HasComponent"), Is.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:Qualifiers"), Has.Length.EqualTo(1));
            });
        }

        [Test]
        public void DerivedBrowseNameAllocationIsIndependentOfSourceOrder()
        {
            var left = new AasEnvironment
            {
                Submodels = PresentSubmodels(
                    new AasSubmodel { Id = "b" },
                    new AasSubmodel { Id = "a" })
            };
            var right = new AasEnvironment
            {
                Submodels = PresentSubmodels(
                    new AasSubmodel { Id = "a" },
                    new AasSubmodel { Id = "b" })
            };

            string[] leftNames = TopLevelBrowseNames(AasEnvironmentMaterializer.Materialize(left).NodeSet);
            string[] rightNames = TopLevelBrowseNames(AasEnvironmentMaterializer.Materialize(right).NodeSet);

            Assert.That(leftNames, Is.EquivalentTo(rightNames));
        }

        [Test]
        public void OperationVariablesAreDirectChildrenAndRolePropertiesReferenceThem()
        {
            var operation = new AasOperation
            {
                IdShort = Present("operation"),
                InputVariables = PresentElements(
                    new AasProperty { IdShort = Present("input"), ValueType = AASDataTypeDefXsdDataType.String }),
                OutputVariables = PresentElements(
                    new AasProperty { IdShort = Present("output"), ValueType = AASDataTypeDefXsdDataType.Boolean }),
                InoutputVariables = AasOptional<ArrayOf<AasSubmodelElement>>.Present(ArrayOf<AasSubmodelElement>.Empty)
            };

            UANodeSet nodeSet = AasEnvironmentMaterializer.Materialize(Environment(Submodel("operationSubmodel", operation)))
                .NodeSet;
            UANode operationNode = SingleNode(nodeSet, "1:operation");
            UANode inputVariables = NodesWithBrowseName(nodeSet, "1:InputVariables")
                .First(node => string.Equals(
                    ((UAInstance)node).ParentNodeId,
                    operationNode.NodeId,
                    StringComparison.Ordinal));
            UANode outputVariables = NodesWithBrowseName(nodeSet, "1:OutputVariables")
                .First(node => string.Equals(
                    ((UAInstance)node).ParentNodeId,
                    operationNode.NodeId,
                    StringComparison.Ordinal));
            string roleXml = string.Concat(
                ((UAVariable)inputVariables).Value!.OuterXml,
                ((UAVariable)outputVariables).Value!.OuterXml);

            Assert.Multiple(() =>
            {
                Assert.That(NodesWithBrowseName(nodeSet, "1:inputVariables"), Is.Empty);
                Assert.That(NodesWithBrowseName(nodeSet, "1:input"), Has.Length.EqualTo(1));
                Assert.That(NodesWithBrowseName(nodeSet, "1:output"), Has.Length.EqualTo(1));
                Assert.That(NodesWithBrowseName(nodeSet, "1:InoutputVariables"), Has.Length.EqualTo(1));

                // The two variable-role Properties plus the Invoke Method that
                // AASOperationType declares Mandatory.
                Assert.That(ForwardReferences(operationNode, "HasComponent"), Has.Length.EqualTo(3));
                Assert.That(roleXml, Does.Contain("inputVariables"));
                Assert.That(roleXml, Does.Contain("outputVariables"));
            });
        }

        /// <summary>
        /// AASOperationType declares Invoke Mandatory, and a NodeSet is
        /// imported as written rather than instantiated from its type, so a
        /// materialized Operation only becomes callable if it carries the
        /// Method itself and points at the declaration for its arguments.
        /// </summary>
        [Test]
        public void MaterializedOperationCarriesTheInvokeMethodAndItsDeclaration()
        {
            var operation = new AasOperation
            {
                IdShort = Present("callable"),
                InputVariables = PresentElements(
                    new AasProperty { IdShort = Present("input"), ValueType = AASDataTypeDefXsdDataType.String })
            };

            UANodeSet nodeSet = AasEnvironmentMaterializer
                .Materialize(Environment(Submodel("operationSubmodel", operation))).NodeSet;
            UANode operationNode = SingleNode(nodeSet, "1:callable");
            UAMethod[] invokes = [.. (nodeSet.Items ?? [])
                .OfType<UAMethod>()
                .Where(method => string.Equals(method.ParentNodeId, operationNode.NodeId, StringComparison.Ordinal))];

            Assert.Multiple(() =>
            {
                Assert.That(invokes, Has.Length.EqualTo(1));
                Assert.That(invokes[0].BrowseName, Is.EqualTo("1:Invoke"));
                Assert.That(invokes[0].MethodDeclarationId, Is.EqualTo("ns=1;i=5103"));
                Assert.That(invokes[0].Executable, Is.True);
                Assert.That(ForwardReferences(operationNode, "HasComponent")
                    .Select(reference => reference.Value),
                    Does.Contain(invokes[0].NodeId));
            });
        }

        [Test]
        public void OverlongNodeIdRejectsWholeIdentifiableWithoutPartialSubtree()
        {
            string idShort = new string('a', AasNodeIdEncoding.MaxIdentifierLength);
            var submodel = Submodel("long", new AasProperty
            {
                IdShort = Present(idShort),
                ValueType = AASDataTypeDefXsdDataType.String
            });

            AasMaterializationResult result = AasEnvironmentMaterializer.Materialize(Environment(submodel));

            Assert.Multiple(() =>
            {
                Assert.That(result.HasErrors, Is.True);
                Assert.That(result.Diagnostics[0].Code, Is.EqualTo(AasMaterializationDiagnosticCode.NodeIdTooLong));
                Assert.That(result.NodeSet.Items, Has.Length.EqualTo(1));
            });
        }

        [Test]
        public void DuplicateIdentifiersWithinOneKindAreRejected()
        {
            var environment = new AasEnvironment
            {
                Submodels = PresentSubmodels(new AasSubmodel { Id = "same" }, new AasSubmodel { Id = "same" })
            };

            AasMaterializationResult result = AasEnvironmentMaterializer.Materialize(environment);

            Assert.Multiple(() =>
            {
                Assert.That(result.HasErrors, Is.True);
                Assert.That(result.Diagnostics[0].Code, Is.EqualTo(AasMaterializationDiagnosticCode.DuplicateIdentifier));
                Assert.That(TopLevelBrowseNames(result.NodeSet), Is.Empty);
            });
        }

        [Test]
        public void MaterializingTheSameEnvironmentTwiceProducesByteIdenticalNodeSet()
        {
            AasEnvironment environment = Environment(Submodel("deterministic", new AasProperty
            {
                IdShort = Present("property"),
                ValueType = AASDataTypeDefXsdDataType.String,
                Value = AasOptional<Variant>.Present(new Variant("value"))
            }));

            byte[] first = Write(AasEnvironmentMaterializer.Materialize(environment).NodeSet);
            byte[] second = Write(AasEnvironmentMaterializer.Materialize(environment).NodeSet);

            Assert.That(first, Is.EqualTo(second));
        }

        private static AasEnvironment Environment(AasSubmodel submodel)
        {
            return new AasEnvironment { Submodels = PresentSubmodels(submodel) };
        }

        private static AasSubmodel Submodel(string id, params AasSubmodelElement[] elements)
        {
            return new AasSubmodel
            {
                Id = id,
                IdShort = Present(id),
                SubmodelElements = PresentElements(elements)
            };
        }

        private static AASReferenceDataType Reference()
        {
            AASKeyDataType key = Generated<AASKeyDataType>();
            key.Type = AASKeyTypesDataType.GlobalReference;
            key.Value = "reference";

            AASReferenceDataType reference = Generated<AASReferenceDataType>();
            reference.Type = AASReferenceTypesDataType.ExternalReference;
            reference.Keys = new ArrayOf<AASKeyDataType>(new[] { key });
            return reference;
        }

        private static AasOptional<string> Present(string value)
        {
            return AasOptional<string>.Present(value);
        }

        private static AasOptional<T> Present<T>(T value)
            where T : class
        {
            return AasOptional<T>.Present(value);
        }

        private static AasOptional<ArrayOf<AasSubmodelElement>> PresentElements(params AasSubmodelElement[] values)
        {
            return AasOptional<ArrayOf<AasSubmodelElement>>.Present(new ArrayOf<AasSubmodelElement>(values));
        }

        private static AasOptional<ArrayOf<AasSubmodel>> PresentSubmodels(params AasSubmodel[] values)
        {
            return AasOptional<ArrayOf<AasSubmodel>>.Present(new ArrayOf<AasSubmodel>(values));
        }

        private static UANode SingleNode(UANodeSet nodeSet, string browseName)
        {
            return NodesWithBrowseName(nodeSet, browseName).Single();
        }

        private static UANode[] NodesWithBrowseName(UANodeSet nodeSet, string browseName)
        {
            return nodeSet.Items!.Where(node => string.Equals(node.BrowseName, browseName, StringComparison.Ordinal)).ToArray();
        }

        private static Reference[] ForwardReferences(UANode node, string referenceType)
        {
            return (node.References ?? Array.Empty<Reference>())
                .Where(reference => reference.IsForward &&
                    string.Equals(reference.ReferenceType, referenceType, StringComparison.Ordinal))
                .ToArray();
        }

        private static bool HasForwardReference(UANode node, string referenceType)
        {
            return ForwardReferences(node, referenceType).Length > 0;
        }

        private static bool HasProperty(UANodeSet nodeSet, UANode parent, string browseName)
        {
            foreach (Reference reference in ForwardReferences(parent, "HasProperty"))
            {
                UANode target = nodeSet.Items!.Single(node => node.NodeId == reference.Value);
                if (string.Equals(target.BrowseName, browseName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static string[] TopLevelBrowseNames(UANodeSet nodeSet)
        {
            UANode environment = SingleNode(nodeSet, "1:AASEnvironment");
            return ForwardReferences(environment, "Organizes")
                .Select(reference => nodeSet.Items!.Single(node => node.NodeId == reference.Value).BrowseName!)
                .ToArray();
        }

        private static byte[] Write(UANodeSet nodeSet)
        {
            using var stream = new MemoryStream();
            nodeSet.Write(stream);
            return stream.ToArray();
        }

        private static T Generated<T>()
            where T : class, new()
        {
            return new T();
        }
    }
}
