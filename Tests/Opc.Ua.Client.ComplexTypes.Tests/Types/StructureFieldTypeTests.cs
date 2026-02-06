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
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Client.ComplexTypes.Tests.Types
{
    /// <summary>
    /// Tests for structure field type emission with different StructureType combinations.
    /// Validates the fix for issue #3510 regarding OptionSet and structure-derived field types.
    /// </summary>
    [TestFixture]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StructureFieldTypeTests
    {
        private MockResolver m_mockResolver;
        private ComplexTypeSystem m_complexTypeSystem;
        private ushort m_namespaceIndex;
        private uint m_nextNodeId;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_mockResolver = new MockResolver();
            m_complexTypeSystem = new ComplexTypeSystem(m_mockResolver, telemetry);
            m_namespaceIndex = m_mockResolver.NamespaceUris.GetIndexOrAppend(Namespaces.MockResolverUrl);
            m_nextNodeId = 5000;
        }

        private void AddDataTypeNode(DataTypeNode node)
        {
            m_mockResolver.DataTypeNodes[node.NodeId] = node;
        }

        /// <summary>
        /// Test that a field with exactly DataTypeIds.Structure emits ExtensionObject type
        /// regardless of StructureType.
        /// </summary>
        [Test]
        public async Task FieldWithExactStructureTypeEmitsExtensionObject(
            [Values(
                StructureType.Structure,
                StructureType.StructureWithOptionalFields,
                StructureType.StructureWithSubtypedValues)]
            StructureType structureType)
        {
            // Arrange: Create a structure with a field of exactly DataTypeIds.Structure
            var structureDefinition = CreateStructureDefinition(structureType);
            structureDefinition.Fields.Add(new StructureField
            {
                Name = "StructureField",
                DataType = DataTypeIds.Structure,
                ValueRank = ValueRanks.Scalar,
                IsOptional = false
            });

            var dataTypeNode = CreateDataTypeNode("TestStructure", structureDefinition);
            AddDataTypeNode(dataTypeNode);

            // Act: Load the type system
            await m_complexTypeSystem.LoadAsync().ConfigureAwait(false);

            // Assert: Field should be ExtensionObject type
            var generatedType = m_complexTypeSystem.GetDefinedTypes()
                .FirstOrDefault(t => t.Name.Contains("TestStructure", StringComparison.Ordinal));
            Assert.That(generatedType, Is.Not.Null, "Type should be generated");

            var fieldProperty = generatedType.GetProperty("StructureField");
            Assert.That(fieldProperty, Is.Not.Null, "Field property should exist");
            Assert.That(fieldProperty.PropertyType, Is.EqualTo(typeof(ExtensionObject)),
                $"Field with exact Structure type should be ExtensionObject for {structureType}");
        }

        /// <summary>
        /// Test that a field with a concrete type derived from Structure (like OptionSet)
        /// uses the generated IEncodeable type in standard Structure.
        /// </summary>
        [Test]
        public async Task FieldWithOptionSetInStandardStructureUsesGeneratedType()
        {
            // Arrange: Create OptionSet type and a structure that uses it
            var optionSetNode = CreateOptionSetDataType();
            AddDataTypeNode(optionSetNode);

            var structureDefinition = CreateStructureDefinition(StructureType.Structure);
            structureDefinition.Fields.Add(new StructureField
            {
                Name = "OptionSetField",
                DataType = optionSetNode.NodeId,
                ValueRank = ValueRanks.Scalar,
                IsOptional = false
            });

            var containerNode = CreateDataTypeNode("ContainerStructure", structureDefinition);
            AddDataTypeNode(containerNode);

            // Act: Load the type system
            await m_complexTypeSystem.LoadAsync().ConfigureAwait(false);

            // Assert: Field should use generated type, not ExtensionObject
            var generatedType = m_complexTypeSystem.GetDefinedTypes()
                .FirstOrDefault(t => t.Name.Contains("ContainerStructure", StringComparison.Ordinal));
            Assert.That(generatedType, Is.Not.Null, "Container type should be generated");

            var fieldProperty = generatedType.GetProperty("OptionSetField");
            Assert.That(fieldProperty, Is.Not.Null, "Field property should exist");
            Assert.That(fieldProperty.PropertyType, Is.Not.EqualTo(typeof(ExtensionObject)),
                "OptionSet field in standard Structure should not be ExtensionObject");
            Assert.That(typeof(IEncodeable).IsAssignableFrom(fieldProperty.PropertyType),
                "OptionSet field should be an IEncodeable type");
        }

        /// <summary>
        /// Test that a field with a concrete type derived from Structure (like OptionSet)
        /// emits ExtensionObject in StructureWithSubtypedValues.
        /// </summary>
        [Test]
        public async Task FieldWithOptionSetInSubtypedStructureEmitsExtensionObject()
        {
            // Arrange: Create OptionSet type and a structure with subtyped values
            var optionSetNode = CreateOptionSetDataType();
            AddDataTypeNode(optionSetNode);

            var structureDefinition = CreateStructureDefinition(StructureType.StructureWithSubtypedValues);
            structureDefinition.Fields.Add(new StructureField
            {
                Name = "OptionSetField",
                DataType = optionSetNode.NodeId,
                ValueRank = ValueRanks.Scalar,
                IsOptional = false
            });

            var containerNode = CreateDataTypeNode("SubtypedContainer", structureDefinition);
            AddDataTypeNode(containerNode);

            // Act: Load the type system
            await m_complexTypeSystem.LoadAsync().ConfigureAwait(false);

            // Assert: Field should be ExtensionObject when structure allows subtypes
            var generatedType = m_complexTypeSystem.GetDefinedTypes()
                .FirstOrDefault(t => t.Name.Contains("SubtypedContainer", StringComparison.Ordinal));
            Assert.That(generatedType, Is.Not.Null, "Container type should be generated");

            var fieldProperty = generatedType.GetProperty("OptionSetField");
            Assert.That(fieldProperty, Is.Not.Null, "Field property should exist");
            Assert.That(fieldProperty.PropertyType, Is.EqualTo(typeof(ExtensionObject)),
                "OptionSet field in StructureWithSubtypedValues should be ExtensionObject");
        }

        /// <summary>
        /// Test that IsOptional flag affects optional mask but not type emission.
        /// </summary>
        [Test]
        public async Task IsOptionalAffectsOnlyOptionalMaskNotTypeEmission()
        {
            // Arrange: Create structure with optional OptionSet field
            var optionSetNode = CreateOptionSetDataType();
            AddDataTypeNode(optionSetNode);

            var structureDefinition = CreateStructureDefinition(StructureType.StructureWithOptionalFields);
            structureDefinition.Fields.Add(new StructureField
            {
                Name = "OptionalOptionSetField",
                DataType = optionSetNode.NodeId,
                ValueRank = ValueRanks.Scalar,
                IsOptional = true  // This should affect option mask, not type
            });

            var containerNode = CreateDataTypeNode("OptionalFieldContainer", structureDefinition);
            AddDataTypeNode(containerNode);

            // Act: Load the type system
            await m_complexTypeSystem.LoadAsync().ConfigureAwait(false);

            // Assert: Field should still use generated type, not ExtensionObject
            var generatedType = m_complexTypeSystem.GetDefinedTypes()
                .FirstOrDefault(t => t.Name.Contains("OptionalFieldContainer", StringComparison.Ordinal));
            Assert.That(generatedType, Is.Not.Null, "Container type should be generated");

            var fieldProperty = generatedType.GetProperty("OptionalOptionSetField");
            Assert.That(fieldProperty, Is.Not.Null, "Field property should exist");
            Assert.That(fieldProperty.PropertyType, Is.Not.EqualTo(typeof(ExtensionObject)),
                "Optional OptionSet field should not be ExtensionObject in StructureWithOptionalFields");
            Assert.That(typeof(IEncodeable).IsAssignableFrom(fieldProperty.PropertyType),
                "Optional OptionSet field should be an IEncodeable type");
        }

        /// <summary>
        /// Test all combinations of StructureType with structure-derived field types.
        /// </summary>
        [Test]
        public async Task AllStructureTypeCombinationsWithDerivedTypes(
            [Values(
                StructureType.Structure,
                StructureType.StructureWithOptionalFields,
                StructureType.StructureWithSubtypedValues,
                StructureType.Union,
                StructureType.UnionWithSubtypedValues)]
            StructureType structureType,
            [Values(true, false)]
            bool isOptional)
        {
            // Arrange: Create custom structure type derived from Structure
            var customStructNode = CreateCustomStructureDataType("CustomStruct");
            AddDataTypeNode(customStructNode);

            var containerDefinition = CreateStructureDefinition(structureType);
            containerDefinition.Fields.Add(new StructureField
            {
                Name = "DerivedStructField",
                DataType = customStructNode.NodeId,
                ValueRank = ValueRanks.Scalar,
                IsOptional = isOptional
            });

            var containerNode = CreateDataTypeNode($"Container_{structureType}_{isOptional}", containerDefinition);
            AddDataTypeNode(containerNode);

            // Act: Load the type system
            await m_complexTypeSystem.LoadAsync().ConfigureAwait(false);

            // Assert: Check field type based on StructureType
            var generatedType = m_complexTypeSystem.GetDefinedTypes()
                .FirstOrDefault(t => t.Name.Contains($"Container_{structureType}_{isOptional}", StringComparison.Ordinal));
            Assert.That(generatedType, Is.Not.Null, $"Container type should be generated for {structureType}");

            var fieldProperty = generatedType.GetProperty("DerivedStructField");
            Assert.That(fieldProperty, Is.Not.Null, "Field property should exist");

            bool allowsSubtypes = structureType == StructureType.StructureWithSubtypedValues ||
                                  structureType == StructureType.UnionWithSubtypedValues;

            if (allowsSubtypes)
            {
                Assert.That(fieldProperty.PropertyType, Is.EqualTo(typeof(ExtensionObject)),
                    $"Field should be ExtensionObject for {structureType} (allows subtypes)");
            }
            else
            {
                Assert.That(fieldProperty.PropertyType, Is.Not.EqualTo(typeof(ExtensionObject)),
                    $"Field should not be ExtensionObject for {structureType} (no subtypes)");
                Assert.That(typeof(IEncodeable).IsAssignableFrom(fieldProperty.PropertyType),
                    $"Field should be IEncodeable for {structureType}");
            }
        }

        #region Helper Methods

        private StructureDefinition CreateStructureDefinition(StructureType structureType)
        {
            return new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = structureType,
                Fields = new StructureFieldCollection()
            };
        }

        private DataTypeNode CreateDataTypeNode(string name, StructureDefinition definition)
        {
            return new DataTypeNode
            {
                NodeId = new NodeId(m_nextNodeId++, m_namespaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName(name, m_namespaceIndex),
                DisplayName = new LocalizedText(name),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(definition)
            };
        }

        private DataTypeNode CreateOptionSetDataType()
        {
            // OptionSet is a structure type (i=12755) derived from Structure
            var optionSetDef = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = new StructureFieldCollection
                {
                    new StructureField
                    {
                        Name = "Value",
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.Scalar
                    },
                    new StructureField
                    {
                        Name = "ValidBits",
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.Scalar
                    }
                }
            };

            return new DataTypeNode
            {
                NodeId = new NodeId(m_nextNodeId++, m_namespaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("CustomOptionSet", m_namespaceIndex),
                DisplayName = new LocalizedText("CustomOptionSet"),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(optionSetDef)
            };
        }

        private DataTypeNode CreateCustomStructureDataType(string name)
        {
            var structDef = new StructureDefinition
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = new StructureFieldCollection
                {
                    new StructureField
                    {
                        Name = "StringValue",
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    new StructureField
                    {
                        Name = "IntValue",
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                }
            };

            return new DataTypeNode
            {
                NodeId = new NodeId(m_nextNodeId++, m_namespaceIndex),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName(name, m_namespaceIndex),
                DisplayName = new LocalizedText(name),
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(structDef)
            };
        }

        #endregion
    }
}
