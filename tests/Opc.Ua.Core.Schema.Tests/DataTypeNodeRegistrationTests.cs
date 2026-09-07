/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;

namespace Opc.Ua.Schema.Tests
{
    /// <summary>
    /// Tests for registering a data type from an address-space data type node.
    /// </summary>
    [TestFixture]
    [Category("Schema")]
    public class DataTypeNodeRegistrationTests
    {
        [Test]
        public void TryAddDataTypeResolvesAllLocalIdForms()
        {
            var definition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = new[]
                {
                    SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32))
                }
            };
            var node = new DataTypeNode
            {
                NodeId = new NodeId(3001, SchemaTestData.TestNamespaceIndex),
                BrowseName = new QualifiedName("NodeType", SchemaTestData.TestNamespaceIndex),
                DataTypeDefinition = new ExtensionObject(definition)
            };
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(SchemaTestData.TestNamespace);
            var registry = new DataTypeDefinitionRegistry();

            bool added = registry.TryAddDataType(node, namespaceUris);

            var provider = new DefaultSchemaProvider(registry, [new Json.JsonSchemaGenerator()]);
            bool resolvedByIndex = provider.TryGetSchema(
                new ExpandedNodeId(node.NodeId),
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? indexSchema);
            bool resolvedByUri = provider.TryGetSchema(
                NodeId.ToExpandedNodeId(node.NodeId, namespaceUris),
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? uriSchema);
            bool resolvedByNodeId = registry.TryResolve(
                node.NodeId,
                out UaTypeDescription? description);

            Assert.Multiple(() =>
            {
                Assert.That(added, Is.True);
                Assert.That(resolvedByIndex, Is.True);
                Assert.That(indexSchema, Is.Not.Null);
                Assert.That(resolvedByUri, Is.True);
                Assert.That(uriSchema, Is.Not.Null);
                Assert.That(resolvedByNodeId, Is.True);
                Assert.That(description, Is.Not.Null);
            });
        }

        [Test]
        public void AddReplacementRemovesStaleUriLookup()
        {
            UaTypeDescription original = SchemaTestData.Structure(
                3003,
                "Original",
                SchemaTestData.TestNamespace,
                SchemaTestData.TestNamespaceIndex,
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription replacement = SchemaTestData.Structure(
                3003,
                "Replacement",
                SchemaTestData.OtherNamespace,
                SchemaTestData.TestNamespaceIndex,
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            var registry = new DataTypeDefinitionRegistry();

            registry.Add(original).Add(replacement);

            bool originalResolved = registry.TryResolve(
                new ExpandedNodeId(original.TypeId.InnerNodeId, original.NamespaceUri),
                out UaTypeDescription? originalDescription);
            bool replacementResolved = registry.TryResolve(
                new ExpandedNodeId(replacement.TypeId.InnerNodeId, replacement.NamespaceUri),
                out UaTypeDescription? replacementDescription);

            Assert.Multiple(() =>
            {
                Assert.That(originalResolved, Is.False);
                Assert.That(originalDescription, Is.Null);
                Assert.That(replacementResolved, Is.True);
                Assert.That(replacementDescription, Is.SameAs(replacement));
                Assert.That(registry.GetNamespaceTypes(original.NamespaceUri), Is.Empty);
                Assert.That(registry.GetNamespaceTypes(replacement.NamespaceUri), Is.EqualTo([replacement]));
            });
        }

        [Test]
        public void AddReindexedTypeReplacesSameUriEntry()
        {
            UaTypeDescription original = SchemaTestData.Structure(
                3004,
                "Original",
                SchemaTestData.TestNamespace,
                SchemaTestData.TestNamespaceIndex,
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            UaTypeDescription replacement = SchemaTestData.Structure(
                3004,
                "Replacement",
                SchemaTestData.TestNamespace,
                SchemaTestData.OtherNamespaceIndex,
                SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32)));
            var registry = new DataTypeDefinitionRegistry();

            registry.Add(original).Add(replacement);

            bool oldNodeIdResolved = registry.TryResolve(
                original.TypeId.InnerNodeId,
                out UaTypeDescription? oldNodeIdDescription);
            bool newNodeIdResolved = registry.TryResolve(
                replacement.TypeId.InnerNodeId,
                out UaTypeDescription? newNodeIdDescription);
            bool uriResolved = registry.TryResolve(
                new ExpandedNodeId(replacement.TypeId.InnerNodeId, replacement.NamespaceUri),
                out UaTypeDescription? uriDescription);

            Assert.Multiple(() =>
            {
                Assert.That(oldNodeIdResolved, Is.False);
                Assert.That(oldNodeIdDescription, Is.Null);
                Assert.That(newNodeIdResolved, Is.True);
                Assert.That(newNodeIdDescription, Is.SameAs(replacement));
                Assert.That(uriResolved, Is.True);
                Assert.That(uriDescription, Is.SameAs(replacement));
                Assert.That(registry.GetNamespaceTypes(replacement.NamespaceUri), Is.EqualTo([replacement]));
            });
        }

        [TestCase((ushort)0)]
        [TestCase(SchemaTestData.TestNamespaceIndex)]
        public void AddUriFormTypeDoesNotInferNamespaceFromBrowseName(ushort browseNamespaceIndex)
        {
            var definition = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    SchemaTestData.Field("Value", SchemaTestData.BuiltIn(BuiltInType.Int32))
                ]
            };
            UaTypeDescription original = SchemaTestData.Structure(
                3005,
                "Original",
                SchemaTestData.Field("OriginalValue", SchemaTestData.BuiltIn(BuiltInType.String)));
            var type = new UaTypeDescription(
                new ExpandedNodeId(3005, SchemaTestData.OtherNamespace),
                new QualifiedName("UriType", browseNamespaceIndex),
                definition);
            var registry = new DataTypeDefinitionRegistry();

            registry.Add(original).Add(type);

            bool localResolved = registry.TryResolve(
                original.TypeId.InnerNodeId,
                out UaTypeDescription? localDescription);
            bool originalUriResolved = registry.TryResolve(
                new ExpandedNodeId(3005, SchemaTestData.TestNamespace),
                out UaTypeDescription? originalUriDescription);
            bool unmappedResolved = registry.TryResolve(
                new NodeId(3005, SchemaTestData.OtherNamespaceIndex),
                out UaTypeDescription? unmappedDescription);
            bool namespaceZeroResolved = registry.TryResolve(
                new NodeId(3005),
                out UaTypeDescription? namespaceZeroDescription);
            bool uriResolved = registry.TryResolve(
                type.TypeId,
                out UaTypeDescription? uriDescription);

            Assert.Multiple(() =>
            {
                Assert.That(localResolved, Is.True);
                Assert.That(localDescription, Is.SameAs(original));
                Assert.That(originalUriResolved, Is.True);
                Assert.That(originalUriDescription, Is.SameAs(original));
                Assert.That(unmappedResolved, Is.False);
                Assert.That(unmappedDescription, Is.Null);
                Assert.That(namespaceZeroResolved, Is.False);
                Assert.That(namespaceZeroDescription, Is.Null);
                Assert.That(uriResolved, Is.True);
                Assert.That(uriDescription, Is.SameAs(type));
                Assert.That(registry.GetNamespaceTypes(SchemaTestData.TestNamespace), Is.EqualTo([original]));
                Assert.That(registry.GetNamespaceTypes(SchemaTestData.OtherNamespace), Is.EqualTo([type]));
            });
        }

        [Test]
        public void AddStandardUriTypeUsesNamespaceZeroIndependentlyOfBrowseName()
        {
            var type = new UaTypeDescription(
                new ExpandedNodeId(7701, Namespaces.OpcUa),
                new QualifiedName("StandardType", SchemaTestData.OtherNamespaceIndex),
                new EnumDefinition());
            var registry = new DataTypeDefinitionRegistry();

            registry.Add(type);

            Assert.Multiple(() =>
            {
                Assert.That(registry.TryResolve(new NodeId(7701), out UaTypeDescription? indexed), Is.True);
                Assert.That(indexed, Is.SameAs(type));
                Assert.That(registry.TryResolve(type.TypeId, out UaTypeDescription? expanded), Is.True);
                Assert.That(expanded, Is.SameAs(type));
                Assert.That(
                    registry.TryResolve(
                        new NodeId(7701, SchemaTestData.OtherNamespaceIndex),
                        out UaTypeDescription? unmapped),
                    Is.False);
                Assert.That(unmapped, Is.Null);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AddKeepsEmptyIdentifiersDistinctByKind(bool reverseOrder)
        {
            (NodeId Id, string Name)[] identifiers =
            [
                (new NodeId(0, SchemaTestData.TestNamespaceIndex), "NumericZero"),
                (new NodeId(Guid.Empty, SchemaTestData.TestNamespaceIndex), "GuidZero"),
                (new NodeId(string.Empty, SchemaTestData.TestNamespaceIndex), "EmptyString"),
                (new NodeId(ByteString.Empty, SchemaTestData.TestNamespaceIndex), "EmptyOpaque")
            ];
            var descriptions = new List<UaTypeDescription>();
            foreach ((NodeId id, string name) in identifiers)
            {
                descriptions.Add(new UaTypeDescription(
                    new ExpandedNodeId(id),
                    new QualifiedName(name, SchemaTestData.TestNamespaceIndex),
                    new EnumDefinition { Fields = [new EnumField { Name = "Enabled", Value = 1 }] },
                    SchemaTestData.TestNamespace));
            }
            if (reverseOrder)
            {
                descriptions.Reverse();
            }
            var registry = new DataTypeDefinitionRegistry();
            foreach (UaTypeDescription description in descriptions)
            {
                registry.Add(description);
            }

            Assert.Multiple(() =>
            {
                foreach (UaTypeDescription expected in descriptions)
                {
                    Assert.That(
                        registry.TryResolve(expected.TypeId.InnerNodeId, out UaTypeDescription? indexed),
                        Is.True,
                        expected.Name);
                    Assert.That(indexed, Is.SameAs(expected), expected.Name);
                    Assert.That(
                        registry.TryResolve(
                            new ExpandedNodeId(expected.TypeId.InnerNodeId, expected.NamespaceUri),
                            out UaTypeDescription? expanded),
                        Is.True,
                        expected.Name);
                    Assert.That(expanded, Is.SameAs(expected), expected.Name);
                }
                Assert.That(
                    registry.GetNamespaceTypes(SchemaTestData.TestNamespace),
                    Is.EquivalentTo(descriptions));
            });
        }

        [TestCaseSource(typeof(SchemaTestData), nameof(SchemaTestData.TypeIdentifierCases))]
        public void AddReplacementPreservesOtherKindsAndNamespaces(NodeId replacedId)
        {
            var registry = new DataTypeDefinitionRegistry();
            var expected = new List<UaTypeDescription>();
            foreach (NodeId id in SchemaTestData.TypeIdentifierCases)
            {
                var description = new UaTypeDescription(
                    new ExpandedNodeId(id),
                    new QualifiedName($"Type{expected.Count}", SchemaTestData.TestNamespaceIndex),
                    new EnumDefinition(),
                    SchemaTestData.TestNamespace);
                registry.Add(description);
                if (id != replacedId)
                {
                    expected.Add(description);
                }
            }
            var otherNamespace = new UaTypeDescription(
                new ExpandedNodeId(replacedId.WithNamespaceIndex(SchemaTestData.OtherNamespaceIndex)),
                new QualifiedName("OtherNamespace", SchemaTestData.OtherNamespaceIndex),
                new EnumDefinition(),
                SchemaTestData.OtherNamespace);
            registry.Add(otherNamespace);
            var replacement = new UaTypeDescription(
                new ExpandedNodeId(replacedId),
                new QualifiedName("Replacement", SchemaTestData.TestNamespaceIndex),
                new EnumDefinition { Fields = [new EnumField { Name = "Replaced", Value = 1 }] },
                SchemaTestData.TestNamespace);

            registry.Add(replacement);
            expected.Add(replacement);

            Assert.Multiple(() =>
            {
                foreach (UaTypeDescription description in expected)
                {
                    Assert.That(
                        registry.TryResolve(
                            new ExpandedNodeId(description.TypeId.InnerNodeId, description.NamespaceUri),
                            out UaTypeDescription? resolved),
                        Is.True,
                        description.Name);
                    Assert.That(resolved, Is.SameAs(description), description.Name);
                    Assert.That(
                        registry.TryResolve(description.TypeId.InnerNodeId, out UaTypeDescription? indexed),
                        Is.True);
                    Assert.That(indexed, Is.SameAs(description));
                }
                Assert.That(
                    registry.TryResolve(
                        new ExpandedNodeId(otherNamespace.TypeId.InnerNodeId, otherNamespace.NamespaceUri),
                        out UaTypeDescription? other),
                    Is.True);
                Assert.That(other, Is.SameAs(otherNamespace));
                Assert.That(registry.GetNamespaceTypes(SchemaTestData.TestNamespace), Is.EquivalentTo(expected));
                Assert.That(registry.GetNamespaceTypes(SchemaTestData.OtherNamespace), Is.EqualTo([otherNamespace]));
            });
        }

        [Test]
        public void TryAddDataTypeWithoutNamespaceTableResolvesIndexForm()
        {
            var node = new DataTypeNode
            {
                NodeId = new NodeId(3006, SchemaTestData.TestNamespaceIndex),
                BrowseName = new QualifiedName("IndexOnly", SchemaTestData.TestNamespaceIndex),
                DataTypeDefinition = new ExtensionObject(new EnumDefinition
                {
                    Fields =
                    [
                        new EnumField { Name = "A", Value = 0 }
                    ]
                })
            };
            var registry = new DataTypeDefinitionRegistry();

            bool added = registry.TryAddDataType(node);
            bool resolved = registry.TryResolve(node.NodeId, out UaTypeDescription? description);

            Assert.Multiple(() =>
            {
                Assert.That(added, Is.True);
                Assert.That(resolved, Is.True);
                Assert.That(description, Is.Not.Null);
            });
        }

        [Test]
        public void TryAddDataTypeReturnsFalseWhenNoDefinition()
        {
            var node = new DataTypeNode
            {
                NodeId = new NodeId(3002, SchemaTestData.TestNamespaceIndex),
                BrowseName = new QualifiedName("Empty", SchemaTestData.TestNamespaceIndex)
            };
            var registry = new DataTypeDefinitionRegistry();

            Assert.That(registry.TryAddDataType(node), Is.False);
        }

        [Test]
        public void TryAddDataTypeThrowsForNullArguments()
        {
            var registry = new DataTypeDefinitionRegistry();
            var node = new DataTypeNode();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => DataTypeDefinitionRegistryExtensions.TryAddDataType(null!, node),
                    Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("registry"));
                Assert.That(
                    () => registry.TryAddDataType(null!),
                    Throws.ArgumentNullException.With.Property(nameof(ArgumentNullException.ParamName)).EqualTo("node"));
            });
        }
    }
}
