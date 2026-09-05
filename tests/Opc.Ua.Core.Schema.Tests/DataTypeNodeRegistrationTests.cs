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

        [Test]
        public void AddUriFormTypeUsesBrowseNameNamespaceIndex()
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
            var type = new UaTypeDescription(
                new ExpandedNodeId(3005, SchemaTestData.TestNamespace),
                new QualifiedName("UriType", SchemaTestData.TestNamespaceIndex),
                definition);
            var registry = new DataTypeDefinitionRegistry();

            registry.Add(type);

            bool localResolved = registry.TryResolve(
                new NodeId(3005, SchemaTestData.TestNamespaceIndex),
                out UaTypeDescription? localDescription);
            bool namespaceZeroResolved = registry.TryResolve(
                new NodeId(3005),
                out UaTypeDescription? namespaceZeroDescription);
            bool uriResolved = registry.TryResolve(
                type.TypeId,
                out UaTypeDescription? uriDescription);

            Assert.Multiple(() =>
            {
                Assert.That(localResolved, Is.True);
                Assert.That(localDescription, Is.SameAs(type));
                Assert.That(namespaceZeroResolved, Is.False);
                Assert.That(namespaceZeroDescription, Is.Null);
                Assert.That(uriResolved, Is.True);
                Assert.That(uriDescription, Is.SameAs(type));
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
