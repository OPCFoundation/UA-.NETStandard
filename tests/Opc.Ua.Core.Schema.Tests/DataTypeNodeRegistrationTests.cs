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
        public void TryAddDataTypeRegistersNodeDefinition()
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
            var registry = new DataTypeDefinitionRegistry();

            bool added = registry.TryAddDataType(node);

            var provider = new DefaultSchemaProvider(registry, [new Json.JsonSchemaGenerator()]);
            bool resolved = provider.TryGetSchema(
                new ExpandedNodeId(new NodeId(3001, SchemaTestData.TestNamespaceIndex)),
                UaSchemaFormat.JsonCompact,
                UaSchemaScope.Type,
                out IUaSchema? schema);

            Assert.Multiple(() =>
            {
                Assert.That(added, Is.True);
                Assert.That(resolved, Is.True);
                Assert.That(schema, Is.Not.Null);
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
