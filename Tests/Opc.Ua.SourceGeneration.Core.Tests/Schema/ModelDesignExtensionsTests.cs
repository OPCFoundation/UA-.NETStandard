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
using System.Xml;
using Moq;
using NUnit.Framework;
using Opc.Ua.Types;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Unit tests for the ModelDesignExtensions class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ModelDesignExtensionsTests
    {
        /// <summary>
        /// Tests that DetermineBasicDataType returns BaseDataType when dataType is null.
        /// </summary>
        [Test]
        public void DetermineBasicDataType_NullDataType_ReturnsBaseDataType()
        {
            // Arrange
            DataTypeDesign dataType = null;

            // Act
            BasicDataType result = dataType.DetermineBasicDataType();

            // Assert
            Assert.That(result, Is.EqualTo(BasicDataType.BaseDataType));
        }

        /// <summary>
        /// Tests that DetermineBasicDataType returns Enumeration when IsOptionSet is true.
        /// </summary>
        [Test]
        public void DetermineBasicDataType_IsOptionSetTrue_ReturnsEnumeration()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                IsOptionSet = true
            };

            // Act
            BasicDataType result = mockDataType.DetermineBasicDataType();

            // Assert
            Assert.That(result, Is.EqualTo(BasicDataType.Enumeration));
        }

        /// <summary>
        /// Tests that DetermineBasicDataType returns the correct BasicDataType when IsBasicDataType returns true.
        /// </summary>
        /// <param name="basicType">The basic data type to test.</param>
        [TestCase(BasicDataType.Boolean)]
        [TestCase(BasicDataType.SByte)]
        [TestCase(BasicDataType.Byte)]
        [TestCase(BasicDataType.Int16)]
        [TestCase(BasicDataType.UInt16)]
        [TestCase(BasicDataType.Int32)]
        [TestCase(BasicDataType.UInt32)]
        [TestCase(BasicDataType.Int64)]
        [TestCase(BasicDataType.UInt64)]
        [TestCase(BasicDataType.Float)]
        [TestCase(BasicDataType.Double)]
        [TestCase(BasicDataType.String)]
        [TestCase(BasicDataType.DateTime)]
        [TestCase(BasicDataType.Guid)]
        [TestCase(BasicDataType.ByteString)]
        [TestCase(BasicDataType.XmlElement)]
        [TestCase(BasicDataType.NodeId)]
        [TestCase(BasicDataType.ExpandedNodeId)]
        [TestCase(BasicDataType.StatusCode)]
        [TestCase(BasicDataType.DiagnosticInfo)]
        [TestCase(BasicDataType.QualifiedName)]
        [TestCase(BasicDataType.LocalizedText)]
        [TestCase(BasicDataType.DataValue)]
        [TestCase(BasicDataType.Number)]
        [TestCase(BasicDataType.Integer)]
        [TestCase(BasicDataType.UInteger)]
        [TestCase(BasicDataType.Enumeration)]
        [TestCase(BasicDataType.Structure)]
        public void DetermineBasicDataType_IsBasicDataType_ReturnsCorrectBasicDataType(BasicDataType basicType)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName(basicType.ToString(), "http://opcfoundation.org/UA/")
            };

            // Act
            BasicDataType result = mockDataType.DetermineBasicDataType();

            // Assert
            Assert.That(result, Is.EqualTo(basicType));
        }

        /// <summary>
        /// Tests that DetermineBasicDataType recursively checks BaseTypeNode when not a basic data type.
        /// </summary>
        [Test]
        public void DetermineBasicDataType_NotBasicDataType_ReturnsBaseTypeNodeBasicDataType()
        {
            // Arrange
            var mockBaseType = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/")
            };

            var mockDataType = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("CustomInt32", "http://custom.org/UA/"),
                BaseTypeNode = mockBaseType
            };

            // Act
            BasicDataType result = mockDataType.DetermineBasicDataType();

            // Assert
            Assert.That(result, Is.EqualTo(BasicDataType.Int32));
        }

        /// <summary>
        /// Tests that DetermineBasicDataType returns UserDefined when BaseTypeNode is Structure.
        /// </summary>
        [Test]
        public void DetermineBasicDataType_BaseTypeIsStructure_ReturnsUserDefined()
        {
            // Arrange
            var mockBaseType = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("Structure", "http://opcfoundation.org/UA/")
            };

            var mockDataType = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("CustomStructure", "http://custom.org/UA/"),
                BaseTypeNode = mockBaseType
            };

            // Act
            BasicDataType result = mockDataType.DetermineBasicDataType();

            // Assert
            Assert.That(result, Is.EqualTo(BasicDataType.UserDefined));
        }

        /// <summary>
        /// Tests that DetermineBasicDataType handles multiple levels of inheritance.
        /// </summary>
        [Test]
        public void DetermineBasicDataType_MultipleLevelsOfInheritance_ReturnsCorrectBasicType()
        {
            // Arrange
            var mockGrandParent = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("String", "http://opcfoundation.org/UA/")
            };

            var mockParent = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("CustomString1", "http://custom.org/UA/"),
                BaseTypeNode = mockGrandParent
            };

            var mockDataType = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("CustomString2", "http://custom.org/UA/"),
                BaseTypeNode = mockParent
            };

            // Act
            BasicDataType result = mockDataType.DetermineBasicDataType();

            // Assert
            Assert.That(result, Is.EqualTo(BasicDataType.String));
        }

        /// <summary>
        /// Tests that DetermineBasicDataType returns BaseDataType when BaseTypeNode is null.
        /// </summary>
        [Test]
        public void DetermineBasicDataType_BaseTypeNodeIsNull_ReturnsBaseDataType()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("CustomType", "http://custom.org/UA/"),
                BaseTypeNode = null
            };

            // Act
            BasicDataType result = mockDataType.DetermineBasicDataType();

            // Assert
            Assert.That(result, Is.EqualTo(BasicDataType.BaseDataType));
        }

        /// <summary>
        /// Tests that DetermineBasicDataType returns UserDefined when inheriting from Structure through multiple levels.
        /// </summary>
        [Test]
        public void DetermineBasicDataType_InheritsFromStructureThroughMultipleLevels_ReturnsUserDefined()
        {
            // Arrange
            var mockGrandParent = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("Structure", "http://opcfoundation.org/UA/")
            };

            var mockParent = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("CustomStructure1", "http://custom.org/UA/"),
                BaseTypeNode = mockGrandParent
            };

            var mockDataType = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("CustomStructure2", "http://custom.org/UA/"),
                BaseTypeNode = mockParent
            };

            // Act
            BasicDataType result = mockDataType.DetermineBasicDataType();

            // Assert
            Assert.That(result, Is.EqualTo(BasicDataType.UserDefined));
        }

        /// <summary>
        /// Tests that DetermineBasicDataType returns correct type when BaseTypeNode is not DataTypeDesign.
        /// </summary>
        [Test]
        public void DetermineBasicDataType_BaseTypeNodeNotDataTypeDesign_ReturnsBaseDataType()
        {
            // Arrange
            var mockBaseType = new TypeDesign();
            var mockDataType = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("CustomType", "http://custom.org/UA/"),
                BaseTypeNode = mockBaseType
            };

            // Act
            BasicDataType result = mockDataType.DetermineBasicDataType();

            // Assert
            Assert.That(result, Is.EqualTo(BasicDataType.BaseDataType));
        }

        /// <summary>
        /// Tests that DetermineBasicDataType handles BaseDataType return value correctly.
        /// </summary>
        [Test]
        public void DetermineBasicDataType_BaseTypeReturnsBaseDataType_ReturnsBaseDataType()
        {
            // Arrange
            var mockBaseType = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("BaseDataType", "http://opcfoundation.org/UA/")
            };

            var mockDataType = new DataTypeDesign
            {
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("CustomType", "http://custom.org/UA/"),
                BaseTypeNode = mockBaseType
            };

            // Act
            BasicDataType result = mockDataType.DetermineBasicDataType();

            // Assert
            Assert.That(result, Is.EqualTo(BasicDataType.BaseDataType));
        }

        /// <summary>
        /// Tests that GetNodeClassString returns "Variable" when the input is a VariableDesign instance.
        /// </summary>
        [Test]
        public void GetNodeClassString_VariableDesign_ReturnsVariable()
        {
            // Arrange
            var node = new VariableDesign();

            // Act
            string result = node.GetNodeClassAsString();

            // Assert
            Assert.That(result, Is.EqualTo("Variable"));
        }

        /// <summary>
        /// Tests that GetNodeClassString returns "VariableType" when the input is a VariableTypeDesign instance.
        /// </summary>
        [Test]
        public void GetNodeClassString_VariableTypeDesign_ReturnsVariableType()
        {
            // Arrange
            var node = new VariableTypeDesign();

            // Act
            string result = node.GetNodeClassAsString();

            // Assert
            Assert.That(result, Is.EqualTo("VariableType"));
        }

        /// <summary>
        /// Tests that GetNodeClassString returns "Object" when the input is an ObjectDesign instance.
        /// </summary>
        [Test]
        public void GetNodeClassString_ObjectDesign_ReturnsObject()
        {
            // Arrange
            var node = new ObjectDesign();

            // Act
            string result = node.GetNodeClassAsString();

            // Assert
            Assert.That(result, Is.EqualTo("Object"));
        }

        /// <summary>
        /// Tests that GetNodeClassString returns "ObjectType" when the input is an ObjectTypeDesign instance.
        /// </summary>
        [Test]
        public void GetNodeClassString_ObjectTypeDesign_ReturnsObjectType()
        {
            // Arrange
            var node = new ObjectTypeDesign();

            // Act
            string result = node.GetNodeClassAsString();

            // Assert
            Assert.That(result, Is.EqualTo("ObjectType"));
        }

        /// <summary>
        /// Tests that GetNodeClassString returns "ReferenceType" when the input is a ReferenceTypeDesign instance.
        /// </summary>
        [Test]
        public void GetNodeClassString_ReferenceTypeDesign_ReturnsReferenceType()
        {
            // Arrange
            var node = new ReferenceTypeDesign();

            // Act
            string result = node.GetNodeClassAsString();

            // Assert
            Assert.That(result, Is.EqualTo("ReferenceType"));
        }

        /// <summary>
        /// Tests that GetNodeClassString returns "DataType" when the input is a DataTypeDesign instance.
        /// </summary>
        [Test]
        public void GetNodeClassString_DataTypeDesign_ReturnsDataType()
        {
            // Arrange
            var node = new DataTypeDesign();

            // Act
            string result = node.GetNodeClassAsString();

            // Assert
            Assert.That(result, Is.EqualTo("DataType"));
        }

        /// <summary>
        /// Tests that GetNodeClassString returns "Method" when the input is a MethodDesign instance.
        /// </summary>
        [Test]
        public void GetNodeClassString_MethodDesign_ReturnsMethod()
        {
            // Arrange
            var node = new MethodDesign();

            // Act
            string result = node.GetNodeClassAsString();

            // Assert
            Assert.That(result, Is.EqualTo("Method"));
        }

        /// <summary>
        /// Tests that GetNodeClassString returns "View" when the input is a ViewDesign instance.
        /// </summary>
        [Test]
        public void GetNodeClassString_ViewDesign_ReturnsView()
        {
            // Arrange
            var node = new ViewDesign();

            // Act
            string result = node.GetNodeClassAsString();

            // Assert
            Assert.That(result, Is.EqualTo("View"));
        }

        /// <summary>
        /// Tests that GetNodeClassString returns "Node" when the input is a base NodeDesign instance.
        /// </summary>
        [Test]
        public void GetNodeClassString_BaseNodeDesign_ReturnsNode()
        {
            // Arrange
            var node = new NodeDesign();

            // Act
            string result = node.GetNodeClassAsString();

            // Assert
            Assert.That(result, Is.EqualTo("Node"));
        }

        /// <summary>
        /// Tests that GetNodeClassString returns "Node" when the input is null.
        /// The 'is' operator returns false for null, so the default "Node" is returned.
        /// </summary>
        [Test]
        public void GetNodeClassString_NullNode_ReturnsNode()
        {
            // Arrange
            NodeDesign node = null;

            // Act
            string result = node.GetNodeClassAsString();

            // Assert
            Assert.That(result, Is.EqualTo("Node"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Array ValueRank and useVariantForObject set to false.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_ArrayValueRankWithUseVariantFalse_ReturnsEmptyArray()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Array,
                null,
                null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ArrayOf.Empty<int>()"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Array ValueRank and useVariantForObject set to true.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_ArrayValueRankWithUseVariantTrue_ReturnsEmptyArray()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Array,
                null,
                null,
                true,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.Variant.From(global::Opc.Ua.ArrayOf.Empty<int>())"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with BaseDataType BasicDataType.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_BaseDataType_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.BaseDataType
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("default"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with non-Scalar ValueRank.
        /// Expected: Returns "default".
        /// </summary>
        [TestCase(ValueRank.Any)]
        [TestCase(ValueRank.ScalarOrArray)]
        [TestCase(ValueRank.ScalarOrOneDimension)]
        [TestCase(ValueRank.OneOrMoreDimensions)]
        public void GetDefaultDotNetValue_NonScalarValueRank_ReturnsDefault(ValueRank valueRank)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                valueRank,
                null,
                null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("default"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Boolean type and valid decoded true value with correct type info.
        /// Expected: Returns "true".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_BooleanWithValidDecodedTrueAndCorrectType_ReturnsTrue()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();
            System.Xml.XmlElement mockDefaultValue = new XmlDocument().CreateElement("Root");

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                mockDefaultValue,
                true,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("true"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Boolean type and valid decoded false value with correct type info.
        /// Expected: Returns "false".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_BooleanWithValidDecodedFalseAndCorrectType_ReturnsFalse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();
            System.Xml.XmlElement mockDefaultValue = new XmlDocument().CreateElement("test");

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                mockDefaultValue,
                false,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("false"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Boolean type and true value without defaultValue (inverted bug).
        /// </summary>
        [Theory]
        public void GetDefaultDotNetValue_BooleanTrueWithoutDefaultValue_ReturnsFalseWithQuirk(bool quirk)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                true,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object,
                dataTypeQuirk: quirk);

            // Assert
            Assert.That(result, Is.EqualTo(quirk ? "false" : "true"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Boolean type and false value without defaultValue (inverted bug).
        /// Expected: Returns "true" due to documented bug.
        /// </summary>
        [Theory]
        public void GetDefaultDotNetValue_BooleanFalseWithoutDefaultValue_ReturnsTrueWhenQuirk(bool quirk)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                false,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object,
                dataTypeQuirk: quirk);

            // Assert
            Assert.That(result, Is.EqualTo(quirk ? "true" : "false"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with SByte type and valid decoded value.
        /// Expected: Returns formatted sbyte cast.
        /// </summary>
        [TestCase((sbyte)0, "(sbyte)0")]
        [TestCase((sbyte)127, "(sbyte)127")]
        [TestCase((sbyte)-128, "(sbyte)-128")]
        [TestCase((sbyte)42, "(sbyte)42")]
        public void GetDefaultDotNetValue_SByteWithValidValue_ReturnsFormattedCast(sbyte value, string expected)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.SByte
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                value,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with SByte type and invalid decoded value.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_SByteWithInvalidValue_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.SByte
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                "not a sbyte",
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("(sbyte)0"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Byte type and valid decoded value.
        /// Expected: Returns formatted byte cast.
        /// </summary>
        [TestCase((byte)0, "(byte)0")]
        [TestCase((byte)255, "(byte)255")]
        [TestCase((byte)128, "(byte)128")]
        public void GetDefaultDotNetValue_ByteWithValidValue_ReturnsFormattedCast(byte value, string expected)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Byte
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                value,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Int16 type and valid decoded value.
        /// Expected: Returns formatted short cast.
        /// </summary>
        [TestCase((short)0, "(short)0")]
        [TestCase((short)32767, "(short)32767")]
        [TestCase((short)-32768, "(short)-32768")]
        public void GetDefaultDotNetValue_Int16WithValidValue_ReturnsFormattedCast(short value, string expected)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int16
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                value,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with UInt16 type and valid decoded value.
        /// Expected: Returns formatted ushort cast.
        /// </summary>
        [TestCase((ushort)0, "(ushort)0")]
        [TestCase((ushort)65535, "(ushort)65535")]
        [TestCase((ushort)32768, "(ushort)32768")]
        public void GetDefaultDotNetValue_UInt16WithValidValue_ReturnsFormattedCast(ushort value, string expected)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInt16
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                value,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Int32 type and valid decoded value.
        /// Expected: Returns formatted int cast.
        /// </summary>
        [TestCase(0, "(int)0")]
        [TestCase(2147483647, "(int)2147483647")]
        [TestCase(-2147483648, "(int)-2147483648")]
        public void GetDefaultDotNetValue_Int32WithValidValue_ReturnsFormattedCast(int value, string expected)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                value,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with UInt32 type and valid decoded value.
        /// Expected: Returns formatted uint cast.
        /// </summary>
        [TestCase((uint)0, "(uint)0")]
        [TestCase(4294967295, "(uint)4294967295")]
        public void GetDefaultDotNetValue_UInt32WithValidValue_ReturnsFormattedCast(uint value, string expected)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInt32
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                value,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Int64 type and valid decoded value.
        /// Expected: Returns formatted long cast.
        /// </summary>
        [TestCase((long)0, "(long)0")]
        [TestCase(9223372036854775807, "(long)9223372036854775807")]
        [TestCase(-9223372036854775808, "(long)-9223372036854775808")]
        public void GetDefaultDotNetValue_Int64WithValidValue_ReturnsFormattedCast(long value, string expected)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int64
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                value,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Integer type and valid decoded value.
        /// Expected: Returns formatted long cast (Integer maps to Int64).
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_IntegerWithValidValue_ReturnsFormattedLongCast()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Integer
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                (long)12345,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("(long)12345"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with UInt64 type and valid decoded value.
        /// Expected: Returns formatted ulong cast.
        /// </summary>
        [TestCase((ulong)0, "(ulong)0")]
        [TestCase(18446744073709551615, "(ulong)18446744073709551615")]
        public void GetDefaultDotNetValue_UInt64WithValidValue_ReturnsFormattedCast(ulong value, string expected)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInt64
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                value,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with UInteger type and valid decoded value.
        /// Expected: Returns formatted ulong cast (UInteger maps to UInt64).
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_UIntegerWithValidValue_ReturnsFormattedUlongCast()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInteger
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                (ulong)12345,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("(ulong)12345"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Float type and valid decoded value.
        /// Expected: Returns formatted float cast.
        /// </summary>
        [TestCase(0.0f, "(float)0")]
        [TestCase(3.14f, "(float)3.14")]
        [TestCase(-2.5f, "(float)-2.5")]
        public void GetDefaultDotNetValue_FloatWithValidValue_ReturnsFormattedCast(float value, string expected)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Float
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                value,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Float type and invalid decoded value.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_FloatWithInvalidValue_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Float
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                "not a float",
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("(float)0"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Double type and valid decoded value.
        /// Expected: Returns formatted double cast.
        /// </summary>
        [TestCase(0.0, "(double)0")]
        [TestCase(3.14159, "(double)3.14159")]
        [TestCase(-2.71828, "(double)-2.71828")]
        public void GetDefaultDotNetValue_DoubleWithValidValue_ReturnsFormattedCast(double value, string expected)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Double
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                value,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Number type and valid decoded value.
        /// Expected: Returns formatted double cast (Number maps to Double).
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_NumberWithValidValue_ReturnsFormattedDoubleCast()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Number
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                123.456,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("(double)123.456"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with String type and empty string.
        /// Expected: Returns "string.Empty".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_StringWithEmptyValue_ReturnsStringEmpty()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                string.Empty,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("string.Empty"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with String type and non-empty string.
        /// Expected: Returns formatted string literal.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_StringWithNonEmptyValue_ReturnsFormattedLiteral()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                "Hello World",
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("\"Hello World\""));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with String type and invalid value.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_StringWithInvalidValue_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                123,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("default(string)"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with DateTime type and DateTime.MinValue.
        /// Expected: Returns "global::System.DateTime.MinValue".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_DateTimeWithMinValue_ReturnsMinValue()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.DateTime
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                DateTime.MinValue,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("global::System.DateTime.MinValue"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with DateTime type and valid non-min DateTime.
        /// Expected: Returns formatted ParseExact call.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_DateTimeWithValidValue_ReturnsParseExact()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.DateTime
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();
            var testDate = new DateTime(2024, 1, 15, 10, 30, 45);

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                testDate,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Does.StartWith("global::System.DateTime.ParseExact("));
            Assert.That(result, Does.Contain("2024-01-15 10:30:45"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with DateTime type and invalid value.
        /// Expected: Returns "global::System.DateTime.MinValue".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_DateTimeWithInvalidValue_ReturnsMinValue()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.DateTime
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                "not a datetime",
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("global::System.DateTime.MinValue"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Guid type and Guid.Empty.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_GuidWithEmptyValue_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Guid
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                Guid.Empty,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.Uuid.Empty"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Guid type and valid Guid.
        /// Expected: Returns formatted Parse call.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_GuidWithValidValue_ReturnsParse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Guid
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();
            var testGuid = Guid.NewGuid();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                testGuid,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Does.StartWith("global::Opc.Ua.Uuid.Parse("));
            Assert.That(result, Does.Contain(testGuid.ToString()));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Guid type and Uuid conversion.
        /// Expected: Returns formatted Parse call with converted Guid.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_GuidWithUuidValue_ReturnsParse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Guid
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();
            var testGuid = Guid.NewGuid();
            var testUuid = new Uuid(testGuid);

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                testUuid,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Does.StartWith("global::Opc.Ua.Uuid.Parse("));
            Assert.That(result, Does.Contain(testGuid.ToString()));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with ByteString type and null value.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_ByteStringWithNullValue_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.ByteString
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ByteString.Empty"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with ByteString type and valid byte array.
        /// Expected: Returns formatted hex string.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_ByteStringWithValidValue_ReturnsFormattedHexString()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.ByteString
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();
            byte[] testBytes = [0x01, 0x02, 0x03, 0xFF];

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                testBytes,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Does.StartWith("global::Opc.Ua.ByteString.FromHexString("));
            Assert.That(result, Does.EndWith(")"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with NodeId type and null value.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_NodeIdWithNullValue_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.NodeId
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.NodeId.Null"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with NodeId type and null NodeId.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_NodeIdWithNullNodeId_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.NodeId
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                NodeId.Null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.NodeId.Null"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with NodeId type and namespace index 0.
        /// Expected: Returns NodeId.Parse format.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_NodeIdWithNamespaceZero_ReturnsNodeIdParse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.NodeId
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();
            var testNodeId = new NodeId(123);

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                testNodeId,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Does.StartWith("global::Opc.Ua.NodeId.Parse("));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with NodeId type and valid namespace.
        /// Expected: Returns ExpandedNodeId.Parse format.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_NodeIdWithValidNamespace_ReturnsExpandedNodeIdParse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.NodeId
            };
            var mockNamespace = new Namespace
            {
                Value = "http://test.namespace"
            };
            Namespace[] namespaces = [mockNamespace, mockNamespace];
            var mockContext = new Mock<IServiceMessageContext>();
            var testNodeId = new NodeId(123, 1);

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                testNodeId,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Does.StartWith("ExpandedNodeId.Parse("));
            Assert.That(result, Does.Contain("context.NamespaceUris"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with ExpandedNodeId type and null value.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_ExpandedNodeIdWithNullValue_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.ExpandedNodeId
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                ExpandedNodeId.Null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ExpandedNodeId.Null"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with ExpandedNodeId type and valid value.
        /// Expected: Returns formatted Parse call.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_ExpandedNodeIdWithValidValue_ReturnsParse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.ExpandedNodeId
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();
            var testExpandedNodeId = new ExpandedNodeId(123, "http://test.namespace");

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                testExpandedNodeId,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Does.StartWith("global::Opc.Ua.ExpandedNodeId.Parse("));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with QualifiedName type and null value.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_QualifiedNameWithNullValue_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.QualifiedName
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                QualifiedName.Null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.QualifiedName.Null"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with QualifiedName type and valid value.
        /// Expected: Returns formatted Parse call.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_QualifiedNameWithValidValue_ReturnsParse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.QualifiedName
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();
            var testQName = new QualifiedName("TestName", 1);

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                testQName,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Does.StartWith("global::Opc.Ua.QualifiedName.Parse("));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with LocalizedText type and valid value.
        /// Expected: Returns formatted constructor call.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_LocalizedTextWithValidValue_ReturnsConstructor()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.LocalizedText
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();
            var testLocalizedText = new Ua.LocalizedText("en-US", "Test Text");

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                testLocalizedText,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Does.StartWith("new global::Opc.Ua.LocalizedText("));
            Assert.That(result, Does.Contain("en-US"));
            Assert.That(result, Does.Contain("Test Text"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with StatusCode type and zero code.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_StatusCodeWithZeroCode_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.StatusCode
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();
            var testStatusCode = new StatusCode(0);

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                testStatusCode,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("default(global::Opc.Ua.StatusCode)"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with StatusCode type and non-zero code.
        /// Expected: Returns formatted cast.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_StatusCodeWithNonZeroCode_ReturnsFormattedCast()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.StatusCode
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();
            var testStatusCode = new StatusCode(0x80000000);

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                testStatusCode,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Does.StartWith("(global::Opc.Ua.StatusCode.StatusCode)"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Enumeration type matching "Enumeration" in OpcUa namespace.
        /// Expected: Returns "0".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_EnumerationMatchingOpcUaEnumeration_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration,
                SymbolicId = new XmlQualifiedName("Enumeration", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("default"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Enumeration type deriving from OptionSet.
        /// Expected: Returns new instance constructor.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_EnumerationDerivedFromOptionSet_ReturnsNewInstance()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration,
                SymbolicId = new XmlQualifiedName("TestEnum", "http://test.namespace"),
                BaseTypeNode = new TypeDesign
                {
                    SymbolicId = new XmlQualifiedName("OptionSet", Namespaces.OpcUa)
                },
                SymbolicName = new XmlQualifiedName("TestEnum", "http://test.namespace")
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("new TestEnum()"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with Enumeration type that IsOptionSet.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_EnumerationIsOptionSet_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration,
                SymbolicName = new XmlQualifiedName("TestEnum", "http://test.namespace"),
                BaseTypeNode = new TypeDesign
                {
                    SymbolicId = new XmlQualifiedName("SomeOtherBase", Namespaces.OpcUa)
                },
                IsOptionSet = true
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("default"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with standard Enumeration type.
        /// Expected: Returns first field name.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_EnumerationStandard_ReturnsFirstFieldName()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration,
                SymbolicId = new XmlQualifiedName("TestEnum", "http://test.namespace"),
                BaseTypeNode = new TypeDesign
                {
                    SymbolicId = new XmlQualifiedName("SomeOtherBase", Namespaces.OpcUa)
                },
                IsOptionSet = false,
                SymbolicName = new XmlQualifiedName("TestEnum", "http://test.namespace")
            };
            var mockField = new Parameter
            {
                Name = "FirstValue"
            };
            mockDataType.Fields = [mockField];
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("TestEnum.FirstValue"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with DataValue type.
        /// Expected: Returns new DataValue constructor.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_DataValue_ReturnsNewDataValue()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.DataValue
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("new global::Opc.Ua.DataValue()"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with UserDefined type and useVariantForObject true.
        /// Expected: Returns new instance constructor.
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_UserDefinedWithUseVariantTrue_ReturnsNewInstance()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicId = new XmlQualifiedName("CustomType", "http://test.namespace"),
                SymbolicName = new XmlQualifiedName("CustomType", "http://test.namespace")
            };
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://test.namespace", Name = "TestNamespace", Prefix = "Test" }
            ];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                null,
                true,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.Variant.FromStructure(new global::Test.CustomType())"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with UserDefined type and useVariantForObject false.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_UserDefinedWithUseVariantFalse_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicId = new XmlQualifiedName("CustomType", "http://test.namespace"),
                SymbolicName = new XmlQualifiedName("CustomType", "http://test.namespace")
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("new CustomType()"));
        }

        /// <summary>
        /// Tests GetDefaultDotNetValue with unknown BasicDataType.
        /// Expected: Returns "default".
        /// </summary>
        [Test]
        public void GetDefaultDotNetValue_UnknownBasicDataType_ReturnsDefault()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = (BasicDataType)9999
            };
            Namespace[] namespaces = [];
            var mockContext = new Mock<IServiceMessageContext>();

            // Act
            string result = mockDataType.GetValueAsCode(
                ValueRank.Scalar,
                null,
                null,
                false,
                "TestNamespace",
                namespaces,
                mockContext.Object);

            // Assert
            Assert.That(result, Is.EqualTo("default"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with all basic numeric and primitive data types.
        /// Verifies that each BasicDataType enum value returns the correct OPC binary type string.
        /// </summary>
        [TestCase(BasicDataType.Boolean, "opc:Boolean")]
        [TestCase(BasicDataType.SByte, "opc:SByte")]
        [TestCase(BasicDataType.Byte, "opc:Byte")]
        [TestCase(BasicDataType.Int16, "opc:Int16")]
        [TestCase(BasicDataType.UInt16, "opc:UInt16")]
        [TestCase(BasicDataType.Int32, "opc:Int32")]
        [TestCase(BasicDataType.UInt32, "opc:UInt32")]
        [TestCase(BasicDataType.Int64, "opc:Int64")]
        [TestCase(BasicDataType.UInt64, "opc:UInt64")]
        [TestCase(BasicDataType.Float, "opc:Float")]
        [TestCase(BasicDataType.Double, "opc:Double")]
        [TestCase(BasicDataType.String, "opc:CharArray")]
        [TestCase(BasicDataType.DateTime, "opc:DateTime")]
        [TestCase(BasicDataType.Guid, "opc:Guid")]
        [TestCase(BasicDataType.ByteString, "opc:ByteString")]
        public void GetBinaryDataType_BasicPrimitiveTypes_ReturnsCorrectOpcType(BasicDataType basicType, string expectedResult)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = basicType
            };
            const string targetNamespace = "http://test.org/";
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo(expectedResult));
        }

        /// <summary>
        /// Tests GetBinaryDataType with UA-specific complex data types.
        /// Verifies that UA data types return the correct "ua:" prefixed type string.
        /// </summary>
        [TestCase(BasicDataType.XmlElement, "ua:XmlElement")]
        [TestCase(BasicDataType.NodeId, "ua:NodeId")]
        [TestCase(BasicDataType.ExpandedNodeId, "ua:ExpandedNodeId")]
        [TestCase(BasicDataType.StatusCode, "ua:StatusCode")]
        [TestCase(BasicDataType.DiagnosticInfo, "ua:DiagnosticInfo")]
        [TestCase(BasicDataType.QualifiedName, "ua:QualifiedName")]
        [TestCase(BasicDataType.LocalizedText, "ua:LocalizedText")]
        [TestCase(BasicDataType.DataValue, "ua:DataValue")]
        public void GetBinaryDataType_UaComplexTypes_ReturnsCorrectUaType(BasicDataType basicType, string expectedResult)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = basicType
            };
            const string targetNamespace = "http://test.org/";
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo(expectedResult));
        }

        /// <summary>
        /// Tests GetBinaryDataType with abstract numeric types.
        /// Verifies that Number, Integer, UInteger, and BaseDataType all return "ua:Variant".
        /// </summary>
        [TestCase(BasicDataType.Number)]
        [TestCase(BasicDataType.Integer)]
        [TestCase(BasicDataType.UInteger)]
        [TestCase(BasicDataType.BaseDataType)]
        public void GetBinaryDataType_AbstractNumericTypes_ReturnsVariant(BasicDataType basicType)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = basicType
            };
            const string targetNamespace = "http://test.org/";
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:Variant"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with Structure data type.
        /// Verifies that a Structure with OpcUa namespace returns "ua:ExtensionObject".
        /// </summary>
        [Test]
        public void GetBinaryDataType_StructureType_ReturnsExtensionObject()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Structure", Namespaces.OpcUa)
            };
            const string targetNamespace = "http://test.org/";
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:ExtensionObject"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with Enumeration data type without IsOptionSet.
        /// Verifies that a standard Enumeration returns "opc:Int32".
        /// </summary>
        [Test]
        public void GetBinaryDataType_EnumerationType_ReturnsInt32()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Enumeration", Namespaces.OpcUa),
                IsOptionSet = false
            };
            const string targetNamespace = "http://test.org/";
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("opc:Int32"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with Enumeration data type with IsOptionSet enabled.
        /// Verifies that an OptionSet recursively calls GetBinaryDataType on the base type.
        /// </summary>
        [Test]
        public void GetBinaryDataType_EnumerationWithOptionSet_ReturnsBaseTypeResult()
        {
            // Arrange
            var mockBaseType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInt32
            };

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Enumeration", Namespaces.OpcUa),
                IsOptionSet = true,
                BaseTypeNode = mockBaseType
            };
            const string targetNamespace = "http://test.org/";
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("opc:UInt32"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with a custom type in the target namespace.
        /// Verifies that custom types in the same namespace return "tns:{TypeName}".
        /// </summary>
        [Test]
        public void GetBinaryDataType_CustomTypeInTargetNamespace_ReturnsTnsPrefix()
        {
            // Arrange
            const string targetNamespace = "http://test.org/";
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", targetNamespace)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("tns:CustomType"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with a custom type in the OpcUa namespace.
        /// Verifies that custom types in the OpcUa namespace return "ua:{TypeName}".
        /// </summary>
        [Test]
        public void GetBinaryDataType_CustomTypeInOpcUaNamespace_ReturnsUaPrefix()
        {
            // Arrange
            const string targetNamespace = "http://test.org/";
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:CustomType"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with a custom type in a different namespace with XmlPrefix defined.
        /// Verifies that the XmlPrefix from the matching namespace is used.
        /// </summary>
        [Test]
        public void GetBinaryDataType_CustomTypeInOtherNamespaceWithXmlPrefix_ReturnsCustomPrefix()
        {
            // Arrange
            const string targetNamespace = "http://test.org/";
            const string customNamespace = "http://custom.org/";
            var namespace1 = new Namespace { Value = customNamespace, XmlPrefix = "custom" };
            Namespace[] namespaces = [namespace1];

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", customNamespace)
            };

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("custom:CustomType"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with a custom type in a different namespace without XmlPrefix.
        /// Verifies that a generated prefix "s{index}" is used when XmlPrefix is not defined.
        /// </summary>
        [Test]
        public void GetBinaryDataType_CustomTypeInOtherNamespaceWithoutXmlPrefix_ReturnsGeneratedPrefix()
        {
            // Arrange
            const string targetNamespace = "http://test.org/";
            const string customNamespace = "http://custom.org/";
            var namespace1 = new Namespace { Value = customNamespace, XmlPrefix = null };
            Namespace[] namespaces = [namespace1];

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", customNamespace)
            };

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("s0:CustomType"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with a custom type in a different namespace with empty XmlPrefix.
        /// Verifies that a generated prefix "s{index}" is used when XmlPrefix is empty.
        /// </summary>
        [Test]
        public void GetBinaryDataType_CustomTypeInOtherNamespaceWithEmptyXmlPrefix_ReturnsGeneratedPrefix()
        {
            // Arrange
            const string targetNamespace = "http://test.org/";
            const string customNamespace = "http://custom.org/";
            var namespace1 = new Namespace { Value = customNamespace, XmlPrefix = string.Empty };
            Namespace[] namespaces = [namespace1];

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", customNamespace)
            };

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("s0:CustomType"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with a custom type in a namespace not found in the array.
        /// Verifies that when the namespace is not found, GetXmlNamespacePrefix returns null,
        /// resulting in a type string with null prefix.
        /// </summary>
        [Test]
        public void GetBinaryDataType_CustomTypeInUnknownNamespace_ReturnsNullPrefixFormat()
        {
            // Arrange
            const string targetNamespace = "http://test.org/";
            const string unknownNamespace = "http://unknown.org/";
            var namespace1 = new Namespace { Value = "http://other.org/", XmlPrefix = "other" };
            Namespace[] namespaces = [namespace1];

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", unknownNamespace)
            };

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo(":CustomType"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with null namespaces array.
        /// Verifies that the method handles null namespaces gracefully for basic types.
        /// </summary>
        [Test]
        public void GetBinaryDataType_NullNamespaces_ReturnsCorrectBasicType()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };
            const string targetNamespace = "http://test.org/";

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, null);

            // Assert
            Assert.That(result, Is.EqualTo("opc:Int32"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with empty namespaces array.
        /// Verifies that the method handles empty namespaces array for basic types.
        /// </summary>
        [Test]
        public void GetBinaryDataType_EmptyNamespaces_ReturnsCorrectBasicType()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            const string targetNamespace = "http://test.org/";
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("opc:CharArray"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with null target namespace.
        /// Verifies that null targetNamespace is handled for basic types.
        /// </summary>
        [Test]
        public void GetBinaryDataType_NullTargetNamespace_ReturnsCorrectBasicType()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(null, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("opc:Boolean"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with empty target namespace string.
        /// Verifies that empty targetNamespace is handled for basic types.
        /// </summary>
        [Test]
        public void GetBinaryDataType_EmptyTargetNamespace_ReturnsCorrectBasicType()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Double
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(string.Empty, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("opc:Double"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with custom type and null target namespace.
        /// Verifies that when targetNamespace is null, the namespace comparison still works.
        /// </summary>
        [Test]
        public void GetBinaryDataType_CustomTypeWithNullTargetNamespace_ReturnsUaPrefixForOpcUa()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(null, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:CustomType"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with Enumeration enum value directly.
        /// Verifies that BasicDataType.Enumeration is handled correctly.
        /// </summary>
        [Test]
        public void GetBinaryDataType_EnumerationBasicDataType_ReturnsCorrectFormat()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration,
                SymbolicName = new XmlQualifiedName("CustomEnum", "http://test.org/")
            };
            const string targetNamespace = "http://test.org/";
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("tns:CustomEnum"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with Structure enum value directly.
        /// Verifies that BasicDataType.Structure is handled correctly.
        /// </summary>
        [Test]
        public void GetBinaryDataType_StructureBasicDataType_ReturnsCorrectFormat()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Structure,
                SymbolicName = new XmlQualifiedName("CustomStruct", "http://test.org/")
            };
            const string targetNamespace = "http://test.org/";
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("tns:CustomStruct"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with multiple namespaces in array.
        /// Verifies that the correct namespace is selected from multiple options.
        /// </summary>
        [Test]
        public void GetBinaryDataType_MultipleNamespaces_SelectsCorrectOne()
        {
            // Arrange
            const string targetNamespace = "http://test.org/";
            const string customNamespace = "http://custom.org/";
            var namespace1 = new Namespace { Value = "http://other.org/", XmlPrefix = "other" };
            var namespace2 = new Namespace { Value = customNamespace, XmlPrefix = "custom" };
            var namespace3 = new Namespace { Value = "http://another.org/", XmlPrefix = "another" };
            Namespace[] namespaces = [namespace1, namespace2, namespace3];

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", customNamespace)
            };

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("custom:CustomType"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with namespace at different index positions.
        /// Verifies that the generated prefix uses the correct index when XmlPrefix is empty.
        /// </summary>
        [Test]
        public void GetBinaryDataType_NamespaceAtIndex2WithoutXmlPrefix_ReturnsS2Prefix()
        {
            // Arrange
            const string targetNamespace = "http://test.org/";
            const string customNamespace = "http://custom.org/";
            var namespace1 = new Namespace { Value = "http://other.org/", XmlPrefix = "other" };
            var namespace2 = new Namespace { Value = "http://another.org/", XmlPrefix = "another" };
            var namespace3 = new Namespace { Value = customNamespace, XmlPrefix = string.Empty };
            Namespace[] namespaces = [namespace1, namespace2, namespace3];

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", customNamespace)
            };

            // Act
            string result = mockDataType.GetBinaryDataType(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("s2:CustomType"));
        }

        /// <summary>
        /// Tests GetBinaryDataType with whitespace-only target namespace.
        /// Verifies that whitespace targetNamespace doesn't match and is treated as different.
        /// </summary>
        [Test]
        public void GetBinaryDataType_WhitespaceTargetNamespace_TreatsAsDifferent()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", "http://test.org/")
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBinaryDataType("   ", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo(":CustomType"));
        }

        /// <summary>
        /// Tests GetClassName with a MethodDesign that has no TypeDefinition and no arguments.
        /// Input: MethodDesign with SymbolicName "TestMethod", no TypeDefinition, no arguments.
        /// Expected: Returns "MethodState".
        /// </summary>
        [Test]
        public void GetClassName_MethodDesignNoTypeDefinitionNoArguments_ReturnsMethodState()
        {
            // Arrange
            var mockMethod = new MethodDesign
            {
                SymbolicName = new XmlQualifiedName("TestMethod", "http://test.org"),
                TypeDefinition = null,
                HasArguments = false
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockMethod.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.MethodState"));
        }

        /// <summary>
        /// Tests GetClassName with a MethodDesign that has TypeDefinition ending with "MethodType" and no arguments.
        /// Input: MethodDesign with TypeDefinition ending in "MethodType", no arguments.
        /// Expected: Returns "MethodState" with "MethodType" suffix stripped.
        /// </summary>
        [Test]
        public void GetClassName_MethodDesignTypeDefinitionEndsWithMethodTypeNoArguments_ReturnsMethodState()
        {
            // Arrange
            var mockMethod = new MethodDesign
            {
                SymbolicName = new XmlQualifiedName("TestMethod", "http://test.org"),
                TypeDefinition = new XmlQualifiedName("CustomMethodType", "http://test.org"),
                HasArguments = false
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockMethod.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.MethodState"));
        }

        /// <summary>
        /// Tests GetClassName with a MethodDesign that has TypeDefinition ending with "Type" (but not "MethodType") and no arguments.
        /// Input: MethodDesign with TypeDefinition ending in "Type" but not "MethodType", no arguments.
        /// Expected: Returns "MethodState" with "Type" suffix stripped.
        /// </summary>
        [Test]
        public void GetClassName_MethodDesignTypeDefinitionEndsWithTypeNoArguments_ReturnsMethodState()
        {
            // Arrange
            var mockMethod = new MethodDesign
            {
                SymbolicName = new XmlQualifiedName("TestMethod", "http://test.org"),
                TypeDefinition = new XmlQualifiedName("CustomType", "http://test.org"),
                HasArguments = false
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockMethod.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.MethodState"));
        }

        /// <summary>
        /// Tests GetClassName with a MethodDesign that has arguments.
        /// Input: MethodDesign with TypeDefinition ending in "MethodType" and HasArguments=true.
        /// Expected: Returns "{className}MethodState" where className is derived from TypeDefinition.
        /// </summary>
        [Test]
        public void GetClassName_MethodDesignWithArguments_ReturnsClassNameMethodState()
        {
            // Arrange
            var mockMethod = new MethodDesign
            {
                SymbolicName = new XmlQualifiedName("TestMethod", "http://test.org"),
                TypeDefinition = new XmlQualifiedName("CustomMethodType", "http://test.org"),
                HasArguments = true
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockMethod.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("CustomMethodState"));
        }

        /// <summary>
        /// Tests GetClassName with a MethodDesign where SymbolicName contains "MethodType" and has arguments.
        /// Input: MethodDesign with SymbolicName containing "MethodType", no TypeDefinition, HasArguments=true.
        /// Expected: Returns "{className}MethodState" with "MethodType" stripped from SymbolicName.
        /// </summary>
        [Test]
        public void GetClassName_MethodDesignSymbolicNameWithMethodTypeAndArguments_ReturnsStrippedMethodState()
        {
            // Arrange
            var mockMethod = new MethodDesign
            {
                SymbolicName = new XmlQualifiedName("TestMethodType", "http://test.org"),
                TypeDefinition = null,
                HasArguments = true
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockMethod.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("TestMethodState"));
        }

        /// <summary>
        /// Tests GetClassName with a MethodDesign that has TypeDefinition not ending with Type suffixes and has arguments.
        /// Input: MethodDesign with TypeDefinition not ending in "Type" or "MethodType", HasArguments=true.
        /// Expected: Returns "{className}MethodState" where className is from TypeDefinition.
        /// </summary>
        [Test]
        public void GetClassName_MethodDesignNoTypeSuffixWithArguments_ReturnsClassNameMethodState()
        {
            // Arrange
            var mockMethod = new MethodDesign
            {
                SymbolicName = new XmlQualifiedName("TestMethod", "http://test.org"),
                TypeDefinition = new XmlQualifiedName("CustomMethod", "http://test.org"),
                HasArguments = true
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockMethod.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("CustomMethodMethodState"));
        }

        /// <summary>
        /// Tests GetClassName with an ObjectDesign (not MethodDesign, not VariableDesign).
        /// Input: ObjectDesign with TypeDefinitionNode having ClassName "BaseObjectType".
        /// Expected: Returns "{GetNodeStateClassName}State" based on TypeDefinitionNode.
        /// </summary>
        [Test]
        public void GetClassName_ObjectDesign_ReturnsTypeDefinitionState()
        {
            // Arrange
            var mockObjectType = new ObjectTypeDesign
            {
                ClassName = "BaseObjectType",
                SymbolicId = new XmlQualifiedName("BaseObjectType", "http://test.org")
            };

            var mockObject = new ObjectDesign
            {
                TypeDefinitionNode = mockObjectType
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockObject.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseObjectTypeState"));
        }

        /// <summary>
        /// Tests GetClassName with a VariableDesign where the VariableType does not require parameter in templates.
        /// Input: VariableDesign with VariableTypeDesign that doesn't require parameters.
        /// Expected: Returns "{GetNodeStateClassName(variableType)}State".
        /// </summary>
        [Test]
        public void GetClassName_VariableDesignTypeNoParameterRequired_ReturnsVariableTypeState()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.BaseDataType
            };

            var mockVariableType = new VariableTypeDesign
            {
                ClassName = "BaseVariableType",
                DataTypeNode = mockDataType,
                ValueRank = ValueRank.Scalar,
                SymbolicId = new XmlQualifiedName("BaseVariableType", "http://test.org")
            };

            var mockVariable = new VariableDesign
            {
                TypeDefinitionNode = mockVariableType,
                DataTypeNode = mockDataType,
                ValueRank = ValueRank.Scalar
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockVariable.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseVariableTypeState"));
        }

        /// <summary>
        /// Tests GetClassName with a VariableDesign where the variable instance does not restrict the datatype.
        /// Input: VariableDesign that doesn't restrict the datatype (doesn't require parameter).
        /// Expected: Returns "{GetNodeStateClassName(variableType)}State".
        /// </summary>
        [Test]
        public void GetClassName_VariableDesignInstanceNoDataTypeRestriction_ReturnsVariableTypeState()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Number
            };

            var mockVariableType = new VariableTypeDesign
            {
                ClassName = "BaseVariableType",
                DataTypeNode = mockDataType,
                ValueRank = ValueRank.ScalarOrArray,
                SymbolicId = new XmlQualifiedName("BaseVariableType", "http://test.org")
            };

            var mockVariable = new VariableDesign
            {
                TypeDefinitionNode = mockVariableType,
                DataTypeNode = mockDataType,
                ValueRank = ValueRank.ScalarOrArray
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockVariable.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseVariableTypeState"));
        }

        /// <summary>
        /// Tests GetClassName with a VariableDesign with UserDefined BasicDataType and Scalar ValueRank.
        /// Input: VariableDesign with BasicDataType.UserDefined, ValueRank.Scalar.
        /// Expected: Returns "{GetNodeStateClassName(variableType)}State<{scalarName}>".
        /// </summary>
        [Test]
        public void GetClassName_VariableDesignUserDefinedScalar_ReturnsTemplatedState()
        {
            // Arrange
            var mockVariableType = new VariableTypeDesign
            {
                ClassName = "BaseVariableType",
                DataTypeNode = new DataTypeDesign
                {
                    BasicDataType = BasicDataType.BaseDataType
                },
                ValueRank = ValueRank.Scalar,
                SymbolicId = new XmlQualifiedName("BaseVariableType", "http://test.org")
            };

            var mockVariable = new VariableDesign
            {
                TypeDefinitionNode = mockVariableType,
                DataTypeNode = new DataTypeDesign
                {
                    BasicDataType = BasicDataType.UserDefined,
                    SymbolicId = new XmlQualifiedName("CustomType", "http://test.org"),
                    SymbolicName = new XmlQualifiedName("CustomType", "http://test.org")
                },
                ValueRank = ValueRank.Scalar,
                DataType = new XmlQualifiedName("CustomType", "http://test.org")
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockVariable.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseVariableTypeState<CustomType>"));
        }

        /// <summary>
        /// Tests GetClassName with a VariableDesign with Structure BasicDataType and Scalar ValueRank.
        /// Input: VariableDesign with BasicDataType.Structure, ValueRank.Scalar.
        /// Expected: Returns "{GetNodeStateClassName(variableType)}State<global::Opc.Ua.ExtensionObject>".
        /// </summary>
        [Test]
        public void GetClassName_VariableDesignStructureScalar_ReturnsTemplatedExtensionObjectState()
        {
            // Arrange
            var mockVariableType = new VariableTypeDesign
            {
                ClassName = "BaseVariableType",
                DataTypeNode = new DataTypeDesign
                {
                    BasicDataType = BasicDataType.BaseDataType
                },
                ValueRank = ValueRank.Scalar,
                SymbolicId = new XmlQualifiedName("BaseVariableType", "http://test.org")
            };

            var mockVariable = new VariableDesign
            {
                TypeDefinitionNode = mockVariableType,
                DataTypeNode = new DataTypeDesign
                {
                    BasicDataType = BasicDataType.Structure
                },
                ValueRank = ValueRank.Scalar,
                DataType = new XmlQualifiedName("Structure", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockVariable.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseVariableTypeState<global::Opc.Ua.ExtensionObject>"));
        }

        /// <summary>
        /// Tests GetClassName with a VariableDesign with Array ValueRank.
        /// Input: VariableDesign with ValueRank.Array, BasicDataType.Int32.
        /// Expected: Returns "{GetNodeStateClassName(variableType)}State<{scalarName}[]>".
        /// </summary>
        [Test]
        public void GetClassName_VariableDesignArrayValueRank_ReturnsArrayTemplatedState()
        {
            // Arrange
            var mockVariableType = new VariableTypeDesign
            {
                ClassName = "BaseVariableType",
                DataTypeNode = new DataTypeDesign
                {
                    BasicDataType = BasicDataType.BaseDataType
                },
                ValueRank = ValueRank.Scalar,
                SymbolicId = new XmlQualifiedName("BaseVariableType", "http://test.org")
            };

            var mockVariable = new VariableDesign
            {
                TypeDefinitionNode = mockVariableType,
                DataTypeNode = new DataTypeDesign
                {
                    BasicDataType = BasicDataType.Int32
                },
                ValueRank = ValueRank.Array,
                DataType = new XmlQualifiedName("Int32", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockVariable.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseVariableTypeState<int[]>"));
        }

        /// <summary>
        /// Tests GetClassName with a VariableDesign that is an indeterminate type.
        /// Input: VariableDesign with ValueRank.OneOrMoreDimensions (indeterminate).
        /// Expected: Returns "{GetNodeStateClassName(variableType)}State" without template parameter.
        /// </summary>
        [Test]
        public void GetClassName_VariableDesignIndeterminateType_ReturnsNonTemplatedState()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };

            var mockVariableType = new VariableTypeDesign
            {
                ClassName = "BaseVariableType",
                DataTypeNode = mockDataType,
                ValueRank = ValueRank.Scalar,
                SymbolicId = new XmlQualifiedName("BaseVariableType", "http://test.org")
            };

            var mockVariable = new VariableDesign
            {
                TypeDefinitionNode = mockVariableType,
                DataTypeNode = mockDataType,
                ValueRank = ValueRank.OneOrMoreDimensions,
                DataType = new XmlQualifiedName("Int32", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockVariable.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseVariableTypeState"));
        }

        /// <summary>
        /// Tests GetClassName with a VariableDesign with TwoStateDiscreteType.
        /// </summary>
        [Test]
        public void GetClassName_VariableDesignTwoStateDiscreteType_ReturnsNonTemplatedState()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };

            var mockVariableType = new VariableTypeDesign
            {
                ClassName = "TwoStateDiscreteType",
                DataTypeNode = mockDataType,
                ValueRank = ValueRank.Scalar,
                SymbolicName = new XmlQualifiedName("TwoStateDiscreteType", Namespaces.OpcUa),
                SymbolicId = new XmlQualifiedName("TwoStateDiscreteType", Namespaces.OpcUa)
            };

            var mockVariable = new VariableDesign
            {
                TypeDefinitionNode = mockVariableType,
                DataTypeNode = mockDataType,
                ValueRank = ValueRank.Scalar,
                DataType = new XmlQualifiedName("Boolean", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockVariable.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("TwoStateDiscreteTypeState"));
        }

        /// <summary>
        /// Tests GetClassName with a VariableDesign with Enumeration DataType (indeterminate type case).
        /// Input: VariableDesign with DataType = Enumeration in OpcUa namespace.
        /// Expected: Returns "{GetNodeStateClassName(variableType)}State" without template parameter.
        /// </summary>
        [Test]
        public void GetClassName_VariableDesignEnumerationDataType_ReturnsNonTemplatedState()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration
            };

            var mockVariableType = new VariableTypeDesign
            {
                ClassName = "BaseVariableType",
                DataTypeNode = mockDataType,
                ValueRank = ValueRank.Scalar,
                SymbolicId = new XmlQualifiedName("BaseVariableType", "http://test.org")
            };

            var mockVariable = new VariableDesign
            {
                TypeDefinitionNode = mockVariableType,
                DataTypeNode = mockDataType,
                ValueRank = ValueRank.Scalar,
                DataType = new XmlQualifiedName("Enumeration", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockVariable.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseVariableTypeState"));
        }

        /// <summary>
        /// Tests GetClassName with empty targetNamespace string.
        /// Input: MethodDesign with empty targetNamespace.
        /// Expected: Method executes without exception and returns expected result.
        /// </summary>
        [Test]
        public void GetClassName_EmptyTargetNamespace_ReturnsExpectedResult()
        {
            // Arrange
            var mockMethod = new MethodDesign
            {
                SymbolicName = new XmlQualifiedName("TestMethod", string.Empty),
                TypeDefinition = null,
                HasArguments = false
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockMethod.GetNodeStateClassName(string.Empty, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.MethodState"));
        }

        /// <summary>
        /// Tests GetClassName with null namespaces array.
        /// Input: MethodDesign with null namespaces array.
        /// Expected: Method executes without exception and returns expected result.
        /// </summary>
        [Test]
        public void GetClassName_NullNamespaces_ReturnsExpectedResult()
        {
            // Arrange
            var mockMethod = new MethodDesign
            {
                SymbolicName = new XmlQualifiedName("TestMethod", "http://test.org"),
                TypeDefinition = null,
                HasArguments = false
            };

            // Act
            string result = mockMethod.GetNodeStateClassName("http://test.org", null);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.MethodState"));
        }

        /// <summary>
        /// Tests GetClassName with empty namespaces array.
        /// Input: VariableDesign with empty namespaces array.
        /// Expected: Method executes without exception and returns expected result.
        /// </summary>
        [Test]
        public void GetClassName_EmptyNamespacesArray_ReturnsExpectedResult()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.BaseDataType
            };

            var mockVariableType = new VariableTypeDesign
            {
                ClassName = "BaseVariableType",
                DataTypeNode = mockDataType,
                ValueRank = ValueRank.Scalar,
                SymbolicId = new XmlQualifiedName("BaseVariableType", "http://test.org")
            };

            var mockVariable = new VariableDesign
            {
                TypeDefinitionNode = mockVariableType,
                DataTypeNode = mockDataType,
                ValueRank = ValueRank.Scalar
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockVariable.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseVariableTypeState"));
        }

        /// <summary>
        /// Tests GetClassName with a VariableDesign with other basic data types.
        /// Input: VariableDesign with BasicDataType.String, ValueRank.Scalar.
        /// Expected: Returns "{GetNodeStateClassName(variableType)}State<string>".
        /// </summary>
        [Test]
        public void GetClassName_VariableDesignStringDataType_ReturnsTemplatedStringState()
        {
            // Arrange
            var mockVariableType = new VariableTypeDesign
            {
                ClassName = "BaseVariableType",
                DataTypeNode = new DataTypeDesign
                {
                    BasicDataType = BasicDataType.BaseDataType
                },
                ValueRank = ValueRank.Scalar,
                SymbolicId = new XmlQualifiedName("BaseVariableType", "http://test.org")
            };

            var mockVariable = new VariableDesign
            {
                TypeDefinitionNode = mockVariableType,
                DataTypeNode = new DataTypeDesign
                {
                    BasicDataType = BasicDataType.String
                },
                ValueRank = ValueRank.Scalar,
                DataType = new XmlQualifiedName("String", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockVariable.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseVariableTypeState<string>"));
        }

        /// <summary>
        /// Tests GetClassName with a MethodDesign where TypeDefinition has only "Type" at the end (edge case).
        /// Input: MethodDesign with TypeDefinition = "Type" only.
        /// Expected: Returns "MethodState" with "Type" stripped resulting in empty className.
        /// </summary>
        [Test]
        public void GetClassName_MethodDesignTypeDefinitionIsOnlyType_ReturnsMethodState()
        {
            // Arrange
            var mockMethod = new MethodDesign
            {
                SymbolicName = new XmlQualifiedName("TestMethod", "http://test.org"),
                TypeDefinition = new XmlQualifiedName("Type", "http://test.org"),
                HasArguments = false
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockMethod.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.MethodState"));
        }

        /// <summary>
        /// Tests GetClassName with a VariableDesign where DataTypeNode is from a different namespace.
        /// Input: VariableDesign with UserDefined datatype from different namespace.
        /// Expected: Returns templated state with namespace prefix in type name.
        /// </summary>
        [Test]
        public void GetClassName_VariableDesignDifferentNamespace_ReturnsTemplatedStateWithPrefix()
        {
            // Arrange
            var mockVariableType = new VariableTypeDesign
            {
                ClassName = "BaseVariableType",
                DataTypeNode = new DataTypeDesign
                {
                    BasicDataType = BasicDataType.BaseDataType
                },
                ValueRank = ValueRank.Scalar,
                SymbolicId = new XmlQualifiedName("BaseVariableType", "http://test.org")
            };

            var mockVariable = new VariableDesign
            {
                TypeDefinitionNode = mockVariableType,
                DataTypeNode = new DataTypeDesign
                {
                    BasicDataType = BasicDataType.UserDefined,
                    SymbolicId = new XmlQualifiedName("CustomType", "http://other.org"),
                    SymbolicName = new XmlQualifiedName("CustomType", "http://other.org")
                },
                ValueRank = ValueRank.Scalar,
                DataType = new XmlQualifiedName("CustomType", "http://other.org")
            };

            var mockNamespace = new Namespace
            {
                Value = "http://other.org",
                Prefix = "Other"
            };
            var namespaces = new Namespace[] { mockNamespace };

            // Act
            string result = mockVariable.GetNodeStateClassName("http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseVariableTypeState<global::Other.CustomType>"));
        }

        /// <summary>
        /// Tests GetValueRankString with Array value rank.
        /// Expects the method to return the OneDimension constant string.
        /// </summary>
        [Test]
        public void GetValueRankString_Array_ReturnsOneDimension()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.Array;
            const string arrayDimensions = null;

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.OneDimension"));
        }

        /// <summary>
        /// Tests GetValueRankString with Scalar value rank.
        /// Expects the method to return the Scalar constant string.
        /// </summary>
        [Test]
        public void GetValueRankString_Scalar_ReturnsScalar()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.Scalar;
            const string arrayDimensions = null;

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.Scalar"));
        }

        /// <summary>
        /// Tests GetValueRankString with Any value rank.
        /// Expects the method to return the Any constant string.
        /// </summary>
        [Test]
        public void GetValueRankString_Any_ReturnsAny()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.Any;
            const string arrayDimensions = null;

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.Any"));
        }

        /// <summary>
        /// Tests GetValueRankString with ScalarOrArray value rank.
        /// Expects the method to return the Any constant string.
        /// </summary>
        [Test]
        public void GetValueRankString_ScalarOrArray_ReturnsAny()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.ScalarOrArray;
            const string arrayDimensions = null;

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.Any"));
        }

        /// <summary>
        /// Tests GetValueRankString with ScalarOrOneDimension value rank.
        /// Expects the method to return the ScalarOrOneDimension constant string.
        /// </summary>
        [Test]
        public void GetValueRankString_ScalarOrOneDimension_ReturnsScalarOrOneDimension()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.ScalarOrOneDimension;
            const string arrayDimensions = null;

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.ScalarOrOneDimension"));
        }

        /// <summary>
        /// Tests GetValueRankString with OneOrMoreDimensions value rank and null arrayDimensions.
        /// Expects the method to return the OneOrMoreDimensions constant string.
        /// </summary>
        [Test]
        public void GetValueRankString_OneOrMoreDimensionsWithNullArrayDimensions_ReturnsOneOrMoreDimensions()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            const string arrayDimensions = null;

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.OneOrMoreDimensions"));
        }

        /// <summary>
        /// Tests GetValueRankString with OneOrMoreDimensions value rank and empty arrayDimensions.
        /// Expects the method to return the OneOrMoreDimensions constant string.
        /// </summary>
        [Test]
        public void GetValueRankString_OneOrMoreDimensionsWithEmptyArrayDimensions_ReturnsOneOrMoreDimensions()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            string arrayDimensions = string.Empty;

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.OneOrMoreDimensions"));
        }

        /// <summary>
        /// Tests GetValueRankString with OneOrMoreDimensions value rank and whitespace arrayDimensions.
        /// Expects the method to return the OneOrMoreDimensions constant string.
        /// </summary>
        [Test]
        public void GetValueRankString_OneOrMoreDimensionsWithWhitespaceArrayDimensions_ReturnsOneOrMoreDimensions()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            const string arrayDimensions = "   ";

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.OneOrMoreDimensions"));
        }

        /// <summary>
        /// Tests GetValueRankString with OneOrMoreDimensions value rank and single dimension.
        /// Expects the method to return the TwoDimensions constant string.
        /// </summary>
        [Test]
        public void GetValueRankString_OneOrMoreDimensionsWithSingleDimension_ReturnsTwoDimensions()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            const string arrayDimensions = "5";

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.TwoDimensions"));
        }

        /// <summary>
        /// Tests GetValueRankString with OneOrMoreDimensions value rank and two dimensions.
        /// Expects the method to return a formatted string with dimension count of 3.
        /// </summary>
        [Test]
        public void GetValueRankString_OneOrMoreDimensionsWithTwoDimensions_ReturnsFormattedDimensionCount()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            const string arrayDimensions = "5,10";

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("3"));
        }

        /// <summary>
        /// Tests GetValueRankString with OneOrMoreDimensions value rank and three dimensions.
        /// Expects the method to return a formatted string with dimension count of 4.
        /// </summary>
        [Test]
        public void GetValueRankString_OneOrMoreDimensionsWithThreeDimensions_ReturnsFormattedDimensionCount()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            const string arrayDimensions = "5,10,15";

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("4"));
        }

        /// <summary>
        /// Tests GetValueRankString with OneOrMoreDimensions value rank and multiple dimensions.
        /// Expects the method to return a formatted string with dimension count of 6.
        /// </summary>
        [Test]
        public void GetValueRankString_OneOrMoreDimensionsWithMultipleDimensions_ReturnsFormattedDimensionCount()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            const string arrayDimensions = "1,2,3,4,5";

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("6"));
        }

        /// <summary>
        /// Tests GetValueRankString with OneOrMoreDimensions value rank and dimensions with spaces.
        /// Expects the method to properly parse the dimensions and return correct count.
        /// </summary>
        [Test]
        public void GetValueRankString_OneOrMoreDimensionsWithSpacesInDimensions_ReturnsFormattedDimensionCount()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            const string arrayDimensions = "5, 10, 15";

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("4"));
        }

        /// <summary>
        /// Tests GetValueRankString with OneOrMoreDimensions value rank and empty entries between commas.
        /// Expects the method to skip empty entries and return correct dimension count.
        /// </summary>
        [Test]
        public void GetValueRankString_OneOrMoreDimensionsWithEmptyEntriesBetweenCommas_ReturnsFormattedDimensionCount()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            const string arrayDimensions = "5,,10";

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("3"));
        }

        /// <summary>
        /// Tests GetValueRankString with OneOrMoreDimensions value rank and only commas.
        /// Expects the method to treat it as empty and return OneOrMoreDimensions.
        /// </summary>
        [Test]
        public void GetValueRankString_OneOrMoreDimensionsWithOnlyCommas_ReturnsOneDimension()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            const string arrayDimensions = ",,,";

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.OneDimension"));
        }

        /// <summary>
        /// Tests GetValueRankString with invalid enum value.
        /// Expects the method to return the default Any constant string.
        /// </summary>
        [Test]
        public void GetValueRankString_InvalidEnumValue_ReturnsAny()
        {
            // Arrange
            const ValueRank valueRank = (ValueRank)999;
            const string arrayDimensions = null;

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.Any"));
        }

        /// <summary>
        /// Tests GetValueRankString with Array value rank and non-null arrayDimensions.
        /// Expects the method to ignore arrayDimensions and return OneDimension.
        /// </summary>
        [Test]
        public void GetValueRankString_ArrayWithNonNullArrayDimensions_ReturnsOneDimension()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.Array;
            const string arrayDimensions = "5,10";

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.OneDimension"));
        }

        /// <summary>
        /// Tests GetValueRankString with Scalar value rank and non-null arrayDimensions.
        /// Expects the method to ignore arrayDimensions and return Scalar.
        /// </summary>
        [Test]
        public void GetValueRankString_ScalarWithNonNullArrayDimensions_ReturnsScalar()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.Scalar;
            const string arrayDimensions = "5,10";

            // Act
            string result = valueRank.GetValueRankAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ValueRanks.Scalar"));
        }

        /// <summary>
        /// Tests that IsXmlNillable returns false for primitive value types.
        /// These types are not nillable in XML as they have default values.
        /// </summary>
        [TestCase(BasicDataType.Boolean, false)]
        [TestCase(BasicDataType.SByte, false)]
        [TestCase(BasicDataType.Byte, false)]
        [TestCase(BasicDataType.Int16, false)]
        [TestCase(BasicDataType.UInt16, false)]
        [TestCase(BasicDataType.Int32, false)]
        [TestCase(BasicDataType.UInt32, false)]
        [TestCase(BasicDataType.Int64, false)]
        [TestCase(BasicDataType.UInt64, false)]
        [TestCase(BasicDataType.Float, false)]
        [TestCase(BasicDataType.Double, false)]
        [TestCase(BasicDataType.StatusCode, false)]
        [TestCase(BasicDataType.Enumeration, false)]
        public void IsXmlNillable_NonNillableTypes_ReturnsFalse(BasicDataType type, bool expected)
        {
            // Act
            bool result = type.IsXmlNillable();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that IsXmlNillable returns true for reference types and complex types.
        /// These types are nillable in XML as they can represent null values.
        /// </summary>
        [TestCase(BasicDataType.String, true)]
        [TestCase(BasicDataType.DateTime, true)]
        [TestCase(BasicDataType.Guid, true)]
        [TestCase(BasicDataType.ByteString, true)]
        [TestCase(BasicDataType.XmlElement, true)]
        [TestCase(BasicDataType.NodeId, true)]
        [TestCase(BasicDataType.ExpandedNodeId, true)]
        [TestCase(BasicDataType.DiagnosticInfo, true)]
        [TestCase(BasicDataType.QualifiedName, true)]
        [TestCase(BasicDataType.LocalizedText, true)]
        [TestCase(BasicDataType.DataValue, true)]
        [TestCase(BasicDataType.Number, true)]
        [TestCase(BasicDataType.Integer, true)]
        [TestCase(BasicDataType.UInteger, true)]
        [TestCase(BasicDataType.Structure, true)]
        [TestCase(BasicDataType.BaseDataType, true)]
        [TestCase(BasicDataType.UserDefined, true)]
        public void IsXmlNillable_NillableTypes_ReturnsTrue(BasicDataType type, bool expected)
        {
            // Act
            bool result = type.IsXmlNillable();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that IsXmlNillable handles undefined enum values correctly.
        /// Undefined enum values should return the default case (true).
        /// </summary>
        [TestCase(-1)]
        [TestCase(999)]
        [TestCase(int.MaxValue)]
        [TestCase(int.MinValue)]
        public void IsXmlNillable_UndefinedEnumValue_ReturnsTrue(int undefinedValue)
        {
            // Arrange
            var type = (BasicDataType)undefinedValue;

            // Act
            bool result = type.IsXmlNillable();

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsXmlNillable returns correct values for all boundary enum values.
        /// Tests the first and last defined enum values to ensure proper handling.
        /// </summary>
        [Test]
        public void IsXmlNillable_BoundaryEnumValues_ReturnsExpectedResults()
        {
            // Arrange
            const BasicDataType firstEnumValue = BasicDataType.Boolean;
            const BasicDataType lastEnumValue = BasicDataType.UserDefined;

            // Act
            bool firstResult = firstEnumValue.IsXmlNillable();
            bool lastResult = lastEnumValue.IsXmlNillable();

            // Assert
            Assert.That(firstResult, Is.False);
            Assert.That(lastResult, Is.True);
        }

        /// <summary>
        /// Tests GetPrefixedName when the qname is null.
        /// Should return an empty string.
        /// </summary>
        [Test]
        public void GetPrefixedName_NullQName_ReturnsEmptyString()
        {
            // Arrange
            XmlQualifiedName qname = null;
            var namespaceUris = new List<string> { "http://example.com/ns1" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests GetPrefixedName when the qname has a null Name property.
        /// Should return an empty string.
        /// </summary>
        [Test]
        public void GetPrefixedName_NullName_ReturnsEmptyString()
        {
            // Arrange
            var qname = new XmlQualifiedName(null, "http://example.com");
            var namespaceUris = new List<string> { "http://example.com" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests GetPrefixedName when the qname has an empty Name property.
        /// Should return an empty string.
        /// </summary>
        [Test]
        public void GetPrefixedName_EmptyName_ReturnsEmptyString()
        {
            // Arrange
            var qname = new XmlQualifiedName(string.Empty, "http://example.com");
            var namespaceUris = new List<string> { "http://example.com" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests GetPrefixedName when the namespace is OpcUaBuiltInTypes.
        /// Should return the name prefixed with "ua:".
        /// </summary>
        [Test]
        public void GetPrefixedName_OpcUaBuiltInTypesNamespace_ReturnsUaPrefix()
        {
            // Arrange
            var qname = new XmlQualifiedName("Int32", Namespaces.OpcUaBuiltInTypes);
            var namespaceUris = new List<string> { "http://example.com" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("ua:Int32"));
        }

        /// <summary>
        /// Tests GetPrefixedName when the namespace is found at index 0.
        /// Should return just the name without prefix (index > 0 is false).
        /// </summary>
        [Test]
        public void GetPrefixedName_NamespaceAtIndexZero_ReturnsNameOnly()
        {
            // Arrange
            var qname = new XmlQualifiedName("MyType", "http://example.com/ns1");
            var namespaceUris = new List<string> { "http://example.com/ns1", "http://example.com/ns2" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("MyType"));
        }

        /// <summary>
        /// Tests GetPrefixedName when the namespace is found at index 1.
        /// Should return the name prefixed with "s0:" (index - 1 = 0).
        /// </summary>
        [Test]
        public void GetPrefixedName_NamespaceAtIndexOne_ReturnsSZeroPrefix()
        {
            // Arrange
            var qname = new XmlQualifiedName("MyType", "http://example.com/ns2");
            var namespaceUris = new List<string> { "http://example.com/ns1", "http://example.com/ns2" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("s0:MyType"));
        }

        /// <summary>
        /// Tests GetPrefixedName when the namespace is found at index 2.
        /// Should return the name prefixed with "s1:" (index - 1 = 1).
        /// </summary>
        [Test]
        public void GetPrefixedName_NamespaceAtIndexTwo_ReturnsSOnePrefix()
        {
            // Arrange
            var qname = new XmlQualifiedName("MyType", "http://example.com/ns3");
            var namespaceUris = new List<string>
            {
                "http://example.com/ns1",
                "http://example.com/ns2",
                "http://example.com/ns3"
            };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("s1:MyType"));
        }

        /// <summary>
        /// Tests GetPrefixedName when the namespace is found at a higher index.
        /// Should return the name prefixed with "s{index-1}:".
        /// </summary>
        [Test]
        public void GetPrefixedName_NamespaceAtHigherIndex_ReturnsCorrectPrefix()
        {
            // Arrange
            var qname = new XmlQualifiedName("MyType", "http://example.com/ns10");
            var namespaceUris = new List<string>
            {
                "http://example.com/ns1",
                "http://example.com/ns2",
                "http://example.com/ns3",
                "http://example.com/ns4",
                "http://example.com/ns5",
                "http://example.com/ns6",
                "http://example.com/ns7",
                "http://example.com/ns8",
                "http://example.com/ns9",
                "http://example.com/ns10"
            };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("s8:MyType"));
        }

        /// <summary>
        /// Tests GetPrefixedName when the namespace is not found in the list.
        /// Should return just the name without prefix.
        /// </summary>
        [Test]
        public void GetPrefixedName_NamespaceNotFound_ReturnsNameOnly()
        {
            // Arrange
            var qname = new XmlQualifiedName("MyType", "http://example.com/unknown");
            var namespaceUris = new List<string> { "http://example.com/ns1", "http://example.com/ns2" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("MyType"));
        }

        /// <summary>
        /// Tests GetPrefixedName when the namespaceUris list is empty.
        /// Should return just the name without prefix (namespace not found).
        /// </summary>
        [Test]
        public void GetPrefixedName_EmptyNamespaceUrisList_ReturnsNameOnly()
        {
            // Arrange
            var qname = new XmlQualifiedName("MyType", "http://example.com");
            var namespaceUris = new List<string>();

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("MyType"));
        }

        /// <summary>
        /// Tests GetPrefixedName when the namespaceUris parameter is null.
        /// Should throw NullReferenceException as the method attempts to call IndexOf on null list.
        /// </summary>
        [Test]
        public void GetPrefixedName_NullNamespaceUrisList_ThrowsArgumentNullException()
        {
            // Arrange
            var qname = new XmlQualifiedName("MyType", "http://example.com");
            List<string> namespaceUris = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => qname.GetPrefixedName(namespaceUris));
        }

        /// <summary>
        /// Tests GetPrefixedName with a name containing special characters.
        /// Should return the name with prefix correctly including special characters.
        /// </summary>
        [Test]
        public void GetPrefixedName_NameWithSpecialCharacters_ReturnsCorrectResult()
        {
            // Arrange
            var qname = new XmlQualifiedName("My-Type_123", "http://example.com/ns2");
            var namespaceUris = new List<string> { "http://example.com/ns1", "http://example.com/ns2" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("s0:My-Type_123"));
        }

        /// <summary>
        /// Tests GetPrefixedName with a name containing whitespace.
        /// Should return the name with prefix correctly including whitespace.
        /// </summary>
        [Test]
        public void GetPrefixedName_NameWithWhitespace_ReturnsCorrectResult()
        {
            // Arrange
            var qname = new XmlQualifiedName("My Type", "http://example.com/ns2");
            var namespaceUris = new List<string> { "http://example.com/ns1", "http://example.com/ns2" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("s0:My Type"));
        }

        /// <summary>
        /// Tests GetPrefixedName with qname having an empty namespace string.
        /// Should return just the name (namespace not found in list).
        /// </summary>
        [Test]
        public void GetPrefixedName_EmptyNamespace_ReturnsNameOnly()
        {
            // Arrange
            var qname = new XmlQualifiedName("MyType", string.Empty);
            var namespaceUris = new List<string> { "http://example.com/ns1" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("MyType"));
        }

        /// <summary>
        /// Tests GetPrefixedName when namespace list contains the empty string at index 0.
        /// Should return just the name (index > 0 is false).
        /// </summary>
        [Test]
        public void GetPrefixedName_EmptyNamespaceAtIndexZero_ReturnsNameOnly()
        {
            // Arrange
            var qname = new XmlQualifiedName("MyType", string.Empty);
            var namespaceUris = new List<string> { string.Empty, "http://example.com/ns1" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("MyType"));
        }

        /// <summary>
        /// Tests GetPrefixedName when namespace list contains the empty string at index > 0.
        /// Should return the name with prefix "s{index-1}:".
        /// </summary>
        [Test]
        public void GetPrefixedName_EmptyNamespaceAtIndexOne_ReturnsCorrectPrefix()
        {
            // Arrange
            var qname = new XmlQualifiedName("MyType", string.Empty);
            var namespaceUris = new List<string> { "http://example.com/ns1", string.Empty };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("s0:MyType"));
        }

        /// <summary>
        /// Tests GetPrefixedName with namespace containing special characters.
        /// Should correctly find and prefix the name if namespace matches.
        /// </summary>
        [Test]
        public void GetPrefixedName_NamespaceWithSpecialCharacters_ReturnsCorrectResult()
        {
            // Arrange
            var qname = new XmlQualifiedName("MyType", "http://example.com/ns-1_special");
            var namespaceUris = new List<string> { "http://example.com/ns1", "http://example.com/ns-1_special" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("s0:MyType"));
        }

        /// <summary>
        /// Tests GetPrefixedName when OpcUaBuiltInTypes namespace with empty name.
        /// Should return empty string (IsNull catches empty name).
        /// </summary>
        [Test]
        public void GetPrefixedName_OpcUaBuiltInTypesWithEmptyName_ReturnsEmptyString()
        {
            // Arrange
            var qname = new XmlQualifiedName(string.Empty, Namespaces.OpcUaBuiltInTypes);
            var namespaceUris = new List<string> { "http://example.com" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests GetPrefixedName with duplicate namespaces in the list.
        /// Should use the first occurrence index for the prefix calculation.
        /// </summary>
        [Test]
        public void GetPrefixedName_DuplicateNamespaces_UsesFirstOccurrence()
        {
            // Arrange
            var qname = new XmlQualifiedName("MyType", "http://example.com/duplicate");
            var namespaceUris = new List<string>
            {
                "http://example.com/ns1",
                "http://example.com/duplicate",
                "http://example.com/duplicate"
            };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("s0:MyType"));
        }

        /// <summary>
        /// Tests GetPrefixedName with very long name string.
        /// Should handle long names correctly with appropriate prefix.
        /// </summary>
        [Test]
        public void GetPrefixedName_VeryLongName_ReturnsCorrectResult()
        {
            // Arrange
            string longName = new('A', 1000);
            var qname = new XmlQualifiedName(longName, "http://example.com/ns2");
            var namespaceUris = new List<string> { "http://example.com/ns1", "http://example.com/ns2" };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo($"s0:{longName}"));
        }

        /// <summary>
        /// Tests GetPrefixedName with namespace URI that is very long.
        /// Should correctly match and prefix if the namespace is found in the list.
        /// </summary>
        [Test]
        public void GetPrefixedName_VeryLongNamespaceUri_ReturnsCorrectResult()
        {
            // Arrange
            string longUri = "http://example.com/" + new string('n', 1000);
            var qname = new XmlQualifiedName("MyType", longUri);
            var namespaceUris = new List<string> { "http://example.com/ns1", longUri };

            // Act
            string result = qname.GetPrefixedName(namespaceUris);

            // Assert
            Assert.That(result, Is.EqualTo("s0:MyType"));
        }

        /// <summary>
        /// Tests that GetBaseClassName throws NullReferenceException when type parameter is null.
        /// Validates null checking for the extension method parameter.
        /// </summary>
        [Test]
        public void GetBaseClassName_NullType_ThrowsArgumentNullException()
        {
            // Arrange
            TypeDesign type = null;
            Namespace[] namespaces = [];

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => type.GetBaseClassName(namespaces));
        }

        /// <summary>
        /// Tests GetBaseClassName when type is not a DataTypeDesign and has a valid BaseTypeNode.
        /// Verifies that the method returns the SymbolicName.Name from BaseTypeNode.
        /// Expected result: Returns the base type's symbolic name.
        /// </summary>
        [Test]
        public void GetBaseClassName_NonDataTypeDesignWithValidBaseTypeNode_ReturnsSymbolicName()
        {
            // Arrange
            var mockType = new TypeDesign
            {
                BaseTypeNode = new TypeDesign
                {
                    SymbolicName = new XmlQualifiedName("BaseTypeName", "http://test.org")
                }
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockType.GetBaseClassName(namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseTypeName"));
        }

        /// <summary>
        /// Tests GetBaseClassName when type is not a DataTypeDesign but BaseTypeNode is null.
        /// Verifies that the method throws NullReferenceException when trying to access BaseTypeNode.SymbolicName.
        /// This tests the potential bug in the original code where BaseTypeNode could be null.
        /// </summary>
        [Test]
        public void GetBaseClassName_NonDataTypeDesignWithNullBaseTypeNode_ThrowsArgumentNullException()
        {
            // Arrange
            var mockType = new TypeDesign
            {
                BaseTypeNode = null
            };
            Namespace[] namespaces = [];

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => mockType.GetBaseClassName(namespaces));
        }

        /// <summary>
        /// Tests GetBaseClassName when type is a DataTypeDesign with BaseTypeNode having BasicDataType.Structure.
        /// Verifies that the method returns the hardcoded "global::Opc.Ua.IEncodeable" string.
        /// Expected result: Returns "global::Opc.Ua.IEncodeable".
        /// </summary>
        [Test]
        public void GetBaseClassName_DataTypeDesignWithStructureBasicType_ReturnsIEncodeable()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BaseTypeNode = new DataTypeDesign
                {
                    BasicDataType = BasicDataType.Structure,
                    SymbolicName = new XmlQualifiedName("Structure", "http://test.org")
                }
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBaseClassName(namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.IEncodeable"));
        }

        /// <summary>
        /// Tests GetBaseClassName when type is a DataTypeDesign with BaseTypeNode having non-Structure BasicDataType.
        /// Verifies that the method constructs a qualified name using namespace prefix and SymbolicName.
        /// Expected result: Returns "{prefix}.{SymbolicName.Name}".
        /// </summary>
        [TestCase(BasicDataType.Boolean)]
        [TestCase(BasicDataType.String)]
        [TestCase(BasicDataType.Int32)]
        [TestCase(BasicDataType.Enumeration)]
        [TestCase(BasicDataType.UserDefined)]
        public void GetBaseClassName_DataTypeDesignWithNonStructureBasicType_ReturnsQualifiedName(BasicDataType basicType)
        {
            // Arrange
            var mockDataType = new DataTypeDesign();
            var mockBaseDataType = new DataTypeDesign();
            var symbolicName = new XmlQualifiedName("BaseTypeName", "http://opcfoundation.org/UA/");
            var symbolicId = new XmlQualifiedName("BaseTypeId", "http://test.org");

            mockBaseDataType.BasicDataType = basicType;
            mockBaseDataType.SymbolicName = symbolicName;
            mockBaseDataType.SymbolicId = symbolicId;
            mockDataType.BaseTypeNode = mockBaseDataType;

            Namespace[] namespaces =
            [
                new Namespace { Value = "http://opcfoundation.org/UA/", Prefix = "OpcUa" }
            ];

            // Act
            string result = mockDataType.GetBaseClassName(namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::OpcUa.BaseTypeName"));
        }

        /// <summary>
        /// Tests GetBaseClassName when type is a DataTypeDesign with non-Structure BasicDataType
        /// and the namespace is not found in the namespaces array.
        /// Verifies that the method handles missing namespace prefix (returns null from GetNamespacePrefix).
        /// Expected result: Returns "null.{SymbolicName.Name}".
        /// </summary>
        [Test]
        public void GetBaseClassName_DataTypeDesignWithNonStructureTypeAndNamespaceNotFound_ReturnsNullPrefix()
        {
            // Arrange
            var mockDataType = new DataTypeDesign();
            var mockBaseDataType = new DataTypeDesign();
            var symbolicName = new XmlQualifiedName("BaseTypeName", "http://test.org");
            var symbolicId = new XmlQualifiedName("BaseTypeId", "http://unknown.org/");

            mockBaseDataType.BasicDataType = BasicDataType.Int32;
            mockBaseDataType.SymbolicName = symbolicName;
            mockBaseDataType.SymbolicId = symbolicId;
            mockDataType.BaseTypeNode = mockBaseDataType;

            Namespace[] namespaces =
            [
                new Namespace { Value = "http://opcfoundation.org/UA/", Prefix = "OpcUa" }
            ];

            // Act
            string result = mockDataType.GetBaseClassName(namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseTypeName"));
        }

        /// <summary>
        /// Tests GetBaseClassName when namespaces array is null.
        /// Verifies that the method handles null namespaces parameter gracefully.
        /// Expected result: GetNamespacePrefix returns null, result is ".{SymbolicName.Name}".
        /// </summary>
        [Test]
        public void GetBaseClassName_DataTypeDesignWithNullNamespacesArray_ReturnsNullPrefix()
        {
            // Arrange
            var mockDataType = new DataTypeDesign();
            var mockBaseDataType = new DataTypeDesign();
            var symbolicName = new XmlQualifiedName("BaseTypeName", "http://test.org");
            var symbolicId = new XmlQualifiedName("BaseTypeId", "http://test.org/");

            mockBaseDataType.BasicDataType = BasicDataType.Int32;
            mockBaseDataType.SymbolicName = symbolicName;
            mockBaseDataType.SymbolicId = symbolicId;
            mockDataType.BaseTypeNode = mockBaseDataType;

            Namespace[] namespaces = null;

            // Act
            string result = mockDataType.GetBaseClassName(namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseTypeName"));
        }

        /// <summary>
        /// Tests GetBaseClassName when namespaces array is empty.
        /// Verifies that the method handles empty namespaces array.
        /// Expected result: GetNamespacePrefix returns null, result is ".{SymbolicName.Name}".
        /// </summary>
        [Test]
        public void GetBaseClassName_DataTypeDesignWithEmptyNamespacesArray_ReturnsNullPrefix()
        {
            // Arrange
            var mockDataType = new DataTypeDesign();
            var mockBaseDataType = new DataTypeDesign();
            var symbolicName = new XmlQualifiedName("BaseTypeName", "http://test.org");
            var symbolicId = new XmlQualifiedName("BaseTypeId", "http://test.org/");

            mockBaseDataType.BasicDataType = BasicDataType.String;
            mockBaseDataType.SymbolicName = symbolicName;
            mockBaseDataType.SymbolicId = symbolicId;
            mockDataType.BaseTypeNode = mockBaseDataType;

            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetBaseClassName(namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseTypeName"));
        }

        /// <summary>
        /// Tests GetBaseClassName when SymbolicName.Name is null.
        /// Verifies that the method returns a string with null for the name part.
        /// Expected result: Returns the concatenated result with null name.
        /// </summary>
        [Test]
        public void GetBaseClassName_DataTypeDesignWithNullSymbolicNameName_ReturnsResultWithNullName()
        {
            // Arrange
            var mockDataType = new DataTypeDesign();
            var mockBaseDataType = new DataTypeDesign();
            var symbolicName = new XmlQualifiedName("null", "http://test.org");
            var symbolicId = new XmlQualifiedName("BaseTypeId", "http://opcfoundation.org/UA/");

            mockBaseDataType.BasicDataType = BasicDataType.Int32;
            mockBaseDataType.SymbolicName = symbolicName;
            mockBaseDataType.SymbolicId = symbolicId;
            mockDataType.BaseTypeNode = mockBaseDataType;

            Namespace[] namespaces =
            [
                new Namespace { Value = "http://test.org", Prefix = "OpcUa" }
            ];

            // Act
            string result = mockDataType.GetBaseClassName(namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::OpcUa.null"));
        }

        /// <summary>
        /// Tests GetBaseClassName when SymbolicName is null.
        /// Verifies that the method throws NullReferenceException when accessing SymbolicName.Name.
        /// Expected behavior: Throws NullReferenceException.
        /// </summary>
        [Test]
        public void GetBaseClassName_NonDataTypeDesignWithNullSymbolicName_ThrowsArgumentNullException()
        {
            // Arrange
            var mockType = new TypeDesign
            {
                BaseTypeNode = new TypeDesign
                {
                    SymbolicName = null
                }
            };
            Namespace[] namespaces = [];

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => mockType.GetBaseClassName(namespaces));
        }

        /// <summary>
        /// Tests GetBaseClassName when SymbolicId.Namespace is null.
        /// Verifies that GetNamespacePrefix handles null namespace string.
        /// Expected result: Returns result with null prefix.
        /// </summary>
        [Test]
        public void GetBaseClassName_DataTypeDesignWithNullSymbolicIdNamespace_ReturnsNullPrefix()
        {
            // Arrange
            var mockDataType = new DataTypeDesign();
            var mockBaseDataType = new DataTypeDesign();
            var symbolicName = new XmlQualifiedName("BaseTypeName", "http://test.org");
            var symbolicId = new XmlQualifiedName("BaseTypeId", null);

            mockBaseDataType.BasicDataType = BasicDataType.Boolean;
            mockBaseDataType.SymbolicName = symbolicName;
            mockBaseDataType.SymbolicId = symbolicId;
            mockDataType.BaseTypeNode = mockBaseDataType;

            Namespace[] namespaces =
            [
                new Namespace { Value = "http://opcfoundation.org/UA/", Prefix = "OpcUa" }
            ];

            // Act
            string result = mockDataType.GetBaseClassName(namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("BaseTypeName"));
        }

        /// <summary>
        /// Tests GetBaseClassName with multiple namespaces and verifies correct namespace resolution.
        /// Ensures that the method correctly searches through multiple namespaces to find the matching one.
        /// Expected result: Returns correctly prefixed name with the matching namespace.
        /// </summary>
        [Test]
        public void GetBaseClassName_DataTypeDesignWithMultipleNamespaces_ReturnsCorrectPrefix()
        {
            // Arrange
            var mockDataType = new DataTypeDesign();
            var mockBaseDataType = new DataTypeDesign();
            var symbolicName = new XmlQualifiedName("CustomType", "http://custom.org/Types/");
            var symbolicId = new XmlQualifiedName("CustomTypeId", "http://test.org");

            mockBaseDataType.BasicDataType = BasicDataType.UserDefined;
            mockBaseDataType.SymbolicName = symbolicName;
            mockBaseDataType.SymbolicId = symbolicId;
            mockDataType.BaseTypeNode = mockBaseDataType;

            Namespace[] namespaces =
            [
                new Namespace { Value = "http://opcfoundation.org/UA/", Prefix = "OpcUa" },
                new Namespace { Value = "http://custom.org/Types/", Prefix = "Custom" },
                new Namespace { Value = "http://other.org/", Prefix = "Other" }
            ];

            // Act
            string result = mockDataType.GetBaseClassName(namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Custom.CustomType"));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName throws ArgumentNullException when node parameter is null.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_NullNode_ThrowsArgumentNullException()
        {
            // Arrange
            TypeDesign node = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => node.GetClassName([]));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName returns SymbolicId.Name when node is a DataTypeDesign.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_DataTypeDesign_ReturnsSymbolicIdName()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                SymbolicName = new XmlQualifiedName("TestDataType", "http://test.namespace"),
                ClassName = "SomeClassName"
            };

            // Act
            string result = dataType.GetClassName([]);

            // Assert
            Assert.That(result, Is.EqualTo("TestDataType"));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName throws when DataTypeDesign has null SymbolicId.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_DataTypeDesignWithNullSymbolicId_ThrowsArgumentException()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                SymbolicId = null,
                ClassName = "SomeClassName"
            };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => dataType.GetClassName([]));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName returns "BaseObject" when ObjectTypeDesign has ClassName "ObjectSource".
        /// </summary>
        [Test]
        public void GetNodeStateClassName_ObjectTypeDesignWithObjectSourceClassName_ReturnsBaseObject()
        {
            // Arrange
            var objectType = new ObjectTypeDesign
            {
                ClassName = "ObjectSource",
                SymbolicName = new XmlQualifiedName("TestObject", "http://test.namespace")
            };

            // Act
            string result = objectType.GetClassName([]);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.BaseObject"));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName returns actual ClassName when ObjectTypeDesign has different ClassName.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_ObjectTypeDesignWithDifferentClassName_ReturnsActualClassName()
        {
            // Arrange
            var objectType = new ObjectTypeDesign
            {
                ClassName = "CustomObjectType",
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.namespace")
            };

            // Act
            string result = objectType.GetClassName([]);

            // Assert
            Assert.That(result, Is.EqualTo("CustomObjectType"));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName throws when ObjectTypeDesign has null ClassName.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_ObjectTypeDesignWithNullClassName_ThrowsArgumentException()
        {
            // Arrange
            var objectType = new ObjectTypeDesign
            {
                ClassName = null,
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.namespace")
            };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => objectType.GetClassName([]));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName returns "BaseDataVariable" when VariableTypeDesign has ClassName "DataVariable".
        /// </summary>
        [Test]
        public void GetNodeStateClassName_VariableTypeDesignWithDataVariableClassName_ReturnsBaseDataVariable()
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                ClassName = "DataVariable",
                SymbolicId = new XmlQualifiedName("TestVariable", "http://test.namespace")
            };

            // Act
            string result = variableType.GetClassName([]);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.BaseDataVariable"));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName returns actual ClassName when VariableTypeDesign has different ClassName.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_VariableTypeDesignWithDifferentClassName_ReturnsActualClassName()
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                ClassName = "CustomVariableType",
                SymbolicId = new XmlQualifiedName("TestVariable", "http://test.namespace")
            };

            // Act
            string result = variableType.GetClassName([]);

            // Assert
            Assert.That(result, Is.EqualTo("CustomVariableType"));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName throws when VariableTypeDesign has null ClassName.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_VariableTypeDesignWithNullClassName_ThrowsArgumentExceptionl()
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                ClassName = null,
                SymbolicId = new XmlQualifiedName("TestVariable", "http://test.namespace")
            };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => variableType.GetClassName([]));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName returns ClassName when TypeDesign is not DataType, ObjectType or VariableType.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_ReferenceTypeDesign_ReturnsClassName()
        {
            // Arrange
            var referenceType = new ReferenceTypeDesign
            {
                ClassName = "CustomReferenceType",
                SymbolicId = new XmlQualifiedName("TestReference", "http://test.namespace")
            };

            // Act
            string result = referenceType.GetClassName([]);

            // Assert
            Assert.That(result, Is.EqualTo("CustomReferenceType"));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName returns empty string when ObjectTypeDesign ClassName is empty.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_ObjectTypeDesignWithEmptyClassName_SymbolicNameInstead()
        {
            // Arrange
            var objectType = new ObjectTypeDesign
            {
                ClassName = string.Empty,
                SymbolicName = new XmlQualifiedName("TestObject", "http://test.namespace")
            };

            // Act
            string result = objectType.GetClassName([]);

            // Assert
            Assert.That(result, Is.EqualTo("TestObject"));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName returns whitespace string when VariableTypeDesign ClassName is whitespace.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_VariableTypeDesignWithWhitespaceClassName_ReturnsWhitespace()
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                ClassName = "   ",
                SymbolicId = new XmlQualifiedName("TestVariable", "http://test.namespace")
            };

            // Act
            string result = variableType.GetClassName([]);

            // Assert
            Assert.That(result, Is.EqualTo("   "));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName comparison is case-sensitive for ObjectSource.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_ObjectTypeDesignWithObjectSourceLowerCase_ReturnsActualClassName()
        {
            // Arrange
            var objectType = new ObjectTypeDesign
            {
                ClassName = "objectsource",
                SymbolicId = new XmlQualifiedName("TestObject", "http://test.namespace")
            };

            // Act
            string result = objectType.GetClassName([]);

            // Assert
            Assert.That(result, Is.EqualTo("objectsource"));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName comparison is case-sensitive for DataVariable.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_VariableTypeDesignWithDataVariableLowerCase_ReturnsActualClassName()
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                ClassName = "datavariable",
                SymbolicId = new XmlQualifiedName("TestVariable", "http://test.namespace")
            };

            // Act
            string result = variableType.GetClassName([]);

            // Assert
            Assert.That(result, Is.EqualTo("datavariable"));
        }

        /// <summary>
        /// Tests that GetNodeStateClassName returns empty string from Symbolic Name
        /// when DataTypeDesign Symbolic Name is empty.
        /// </summary>
        [Test]
        public void GetNodeStateClassName_DataTypeDesignWithEmptySymbolicIdName_Throws()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                SymbolicName = new XmlQualifiedName(string.Empty, "http://test.namespace"),
                ClassName = "SomeClassName"
            };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => dataType.GetClassName([]));
        }

        /// <summary>
        /// Tests that IsDotNetValueType returns false when valueRank is not Scalar.
        /// Input: DataTypeDesign with any BasicDataType, valueRank = Array (non-Scalar)
        /// Expected: Returns false regardless of BasicDataType.
        /// </summary>
        [TestCase(ValueRank.Array)]
        [TestCase(ValueRank.ScalarOrArray)]
        [TestCase(ValueRank.OneOrMoreDimensions)]
        [TestCase(ValueRank.ScalarOrOneDimension)]
        [TestCase(ValueRank.Any)]
        public void IsDotNetValueType_NonScalarValueRank_ReturnsFalse(ValueRank valueRank)
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };

            // Act
            bool result = dataType.IsDotNetValueType(valueRank);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsDotNetValueType returns true for all value type BasicDataTypes when valueRank is Scalar.
        /// Input: DataTypeDesign with BasicDataType representing .NET value types, valueRank = Scalar
        /// Expected: Returns true.
        /// </summary>
        [TestCase(BasicDataType.Boolean)]
        [TestCase(BasicDataType.SByte)]
        [TestCase(BasicDataType.Byte)]
        [TestCase(BasicDataType.Int16)]
        [TestCase(BasicDataType.UInt16)]
        [TestCase(BasicDataType.Int32)]
        [TestCase(BasicDataType.UInt32)]
        [TestCase(BasicDataType.Int64)]
        [TestCase(BasicDataType.UInt64)]
        [TestCase(BasicDataType.Float)]
        [TestCase(BasicDataType.Double)]
        [TestCase(BasicDataType.DateTime)]
        [TestCase(BasicDataType.Guid)]
        [TestCase(BasicDataType.NodeId)]
        [TestCase(BasicDataType.ExpandedNodeId)]
        [TestCase(BasicDataType.QualifiedName)]
        [TestCase(BasicDataType.LocalizedText)]
        [TestCase(BasicDataType.StatusCode)]
        [TestCase(BasicDataType.Structure)]
        [TestCase(BasicDataType.BaseDataType)]
        public void IsDotNetValueType_ScalarValueTypeBasicDataType_ReturnsTrue(BasicDataType basicDataType)
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = basicDataType
            };

            // Act
            bool result = dataType.IsDotNetValueType(ValueRank.Scalar);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsDotNetValueType returns false for all reference type BasicDataTypes when valueRank is Scalar.
        /// Input: DataTypeDesign with BasicDataType representing .NET reference types, valueRank = Scalar
        /// Expected: Returns false.
        /// </summary>
        [TestCase(BasicDataType.String)]
        [TestCase(BasicDataType.ByteString)]
        [TestCase(BasicDataType.XmlElement)]
        [TestCase(BasicDataType.DiagnosticInfo)]
        [TestCase(BasicDataType.DataValue)]
        [TestCase(BasicDataType.Number)]
        [TestCase(BasicDataType.Integer)]
        [TestCase(BasicDataType.UInteger)]
        [TestCase(BasicDataType.Enumeration)]
        [TestCase(BasicDataType.UserDefined)]
        public void IsDotNetValueType_ScalarReferenceTypeBasicDataType_ReturnsFalse(BasicDataType basicDataType)
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = basicDataType
            };

            // Act
            bool result = dataType.IsDotNetValueType(ValueRank.Scalar);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsDotNetValueType returns false for out-of-range BasicDataType enum values.
        /// Input: DataTypeDesign with invalid (cast from int) BasicDataType, valueRank = Scalar
        /// Expected: Returns false (falls into default case).
        /// </summary>
        [TestCase(-1)]
        [TestCase(999)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void IsDotNetValueType_InvalidBasicDataType_ReturnsFalse(int invalidBasicDataType)
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = (BasicDataType)invalidBasicDataType
            };

            // Act
            bool result = dataType.IsDotNetValueType(ValueRank.Scalar);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsDotNetValueType returns false for out-of-range ValueRank enum values.
        /// Input: DataTypeDesign with any BasicDataType, valueRank = invalid (cast from int)
        /// Expected: Returns false (not equal to Scalar).
        /// </summary>
        [TestCase(-1)]
        [TestCase(999)]
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        public void IsDotNetValueType_InvalidValueRank_ReturnsFalse(int invalidValueRank)
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };

            // Act
            bool result = dataType.IsDotNetValueType((ValueRank)invalidValueRank);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsOverridden throws NullReferenceException when called on a null instance.
        /// </summary>
        [Test]
        public void IsOverridden_NullInstance_ThrowsArgumentNullException()
        {
            // Arrange
            InstanceDesign instance = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => instance.IsOverridden());
        }

        /// <summary>
        /// Tests that IsOverridden returns false when OveriddenNode is null,
        /// regardless of the ModellingRule value.
        /// </summary>
        [TestCase(ModellingRule.None)]
        [TestCase(ModellingRule.Mandatory)]
        [TestCase(ModellingRule.Optional)]
        [TestCase(ModellingRule.ExposesItsArray)]
        [TestCase(ModellingRule.CardinalityRestriction)]
        [TestCase(ModellingRule.MandatoryShared)]
        [TestCase(ModellingRule.OptionalPlaceholder)]
        [TestCase(ModellingRule.MandatoryPlaceholder)]
        public void IsOverridden_OveriddenNodeIsNull_ReturnsFalse(ModellingRule modellingRule)
        {
            // Arrange
            var instance = new InstanceDesign
            {
                OveriddenNode = null,
                ModellingRule = modellingRule
            };

            // Act
            bool result = instance.IsOverridden();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsOverridden returns false when OveriddenNode is not null
        /// but ModellingRule is one of the excluded values (None, ExposesItsArray,
        /// MandatoryPlaceholder, OptionalPlaceholder).
        /// </summary>
        [TestCase(ModellingRule.None)]
        [TestCase(ModellingRule.ExposesItsArray)]
        [TestCase(ModellingRule.MandatoryPlaceholder)]
        [TestCase(ModellingRule.OptionalPlaceholder)]
        public void IsOverridden_OveriddenNodeNotNullAndExcludedModellingRule_ReturnsFalse(ModellingRule modellingRule)
        {
            // Arrange
            var overriddenNode = new InstanceDesign();
            var instance = new InstanceDesign
            {
                OveriddenNode = overriddenNode,
                ModellingRule = modellingRule
            };

            // Act
            bool result = instance.IsOverridden();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsOverridden returns true when OveriddenNode is not null
        /// and ModellingRule is one of the valid values (Mandatory, Optional,
        /// CardinalityRestriction, MandatoryShared).
        /// </summary>
        [TestCase(ModellingRule.Mandatory)]
        [TestCase(ModellingRule.Optional)]
        [TestCase(ModellingRule.CardinalityRestriction)]
        [TestCase(ModellingRule.MandatoryShared)]
        public void IsOverridden_OveriddenNodeNotNullAndValidModellingRule_ReturnsTrue(ModellingRule modellingRule)
        {
            // Arrange
            var overriddenNode = new InstanceDesign();
            var instance = new InstanceDesign
            {
                OveriddenNode = overriddenNode,
                ModellingRule = modellingRule
            };

            // Act
            bool result = instance.IsOverridden();

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that GetNamespace returns null when the namespaces array is null.
        /// </summary>
        [Test]
        public void GetNamespace_NullNamespacesArray_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces = null;
            const string namespaceUri = "http://example.com/namespace";

            // Act
            Namespace result = namespaces.GetNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespace returns null when the namespaces array is empty.
        /// </summary>
        [Test]
        public void GetNamespace_EmptyNamespacesArray_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces = [];
            const string namespaceUri = "http://example.com/namespace";

            // Act
            Namespace result = namespaces.GetNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespace returns the matching namespace when a single matching namespace exists.
        /// </summary>
        [Test]
        public void GetNamespace_SingleMatchingNamespace_ReturnsMatchingNamespace()
        {
            // Arrange
            var expectedNamespace = new Namespace { Value = "http://example.com/namespace" };
            Namespace[] namespaces = [expectedNamespace];
            const string namespaceUri = "http://example.com/namespace";

            // Act
            Namespace result = namespaces.GetNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(expectedNamespace));
        }

        /// <summary>
        /// Tests that GetNamespace returns null when no matching namespace exists.
        /// </summary>
        [Test]
        public void GetNamespace_NoMatchingNamespace_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.com/namespace1" },
                new Namespace { Value = "http://example.com/namespace2" },
                new Namespace { Value = "http://example.com/namespace3" }
            ];
            const string namespaceUri = "http://example.com/nonexistent";

            // Act
            Namespace result = namespaces.GetNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespace returns the first matching namespace when multiple matches exist.
        /// </summary>
        [Test]
        public void GetNamespace_MultipleMatchingNamespaces_ReturnsFirstMatch()
        {
            // Arrange
            var firstMatch = new Namespace { Value = "http://example.com/namespace" };
            var secondMatch = new Namespace { Value = "http://example.com/namespace" };
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.com/other1" },
                firstMatch,
                secondMatch,
                new Namespace { Value = "http://example.com/other2" }
            ];
            const string namespaceUri = "http://example.com/namespace";

            // Act
            Namespace result = namespaces.GetNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(firstMatch));
        }

        /// <summary>
        /// Tests that GetNamespace correctly handles null namespace URI parameter.
        /// </summary>
        [Test]
        public void GetNamespace_NullNamespaceUri_ReturnsMatchingNamespaceWithNullValue()
        {
            // Arrange
            var namespaceWithNull = new Namespace { Value = null };
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.com/namespace" },
                namespaceWithNull
            ];
            const string namespaceUri = null;

            // Act
            Namespace result = namespaces.GetNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(namespaceWithNull));
        }

        /// <summary>
        /// Tests that GetNamespace returns null when searching for null in array without null values.
        /// </summary>
        [Test]
        public void GetNamespace_NullNamespaceUriWithNoNullValues_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.com/namespace1" },
                new Namespace { Value = "http://example.com/namespace2" }
            ];
            const string namespaceUri = null;

            // Act
            Namespace result = namespaces.GetNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespace correctly handles empty string namespace URI.
        /// </summary>
        [Test]
        public void GetNamespace_EmptyStringNamespaceUri_ReturnsMatchingNamespace()
        {
            // Arrange
            var emptyNamespace = new Namespace { Value = string.Empty };
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.com/namespace" },
                emptyNamespace
            ];
            string namespaceUri = string.Empty;

            // Act
            Namespace result = namespaces.GetNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(emptyNamespace));
        }

        /// <summary>
        /// Tests that GetNamespace correctly handles whitespace-only namespace URI.
        /// </summary>
        [Test]
        public void GetNamespace_WhitespaceNamespaceUri_ReturnsMatchingNamespace()
        {
            // Arrange
            var whitespaceNamespace = new Namespace { Value = "   " };
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.com/namespace" },
                whitespaceNamespace
            ];
            const string namespaceUri = "   ";

            // Act
            Namespace result = namespaces.GetNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(whitespaceNamespace));
        }

        /// <summary>
        /// Tests that GetNamespace is case-sensitive when comparing namespace URIs.
        /// </summary>
        [Test]
        public void GetNamespace_CaseSensitiveComparison_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.com/Namespace" }
            ];
            const string namespaceUri = "http://example.com/namespace";

            // Act
            Namespace result = namespaces.GetNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespace correctly handles namespace URIs with special characters.
        /// </summary>
        [Test]
        public void GetNamespace_SpecialCharactersInUri_ReturnsMatchingNamespace()
        {
            // Arrange
            const string specialUri = "http://example.com/namespace?param=value&other=123#fragment";
            var specialNamespace = new Namespace { Value = specialUri };
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.com/namespace" },
                specialNamespace
            ];

            // Act
            Namespace result = namespaces.GetNamespace(specialUri);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(specialNamespace));
        }

        /// <summary>
        /// Tests that GetNamespace correctly handles very long namespace URIs.
        /// </summary>
        [Test]
        public void GetNamespace_VeryLongNamespaceUri_ReturnsMatchingNamespace()
        {
            // Arrange
            string longUri = "http://example.com/namespace/" + new string('a', 10000);
            var longNamespace = new Namespace { Value = longUri };
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.com/short" },
                longNamespace
            ];

            // Act
            Namespace result = namespaces.GetNamespace(longUri);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(longNamespace));
        }

        /// <summary>
        /// Tests that GetNamespace correctly handles URIs with Unicode characters.
        /// </summary>
        [Test]
        public void GetNamespace_UnicodeCharactersInUri_ReturnsMatchingNamespace()
        {
            // Arrange
            const string unicodeUri = "http://example.com/名前空間/测试/тест";
            var unicodeNamespace = new Namespace { Value = unicodeUri };
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.com/namespace" },
                unicodeNamespace
            ];

            // Act
            Namespace result = namespaces.GetNamespace(unicodeUri);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(unicodeNamespace));
        }

        /// <summary>
        /// Tests that GetNamespace returns the correct namespace from a large array.
        /// </summary>
        [Test]
        public void GetNamespace_LargeNamespaceArray_ReturnsCorrectNamespace()
        {
            // Arrange
            var targetNamespace = new Namespace { Value = "http://example.com/target" };
            var namespaces = new Namespace[1000];
            for (int i = 0; i < 1000; i++)
            {
                if (i == 500)
                {
                    namespaces[i] = targetNamespace;
                }
                else
                {
                    namespaces[i] = new Namespace { Value = $"http://example.com/namespace{i}" };
                }
            }
            const string namespaceUri = "http://example.com/target";

            // Act
            Namespace result = namespaces.GetNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.SameAs(targetNamespace));
        }

        /// <summary>
        /// Tests IsDerivedDataType with null type parameter.
        /// </summary>
        [Test]
        public void IsDerivedDataType_NullType_ThrowsArgumentNullException()
        {
            // Arrange
            TypeDesign type = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => type.IsDerivedDataType());
        }

        /// <summary>
        /// Tests IsDerivedDataType when type is not a DataTypeDesign.
        /// Expected to return true as non-DataTypeDesign types are considered derived.
        /// </summary>
        [Test]
        public void IsDerivedDataType_TypeIsNotDataTypeDesign_ReturnsTrue()
        {
            // Arrange
            var type = new TypeDesign();

            // Act
            bool result = type.IsDerivedDataType();

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests IsDerivedDataType when type is DataTypeDesign but BaseTypeNode is null.
        /// Expected to return true as null BaseTypeNode means it's not a valid DataTypeDesign chain.
        /// </summary>
        [Test]
        public void IsDerivedDataType_DataTypeDesignWithNullBaseTypeNode_ReturnsTrue()
        {
            // Arrange
            var type = new DataTypeDesign
            {
                BaseTypeNode = null
            };

            // Act
            bool result = type.IsDerivedDataType();

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests IsDerivedDataType when type is DataTypeDesign but BaseTypeNode
        /// is not DataTypeDesign.
        /// Expected to return true as the base type chain is not a DataTypeDesign.
        /// </summary>
        [Test]
        public void IsDerivedDataType_DataTypeDesignWithNonDataTypeDesignBaseTypeNode_ReturnsTrue()
        {
            // Arrange
            var type = new DataTypeDesign
            {
                BaseTypeNode = new TypeDesign()
            };

            // Act
            bool result = type.IsDerivedDataType();

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests IsDerivedDataType when type is DataTypeDesign with DataTypeDesign
        /// BaseTypeNode having BasicDataType.Structure.
        /// Expected to return false as Structure type is not considered a derived data type.
        /// </summary>
        [Test]
        public void IsDerivedDataType_DataTypeDesignWithStructureBaseType_ReturnsFalse()
        {
            // Arrange
            var baseType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Structure
            };

            var type = new DataTypeDesign
            {
                BaseTypeNode = baseType
            };

            // Act
            bool result = type.IsDerivedDataType();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests IsDerivedDataType when type is DataTypeDesign with DataTypeDesign
        /// BaseTypeNode having non-Structure BasicDataType.
        /// Expected to return true for various BasicDataType enum values except Structure.
        /// </summary>
        /// <param name="basicDataType">The BasicDataType value to test.</param>
        [TestCase(BasicDataType.Boolean)]
        [TestCase(BasicDataType.SByte)]
        [TestCase(BasicDataType.Byte)]
        [TestCase(BasicDataType.Int16)]
        [TestCase(BasicDataType.UInt16)]
        [TestCase(BasicDataType.Int32)]
        [TestCase(BasicDataType.UInt32)]
        [TestCase(BasicDataType.Int64)]
        [TestCase(BasicDataType.UInt64)]
        [TestCase(BasicDataType.Float)]
        [TestCase(BasicDataType.Double)]
        [TestCase(BasicDataType.String)]
        [TestCase(BasicDataType.DateTime)]
        [TestCase(BasicDataType.Guid)]
        [TestCase(BasicDataType.ByteString)]
        [TestCase(BasicDataType.XmlElement)]
        [TestCase(BasicDataType.NodeId)]
        [TestCase(BasicDataType.ExpandedNodeId)]
        [TestCase(BasicDataType.StatusCode)]
        [TestCase(BasicDataType.DiagnosticInfo)]
        [TestCase(BasicDataType.QualifiedName)]
        [TestCase(BasicDataType.LocalizedText)]
        [TestCase(BasicDataType.DataValue)]
        [TestCase(BasicDataType.Number)]
        [TestCase(BasicDataType.Integer)]
        [TestCase(BasicDataType.UInteger)]
        [TestCase(BasicDataType.Enumeration)]
        [TestCase(BasicDataType.BaseDataType)]
        [TestCase(BasicDataType.UserDefined)]
        public void IsDerivedDataType_DataTypeDesignWithNonStructureBaseType_ReturnsTrue(
            BasicDataType basicDataType)
        {
            // Arrange
            var baseType = new DataTypeDesign
            {
                BasicDataType = basicDataType
            };

            var type = new DataTypeDesign
            {
                BaseTypeNode = baseType
            };

            // Act
            bool result = type.IsDerivedDataType();

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests IsDerivedDataType with an invalid BasicDataType value outside the
        /// defined enum range.
        /// Expected to return true as any non-Structure value should return true.
        /// </summary>
        [Test]
        public void IsDerivedDataType_DataTypeDesignWithInvalidBasicDataType_ReturnsTrue()
        {
            // Arrange
            var baseType = new DataTypeDesign
            {
                BasicDataType = (BasicDataType)9999
            };

            var type = new DataTypeDesign
            {
                BaseTypeNode = baseType
            };

            // Act
            bool result = type.IsDerivedDataType();

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that GetChildFieldName returns empty string when the parameter is null.
        /// </summary>
        [Test]
        public void GetChildFieldName_NullParameter_ReturnsEmptyString()
        {
            // Arrange
            Parameter field = null;

            // Act
            string result = field.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that GetChildFieldName returns empty string when the parameter's
        /// Name is null.
        /// </summary>
        [Test]
        public void GetChildFieldName_NullName_ReturnsEmptyString()
        {
            // Arrange
            var field = new Parameter { Name = null };

            // Act
            string result = field.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that GetChildFieldNamereturns empty string when
        /// the parameter's Name is empty.
        /// </summary>
        [Test]
        public void GetChildFieldName_EmptyName_ReturnsEmptyString()
        {
            // Arrange
            var field = new Parameter { Name = string.Empty };

            // Act
            string result = field.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that GetChildFieldName formats a single character name correctly by prefixing with "m_" and converting to lowercase.
        /// </summary>
        [TestCase("A", "m_a")]
        [TestCase("Z", "m_z")]
        [TestCase("a", "m_a")]
        [TestCase("z", "m_z")]
        [TestCase("0", "m_0")]
        [TestCase("_", "m__")]
        public void GetChildFieldName_SingleCharacterName_ReturnsCorrectFieldName(string name, string expected)
        {
            // Arrange
            var field = new Parameter { Name = name };

            // Act
            string result = field.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that GetChildFieldName formats names starting with uppercase correctly by lowercasing the first character.
        /// </summary>
        [TestCase("MyProperty", "m_myProperty")]
        [TestCase("Property", "m_property")]
        [TestCase("ABC", "m_aBC")]
        [TestCase("Value123", "m_value123")]
        public void GetChildFieldName_UppercaseStartName_ReturnsCorrectFieldName(string name, string expected)
        {
            // Arrange
            var field = new Parameter { Name = name };

            // Act
            string result = field.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that GetChildFieldName formats names starting with lowercase correctly by keeping the first character lowercase.
        /// </summary>
        [TestCase("myProperty", "m_myProperty")]
        [TestCase("property", "m_property")]
        [TestCase("value", "m_value")]
        public void GetChildFieldName_LowercaseStartName_ReturnsCorrectFieldName(string name, string expected)
        {
            // Arrange
            var field = new Parameter { Name = name };

            // Act
            string result = field.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that GetChildFieldName handles names with special characters correctly.
        /// </summary>
        [TestCase("_Property", "m__Property")]
        [TestCase("$Value", "m_$Value")]
        [TestCase("Name-With-Dash", "m_name-With-Dash")]
        [TestCase("Name.With.Dot", "m_name.With.Dot")]
        public void GetChildFieldName_SpecialCharacters_ReturnsCorrectFieldName(string name, string expected)
        {
            // Arrange
            var field = new Parameter { Name = name };

            // Act
            string result = field.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that GetChildFieldName handles names with numbers correctly.
        /// </summary>
        [TestCase("Property123", "m_property123")]
        [TestCase("Property1", "m_property1")]
        [TestCase("1Property", "m_1Property")]
        [TestCase("123", "m_123")]
        public void GetChildFieldName_NamesWithNumbers_ReturnsCorrectFieldName(string name, string expected)
        {
            // Arrange
            var field = new Parameter { Name = name };

            // Act
            string result = field.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that GetChildFieldName handles very long names correctly.
        /// </summary>
        [Test]
        public void GetChildFieldName_VeryLongName_ReturnsCorrectFieldName()
        {
            // Arrange
            string longName = new string('A', 1000) + "PropertyName";
            var field = new Parameter { Name = longName };
            string expected = $"m_a{longName[1..]}";

            // Act
            string result = field.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that GetChildFieldName handles two-character names correctly.
        /// </summary>
        [TestCase("AB", "m_aB")]
        [TestCase("Ab", "m_ab")]
        [TestCase("ab", "m_ab")]
        [TestCase("A1", "m_a1")]
        public void GetChildFieldName_TwoCharacterName_ReturnsCorrectFieldName(string name, string expected)
        {
            // Arrange
            var field = new Parameter { Name = name };

            // Act
            string result = field.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that GetChildFieldName handles names with Unicode characters correctly.
        /// </summary>
        [TestCase("Ñame", "m_ñame")]
        [TestCase("Über", "m_über")]
        [TestCase("Привет", "m_привет")]
        public void GetChildFieldName_UnicodeCharacters_ReturnsCorrectFieldName(string name, string expected)
        {
            // Arrange
            var field = new Parameter { Name = name };

            // Act
            string result = field.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that GetChildFieldName handles whitespace in names correctly.
        /// </summary>
        [TestCase("My Property", "m_myProperty")]
        [TestCase(" Property", "m_property")]
        [TestCase("Property ", "m_property")]
        public void GetChildFieldName_WhitespaceInName_ReturnsCorrectFieldName(string name, string expected)
        {
            // Arrange
            var field = new Parameter { Name = name };

            // Act
            string result = field.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that GetArrayDimensionsAsCode returns "null" when valueRank is not OneOrMoreDimensions.
        /// </summary>
        [TestCase(ValueRank.Scalar)]
        [TestCase(ValueRank.Array)]
        [TestCase(ValueRank.ScalarOrArray)]
        [TestCase(ValueRank.ScalarOrOneDimension)]
        [TestCase(ValueRank.Any)]
        public void GetArrayDimensionsAsCode_ValueRankNotOneOrMoreDimensions_ReturnsNull(ValueRank valueRank)
        {
            // Arrange
            const string arrayDimensions = "1,2,3";

            // Act
            string result = valueRank.GetArrayDimensionsAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("new uint[] { 1, 2, 3 }"));
        }

        /// <summary>
        /// Tests that GetArrayDimensionsAsCode handles empty string when valueRank is OneOrMoreDimensions.
        /// </summary>
        [Test]
        public void GetArrayDimensionsAsCode_ValueRankOneOrMoreDimensionsWithEmptyString_ReturnsFormattedArrayWithEmptyContent()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            string arrayDimensions = string.Empty;

            // Act
            string result = valueRank.GetArrayDimensionsAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetArrayDimensionsAsCode handles null string when valueRank is OneOrMoreDimensions.
        /// </summary>
        [Test]
        public void GetArrayDimensionsAsCode_ValueRankOneOrMoreDimensionsWithNull_ReturnsFormattedArrayWithNull()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            const string arrayDimensions = null;

            // Act
            string result = valueRank.GetArrayDimensionsAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetArrayDimensionsAsCode handles very long string when valueRank is OneOrMoreDimensions.
        /// </summary>
        [Test]
        public void GetArrayDimensionsAsCode_ValueRankOneOrMoreDimensionsWithVeryLongString_ReturnsFormattedArray()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;
            string arrayDimensions = new('1', 10000);

            // Act
            string result = valueRank.GetArrayDimensionsAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("new uint[] { 0 }"));
        }

        /// <summary>
        /// Tests that GetArrayDimensionsAsCode handles invalid enum value cast from int.
        /// </summary>
        [Test]
        public void GetArrayDimensionsAsCode_LargeValueRankValue_Array_dimension()
        {
            // Arrange
            const ValueRank valueRank = (ValueRank)999;
            const string arrayDimensions = "1,    2,    3";

            // Act
            string result = valueRank.GetArrayDimensionsAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo("new uint[] { 1, 2, 3 }"));
        }

        /// <summary>
        /// Tests that GetArrayDimensionsAsCode returns "null" even when arrayDimensions is null for non-OneOrMoreDimensions values.
        /// </summary>
        [Test]
        public void GetArrayDimensionsAsCode_ValueRankNotOneOrMoreDimensionsWithNullDimensions_ReturnsNull()
        {
            // Arrange
            const ValueRank valueRank = ValueRank.Scalar;
            const string arrayDimensions = null;

            // Act
            string result = valueRank.GetArrayDimensionsAsCode(arrayDimensions);

            // Assert
            Assert.That(result, Is.EqualTo(null));
        }

        /// <summary>
        /// Tests GetDotNetTypeName returns "Variant" when valueRank is not Scalar or Array.
        /// </summary>
        [TestCase(ValueRank.ScalarOrArray, TestName = "GetDotNetTypeName_ScalarOrArrayValueRank_ReturnsVariant")]
        [TestCase(ValueRank.OneOrMoreDimensions, TestName = "GetDotNetTypeName_OneOrMoreDimensionsValueRank_ReturnsVariant")]
        [TestCase(ValueRank.ScalarOrOneDimension, TestName = "GetDotNetTypeName_ScalarOrOneDimensionValueRank_ReturnsVariant")]
        [TestCase(ValueRank.Any, TestName = "GetDotNetTypeName_AnyValueRank_ReturnsVariant")]
        public void GetDotNetTypeName_NonScalarOrArrayValueRank_ReturnsVariant(ValueRank valueRank)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                valueRank,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.Variant"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName returns proper collection type names for Array valueRank with all basic data types.
        /// </summary>
        [TestCase(BasicDataType.Boolean, "global::Opc.Ua.BooleanCollection")]
        [TestCase(BasicDataType.SByte, "global::Opc.Ua.SByteCollection")]
        [TestCase(BasicDataType.Byte, "global::Opc.Ua.ByteCollection")]
        [TestCase(BasicDataType.Int16, "global::Opc.Ua.Int16Collection")]
        [TestCase(BasicDataType.UInt16, "global::Opc.Ua.UInt16Collection")]
        [TestCase(BasicDataType.Int32, "global::Opc.Ua.Int32Collection")]
        [TestCase(BasicDataType.UInt32, "global::Opc.Ua.UInt32Collection")]
        [TestCase(BasicDataType.Int64, "global::Opc.Ua.Int64Collection")]
        [TestCase(BasicDataType.UInt64, "global::Opc.Ua.UInt64Collection")]
        [TestCase(BasicDataType.Float, "global::Opc.Ua.FloatCollection")]
        [TestCase(BasicDataType.Double, "global::Opc.Ua.DoubleCollection")]
        [TestCase(BasicDataType.String, "global::Opc.Ua.StringCollection")]
        [TestCase(BasicDataType.DateTime, "global::Opc.Ua.DateTimeCollection")]
        [TestCase(BasicDataType.Guid, "global::Opc.Ua.UuidCollection")]
        [TestCase(BasicDataType.ByteString, "global::Opc.Ua.ByteStringCollection")]
        [TestCase(BasicDataType.XmlElement, "global::Opc.Ua.XmlElementCollection")]
        [TestCase(BasicDataType.NodeId, "global::Opc.Ua.NodeIdCollection")]
        [TestCase(BasicDataType.ExpandedNodeId, "global::Opc.Ua.ExpandedNodeIdCollection")]
        [TestCase(BasicDataType.StatusCode, "global::Opc.Ua.StatusCodeCollection")]
        [TestCase(BasicDataType.DiagnosticInfo, "global::Opc.Ua.DiagnosticInfoCollection")]
        [TestCase(BasicDataType.QualifiedName, "global::Opc.Ua.QualifiedNameCollection")]
        [TestCase(BasicDataType.LocalizedText, "global::Opc.Ua.LocalizedTextCollection")]
        [TestCase(BasicDataType.DataValue, "global::Opc.Ua.DataValueCollection")]
        [TestCase(BasicDataType.Number, "global::Opc.Ua.VariantCollection")]
        [TestCase(BasicDataType.Integer, "global::Opc.Ua.VariantCollection")]
        [TestCase(BasicDataType.UInteger, "global::Opc.Ua.VariantCollection")]
        [TestCase(BasicDataType.BaseDataType, "global::Opc.Ua.VariantCollection")]
        [TestCase(BasicDataType.Structure, "global::Opc.Ua.ExtensionObjectCollection")]
        public void GetDotNetTypeName_ArrayValueRankWithBasicDataTypes_ReturnsCorrectCollectionType(
            BasicDataType basicDataType,
            string expectedCollectionType)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = basicDataType
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Array,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo(expectedCollectionType));
        }

        /// <summary>
        /// Tests GetDotNetTypeName returns Int32Collection for built-in Enumeration type with Array valueRank.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ArrayValueRankWithBuiltInEnumeration_ReturnsInt32Collection()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration,
                SymbolicId = new XmlQualifiedName("Enumeration", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Array,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.Int32Collection"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName returns custom enumeration collection name for non-built-in Enumeration type with Array valueRank.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ArrayValueRankWithCustomEnumeration_ReturnsCustomEnumerationCollection()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration,
                SymbolicId = new XmlQualifiedName("MyCustomEnum", "http://custom.namespace"),
                SymbolicName = new XmlQualifiedName("MyCustomEnum", "http://custom.namespace"),
                IsOptionSet = false,
                BaseType = new XmlQualifiedName("Enumeration", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Array,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("MyCustomEnumCollection"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName for Array valueRank with OptionSet Enumeration calls recursively to base type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ArrayValueRankWithOptionSetEnumeration_ReturnsBaseTypeCollection()
        {
            // Arrange
            var mockBaseDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInt32
            };

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration,
                SymbolicId = new XmlQualifiedName("MyOptionSet", "http://custom.namespace"),
                IsOptionSet = true,
                BaseTypeNode = mockBaseDataType
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Array,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.UInt32Collection"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName for Array valueRank with Enumeration not derived from built-in calls recursively to base type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ArrayValueRankWithDerivedEnumeration_ReturnsBaseTypeCollection()
        {
            // Arrange
            var mockBaseDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration,
                SymbolicId = new XmlQualifiedName("DerivedEnum", "http://custom.namespace"),
                IsOptionSet = false,
                BaseType = new XmlQualifiedName("CustomBase", "http://custom.namespace"),
                BaseTypeNode = mockBaseDataType
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Array,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.Int32Collection"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName returns custom user-defined type collection for Array valueRank.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ArrayValueRankWithUserDefinedType_ReturnsCustomCollection()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("MyCustomType", "http://custom.namespace")
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Array,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("MyCustomTypeCollection"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName returns Variant for Scalar valueRank when base method returns "object".
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ScalarValueRankWithObjectType_ReturnsVariant()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.BaseDataType
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Scalar,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.Variant"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName returns Variant for Scalar valueRank when base method returns "object?".
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ScalarValueRankWithNullableObjectType_ReturnsVariant()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.BaseDataType
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Scalar,
                targetNamespace,
                namespaces,
                NullableAnnotation.Nullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.Variant"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName returns Bool type for Scalar valueRank with Boolean basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ScalarValueRankWithBoolean_ReturnsBoolType()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Scalar,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("bool"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName returns string type for Scalar valueRank with String basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ScalarValueRankWithString_ReturnsStringType()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Scalar,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("string"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName returns nullable string type for Scalar valueRank with Nullable annotation.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ScalarValueRankWithStringAndNullable_ReturnsNullableStringType()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Scalar,
                targetNamespace,
                namespaces,
                NullableAnnotation.Nullable);

            // Assert
            Assert.That(result, Is.EqualTo("string?"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName with null targetNamespace parameter.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_NullTargetNamespace_HandlesGracefully()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Array,
                null,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.BooleanCollection"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName with empty targetNamespace parameter.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_EmptyTargetNamespace_HandlesGracefully()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Array,
                string.Empty,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.Int32Collection"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName with null namespaces parameter.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_NullNamespaces_HandlesGracefully()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Double
            };
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Array,
                targetNamespace,
                null,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.DoubleCollection"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName with empty namespaces array.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_EmptyNamespacesArray_HandlesGracefully()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Float
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Array,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.FloatCollection"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName with NullableExceptDataTypes annotation for Array valueRank.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ArrayValueRankWithNullableExceptDataTypes_ReturnsCollectionType()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Array,
                targetNamespace,
                namespaces,
                NullableAnnotation.NullableExceptDataTypes);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.StringCollection"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName returns ExtensionObject for Scalar valueRank with Structure type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ScalarValueRankWithStructure_ReturnsExtensionObject()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Structure
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Scalar,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ExtensionObject"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName with undefined ValueRank enum value returns Variant.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UndefinedValueRank_ReturnsVariant()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";
            const ValueRank invalidValueRank = (ValueRank)999;

            // Act
            string result = mockDataType.GetDotNetTypeName(
                invalidValueRank,
                targetNamespace,
                namespaces,
                NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.Variant"));
        }

        /// <summary>
        /// Tests GetDotNetTypeName with default nullable parameter value.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_DefaultNullableParameter_UsesNonNullable()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.namespace";

            // Act
            string result = mockDataType.GetDotNetTypeName(
                ValueRank.Scalar,
                targetNamespace,
                namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("string"));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace returns null when namespaces array is null.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NullNamespacesArray_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces = null;
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace returns null when namespaces array is empty.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_EmptyNamespacesArray_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces = [];
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace returns null when namespaceUri is null.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NullNamespaceUri_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "TestName", Prefix = "TestPrefix", Value = "http://example.com/test" }
            ];
            const string namespaceUri = null;

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace returns null when namespaceUri is empty string.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_EmptyNamespaceUri_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "TestName", Prefix = "TestPrefix", Value = "http://example.com/test" }
            ];
            string namespaceUri = string.Empty;

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace returns null when namespaceUri contains only whitespace.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_WhitespaceNamespaceUri_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "TestName", Prefix = "TestPrefix", Value = "http://example.com/test" }
            ];
            const string namespaceUri = "   ";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace returns null when namespace is not found in array.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NamespaceNotFound_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "TestName", Prefix = "TestPrefix", Value = "http://example.com/test" }
            ];
            const string namespaceUri = "http://example.com/notfound";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace returns correct constant when namespace is found with XmlNamespace set.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NamespaceFoundWithXmlNamespace_ReturnsConstantWithXsdSuffix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = "TestName",
                    Prefix = "TestPrefix",
                    Value = "http://example.com/test",
                    XmlNamespace = "http://example.com/test/xml"
                }
            ];
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("TestPrefix.Namespaces.TestNameXsd"));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace returns correct constant when namespace is found without XmlNamespace.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NamespaceFoundWithoutXmlNamespace_ReturnsConstantWithoutXsdSuffix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = "TestName",
                    Prefix = "TestPrefix",
                    Value = "http://example.com/test"
                }
            ];
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("TestPrefix.Namespaces.TestName"));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace returns correct constant when namespace has empty XmlNamespace.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NamespaceFoundWithEmptyXmlNamespace_ReturnsConstantWithoutXsdSuffix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = "TestName",
                    Prefix = "TestPrefix",
                    Value = "http://example.com/test",
                    XmlNamespace = string.Empty
                }
            ];
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("TestPrefix.Namespaces.TestName"));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace returns correct constant when namespace has whitespace XmlNamespace.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NamespaceFoundWithWhitespaceXmlNamespace_ReturnsConstantWithXsdSuffix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = "TestName",
                    Prefix = "TestPrefix",
                    Value = "http://example.com/test",
                    XmlNamespace = "   "
                }
            ];
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("TestPrefix.Namespaces.TestNameXsd"));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace correctly formats when namespace has special characters in Name.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NamespaceWithSpecialCharactersInName_ReturnsFormattedConstant()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = "Test_Name-123",
                    Prefix = "TestPrefix",
                    Value = "http://example.com/test"
                }
            ];
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("TestPrefix.Namespaces.Test_Name-123"));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace correctly formats when namespace has special characters in Prefix.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NamespaceWithSpecialCharactersInPrefix_ReturnsFormattedConstant()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = "TestName",
                    Prefix = "Test_Prefix-123",
                    Value = "http://example.com/test"
                }
            ];
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("Test_Prefix-123.Namespaces.TestName"));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace finds correct namespace when multiple namespaces exist.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_MultipleNamespaces_ReturnsCorrectConstant()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = "FirstName",
                    Prefix = "FirstPrefix",
                    Value = "http://example.com/first"
                },
                new Namespace
                {
                    Name = "SecondName",
                    Prefix = "SecondPrefix",
                    Value = "http://example.com/second",
                    XmlNamespace = "http://example.com/second/xml"
                },
                new Namespace
                {
                    Name = "ThirdName",
                    Prefix = "ThirdPrefix",
                    Value = "http://example.com/third"
                }
            ];
            const string namespaceUri = "http://example.com/second";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("SecondPrefix.Namespaces.SecondNameXsd"));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace handles namespace with null Name property.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NamespaceWithNullName_ReturnsFormattedConstantWithNullName()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = null,
                    Prefix = "TestPrefix",
                    Value = "http://example.com/test"
                }
            ];
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("TestPrefix.Namespaces."));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace handles namespace with null Prefix property.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NamespaceWithNullPrefix_ReturnsFormattedConstantWithNullPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = "TestName",
                    Prefix = null,
                    Value = "http://example.com/test"
                }
            ];
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo(".Namespaces.TestName"));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace handles namespace with empty Name property.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NamespaceWithEmptyName_ReturnsFormattedConstantWithEmptyName()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = string.Empty,
                    Prefix = "TestPrefix",
                    Value = "http://example.com/test"
                }
            ];
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("TestPrefix.Namespaces."));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace handles namespace with empty Prefix property.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NamespaceWithEmptyPrefix_ReturnsFormattedConstantWithEmptyPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = "TestName",
                    Prefix = string.Empty,
                    Value = "http://example.com/test"
                }
            ];
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo(".Namespaces.TestName"));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace handles very long namespace URI strings.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_VeryLongNamespaceUri_HandlesCorrectly()
        {
            // Arrange
            string longUri = "http://example.com/" + new string('a', 10000);
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = "TestName",
                    Prefix = "TestPrefix",
                    Value = longUri
                }
            ];

            // Act
            string result = namespaces.GetConstantForXmlNamespace(longUri);

            // Assert
            Assert.That(result, Is.EqualTo("TestPrefix.Namespaces.TestName"));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace handles namespace URI with special characters.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_NamespaceUriWithSpecialCharacters_HandlesCorrectly()
        {
            // Arrange
            const string specialUri = "http://example.com/test?param=value&other=123#fragment";
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = "TestName",
                    Prefix = "TestPrefix",
                    Value = specialUri
                }
            ];

            // Act
            string result = namespaces.GetConstantForXmlNamespace(specialUri);

            // Assert
            Assert.That(result, Is.EqualTo("TestPrefix.Namespaces.TestName"));
        }

        /// <summary>
        /// Tests that GetConstantForXmlNamespace handles case-sensitive namespace URI matching.
        /// </summary>
        [Test]
        public void GetConstantForXmlNamespace_CaseSensitiveNamespaceUri_ReturnsNullWhenCaseDiffers()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace
                {
                    Name = "TestName",
                    Prefix = "TestPrefix",
                    Value = "http://example.com/Test"
                }
            ];
            const string namespaceUri = "http://example.com/test";

            // Act
            string result = namespaces.GetConstantForXmlNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns null when the namespaces array is null.
        /// Input: null namespaces array and any namespace URI.
        /// Expected: Returns null.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_NullNamespacesArray_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces = null;
            const string namespaceUri = "http://example.org/test";

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns null when the namespaces array is empty.
        /// Input: Empty namespaces array and any namespace URI.
        /// Expected: Returns null.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_EmptyNamespacesArray_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces = [];
            const string namespaceUri = "http://example.org/test";

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns the formatted prefix when a matching namespace is found.
        /// Input: Namespaces array with a matching namespace URI.
        /// Expected: Returns the formatted prefix of the matching namespace.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_MatchingNamespaceFound_ReturnsFormattedPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.org/test", Prefix = "TestPrefix" }
            ];
            const string namespaceUri = "http://example.org/test";

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("TestPrefix"));
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns null when no matching namespace is found.
        /// Input: Namespaces array without a matching namespace URI.
        /// Expected: Returns null.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_NoMatchingNamespace_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.org/other", Prefix = "OtherPrefix" }
            ];
            const string namespaceUri = "http://example.org/test";

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns the prefix of the first matching namespace when multiple namespaces exist.
        /// Input: Namespaces array with multiple entries, one of which matches the URI.
        /// Expected: Returns the formatted prefix of the matching namespace.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_MultipleNamespacesWithOneMatch_ReturnsMatchingPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.org/first", Prefix = "FirstPrefix" },
                new Namespace { Value = "http://example.org/test", Prefix = "TestPrefix" },
                new Namespace { Value = "http://example.org/third", Prefix = "ThirdPrefix" }
            ];
            const string namespaceUri = "http://example.org/test";

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("TestPrefix"));
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns the first matching prefix when multiple namespaces have the same URI.
        /// Input: Namespaces array with duplicate matching URIs.
        /// Expected: Returns the prefix of the first matching namespace.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_DuplicateMatchingNamespaces_ReturnsFirstMatch()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.org/test", Prefix = "FirstPrefix" },
                new Namespace { Value = "http://example.org/test", Prefix = "SecondPrefix" }
            ];
            const string namespaceUri = "http://example.org/test";

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("FirstPrefix"));
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns null when the namespace URI parameter is null.
        /// Input: Valid namespaces array and null namespace URI.
        /// Expected: Returns null.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_NullNamespaceUri_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.org/test", Prefix = "TestPrefix" }
            ];
            const string namespaceUri = null;

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns null when the namespace URI parameter is an empty string.
        /// Input: Valid namespaces array and empty string namespace URI.
        /// Expected: Returns null.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_EmptyNamespaceUri_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.org/test", Prefix = "TestPrefix" }
            ];
            string namespaceUri = string.Empty;

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns the prefix when matching an empty string namespace URI.
        /// Input: Namespaces array with an empty string Value and empty string namespace URI.
        /// Expected: Returns the formatted prefix.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_MatchingEmptyString_ReturnsPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = string.Empty, Prefix = "EmptyPrefix" }
            ];
            string namespaceUri = string.Empty;

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("EmptyPrefix"));
        }

        /// <summary>
        /// Tests that GetNamespacePrefix performs case-sensitive comparison.
        /// Input: Namespaces array with a URI in different case than the search URI.
        /// Expected: Returns null as the comparison is case-sensitive.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_CaseSensitiveComparison_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.org/TEST", Prefix = "TestPrefix" }
            ];
            const string namespaceUri = "http://example.org/test";

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns null when the Namespace object has a null Value property.
        /// Input: Namespaces array with a Namespace having null Value.
        /// Expected: Returns null.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_NamespaceWithNullValue_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = null, Prefix = "TestPrefix" }
            ];
            const string namespaceUri = "http://example.org/test";

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns the formatted prefix when both Value and search URI are null.
        /// Input: Namespaces array with a Namespace having null Value and null namespace URI.
        /// Expected: Returns the formatted prefix.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_BothNullValues_ReturnsPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = null, Prefix = "NullPrefix" }
            ];
            const string namespaceUri = null;

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("NullPrefix"));
        }

        /// <summary>
        /// Tests that GetNamespacePrefix handles whitespace-only namespace URIs.
        /// Input: Namespaces array with whitespace Value and whitespace namespace URI.
        /// Expected: Returns the formatted prefix.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_WhitespaceNamespaceUri_ReturnsPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "   ", Prefix = "WhitespacePrefix" }
            ];
            const string namespaceUri = "   ";

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("WhitespacePrefix"));
        }

        /// <summary>
        /// Tests that GetNamespacePrefix handles very long namespace URIs correctly.
        /// Input: Namespaces array with a very long URI and matching search URI.
        /// Expected: Returns the formatted prefix.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_VeryLongNamespaceUri_ReturnsPrefix()
        {
            // Arrange
            string longUri = new('a', 10000);
            Namespace[] namespaces =
            [
                new Namespace { Value = longUri, Prefix = "LongPrefix" }
            ];
            string namespaceUri = longUri;

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("LongPrefix"));
        }

        /// <summary>
        /// Tests that GetNamespacePrefix handles special characters in namespace URIs.
        /// Input: Namespaces array with special characters in Value and matching search URI.
        /// Expected: Returns the formatted prefix.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_SpecialCharactersInUri_ReturnsPrefix()
        {
            // Arrange
            const string specialUri = "http://example.org/test?param=value&other=123#fragment";
            Namespace[] namespaces =
            [
                new Namespace { Value = specialUri, Prefix = "SpecialPrefix" }
            ];

            // Act
            string result = namespaces.GetNamespacePrefix(specialUri);

            // Assert
            Assert.That(result, Is.EqualTo("SpecialPrefix"));
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns an empty string when the Prefix property is empty.
        /// Input: Namespaces array with matching URI but empty Prefix.
        /// Expected: Returns an empty string.
        /// </summary>
        [Test]
        public void GetNamespacePrefix_EmptyPrefix_ReturnsEmptyString()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.org/test", Prefix = string.Empty }
            ];
            const string namespaceUri = "http://example.org/test";

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that GetNamespacePrefix returns an empty string when the Prefix property is null.
        /// Input: Namespaces array with matching URI but null Prefix.
        /// Expected: Returns an empty string (formatted null becomes empty string).
        /// </summary>
        [Test]
        public void GetNamespacePrefix_NullPrefix_ReturnsEmptyString()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://example.org/test", Prefix = null }
            ];
            const string namespaceUri = "http://example.org/test";

            // Act
            string result = namespaces.GetNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests GetChildFieldName with a null instance.
        /// Expected: Returns empty string.
        /// </summary>
        [Test]
        public void GetChildFieldName_NullInstance_ReturnsEmptyString()
        {
            // Arrange
            InstanceDesign instance = null;

            // Act
            string result = instance.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests GetChildFieldName with a regular InstanceDesign having a simple name starting with uppercase.
        /// Expected: Returns field name formatted as "m_{lowercase}{rest}".
        /// </summary>
        [Test]
        public void GetChildFieldName_RegularInstanceWithSimpleName_ReturnsFormattedFieldName()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                SymbolicName = new XmlQualifiedName("Property", "http://test.com")
            };

            // Act
            string result = instance.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo("m_property"));
        }

        /// <summary>
        /// Tests GetChildFieldName with a MethodDesign.
        /// Expected: Returns field name with "Method" suffix.
        /// </summary>
        [Test]
        public void GetChildFieldName_MethodDesign_ReturnsFormattedFieldNameWithMethodSuffix()
        {
            // Arrange
            var instance = new MethodDesign
            {
                SymbolicName = new XmlQualifiedName("Execute", "http://test.com")
            };

            // Act
            string result = instance.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo("m_executeMethod"));
        }

        /// <summary>
        /// Tests GetChildFieldName with various name formats.
        /// Expected: Returns correctly formatted field names for each input.
        /// </summary>
        [TestCase("MyProperty", "m_myProperty", TestName = "CamelCase name")]
        [TestCase("Property", "m_property", TestName = "Single word uppercase")]
        [TestCase("ABC", "m_aBC", TestName = "All uppercase")]
        [TestCase("test", "m_test", TestName = "All lowercase")]
        [TestCase("A", "m_a", TestName = "Single character uppercase")]
        [TestCase("a", "m_a", TestName = "Single character lowercase")]
        [TestCase("AB", "m_aB", TestName = "Two character uppercase")]
        [TestCase("MyLongPropertyName", "m_myLongPropertyName", TestName = "Long name")]
        [TestCase("Property123", "m_property123", TestName = "Name with numbers")]
        [TestCase("Property_Name", "m_property_Name", TestName = "Name with underscore")]
        public void GetChildFieldName_VariousNames_ReturnsExpectedFieldName(string symbolicName, string expectedResult)
        {
            // Arrange
            var instance = new InstanceDesign
            {
                SymbolicName = new XmlQualifiedName(symbolicName, "http://test.com")
            };

            // Act
            string result = instance.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expectedResult));
        }

        /// <summary>
        /// Tests GetChildFieldName with an empty symbolic name.
        /// </summary>
        [Test]
        public void GetChildFieldName_EmptySymbolicName_ReturnsEmptyString()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                SymbolicName = new XmlQualifiedName(string.Empty, "http://test.com")
            };

            // Act
            string result = instance.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests GetChildFieldName with null SymbolicName.
        /// </summary>
        [Test]
        public void GetChildFieldName_NullSymbolicName_ReturnsEmptyString()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                SymbolicName = null
            };

            // Act
            string result = instance.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests GetChildFieldName with SymbolicName having null Name property.
        /// </summary>
        [Test]
        public void GetChildFieldName_SymbolicNameWithNullName_ReturnsEmptyString()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                SymbolicName = new XmlQualifiedName(null, "http://test.com")
            };

            // Act
            string result = instance.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests GetChildFieldName with names containing special characters.
        /// </summary>
        [TestCase("Property$Name", "m_property$Name", TestName = "Name with dollar sign")]
        [TestCase("Property@Name", "m_property@Name", TestName = "Name with at sign")]
        [TestCase("Property-Name", "m_property-Name", TestName = "Name with hyphen")]
        [TestCase("Property.Name", "m_property.Name", TestName = "Name with dot")]
        public void GetChildFieldName_NamesWithSpecialCharacters_ReturnsFormattedFieldName(
            string symbolicName,
            string expectedResult)
        {
            // Arrange
            var instance = new InstanceDesign
            {
                SymbolicName = new XmlQualifiedName(symbolicName, "http://test.com")
            };

            // Act
            string result = instance.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expectedResult));
        }

        /// <summary>
        /// Tests GetChildFieldName with names containing Unicode characters.
        /// </summary>
        [TestCase("Übung", "m_übung", TestName = "Name with German umlaut")]
        [TestCase("Café", "m_café", TestName = "Name with accented character")]
        [TestCase("日本", "m_日本", TestName = "Name with Japanese characters")]
        public void GetChildFieldName_NamesWithUnicodeCharacters_ReturnsFormattedFieldName(
            string symbolicName,
            string expectedResult)
        {
            // Arrange
            var instance = new InstanceDesign
            {
                SymbolicName = new XmlQualifiedName(symbolicName, "http://test.com")
            };

            // Act
            string result = instance.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expectedResult));
        }

        /// <summary>
        /// Tests GetChildFieldName with an extremely long name.
        /// Expected: Returns formatted field name without truncation.
        /// </summary>
        [Test]
        public void GetChildFieldName_VeryLongName_ReturnsFormattedFieldName()
        {
            // Arrange
            string longName = "A" + new string('B', 1000);
            string expected = "m_a" + new string('B', 1000);
            var instance = new InstanceDesign
            {
                SymbolicName = new XmlQualifiedName(longName, "http://test.com")
            };

            // Act
            string result = instance.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(result.Length, Is.EqualTo(1003));
        }

        /// <summary>
        /// Tests GetChildFieldName with whitespace-only name.
        /// Expected: Returns formatted field name with whitespace preserved.
        /// </summary>
        [Test]
        public void GetChildFieldName_WhitespaceOnlyName_ReturnsFormattedFieldName()
        {
            // Arrange
            var instance = new InstanceDesign
            {
                SymbolicName = new XmlQualifiedName("   ", "http://test.com")
            };

            // Act
            string result = instance.GetChildFieldName();

            // Assert
            Assert.That(result, Is.EqualTo("m_"));
        }

        /// <summary>
        /// Tests that GetMinimumSamplingIntervalString returns the correct constant string for Indeterminate value (-1).
        /// </summary>
        [Test]
        public void GetMinimumSamplingIntervalString_MinusOne_ReturnsIndeterminateConstant()
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                MinimumSamplingInterval = -1
            };

            // Act
            string result = variableType.GetMinimumSamplingIntervalAsCode();

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.MinimumSamplingIntervals.Indeterminate"));
        }

        /// <summary>
        /// Tests that GetMinimumSamplingIntervalString returns the correct constant string for Continuous value (0).
        /// </summary>
        [Test]
        public void GetMinimumSamplingIntervalString_Zero_ReturnsContinuousConstant()
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                MinimumSamplingInterval = 0
            };

            // Act
            string result = variableType.GetMinimumSamplingIntervalAsCode();

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.MinimumSamplingIntervals.Continuous"));
        }

        /// <summary>
        /// Tests that GetMinimumSamplingIntervalString returns the numeric string representation for positive values.
        /// Input: Positive integer value (1).
        /// Expected: String representation "1" using InvariantCulture.
        /// </summary>
        [Test]
        public void GetMinimumSamplingIntervalString_PositiveValue_ReturnsNumericString()
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                MinimumSamplingInterval = 1
            };

            // Act
            string result = variableType.GetMinimumSamplingIntervalAsCode();

            // Assert
            Assert.That(result, Is.EqualTo("1"));
        }

        /// <summary>
        /// Tests that GetMinimumSamplingIntervalString returns the numeric string representation for large positive values.
        /// Input: int.MaxValue.
        /// Expected: String representation using InvariantCulture.
        /// </summary>
        [Test]
        public void GetMinimumSamplingIntervalString_MaxValue_ReturnsNumericString()
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                MinimumSamplingInterval = int.MaxValue
            };

            // Act
            string result = variableType.GetMinimumSamplingIntervalAsCode();

            // Assert
            Assert.That(result, Is.EqualTo(int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Tests that GetMinimumSamplingIntervalString returns the numeric string representation for negative values other than -1.
        /// Input: -2 (negative but not -1).
        /// Expected: String representation "-2" using InvariantCulture.
        /// </summary>
        [Test]
        public void GetMinimumSamplingIntervalString_NegativeValueOtherThanMinusOne_ReturnsNumericString()
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                MinimumSamplingInterval = -2
            };

            // Act
            string result = variableType.GetMinimumSamplingIntervalAsCode();

            // Assert
            Assert.That(result, Is.EqualTo("-2"));
        }

        /// <summary>
        /// Tests that GetMinimumSamplingIntervalString returns the numeric string representation for int.MinValue.
        /// Input: int.MinValue.
        /// Expected: String representation using InvariantCulture.
        /// </summary>
        [Test]
        public void GetMinimumSamplingIntervalString_MinValue_ReturnsNumericString()
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                MinimumSamplingInterval = int.MinValue
            };

            // Act
            string result = variableType.GetMinimumSamplingIntervalAsCode();

            // Assert
            Assert.That(result, Is.EqualTo(int.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Tests that GetMinimumSamplingIntervalString uses InvariantCulture for formatting numeric values.
        /// Input: Various positive values.
        /// Expected: Consistent string representation regardless of current culture.
        /// </summary>
        [TestCase(100, "100")]
        [TestCase(1000, "1000")]
        [TestCase(500, "500")]
        [TestCase(999999, "999999")]
        public void GetMinimumSamplingIntervalString_VariousPositiveValues_ReturnsInvariantCultureString(int value, string expected)
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                MinimumSamplingInterval = value
            };

            // Act
            string result = variableType.GetMinimumSamplingIntervalAsCode();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that GetMinimumSamplingIntervalString uses InvariantCulture for formatting negative values.
        /// Input: Various negative values (excluding -1).
        /// Expected: Consistent string representation regardless of current culture.
        /// </summary>
        [TestCase(-100, "-100")]
        [TestCase(-1000, "-1000")]
        [TestCase(-500, "-500")]
        public void GetMinimumSamplingIntervalString_VariousNegativeValues_ReturnsInvariantCultureString(int value, string expected)
        {
            // Arrange
            var variableType = new VariableTypeDesign
            {
                MinimumSamplingInterval = value
            };

            // Act
            string result = variableType.GetMinimumSamplingIntervalAsCode();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for Boolean basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_BooleanBasicDataType_ReturnsBool()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("bool"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for SByte basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_SByteBasicDataType_ReturnsSByte()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.SByte
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("sbyte"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for Byte basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ByteBasicDataType_ReturnsByte()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Byte
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("byte"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for Int16 basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_Int16BasicDataType_ReturnsShort()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int16
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("short"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for UInt16 basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UInt16BasicDataType_ReturnsUShort()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInt16
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ushort"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for Int32 basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_Int32BasicDataType_ReturnsInt()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("int"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for UInt32 basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UInt32BasicDataType_ReturnsUInt()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInt32
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("uint"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for Int64 basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_Int64BasicDataType_ReturnsLong()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int64
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("long"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for UInt64 basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UInt64BasicDataType_ReturnsULong()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInt64
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ulong"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for Float basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_FloatBasicDataType_ReturnsFloat()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Float
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("float"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for Double basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_DoubleBasicDataType_ReturnsDouble()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Double
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("double"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns non-nullable string for String type with NonNullable annotation.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_StringWithNonNullable_ReturnsString()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("string"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns nullable string for String type with Nullable annotation.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_StringWithNullable_ReturnsNullableString()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.Nullable);

            // Assert
            Assert.That(result, Is.EqualTo("string?"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns nullable string for String type with NullableExceptDataTypes annotation.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_StringWithNullableExceptDataTypes_ReturnsNullableString()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.NullableExceptDataTypes);

            // Assert
            Assert.That(result, Is.EqualTo("string?"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for DateTime basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_DateTimeBasicDataType_ReturnsDateTime()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.DateTime
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::System.DateTime"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for Guid basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_GuidBasicDataType_ReturnsUuid()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Guid
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.Uuid"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns non-nullable byte array for ByteString type with NonNullable annotation.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ByteStringWithNonNullable_ReturnsByteArray()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.ByteString
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ByteString"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns nullable byte array for ByteString type with Nullable annotation.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ByteStringWithNullable_ReturnsNullableByteArray()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.ByteString
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.Nullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ByteString"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for XmlElement basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_XmlElementBasicDataType_ReturnsXmlElement()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.XmlElement
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.XmlElement"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for NodeId basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_NodeIdBasicDataType_ReturnsNodeId()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.NodeId
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.NodeId"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for ExpandedNodeId basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_ExpandedNodeIdBasicDataType_ReturnsExpandedNodeId()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.ExpandedNodeId
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ExpandedNodeId"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for StatusCode basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_StatusCodeBasicDataType_ReturnsStatusCode()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.StatusCode
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.StatusCode"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns non-nullable DiagnosticInfo for DiagnosticInfo type with NonNullable annotation.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_DiagnosticInfoWithNonNullable_ReturnsDiagnosticInfo()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.DiagnosticInfo
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.DiagnosticInfo"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns nullable DiagnosticInfo for DiagnosticInfo type with Nullable annotation.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_DiagnosticInfoWithNullable_ReturnsNullableDiagnosticInfo()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.DiagnosticInfo
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.Nullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.DiagnosticInfo?"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for QualifiedName basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_QualifiedNameBasicDataType_ReturnsQualifiedName()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.QualifiedName
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.QualifiedName"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for LocalizedText basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_LocalizedTextBasicDataType_ReturnsLocalizedText()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.LocalizedText
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.LocalizedText"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for DataValue basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_DataValueBasicDataType_ReturnsDataValue()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.DataValue
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.DataValue"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns correct .NET type name for Structure basic data type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_StructureBasicDataType_ReturnsExtensionObject()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Structure
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.ExtensionObject"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns int for OpcUa Enumeration symbolic ID.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_EnumerationWithOpcUaSymbolicId_ReturnsInt()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration,
                SymbolicId = new XmlQualifiedName("Enumeration", Namespaces.OpcUa)
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("int"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns symbolic name for regular Enumeration type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_RegularEnumeration_ReturnsSymbolicName()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration,
                SymbolicId = new XmlQualifiedName("CustomEnum", "http://custom.namespace"),
                SymbolicName = new XmlQualifiedName("CustomEnum", "http://custom.namespace"),
                IsOptionSet = false
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("CustomEnum"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName recursively processes OptionSet enumeration and returns base type.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_OptionSetEnumeration_ReturnsBaseTypeName()
        {
            // Arrange
            var baseType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInt32
            };
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Enumeration,
                SymbolicId = new XmlQualifiedName("OptionSetEnum", "http://custom.namespace"),
                SymbolicName = new XmlQualifiedName("OptionSetEnum", "http://custom.namespace"),
                IsOptionSet = true,
                BaseTypeNode = baseType
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("uint"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns prefixed type name for UserDefined type in different namespace.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UserDefinedInDifferentNamespace_ReturnsPrefixedTypeName()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicId = new XmlQualifiedName("CustomType", "http://other.namespace"),
                SymbolicName = new XmlQualifiedName("CustomType", "http://other.namespace"),
                IsEnumeration = false
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://other.namespace", Prefix = "Other" }
            ];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Other.CustomType"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns non-prefixed type name for UserDefined type in target namespace.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UserDefinedInTargetNamespace_ReturnsTypeName()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicId = new XmlQualifiedName("CustomType", "http://test.namespace"),
                SymbolicName = new XmlQualifiedName("CustomType", "http://test.namespace"),
                IsEnumeration = false
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("CustomType"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns non-nullable UserDefined enumeration type name.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UserDefinedEnumerationType_ReturnsNonNullableTypeName()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicId = new XmlQualifiedName("CustomEnum", "http://test.namespace"),
                SymbolicName = new XmlQualifiedName("CustomEnum", "http://test.namespace"),
                IsEnumeration = true
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.Nullable);

            // Assert
            Assert.That(result, Is.EqualTo("CustomEnum"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns nullable UserDefined type for Nullable annotation.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UserDefinedWithNullable_ReturnsNullableTypeName()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicId = new XmlQualifiedName("CustomType", "http://test.namespace"),
                SymbolicName = new XmlQualifiedName("CustomType", "http://test.namespace"),
                IsEnumeration = false
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.Nullable);

            // Assert
            Assert.That(result, Is.EqualTo("CustomType?"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns non-nullable UserDefined type for NullableExceptDataTypes annotation.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UserDefinedWithNullableExceptDataTypes_ReturnsNonNullableTypeName()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicId = new XmlQualifiedName("CustomType", "http://test.namespace"),
                SymbolicName = new XmlQualifiedName("CustomType", "http://test.namespace"),
                IsEnumeration = false
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.NullableExceptDataTypes);

            // Assert
            Assert.That(result, Is.EqualTo("CustomType"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns object for unhandled BasicDataType values with NonNullable annotation.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UnhandledBasicDataTypeWithNonNullable_ReturnsObject()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Number
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("object"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName returns nullable object for unhandled BasicDataType values with Nullable annotation.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UnhandledBasicDataTypeWithNullable_ReturnsNullableObject()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Integer
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.Nullable);

            // Assert
            Assert.That(result, Is.EqualTo("object?"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName handles empty namespace array for local types.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_EmptyNamespaceArray_HandlesCorrectly()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("bool"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName uses default parameter value when nullable parameter is omitted.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_OmittedNullableParameter_UsesDefaultNonNullable()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("string"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName handles BaseDataType enum value.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_BaseDataType_ReturnsObject()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.BaseDataType
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("object"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName handles UInteger enum value.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UInteger_ReturnsObject()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInteger
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces = [];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("object"));
        }

        /// <summary>
        /// Tests that GetDotNetTypeName handles prefixed UserDefined type with multiple namespaces.
        /// </summary>
        [Test]
        public void GetDotNetTypeName_UserDefinedWithMultipleNamespaces_FindsCorrectPrefix()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicId = new XmlQualifiedName("CustomType", "http://second.namespace"),
                SymbolicName = new XmlQualifiedName("CustomType", "http://second.namespace"),
                IsEnumeration = false
            };
            const string targetNamespace = "http://test.namespace";
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://first.namespace", Prefix = "First" },
                new Namespace { Value = "http://second.namespace", Prefix = "Second" },
                new Namespace { Value = "http://third.namespace", Prefix = "Third" }
            ];

            // Act
            string result = dataType.GetDotNetTypeName(targetNamespace, namespaces, NullableAnnotation.NonNullable);

            // Assert
            Assert.That(result, Is.EqualTo("global::Second.CustomType"));
        }

        /// <summary>
        /// Tests that IsPartOfOpcUaTypesLibrary returns false when the dataType parameter is null.
        /// </summary>
        [Test]
        public void IsPartOfOpcUaTypesLibrary_NullDataType_ReturnsFalse()
        {
            // Arrange
            DataTypeDesign dataType = null;

            // Act
            bool result = dataType.IsPartOfOpcUaTypesLibrary();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsPartOfOpcUaTypesLibrary returns false when the namespace is not OpcUa.
        /// </summary>
        [Test]
        public void IsPartOfOpcUaTypesLibrary_NonOpcUaNamespace_ReturnsFalse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://example.org/CustomNamespace/"),
                NumericId = DataTypes.Argument
            };

            // Act
            bool result = mockDataType.IsPartOfOpcUaTypesLibrary();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsPartOfOpcUaTypesLibrary returns false when the namespace is OpcUa but NumericId does not match any listed types.
        /// </summary>
        [Test]
        public void IsPartOfOpcUaTypesLibrary_OpcUaNamespaceWithUnlistedNumericId_ReturnsFalse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", Namespaces.OpcUa),
                NumericId = 999999u
            };

            // Act
            bool result = mockDataType.IsPartOfOpcUaTypesLibrary();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsPartOfOpcUaTypesLibrary returns true for AccessRestrictionType in OpcUa namespace.
        /// </summary>
        [Test]
        public void IsPartOfOpcUaTypesLibrary_AccessRestrictionType_ReturnsTrue()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                SymbolicId = new XmlQualifiedName("AccessRestrictionType", Namespaces.OpcUa),
                NumericId = DataTypes.AccessRestrictionType
            };

            // Act
            bool result = mockDataType.IsPartOfOpcUaTypesLibrary();

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsPartOfOpcUaTypesLibrary returns true for all supported OpcUa data types.
        /// </summary>
        [TestCase(DataTypes.AccessRestrictionType)]
        [TestCase(DataTypes.ReferenceDescription)]
        [TestCase(DataTypes.AttributeWriteMask)]
        [TestCase(DataTypes.Argument)]
        [TestCase(DataTypes.IdType)]
        [TestCase(DataTypes.RolePermissionType)]
        [TestCase(DataTypes.PermissionType)]
        [TestCase(DataTypes.ViewDescription)]
        [TestCase(DataTypes.BrowseDescription)]
        [TestCase(DataTypes.StructureDefinition)]
        [TestCase(DataTypes.StructureType)]
        [TestCase(DataTypes.StructureField)]
        [TestCase(DataTypes.InstanceNode)]
        [TestCase(DataTypes.ReferenceTypeNode)]
        [TestCase(DataTypes.ReferenceNode)]
        [TestCase(DataTypes.DataTypeDefinition)]
        [TestCase(DataTypes.EnumDefinition)]
        [TestCase(DataTypes.EnumField)]
        [TestCase(DataTypes.EnumValueType)]
        [TestCase(DataTypes.RelativePath)]
        [TestCase(DataTypes.BrowseDirection)]
        [TestCase(DataTypes.RelativePathElement)]
        [TestCase(DataTypes.NodeClass)]
        [TestCase(DataTypes.Node)]
        [TestCase(DataTypes.ViewNode)]
        [TestCase(DataTypes.ObjectNode)]
        [TestCase(DataTypes.MethodNode)]
        [TestCase(DataTypes.TypeNode)]
        [TestCase(DataTypes.ObjectTypeNode)]
        [TestCase(DataTypes.DataTypeNode)]
        [TestCase(DataTypes.VariableTypeNode)]
        [TestCase(DataTypes.VariableNode)]
        public void IsPartOfOpcUaTypesLibrary_AllSupportedOpcUaDataTypes_ReturnsTrue(uint numericId)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", Namespaces.OpcUa),
                NumericId = numericId
            };

            // Act
            bool result = mockDataType.IsPartOfOpcUaTypesLibrary();

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsPartOfOpcUaTypesLibrary returns false for unsupported OpcUa data types.
        /// </summary>
        [TestCase(DataTypes.Boolean)]
        [TestCase(DataTypes.String)]
        [TestCase(DataTypes.Int32)]
        [TestCase(DataTypes.UInt32)]
        [TestCase(DataTypes.DateTime)]
        [TestCase(DataTypes.BaseDataType)]
        [TestCase(DataTypes.Number)]
        [TestCase(DataTypes.Structure)]
        [TestCase(0u)]
        [TestCase(uint.MaxValue)]
        public void IsPartOfOpcUaTypesLibrary_UnsupportedOpcUaDataTypes_ReturnsFalse(uint numericId)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", Namespaces.OpcUa),
                NumericId = numericId
            };

            // Act
            bool result = mockDataType.IsPartOfOpcUaTypesLibrary();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsPartOfOpcUaTypesLibrary returns false when namespace is empty string.
        /// </summary>
        [Test]
        public void IsPartOfOpcUaTypesLibrary_EmptyNamespace_ReturnsFalse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", string.Empty),
                NumericId = DataTypes.Argument
            };

            // Act
            bool result = mockDataType.IsPartOfOpcUaTypesLibrary();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsPartOfOpcUaTypesLibrary returns false when namespace is null.
        /// </summary>
        [Test]
        public void IsPartOfOpcUaTypesLibrary_NullNamespace_ReturnsFalse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", null),
                NumericId = DataTypes.Argument
            };

            // Act
            bool result = mockDataType.IsPartOfOpcUaTypesLibrary();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsPartOfOpcUaTypesLibrary handles case-sensitive namespace comparison correctly.
        /// </summary>
        [Test]
        public void IsPartOfOpcUaTypesLibrary_CaseVariationInNamespace_ReturnsFalse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "HTTP://OPCFOUNDATION.ORG/UA/"),
                NumericId = DataTypes.Argument
            };

            // Act
            bool result = mockDataType.IsPartOfOpcUaTypesLibrary();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsPartOfOpcUaTypesLibrary handles namespace with trailing slash correctly.
        /// </summary>
        [Test]
        public void IsPartOfOpcUaTypesLibrary_NamespaceWithoutTrailingSlash_ReturnsFalse()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                SymbolicId = new XmlQualifiedName("TestType", "http://opcfoundation.org/UA"),
                NumericId = DataTypes.Argument
            };

            // Act
            bool result = mockDataType.IsPartOfOpcUaTypesLibrary();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsOverriddenWithSameClass throws ArgumentNullException when
        /// instance is null.
        /// </summary>
        [Test]
        public void IsOverriddenWithSameClass_NullInstance_ThrowsArgumentNullException()
        {
            // Arrange
            InstanceDesign instance = null;
            const string targetNamespace = "http://test.org/UA/";
            Namespace[] namespaces = [];

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                instance.IsOverriddenWithSameClass(targetNamespace, namespaces));
        }

        /// <summary>
        /// Tests that IsOverriddenWithSameClass returns false when instance is not overridden.
        /// Input: InstanceDesign with OveriddenNode set to null.
        /// Expected: Returns false without evaluating class names.
        /// </summary>
        [Test]
        public void IsOverriddenWithSameClass_NotOverridden_ReturnsFalse()
        {
            // Arrange
            var mockInstance = new InstanceDesign
            {
                OveriddenNode = null,
                ModellingRule = ModellingRule.Mandatory
            };
            const string targetNamespace = "http://test.org/UA/";
            Namespace[] namespaces = [];

            // Act
            bool result = mockInstance.IsOverriddenWithSameClass(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsOverriddenWithSameClass returns false when ModellingRule is None.
        /// Input: InstanceDesign with non-null OveriddenNode but ModellingRule set to None.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void IsOverriddenWithSameClass_ModellingRuleNone_ReturnsFalse()
        {
            // Arrange
            var mockInstance = new InstanceDesign
            {
                OveriddenNode = new InstanceDesign(),
                ModellingRule = ModellingRule.None
            };
            const string targetNamespace = "http://test.org/UA/";
            Namespace[] namespaces = [];

            // Act
            bool result = mockInstance.IsOverriddenWithSameClass(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsOverriddenWithSameClass returns false when ModellingRule is ExposesItsArray.
        /// Input: InstanceDesign with non-null OveriddenNode but ModellingRule set to ExposesItsArray.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void IsOverriddenWithSameClass_ModellingRuleExposesItsArray_ReturnsFalse()
        {
            // Arrange
            var mockInstance = new InstanceDesign
            {
                OveriddenNode = new InstanceDesign(),
                ModellingRule = ModellingRule.ExposesItsArray
            };
            const string targetNamespace = "http://test.org/UA/";
            Namespace[] namespaces = [];

            // Act
            bool result = mockInstance.IsOverriddenWithSameClass(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsOverriddenWithSameClass returns false when ModellingRule is MandatoryPlaceholder.
        /// Input: InstanceDesign with non-null OveriddenNode but ModellingRule set to MandatoryPlaceholder.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void IsOverriddenWithSameClass_ModellingRuleMandatoryPlaceholder_ReturnsFalse()
        {
            // Arrange
            var mockInstance = new InstanceDesign
            {
                OveriddenNode = new InstanceDesign(),
                ModellingRule = ModellingRule.MandatoryPlaceholder
            };
            const string targetNamespace = "http://test.org/UA/";
            Namespace[] namespaces = [];

            // Act
            bool result = mockInstance.IsOverriddenWithSameClass(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsOverriddenWithSameClass returns false when ModellingRule is OptionalPlaceholder.
        /// Input: InstanceDesign with non-null OveriddenNode but ModellingRule set to OptionalPlaceholder.
        /// Expected: Returns false.
        /// </summary>
        [Test]
        public void IsOverriddenWithSameClass_ModellingRuleOptionalPlaceholder_ReturnsFalse()
        {
            // Arrange
            var mockInstance = new InstanceDesign
            {
                OveriddenNode = new InstanceDesign(),
                ModellingRule = ModellingRule.OptionalPlaceholder
            };
            const string targetNamespace = "http://test.org/UA/";
            Namespace[] namespaces = [];

            // Act
            bool result = mockInstance.IsOverriddenWithSameClass(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsOverriddenWithSameClass returns true when both class names start with BaseDataVariableState with different generic parameters.
        /// Input: Merged instance class name is "BaseDataVariableState&lt;int&gt;", overridden node class name is "BaseDataVariableState&lt;string&gt;".
        /// Expected: Returns true because both start with "BaseDataVariableState&lt;".
        /// </summary>
        [TestCase("BaseDataVariableState<int>", "BaseDataVariableState<string>", true)]
        [TestCase("BaseDataVariableState<double>", "BaseDataVariableState<bool>", true)]
        [TestCase("BaseDataVariableState<CustomType>", "BaseDataVariableState<AnotherType>", true)]
        public void IsOverriddenWithSameClass_BothBaseDataVariableState_ReturnsTrue(
            string mergedClassName, string overriddenClassName, bool expected)
        {
            // Arrange
            var mockOverriddenNode = new InstanceDesign
            {
                TypeDefinitionNode = new ObjectTypeDesign
                {
                    ClassName = overriddenClassName
                }
            };
            var mockInstance = new InstanceDesign
            {
                ModellingRule = ModellingRule.Mandatory,
                OveriddenNode = mockOverriddenNode,
                TypeDefinitionNode = new ObjectTypeDesign
                {
                    ClassName = mergedClassName
                }
            };
            const string targetNamespace = "http://test.org/UA/";
            Namespace[] namespaces = [];

            // Act
            bool result = mockInstance.IsOverriddenWithSameClass(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that IsOverriddenWithSameClass returns false when only one class name starts with BaseDataVariableState.
        /// Input: Merged instance class name is "BaseDataVariableState&lt;int&gt;", overridden node class name is "PropertyState".
        /// Expected: Returns false because they are not equal and only one starts with "BaseDataVariableState&lt;".
        /// </summary>
        [TestCase("BaseDataVariableState<int>", "PropertyState", false)]
        [TestCase("PropertyState", "BaseDataVariableState<int>", false)]
        [TestCase("BaseDataVariableState<int>", "DataItemState", false)]
        public void IsOverriddenWithSameClass_OnlyOneBaseDataVariableState_ReturnsFalse(
            string mergedClassName, string overriddenClassName, bool expected)
        {
            // Arrange
            var mockOverriddenNode = new InstanceDesign
            {
                TypeDefinitionNode = new ObjectTypeDesign
                {
                    ClassName = overriddenClassName
                }
            };
            var mockInstance = new InstanceDesign
            {
                ModellingRule = ModellingRule.Mandatory,
                OveriddenNode = mockOverriddenNode,
                TypeDefinitionNode = new ObjectTypeDesign
                {
                    ClassName = mergedClassName
                }
            };

            const string targetNamespace = "http://test.org/UA/";
            Namespace[] namespaces = [];

            // Act
            bool result = mockInstance.IsOverriddenWithSameClass(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that IsOverriddenWithSameClass returns true when both class names are identical.
        /// Input: Both merged and overridden class names are "PropertyState".
        /// Expected: Returns true.
        /// </summary>
        [TestCase("PropertyState", "PropertyState", true)]
        [TestCase("DataItemState", "DataItemState", true)]
        [TestCase("AnalogItemState", "AnalogItemState", true)]
        public void IsOverriddenWithSameClass_SameClassName_ReturnsTrue(
            string mergedClassName, string overriddenClassName, bool expected)
        {
            // Arrange
            var mockOverriddenNode = new InstanceDesign
            {
                TypeDefinitionNode = new ObjectTypeDesign
                {
                    ClassName = overriddenClassName
                }
            };
            var mockInstance = new InstanceDesign
            {
                ModellingRule = ModellingRule.Mandatory,
                OveriddenNode = mockOverriddenNode,
                TypeDefinitionNode = new ObjectTypeDesign
                {
                    ClassName = mergedClassName
                }
            };

            const string targetNamespace = "http://test.org/UA/";
            Namespace[] namespaces = [];

            // Act
            bool result = mockInstance.IsOverriddenWithSameClass(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that IsOverriddenWithSameClass returns false when class names are different.
        /// Input: Merged class name is "PropertyState", overridden class name is "DataItemState".
        /// Expected: Returns false.
        /// </summary>
        [TestCase("PropertyState", "DataItemState", false)]
        [TestCase("DataItemState", "AnalogItemState", false)]
        [TestCase("MethodState", "PropertyState", false)]
        public void IsOverriddenWithSameClass_DifferentClassName_ReturnsFalse(
            string mergedClassName, string overriddenClassName, bool expected)
        {
            // Arrange
            var mockOverriddenNode = new InstanceDesign
            {
                TypeDefinitionNode = new ObjectTypeDesign
                {
                    ClassName = overriddenClassName
                }
            };
            var mockInstance = new InstanceDesign
            {
                ModellingRule = ModellingRule.Mandatory,
                OveriddenNode = mockOverriddenNode,
                TypeDefinitionNode = new ObjectTypeDesign
                {
                    ClassName = mergedClassName
                }
            };

            const string targetNamespace = "http://test.org/UA/";
            Namespace[] namespaces = [];

            // Act
            bool result = mockInstance.IsOverriddenWithSameClass(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that IsOverriddenWithSameClass is case-sensitive when comparing class names.
        /// Input: Class names differ only in casing.
        /// Expected: Returns false because string equality comparison is case-sensitive.
        /// </summary>
        [TestCase("PropertyState", "propertystate", false)]
        [TestCase("PROPERTYSTATE", "PropertyState", false)]
        [TestCase("DataItemState", "dataitemstate", false)]
        public void IsOverriddenWithSameClass_CaseSensitiveComparison_ReturnsFalse(
            string mergedClassName, string overriddenClassName, bool expected)
        {
            // Arrange
            var mockOverriddenNode = new InstanceDesign
            {
                TypeDefinitionNode = new ObjectTypeDesign
                {
                    ClassName = overriddenClassName
                }
            };
            var mockInstance = new InstanceDesign
            {
                ModellingRule = ModellingRule.Mandatory,
                OveriddenNode = mockOverriddenNode,
                TypeDefinitionNode = new ObjectTypeDesign
                {
                    ClassName = mergedClassName
                }
            };

            const string targetNamespace = "http://test.org/UA/";
            Namespace[] namespaces = [];

            // Act
            bool result = mockInstance.IsOverriddenWithSameClass(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests IsOverriddenWithSameClass with empty class names.
        /// Input: Both class names are empty strings.
        /// Expected: Returns true because empty strings are equal.
        /// </summary>
        [Test]
        public void IsOverriddenWithSameClass_EmptyClassNames_ReturnsTrue()
        {
            // Arrange
            var typeNode = new ObjectTypeDesign
            {
                ClassName = "TypeName"
            };
            var mockOverriddenNode = new InstanceDesign
            {
                TypeDefinitionNode = typeNode
            };
            var mockInstance = new InstanceDesign
            {
                ModellingRule = ModellingRule.Mandatory,
                OveriddenNode = mockOverriddenNode,
                TypeDefinitionNode = typeNode
            };

            const string targetNamespace = "http://test.org/UA/";
            Namespace[] namespaces = [];

            // Act
            bool result = mockInstance.IsOverriddenWithSameClass(targetNamespace, namespaces);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests IsOverriddenWithSameClass with whitespace-only target namespace.
        /// Input: targetNamespace contains only whitespace.
        /// Expected: Method executes without error.
        /// </summary>
        [TestCase("   ")]
        [TestCase("\t")]
        [TestCase("\n")]
        [TestCase("")]
        public void IsOverriddenWithSameClass_WhitespaceTargetNamespace_ExecutesWithoutError(string targetNamespace)
        {
            // Arrange
            var mockOverriddenNode = new InstanceDesign
            {
                TypeDefinitionNode = new ObjectTypeDesign
                {
                    ClassName = "TypeState"
                }
            };
            var mockInstance = new InstanceDesign
            {
                ModellingRule = ModellingRule.Mandatory,
                OveriddenNode = mockOverriddenNode,
                TypeDefinitionNode = new ObjectTypeDesign
                {
                    ClassName = "TypeState"
                }
            };

            Namespace[] namespaces = [];

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                bool result = mockInstance.IsOverriddenWithSameClass(targetNamespace, namespaces);
            });
        }

        /// <summary>
        /// Tests that IsDotNetEqualityComparable returns true for value types.
        /// Tests all basic data types that are considered value types when scalar.
        /// The method should return true for any value type regardless of its specific type.
        /// </summary>
        [TestCase(BasicDataType.Boolean)]
        [TestCase(BasicDataType.SByte)]
        [TestCase(BasicDataType.Byte)]
        [TestCase(BasicDataType.Int16)]
        [TestCase(BasicDataType.UInt16)]
        [TestCase(BasicDataType.Int32)]
        [TestCase(BasicDataType.UInt32)]
        [TestCase(BasicDataType.Int64)]
        [TestCase(BasicDataType.UInt64)]
        [TestCase(BasicDataType.Float)]
        [TestCase(BasicDataType.Double)]
        [TestCase(BasicDataType.DateTime)]
        [TestCase(BasicDataType.Guid)]
        [TestCase(BasicDataType.NodeId)]
        [TestCase(BasicDataType.ExpandedNodeId)]
        [TestCase(BasicDataType.QualifiedName)]
        [TestCase(BasicDataType.LocalizedText)]
        [TestCase(BasicDataType.StatusCode)]
        [TestCase(BasicDataType.Structure)]
        [TestCase(BasicDataType.BaseDataType)]
        public void IsDotNetEqualityComparable_ScalarValueType_ReturnsTrue(BasicDataType basicDataType)
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = basicDataType };
            const ValueRank valueRank = ValueRank.Scalar;

            // Act
            bool result = dataType.IsDotNetEqualityComparable(valueRank);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsDotNetEqualityComparable returns true for scalar strings.
        /// Strings are reference types but support equality comparison in .NET.
        /// The method should return true for scalar String type.
        /// </summary>
        [Test]
        public void IsDotNetEqualityComparable_ScalarString_ReturnsTrue()
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = BasicDataType.String };
            const ValueRank valueRank = ValueRank.Scalar;

            // Act
            bool result = dataType.IsDotNetEqualityComparable(valueRank);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsDotNetEqualityComparable returns false for scalar reference types that are not strings.
        /// Tests reference types like ByteString, XmlElement, DiagnosticInfo, DataValue, etc.
        /// These types do not support simple equality comparison.
        /// </summary>
        [TestCase(BasicDataType.ByteString)]
        [TestCase(BasicDataType.XmlElement)]
        [TestCase(BasicDataType.DiagnosticInfo)]
        [TestCase(BasicDataType.DataValue)]
        [TestCase(BasicDataType.Number)]
        [TestCase(BasicDataType.Integer)]
        [TestCase(BasicDataType.UInteger)]
        [TestCase(BasicDataType.Enumeration)]
        [TestCase(BasicDataType.UserDefined)]
        public void IsDotNetEqualityComparable_ScalarReferenceTypeNotString_ReturnsFalse(BasicDataType basicDataType)
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = basicDataType };
            const ValueRank valueRank = ValueRank.Scalar;

            // Act
            bool result = dataType.IsDotNetEqualityComparable(valueRank);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsDotNetEqualityComparable returns false for non-scalar value ranks.
        /// Tests all non-scalar value rank enumerations including Array, ScalarOrArray, etc.
        /// Arrays and complex value ranks are not equality comparable even if the base type is.
        /// </summary>
        [TestCase(ValueRank.Array)]
        [TestCase(ValueRank.ScalarOrArray)]
        [TestCase(ValueRank.OneOrMoreDimensions)]
        [TestCase(ValueRank.ScalarOrOneDimension)]
        [TestCase(ValueRank.Any)]
        public void IsDotNetEqualityComparable_NonScalarValueType_ReturnsFalse(ValueRank valueRank)
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = BasicDataType.Int32 };

            // Act
            bool result = dataType.IsDotNetEqualityComparable(valueRank);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsDotNetEqualityComparable returns false for non-scalar strings.
        /// Even though scalar strings are equality comparable, string arrays are not.
        /// The method should return false for any non-scalar string type.
        /// </summary>
        [TestCase(ValueRank.Array)]
        [TestCase(ValueRank.ScalarOrArray)]
        [TestCase(ValueRank.OneOrMoreDimensions)]
        [TestCase(ValueRank.ScalarOrOneDimension)]
        [TestCase(ValueRank.Any)]
        public void IsDotNetEqualityComparable_NonScalarString_ReturnsFalse(ValueRank valueRank)
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = BasicDataType.String };

            // Act
            bool result = dataType.IsDotNetEqualityComparable(valueRank);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsDotNetEqualityComparable returns false for non-scalar reference types.
        /// Tests that arrays of reference types are not equality comparable.
        /// </summary>
        [TestCase(ValueRank.Array)]
        [TestCase(ValueRank.ScalarOrArray)]
        [TestCase(ValueRank.OneOrMoreDimensions)]
        [TestCase(ValueRank.ScalarOrOneDimension)]
        [TestCase(ValueRank.Any)]
        public void IsDotNetEqualityComparable_NonScalarReferenceType_ReturnsFalse(ValueRank valueRank)
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = BasicDataType.ByteString };

            // Act
            bool result = dataType.IsDotNetEqualityComparable(valueRank);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsDotNetEqualityComparable handles undefined enum values correctly.
        /// Tests casting BasicDataType to an invalid value to ensure proper handling.
        /// The method should return false for undefined enum values.
        /// </summary>
        [Test]
        public void IsDotNetEqualityComparable_UndefinedBasicDataType_ReturnsFalse()
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = (BasicDataType)9999 };
            const ValueRank valueRank = ValueRank.Scalar;

            // Act
            bool result = dataType.IsDotNetEqualityComparable(valueRank);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsDotNetEqualityComparable handles undefined ValueRank enum values correctly.
        /// Tests casting ValueRank to an invalid value to ensure proper handling.
        /// The method should return false for undefined ValueRank values with value types.
        /// </summary>
        [Test]
        public void IsDotNetEqualityComparable_UndefinedValueRank_ReturnsFalse()
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = BasicDataType.Int32 };
            const ValueRank valueRank = (ValueRank)9999;

            // Act
            bool result = dataType.IsDotNetEqualityComparable(valueRank);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Test IsRequiredParameterInTemplates returns true when BasicDataType is not BaseDataType, Number, UInteger, or Integer
        /// and ValueRank is not OneOrMoreDimensions, ScalarOrOneDimension, or ScalarOrArray.
        /// Tests specific cases where both conditions are met.
        /// </summary>
        [TestCase(BasicDataType.Boolean, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.SByte, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.Byte, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.Int16, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.UInt16, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.Int32, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.UInt32, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.Int64, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.UInt64, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.Float, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.Double, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.String, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.DateTime, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.Guid, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.ByteString, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.XmlElement, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.NodeId, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.ExpandedNodeId, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.StatusCode, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.DiagnosticInfo, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.QualifiedName, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.LocalizedText, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.DataValue, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.Enumeration, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.Structure, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.UserDefined, ValueRank.Scalar, true)]
        [TestCase(BasicDataType.Boolean, ValueRank.Any, true)]
        [TestCase(BasicDataType.String, ValueRank.Any, true)]
        public void IsRequiredParameterInTemplates_NonExcludedBasicDataTypeAndNonExcludedValueRank_ReturnsTrue(
            BasicDataType basicDataType,
            ValueRank valueRank,
            bool expected)
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = basicDataType };

            // Act
            bool result = dataType.IsTemplateParameterRequired(valueRank);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Test IsRequiredParameterInTemplates returns false when BasicDataType is BaseDataType, Number, UInteger, or Integer
        /// and ValueRank is not Array.
        /// Tests specific excluded BasicDataType values with various ValueRank values.
        /// </summary>
        [TestCase(BasicDataType.BaseDataType, ValueRank.Scalar, false)]
        [TestCase(BasicDataType.BaseDataType, ValueRank.OneOrMoreDimensions, false)]
        [TestCase(BasicDataType.BaseDataType, ValueRank.ScalarOrOneDimension, false)]
        [TestCase(BasicDataType.BaseDataType, ValueRank.ScalarOrArray, false)]
        [TestCase(BasicDataType.BaseDataType, ValueRank.Any, false)]
        [TestCase(BasicDataType.Number, ValueRank.Scalar, false)]
        [TestCase(BasicDataType.Number, ValueRank.OneOrMoreDimensions, false)]
        [TestCase(BasicDataType.Number, ValueRank.ScalarOrOneDimension, false)]
        [TestCase(BasicDataType.Number, ValueRank.ScalarOrArray, false)]
        [TestCase(BasicDataType.Number, ValueRank.Any, false)]
        [TestCase(BasicDataType.UInteger, ValueRank.Scalar, false)]
        [TestCase(BasicDataType.UInteger, ValueRank.OneOrMoreDimensions, false)]
        [TestCase(BasicDataType.UInteger, ValueRank.ScalarOrOneDimension, false)]
        [TestCase(BasicDataType.UInteger, ValueRank.ScalarOrArray, false)]
        [TestCase(BasicDataType.UInteger, ValueRank.Any, false)]
        [TestCase(BasicDataType.Integer, ValueRank.Scalar, false)]
        [TestCase(BasicDataType.Integer, ValueRank.OneOrMoreDimensions, false)]
        [TestCase(BasicDataType.Integer, ValueRank.ScalarOrOneDimension, false)]
        [TestCase(BasicDataType.Integer, ValueRank.ScalarOrArray, false)]
        [TestCase(BasicDataType.Integer, ValueRank.Any, false)]
        public void IsRequiredParameterInTemplates_ExcludedBasicDataType_ReturnsFalse(
            BasicDataType basicDataType,
            ValueRank valueRank,
            bool expected)
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = basicDataType };

            // Act
            bool result = dataType.IsTemplateParameterRequired(valueRank);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Test IsRequiredParameterInTemplates returns false when ValueRank is OneOrMoreDimensions, ScalarOrOneDimension, or ScalarOrArray
        /// regardless of BasicDataType value (except excluded BasicDataTypes).
        /// Tests specific excluded ValueRank values with various BasicDataType values.
        /// </summary>
        [TestCase(BasicDataType.Boolean, ValueRank.OneOrMoreDimensions, false)]
        [TestCase(BasicDataType.Boolean, ValueRank.ScalarOrOneDimension, false)]
        [TestCase(BasicDataType.Boolean, ValueRank.ScalarOrArray, false)]
        [TestCase(BasicDataType.String, ValueRank.OneOrMoreDimensions, false)]
        [TestCase(BasicDataType.String, ValueRank.ScalarOrOneDimension, false)]
        [TestCase(BasicDataType.String, ValueRank.ScalarOrArray, false)]
        [TestCase(BasicDataType.Int32, ValueRank.OneOrMoreDimensions, false)]
        [TestCase(BasicDataType.Int32, ValueRank.ScalarOrOneDimension, false)]
        [TestCase(BasicDataType.Int32, ValueRank.ScalarOrArray, false)]
        [TestCase(BasicDataType.Structure, ValueRank.OneOrMoreDimensions, false)]
        [TestCase(BasicDataType.Structure, ValueRank.ScalarOrOneDimension, false)]
        [TestCase(BasicDataType.Structure, ValueRank.ScalarOrArray, false)]
        public void IsRequiredParameterInTemplates_ExcludedValueRank_ReturnsFalse(
            BasicDataType basicDataType,
            ValueRank valueRank,
            bool expected)
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = basicDataType };

            // Act
            bool result = dataType.IsTemplateParameterRequired(valueRank);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Test IsRequiredParameterInTemplates returns true when ValueRank is Array regardless of BasicDataType value.
        /// Tests that Array ValueRank always returns true as per the second condition in the method.
        /// </summary>
        [TestCase(BasicDataType.Boolean)]
        [TestCase(BasicDataType.SByte)]
        [TestCase(BasicDataType.Byte)]
        [TestCase(BasicDataType.Int16)]
        [TestCase(BasicDataType.UInt16)]
        [TestCase(BasicDataType.Int32)]
        [TestCase(BasicDataType.UInt32)]
        [TestCase(BasicDataType.Int64)]
        [TestCase(BasicDataType.UInt64)]
        [TestCase(BasicDataType.Float)]
        [TestCase(BasicDataType.Double)]
        [TestCase(BasicDataType.String)]
        [TestCase(BasicDataType.DateTime)]
        [TestCase(BasicDataType.Guid)]
        [TestCase(BasicDataType.ByteString)]
        [TestCase(BasicDataType.XmlElement)]
        [TestCase(BasicDataType.NodeId)]
        [TestCase(BasicDataType.ExpandedNodeId)]
        [TestCase(BasicDataType.StatusCode)]
        [TestCase(BasicDataType.DiagnosticInfo)]
        [TestCase(BasicDataType.QualifiedName)]
        [TestCase(BasicDataType.LocalizedText)]
        [TestCase(BasicDataType.DataValue)]
        [TestCase(BasicDataType.Number)]
        [TestCase(BasicDataType.Integer)]
        [TestCase(BasicDataType.UInteger)]
        [TestCase(BasicDataType.Enumeration)]
        [TestCase(BasicDataType.Structure)]
        [TestCase(BasicDataType.BaseDataType)]
        [TestCase(BasicDataType.UserDefined)]
        public void IsRequiredParameterInTemplates_ArrayValueRank_ReturnsTrue(BasicDataType basicDataType)
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = basicDataType };

            // Act
            bool result = dataType.IsTemplateParameterRequired(ValueRank.Array);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Test IsRequiredParameterInTemplates with combinations that should return false.
        /// Tests edge cases where either the BasicDataType or ValueRank falls into the exclusion list.
        /// </summary>
        [TestCase(BasicDataType.BaseDataType, ValueRank.Scalar, false)]
        [TestCase(BasicDataType.Number, ValueRank.Scalar, false)]
        [TestCase(BasicDataType.Integer, ValueRank.Scalar, false)]
        [TestCase(BasicDataType.UInteger, ValueRank.Scalar, false)]
        [TestCase(BasicDataType.String, ValueRank.OneOrMoreDimensions, false)]
        [TestCase(BasicDataType.Int32, ValueRank.ScalarOrOneDimension, false)]
        [TestCase(BasicDataType.Boolean, ValueRank.ScalarOrArray, false)]
        [TestCase(BasicDataType.BaseDataType, ValueRank.OneOrMoreDimensions, false)]
        [TestCase(BasicDataType.Number, ValueRank.ScalarOrOneDimension, false)]
        [TestCase(BasicDataType.Integer, ValueRank.ScalarOrArray, false)]
        public void IsRequiredParameterInTemplates_VariousCombinations_ReturnsFalse(
            BasicDataType basicDataType,
            ValueRank valueRank,
            bool expected)
        {
            // Arrange
            var dataType = new DataTypeDesign { BasicDataType = basicDataType };

            // Act
            bool result = dataType.IsTemplateParameterRequired(valueRank);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns correct XML type string for Boolean scalar type.
        /// </summary>
        [Test]
        public void GetXmlDataType_BooleanScalar_ReturnsXsBoolean()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("xs:boolean"));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns correct XML type string for Boolean array type.
        /// </summary>
        [Test]
        public void GetXmlDataType_BooleanArray_ReturnsUaListOfBoolean()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Boolean
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Array, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:ListOfBoolean"));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns correct XML type strings for all basic data types in scalar mode.
        /// </summary>
        /// <param name="basicDataType">The basic data type to test.</param>
        /// <param name="expectedXmlType">The expected XML type string.</param>
        [TestCase(BasicDataType.Boolean, "xs:boolean")]
        [TestCase(BasicDataType.SByte, "xs:byte")]
        [TestCase(BasicDataType.Byte, "xs:unsignedByte")]
        [TestCase(BasicDataType.Int16, "xs:short")]
        [TestCase(BasicDataType.UInt16, "xs:unsignedShort")]
        [TestCase(BasicDataType.Int32, "xs:int")]
        [TestCase(BasicDataType.UInt32, "xs:unsignedInt")]
        [TestCase(BasicDataType.Int64, "xs:long")]
        [TestCase(BasicDataType.UInt64, "xs:unsignedLong")]
        [TestCase(BasicDataType.Float, "xs:float")]
        [TestCase(BasicDataType.Double, "xs:double")]
        [TestCase(BasicDataType.String, "xs:string")]
        [TestCase(BasicDataType.DateTime, "xs:dateTime")]
        [TestCase(BasicDataType.Guid, "ua:Guid")]
        [TestCase(BasicDataType.ByteString, "xs:base64Binary")]
        [TestCase(BasicDataType.XmlElement, "ua:XmlElement")]
        [TestCase(BasicDataType.NodeId, "ua:NodeId")]
        [TestCase(BasicDataType.ExpandedNodeId, "ua:ExpandedNodeId")]
        [TestCase(BasicDataType.StatusCode, "ua:StatusCode")]
        [TestCase(BasicDataType.DiagnosticInfo, "ua:DiagnosticInfo")]
        [TestCase(BasicDataType.QualifiedName, "ua:QualifiedName")]
        [TestCase(BasicDataType.LocalizedText, "ua:LocalizedText")]
        [TestCase(BasicDataType.DataValue, "ua:DataValue")]
        [TestCase(BasicDataType.Number, "ua:Variant")]
        [TestCase(BasicDataType.Integer, "ua:Variant")]
        [TestCase(BasicDataType.UInteger, "ua:Variant")]
        [TestCase(BasicDataType.BaseDataType, "ua:Variant")]
        public void GetXmlDataType_BasicDataTypeScalar_ReturnsExpectedXmlType(BasicDataType basicDataType, string expectedXmlType)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = basicDataType
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo(expectedXmlType));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns correct XML type strings for all basic data types in array mode.
        /// </summary>
        /// <param name="basicDataType">The basic data type to test.</param>
        /// <param name="expectedXmlType">The expected XML type string.</param>
        [TestCase(BasicDataType.Boolean, "ua:ListOfBoolean")]
        [TestCase(BasicDataType.SByte, "ua:ListOfSByte")]
        [TestCase(BasicDataType.Byte, "ua:ListOfByte")]
        [TestCase(BasicDataType.Int16, "ua:ListOfInt16")]
        [TestCase(BasicDataType.UInt16, "ua:ListOfUInt16")]
        [TestCase(BasicDataType.Int32, "ua:ListOfInt32")]
        [TestCase(BasicDataType.UInt32, "ua:ListOfUInt32")]
        [TestCase(BasicDataType.Int64, "ua:ListOfInt64")]
        [TestCase(BasicDataType.UInt64, "ua:ListOfUInt64")]
        [TestCase(BasicDataType.Float, "ua:ListOfFloat")]
        [TestCase(BasicDataType.Double, "ua:ListOfDouble")]
        [TestCase(BasicDataType.String, "ua:ListOfString")]
        [TestCase(BasicDataType.DateTime, "ua:ListOfDateTime")]
        [TestCase(BasicDataType.Guid, "ua:ListOfGuid")]
        [TestCase(BasicDataType.ByteString, "ua:ListOfByteString")]
        [TestCase(BasicDataType.XmlElement, "ua:ListOfXmlElement")]
        [TestCase(BasicDataType.NodeId, "ua:ListOfNodeId")]
        [TestCase(BasicDataType.ExpandedNodeId, "ua:ListOfExpandedNodeId")]
        [TestCase(BasicDataType.StatusCode, "ua:ListOfStatusCode")]
        [TestCase(BasicDataType.DiagnosticInfo, "ua:ListOfDiagnosticInfo")]
        [TestCase(BasicDataType.QualifiedName, "ua:ListOfQualifiedName")]
        [TestCase(BasicDataType.LocalizedText, "ua:ListOfLocalizedText")]
        [TestCase(BasicDataType.DataValue, "ua:ListOfDataValue")]
        [TestCase(BasicDataType.Number, "ua:ListOfVariant")]
        [TestCase(BasicDataType.Integer, "ua:ListOfVariant")]
        [TestCase(BasicDataType.UInteger, "ua:ListOfVariant")]
        [TestCase(BasicDataType.BaseDataType, "ua:ListOfVariant")]
        public void GetXmlDataType_BasicDataTypeArray_ReturnsExpectedXmlType(BasicDataType basicDataType, string expectedXmlType)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = basicDataType
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Array, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo(expectedXmlType));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns correct XML type for all non-scalar ValueRank values.
        /// </summary>
        /// <param name="valueRank">The value rank to test.</param>
        [TestCase(ValueRank.Array)]
        [TestCase(ValueRank.ScalarOrArray)]
        [TestCase(ValueRank.OneOrMoreDimensions)]
        [TestCase(ValueRank.ScalarOrOneDimension)]
        [TestCase(ValueRank.Any)]
        public void GetXmlDataType_NonScalarValueRank_ReturnsListType(ValueRank valueRank)
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(valueRank, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:ListOfInt32"));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns ExtensionObject for Structure type in scalar mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_StructureScalar_ReturnsUaExtensionObject()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Structure", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:ExtensionObject"));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns ListOfExtensionObject for Structure type in array mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_StructureArray_ReturnsUaListOfExtensionObject()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Structure", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Array, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:ListOfExtensionObject"));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns xs:int for Enumeration type without option set in scalar mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_EnumerationScalarNotOptionSet_ReturnsXsInt()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Enumeration", Namespaces.OpcUa),
                IsOptionSet = false
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("xs:int"));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns ua:ListOfInt32 for Enumeration type without option set in array mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_EnumerationArrayNotOptionSet_ReturnsUaListOfInt32()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Enumeration", Namespaces.OpcUa),
                IsOptionSet = false
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Array, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:ListOfInt32"));
        }

        /// <summary>
        /// Tests that GetXmlDataType recursively calls for Enumeration with IsOptionSet true in scalar mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_EnumerationScalarWithOptionSet_CallsRecursivelyAndReturnsBaseTypeXmlType()
        {
            // Arrange
            var mockBaseDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInt32
            };

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Enumeration", Namespaces.OpcUa),
                IsOptionSet = true,
                BaseTypeNode = mockBaseDataType
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("xs:unsignedInt"));
        }

        /// <summary>
        /// Tests that GetXmlDataType recursively calls for Enumeration with IsOptionSet true in array mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_EnumerationArrayWithOptionSet_CallsRecursivelyAndReturnsBaseTypeListXmlType()
        {
            // Arrange
            var mockBaseDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInt32
            };

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Enumeration", Namespaces.OpcUa),
                IsOptionSet = true,
                BaseTypeNode = mockBaseDataType
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Array, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:ListOfUInt32"));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns correct type for custom type in target namespace in scalar mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_CustomTypeInTargetNamespaceScalar_ReturnsTnsTypeName()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", "http://test.org")
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("tns:CustomType"));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns correct type for custom type in target namespace in array mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_CustomTypeInTargetNamespaceArray_ReturnsTnsListOfTypeName()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", "http://test.org")
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Array, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("tns:ListOfCustomType"));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns correct type for custom type in OpcUa namespace in scalar mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_CustomTypeInOpcUaNamespaceScalar_ReturnsUaTypeName()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:CustomType"));
        }

        /// <summary>
        /// Tests that GetXmlDataType returns correct type for custom type in OpcUa namespace in array mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_CustomTypeInOpcUaNamespaceArray_ReturnsUaListOfTypeName()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", Namespaces.OpcUa)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Array, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:ListOfCustomType"));
        }

        /// <summary>
        /// Tests that GetXmlDataType handles Enumeration name in OpcUa namespace without option set in scalar mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_EnumerationNameInOpcUaNamespaceScalarNotOptionSet_ReturnsXsInt()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Enumeration", Namespaces.OpcUa),
                IsOptionSet = false
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("xs:int"));
        }

        /// <summary>
        /// Tests that GetXmlDataType handles Enumeration name in OpcUa namespace without option set in array mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_EnumerationNameInOpcUaNamespaceArrayNotOptionSet_ReturnsUaListOfInt32()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Enumeration", Namespaces.OpcUa),
                IsOptionSet = false
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Array, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:ListOfInt32"));
        }

        /// <summary>
        /// Tests that GetXmlDataType handles Enumeration name in OpcUa namespace with option set in scalar mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_EnumerationNameInOpcUaNamespaceScalarWithOptionSet_CallsRecursivelyAndReturnsBaseTypeXmlType()
        {
            // Arrange
            var mockBaseDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Enumeration", Namespaces.OpcUa),
                IsOptionSet = true,
                BaseTypeNode = mockBaseDataType
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("xs:int"));
        }

        /// <summary>
        /// Tests that GetXmlDataType handles Enumeration name in OpcUa namespace with option set in array mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_EnumerationNameInOpcUaNamespaceArrayWithOptionSet_CallsRecursivelyAndReturnsBaseTypeListXmlType()
        {
            // Arrange
            var mockBaseDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Enumeration", Namespaces.OpcUa),
                IsOptionSet = true,
                BaseTypeNode = mockBaseDataType
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Array, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("ua:ListOfInt32"));
        }

        /// <summary>
        /// Tests that GetXmlDataType handles empty target namespace correctly in scalar mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_EmptyTargetNamespaceScalar_ReturnsTnsTypeName()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", string.Empty)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, string.Empty, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("tns:CustomType"));
        }

        /// <summary>
        /// Tests that GetXmlDataType handles null target namespace correctly in scalar mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_NullTargetNamespaceScalar_ReturnsTnsTypeName()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", string.Empty)
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, null, namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("tns:CustomType"));
        }

        /// <summary>
        /// Tests that GetXmlDataType handles null namespaces array correctly for basic types in scalar mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_NullNamespacesArrayWithBasicTypeScalar_ReturnsXmlType()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, "http://test.org", null);

            // Assert
            Assert.That(result, Is.EqualTo("xs:int"));
        }

        /// <summary>
        /// Tests that GetXmlDataType handles null namespaces array correctly for basic types in array mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_NullNamespacesArrayWithBasicTypeArray_ReturnsXmlType()
        {
            // Arrange
            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.Int32
            };

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Array, "http://test.org", null);

            // Assert
            Assert.That(result, Is.EqualTo("ua:ListOfInt32"));
        }

        /// <summary>
        /// Tests that GetXmlDataType handles deeply nested recursion for IsOptionSet with multiple levels.
        /// </summary>
        [Test]
        public void GetXmlDataType_DeeplyNestedOptionSet_ReturnsBaseTypeXmlType()
        {
            // Arrange
            var mockBaseBaseDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UInt64
            };

            var mockBaseDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Enumeration", Namespaces.OpcUa),
                IsOptionSet = true,
                BaseTypeNode = mockBaseBaseDataType
            };

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("Enumeration", Namespaces.OpcUa),
                IsOptionSet = true,
                BaseTypeNode = mockBaseDataType
            };
            Namespace[] namespaces = [];

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("xs:unsignedLong"));
        }

        /// <summary>
        /// Tests that GetXmlDataType handles UserDefined basic data type with custom namespace in scalar mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_UserDefinedTypeCustomNamespaceScalar_ReturnsCustomPrefixTypeName()
        {
            // Arrange
            var mockNamespace = new Namespace
            {
                Value = "http://custom.org",
                XmlPrefix = "custom"
            };

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", "http://custom.org")
            };
            var namespaces = new Namespace[] { mockNamespace };

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Scalar, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("custom:CustomType"));
        }

        /// <summary>
        /// Tests that GetXmlDataType handles UserDefined basic data type with custom namespace in array mode.
        /// </summary>
        [Test]
        public void GetXmlDataType_UserDefinedTypeCustomNamespaceArray_ReturnsCustomPrefixListOfTypeName()
        {
            // Arrange
            var mockNamespace = new Namespace
            {
                Value = "http://custom.org",
                XmlPrefix = "custom"
            };

            var mockDataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined,
                SymbolicName = new XmlQualifiedName("CustomType", "http://custom.org")
            };
            var namespaces = new Namespace[] { mockNamespace };

            // Act
            string result = mockDataType.GetXmlDataType(ValueRank.Array, "http://test.org", namespaces);

            // Assert
            Assert.That(result, Is.EqualTo("custom:ListOfCustomType"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName returns null when the target parameter is null.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_NullTarget_ReturnsNull()
        {
            // Arrange
            Parameter target = null;

            // Act
            string result = target.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName returns the target name when the parent is null.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_NullParent_ReturnsTargetName()
        {
            // Arrange
            var target = new Parameter { Name = "TestField", Identifier = 1 };

            // Act
            string result = target.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("TestField"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName returns the target name when the parent is not a DataTypeDesign.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_ParentNotDataTypeDesign_ReturnsTargetName()
        {
            // Arrange
            var target = new Parameter
            {
                Name = "TestField",
                Identifier = 1,
                Parent = new TypeDesign()
            };

            // Act
            string result = target.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("TestField"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName returns the target name when HasFields is false.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_HasFieldsFalse_ReturnsTargetName()
        {
            // Arrange
            var target = new Parameter
            {
                Name = "TestField",
                Identifier = 1,
                Parent = new DataTypeDesign { HasFields = false }
            };

            // Act
            string result = target.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("TestField"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName returns the target name when Fields is null.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_FieldsNull_ReturnsTargetName()
        {
            // Arrange
            var target = new Parameter
            {
                Name = "TestField",
                Identifier = 1,
                Parent = new DataTypeDesign { HasFields = true, Fields = null }
            };

            // Act
            string result = target.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("TestField"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName returns the target name when Fields is empty.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_EmptyFields_ReturnsTargetName()
        {
            // Arrange
            var target = new Parameter
            {
                Name = "TestField",
                Identifier = 1,
                Parent = new DataTypeDesign { HasFields = true, Fields = [] }
            };

            // Act
            string result = target.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("TestField"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName returns the original name when there are no name collisions.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_NoCollisions_ReturnsOriginalName()
        {
            // Arrange
            var field1 = new Parameter { Name = "Field1", Identifier = 1 };
            var field2 = new Parameter { Name = "Field2", Identifier = 2 };
            var field3 = new Parameter { Name = "Field3", Identifier = 3 };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2, field3]
            };

            field1.Parent = parent;
            field2.Parent = parent;
            field3.Parent = parent;

            // Act
            string result = field2.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("Field2"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName returns the name with identifier suffix when there is a name collision.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_NameCollision_ReturnsNameWithIdentifier()
        {
            // Arrange
            var field1 = new Parameter { Name = "Field", Identifier = 1 };
            var field2 = new Parameter { Name = "Field", Identifier = 2 };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2]
            };

            field1.Parent = parent;
            field2.Parent = parent;

            // Act
            string result = field2.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("Field_2"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName returns the name with version suffix when both
        /// name and identifier suffix collide.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_NameAndIdentifierCollision_ReturnsNameWithVersion()
        {
            // Arrange
            var field1 = new Parameter { Name = "Field", Identifier = 1 };
            var field2 = new Parameter { Name = "Field_2", Identifier = 2 };
            var field3 = new Parameter { Name = "Field", Identifier = 3 };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2, field3]
            };

            field1.Parent = parent;
            field2.Parent = parent;
            field3.Parent = parent;

            // Act
            string result = field3.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("Field_3"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName handles multiple version increments when necessary.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_MultipleVersionIncrements_ReturnsCorrectVersion()
        {
            // Arrange
            var field1 = new Parameter { Name = "Field", Identifier = 1 };
            var field2 = new Parameter { Name = "Field_2", Identifier = 2 };
            var field3 = new Parameter { Name = "Field_v1", Identifier = 3 };
            var field4 = new Parameter { Name = "Field", Identifier = 2 };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2, field3, field4]
            };

            field1.Parent = parent;
            field2.Parent = parent;
            field3.Parent = parent;
            field4.Parent = parent;

            // Act
            string result = field4.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("Field_2"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName correctly identifies the first field in a collection.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_FirstField_ReturnsOriginalName()
        {
            // Arrange
            var field1 = new Parameter { Name = "FirstField", Identifier = 1 };
            var field2 = new Parameter { Name = "SecondField", Identifier = 2 };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2]
            };

            field1.Parent = parent;
            field2.Parent = parent;

            // Act
            string result = field1.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("FirstField"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName correctly identifies the last field in a collection.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_LastField_ReturnsOriginalName()
        {
            // Arrange
            var field1 = new Parameter { Name = "FirstField", Identifier = 1 };
            var field2 = new Parameter { Name = "LastField", Identifier = 2 };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2]
            };

            field1.Parent = parent;
            field2.Parent = parent;

            // Act
            string result = field2.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("LastField"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName handles three fields with the same name correctly.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_ThreeFieldsSameName_ReturnsCorrectNames()
        {
            // Arrange
            var field1 = new Parameter { Name = "Field", Identifier = 1 };
            var field2 = new Parameter { Name = "Field", Identifier = 2 };
            var field3 = new Parameter { Name = "Field", Identifier = 3 };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2, field3]
            };

            field1.Parent = parent;
            field2.Parent = parent;
            field3.Parent = parent;

            // Act
            string result1 = field1.EnsureUniqueEnumName();
            string result2 = field2.EnsureUniqueEnumName();
            string result3 = field3.EnsureUniqueEnumName();

            // Assert
            Assert.That(result1, Is.EqualTo("Field"));
            Assert.That(result2, Is.EqualTo("Field_2"));
            Assert.That(result3, Is.EqualTo("Field_3"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName returns null when target name is null.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_TargetNameNull_ReturnsNull()
        {
            // Arrange
            var target = new Parameter { Name = null, Identifier = 1 };

            // Act
            string result = target.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName handles empty string as field name.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_EmptyStringName_ReturnsEmptyString()
        {
            // Arrange
            var target = new Parameter { Name = string.Empty, Identifier = 1 };

            // Act
            string result = target.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName handles whitespace string as field name.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_WhitespaceStringName_ReturnsWhitespaceString()
        {
            // Arrange
            var target = new Parameter { Name = "   ", Identifier = 1 };

            // Act
            string result = target.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("   "));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName handles special characters in field names.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_SpecialCharactersInName_ReturnsCorrectName()
        {
            // Arrange
            var field1 = new Parameter { Name = "Field@#$", Identifier = 1 };
            var field2 = new Parameter { Name = "Field@#$", Identifier = 2 };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2]
            };

            field1.Parent = parent;
            field2.Parent = parent;

            // Act
            string result = field2.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("Field@#$_2"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName handles decimal identifiers correctly.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_DecimalIdentifier_ReturnsNameWithDecimalIdentifier()
        {
            // Arrange
            var field1 = new Parameter { Name = "Field", Identifier = 1.5m };
            var field2 = new Parameter { Name = "Field", Identifier = 2.5m };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2]
            };

            field1.Parent = parent;
            field2.Parent = parent;

            // Act
            string result = field2.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("Field_2.5"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName handles zero identifier.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_ZeroIdentifier_ReturnsNameWithZeroIdentifier()
        {
            // Arrange
            var field1 = new Parameter { Name = "Field", Identifier = 0 };
            var field2 = new Parameter { Name = "Field", Identifier = 1 };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2]
            };

            field1.Parent = parent;
            field2.Parent = parent;

            // Act
            string result = field2.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("Field_1"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName handles negative identifier.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_NegativeIdentifier_ReturnsNameWithNegativeIdentifier()
        {
            // Arrange
            var field1 = new Parameter { Name = "Field", Identifier = -1 };
            var field2 = new Parameter { Name = "Field", Identifier = -2 };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2]
            };

            field1.Parent = parent;
            field2.Parent = parent;

            // Act
            string result = field2.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("Field_-2"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName handles very large identifier values.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_LargeIdentifier_ReturnsNameWithLargeIdentifier()
        {
            // Arrange
            var field1 = new Parameter { Name = "Field", Identifier = decimal.MaxValue };
            var field2 = new Parameter { Name = "Field", Identifier = decimal.MaxValue - 1 };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2]
            };

            field1.Parent = parent;
            field2.Parent = parent;

            // Act
            string result = field2.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo($"Field_{decimal.MaxValue - 1}"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName handles very long field names.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_VeryLongName_ReturnsExtendedName()
        {
            // Arrange
            string longName = new('A', 1000);
            var field1 = new Parameter { Name = longName, Identifier = 1 };
            var field2 = new Parameter { Name = longName, Identifier = 2 };

            var parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1, field2]
            };

            field1.Parent = parent;
            field2.Parent = parent;

            // Act
            string result = field2.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo($"{longName}_2"));
        }

        /// <summary>
        /// Tests that EnsureUniqueEnumName handles a single field correctly.
        /// </summary>
        [Test]
        public void EnsureUniqueEnumName_SingleField_ReturnsOriginalName()
        {
            // Arrange
            var field1 = new Parameter { Name = "OnlyField", Identifier = 1 };

            field1.Parent = new DataTypeDesign
            {
                HasFields = true,
                Fields = [field1]
            };

            // Act
            string result = field1.EnsureUniqueEnumName();

            // Assert
            Assert.That(result, Is.EqualTo("OnlyField"));
        }

        /// <summary>
        /// Tests that GetAccessLevelString returns the correct string representation
        /// for all valid AccessLevel enum values with explicit switch cases.
        /// </summary>
        /// <param name="accessLevel">The AccessLevel enum value to test.</param>
        /// <param name="expectedResult">The expected string representation.</param>
        [TestCase(AccessLevel.Read, "global::Opc.Ua.AccessLevels.CurrentRead")]
        [TestCase(AccessLevel.Write, "global::Opc.Ua.AccessLevels.CurrentWrite")]
        [TestCase(AccessLevel.ReadWrite, "global::Opc.Ua.AccessLevels.CurrentReadOrWrite")]
        [TestCase(AccessLevel.HistoryRead, "global::Opc.Ua.AccessLevels.HistoryRead")]
        [TestCase(AccessLevel.HistoryWrite, "global::Opc.Ua.AccessLevels.HistoryWrite")]
        [TestCase(AccessLevel.HistoryReadWrite, "global::Opc.Ua.AccessLevels.HistoryReadOrWrite")]
        public void GetAccessLevelString_ValidAccessLevel_ReturnsCorrectString(AccessLevel accessLevel, string expectedResult)
        {
            // Act
            string result = accessLevel.GetAccessLevelAsCode();

            // Assert
            Assert.That(result, Is.EqualTo(expectedResult));
        }

        /// <summary>
        /// Tests that GetMethodArgumentDotNetType converts object to Variant array with array value rank.
        /// Input: Array value rank, type name "object" or "object?".
        /// Expected: Returns "global::Opc.Ua.Variant[]".
        /// </summary>
        [TestCase("object", "global::Opc.Ua.Variant[]")]
        [TestCase("object?", "global::Opc.Ua.Variant[]")]
        public void GetMethodArgumentDotNetType_ArrayValueRankWithObject_ReturnsVariantArray(string mockReturnType, string expectedResult)
        {
            // Arrange
            var dataTypeMock = new DataTypeDesign
            {
                BasicDataType = BasicDataType.UserDefined
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.com";

            var getDotNetTypeNameMock = new MockGetDotNetTypeName(mockReturnType);

            // Act
            string result = getDotNetTypeNameMock.GetMethodArgumentDotNetTypeWrapper(
                dataTypeMock,
                ValueRank.Array,
                targetNamespace,
                namespaces,
                false);

            // Assert
            Assert.That(result, Is.EqualTo(expectedResult));
        }

        /// <summary>
        /// Tests that GetMethodArgumentDotNetType returns "object" for non-scalar and non-array value ranks.
        /// Input: ValueRank other than Scalar or Array (e.g., ScalarOrArray, OneOrMoreDimensions, Any).
        /// Expected: Returns "object".
        /// </summary>
        [TestCase(ValueRank.ScalarOrArray)]
        [TestCase(ValueRank.OneOrMoreDimensions)]
        [TestCase(ValueRank.ScalarOrOneDimension)]
        [TestCase(ValueRank.Any)]
        public void GetMethodArgumentDotNetType_NonScalarOrArrayValueRank_ReturnsObject(ValueRank valueRank)
        {
            // Arrange
            var dataTypeMock = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.com";

            var getDotNetTypeNameMock = new MockGetDotNetTypeName("string");

            // Act
            string result = getDotNetTypeNameMock.GetMethodArgumentDotNetTypeWrapper(
                dataTypeMock,
                valueRank,
                targetNamespace,
                namespaces,
                false);

            // Assert
            Assert.That(result, Is.EqualTo("object"));
        }

        /// <summary>
        /// Tests that GetMethodArgumentDotNetType handles null namespaces array.
        /// Input: Null namespaces array with scalar value rank.
        /// Expected: Returns type name without throwing exception.
        /// </summary>
        [Test]
        public void GetMethodArgumentDotNetType_NullNamespaces_ReturnsTypeName()
        {
            // Arrange
            var dataTypeMock = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            const string targetNamespace = "http://test.com";

            var getDotNetTypeNameMock = new MockGetDotNetTypeName("string");

            // Act
            string result = getDotNetTypeNameMock.GetMethodArgumentDotNetTypeWrapper(
                dataTypeMock,
                ValueRank.Scalar,
                targetNamespace,
                null,
                false);

            // Assert
            Assert.That(result, Is.EqualTo("string"));
        }

        /// <summary>
        /// Tests that GetMethodArgumentDotNetType handles empty namespaces array.
        /// Input: Empty namespaces array with scalar value rank.
        /// Expected: Returns type name without throwing exception.
        /// </summary>
        [Test]
        public void GetMethodArgumentDotNetType_EmptyNamespaces_ReturnsTypeName()
        {
            // Arrange
            var dataTypeMock = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            Namespace[] namespaces = [];
            const string targetNamespace = "http://test.com";

            var getDotNetTypeNameMock = new MockGetDotNetTypeName("string");

            // Act
            string result = getDotNetTypeNameMock.GetMethodArgumentDotNetTypeWrapper(
                dataTypeMock,
                ValueRank.Scalar,
                targetNamespace,
                namespaces,
                false);

            // Assert
            Assert.That(result, Is.EqualTo("string"));
        }

        /// <summary>
        /// Tests that GetMethodArgumentDotNetType handles null target namespace.
        /// Input: Null target namespace with scalar value rank.
        /// Expected: Returns type name without throwing exception.
        /// </summary>
        [Test]
        public void GetMethodArgumentDotNetType_NullTargetNamespace_ReturnsTypeName()
        {
            // Arrange
            var dataTypeMock = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            Namespace[] namespaces = [];

            var getDotNetTypeNameMock = new MockGetDotNetTypeName("string");

            // Act
            string result = getDotNetTypeNameMock.GetMethodArgumentDotNetTypeWrapper(
                dataTypeMock,
                ValueRank.Scalar,
                null,
                namespaces,
                false);

            // Assert
            Assert.That(result, Is.EqualTo("string"));
        }

        /// <summary>
        /// Tests that GetMethodArgumentDotNetType handles empty target namespace.
        /// Input: Empty target namespace with scalar value rank.
        /// Expected: Returns type name without throwing exception.
        /// </summary>
        [Test]
        public void GetMethodArgumentDotNetType_EmptyTargetNamespace_ReturnsTypeName()
        {
            // Arrange
            var dataTypeMock = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            Namespace[] namespaces = [];
            string targetNamespace = string.Empty;

            var getDotNetTypeNameMock = new MockGetDotNetTypeName("string");

            // Act
            string result = getDotNetTypeNameMock.GetMethodArgumentDotNetTypeWrapper(
                dataTypeMock,
                ValueRank.Scalar,
                targetNamespace,
                namespaces,
                false);

            // Assert
            Assert.That(result, Is.EqualTo("string"));
        }

        /// <summary>
        /// Helper class to wrap GetMethodArgumentDotNetType method and mock GetDotNetTypeName dependency.
        /// </summary>
        private sealed class MockGetDotNetTypeName
        {
            private readonly string m_returnValue;
            public NullableAnnotation LastNullableAnnotation { get; private set; }

            public MockGetDotNetTypeName(string returnValue)
            {
                m_returnValue = returnValue;
            }

            public string GetMethodArgumentDotNetTypeWrapper(
                DataTypeDesign datatype,
                ValueRank valueRank,
                string targetNamespace,
                Namespace[] namespaces,
                bool isOptional)
            {
                // Store the nullable annotation that would be passed
                LastNullableAnnotation = isOptional ? NullableAnnotation.Nullable : NullableAnnotation.NonNullable;

                // Mock the GetDotNetTypeName call
                string typeName = m_returnValue;

                // Replicate the method logic
                if (typeName is "global::Opc.Ua.IEncodeable" or "global::Opc.Ua.IEncodeable?")
                {
                    typeName = "global::Opc.Ua.ExtensionObject";
                }

                if (valueRank == ValueRank.Array)
                {
                    if (typeName is "object" or "object?")
                    {
                        typeName = "global::Opc.Ua.Variant";
                    }

                    return typeName + "[]";
                }

                if (valueRank == ValueRank.Scalar)
                {
                    return typeName;
                }

                return "object";
            }
        }

        /// <summary>
        /// Tests IsMethodTypeNode returns false when the node parameter is null.
        /// </summary>
        [Test]
        public void IsMethodTypeNode_NullNode_ReturnsFalse()
        {
            // Arrange
            NodeDesign node = null;

            // Act
            bool result = node.IsMethodTypeDesign();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests IsMethodTypeNode with various SymbolicId names.
        /// Validates correct identification of method type nodes based on naming conventions.
        /// </summary>
        /// <param name="symbolicName">The name of the SymbolicId to test.</param>
        /// <param name="expected">The expected result (true if it should be recognized as a method type node).</param>
        [TestCase("SomeMethodType", true)]
        [TestCase("MyCustomMethodType", true)]
        [TestCase("MethodType", true)]
        [TestCase("NotMethodType", true)]
        [TestCase("MethodTypeExtended", false)]
        [TestCase("SomethingElse", false)]
        [TestCase("MyMethodType_Extra", true)]
        [TestCase("CustomMethodType_Parameter", true)]
        [TestCase("Something_Else", false)]
        [TestCase("_MethodType", true)]
        [TestCase("methodtype", false)]
        [TestCase("METHODTYPE", false)]
        [TestCase("MethodTypE", false)]
        [TestCase("", false)]
        [TestCase("A_B_MethodType", false)]
        [TestCase("Method_Type", false)]
        [TestCase("MethodType_", true)]
        [TestCase("_", false)]
        [TestCase("M", false)]
        public void IsMethodTypeNode_VariousSymbolicNames_ReturnsExpectedResult(string symbolicName, bool expected)
        {
            // Arrange
            var mockNode = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName(symbolicName)
            };

            // Act
            bool result = mockNode.IsMethodTypeDesign();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests IsMethodTypeNode throws NullReferenceException when SymbolicId is null.
        /// This tests the edge case where the node is not null but its SymbolicId property is null.
        /// </summary>
        [Test]
        public void IsMethodTypeNode_NullSymbolicId_ThrowsArgumentNullException()
        {
            // Arrange
            var mockNode = new NodeDesign
            {
                SymbolicId = null
            };

            // Act
            bool result = mockNode.IsMethodTypeDesign();

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests IsMethodTypeNode with multiple underscores in the symbolic name.
        /// Validates that only the first underscore is used for truncation.
        /// </summary>
        [TestCase("First_Second_MethodType", false)]
        [TestCase("AMethodType_IP", true)]
        [TestCase("AMethodType", true)]
        // TODO [TestCase("A_MethodType", true)]
        [TestCase("AB_CD_EF_MethodType", false)]
        public void IsMethodTypeNode_MultipleUnderscores_TruncatesAtFirstUnderscore(
            string symbolicName,
            bool expected)
        {
            // Arrange
            var mockNode = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName(symbolicName)
            };

            // Act
            bool result = mockNode.IsMethodTypeDesign();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests IsMethodTypeNode with underscore at the beginning of the symbolic name.
        /// Validates that underscore at position 0 is not used for truncation.
        /// </summary>
        [TestCase("_MethodType", true)]
        [TestCase("_NotMethodTypeA", false)]
        [TestCase("_SomeMethodType", true)]
        public void IsMethodTypeNode_UnderscoreAtStart_DoesNotTruncate(string symbolicName, bool expected)
        {
            // Arrange
            var mockNode = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName(symbolicName)
            };

            // Act
            bool result = mockNode.IsMethodTypeDesign();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests IsMethodTypeNode with case variations of "MethodType".
        /// Validates that the comparison is case-sensitive (ordinal).
        /// </summary>
        [TestCase("MethodType", true)]
        [TestCase("methodtype", false)]
        [TestCase("METHODTYPE", false)]
        [TestCase("Methodtype", false)]
        [TestCase("MethodTYPE", false)]
        [TestCase("mEtHoDtYpE", false)]
        public void IsMethodTypeNode_CaseSensitivity_ReturnsExpectedResult(string symbolicName, bool expected)
        {
            // Arrange
            var mockNode = new MethodDesign
            {
                SymbolicId = new XmlQualifiedName(symbolicName)
            };

            // Act
            bool result = mockNode.IsMethodTypeDesign();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests IsMethodTypeNode with empty and whitespace symbolic names.
        /// Validates that empty or whitespace-only names return false.
        /// </summary>
        [TestCase("", false)]
        [TestCase(" ", false)]
        [TestCase("  ", false)]
        [TestCase("\t", false)]
        [TestCase("\n", false)]
        public void IsMethodTypeNode_EmptyOrWhitespace_ReturnsFalse(string symbolicName, bool expected)
        {
            // Arrange
            var mockNode = new NodeDesign
            {
                SymbolicId = new XmlQualifiedName(symbolicName)
            };

            // Act
            bool result = mockNode.IsMethodTypeDesign();

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with a null namespaces array.
        /// Expects a NullReferenceException to be thrown.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_NullNamespacesArray_ThrowsArgumentNullException()
        {
            // Arrange
            Namespace[] namespaces = null;
            const string namespaceUri = "http://opcfoundation.org/Test";

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => namespaces.GetConstantSymbolForNamespace(namespaceUri));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with a null namespace URI.
        /// Expects null to be returned since no namespace will match.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_NullNamespaceUri_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "Test", Prefix = "TestPrefix", Value = "http://opcfoundation.org/Test" }
            ];

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(null);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with an empty namespace URI.
        /// Expects null to be returned since no namespace will match.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_EmptyNamespaceUri_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "Test", Prefix = "TestPrefix", Value = "http://opcfoundation.org/Test" }
            ];

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(string.Empty);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with a whitespace namespace URI.
        /// Expects null to be returned since no namespace will match.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_WhitespaceNamespaceUri_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "Test", Prefix = "TestPrefix", Value = "http://opcfoundation.org/Test" }
            ];

            // Act
            string result = namespaces.GetConstantSymbolForNamespace("   ");

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with an empty namespaces array.
        /// Expects null to be returned since no namespace exists to match.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_EmptyNamespacesArray_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces = [];
            const string namespaceUri = "http://opcfoundation.org/Test";

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with a namespace URI that does not match any namespace.
        /// Expects null to be returned.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_NamespaceNotFound_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "Test", Prefix = "TestPrefix", Value = "http://opcfoundation.org/Test" }
            ];
            const string namespaceUri = "http://opcfoundation.org/NotFound";

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with a valid namespace URI that matches a namespace.
        /// Expects the formatted constant symbol string to be returned.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_ValidNamespaceUri_ReturnsFormattedConstant()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "TestName", Prefix = "TestPrefix", Value = "http://opcfoundation.org/Test" }
            ];
            const string namespaceUri = "http://opcfoundation.org/Test";

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("global::TestPrefix.Namespaces.TestName"));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with multiple namespaces, matching the first one.
        /// Expects the formatted constant symbol string for the first namespace to be returned.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_MultipleNamespaces_MatchesFirstNamespace_ReturnsFormattedConstant()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "First", Prefix = "PrefixFirst", Value = "http://opcfoundation.org/First" },
                new Namespace { Name = "Second", Prefix = "PrefixSecond", Value = "http://opcfoundation.org/Second" },
                new Namespace { Name = "Third", Prefix = "PrefixThird", Value = "http://opcfoundation.org/Third" }
            ];
            const string namespaceUri = "http://opcfoundation.org/First";

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("global::PrefixFirst.Namespaces.First"));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with multiple namespaces, matching the middle one.
        /// Expects the formatted constant symbol string for the middle namespace to be returned.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_MultipleNamespaces_MatchesMiddleNamespace_ReturnsFormattedConstant()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "First", Prefix = "PrefixFirst", Value = "http://opcfoundation.org/First" },
                new Namespace { Name = "Second", Prefix = "PrefixSecond", Value = "http://opcfoundation.org/Second" },
                new Namespace { Name = "Third", Prefix = "PrefixThird", Value = "http://opcfoundation.org/Third" }
            ];
            const string namespaceUri = "http://opcfoundation.org/Second";

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("global::PrefixSecond.Namespaces.Second"));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with multiple namespaces, matching the last one.
        /// Expects the formatted constant symbol string for the last namespace to be returned.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_MultipleNamespaces_MatchesLastNamespace_ReturnsFormattedConstant()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "First", Prefix = "PrefixFirst", Value = "http://opcfoundation.org/First" },
                new Namespace { Name = "Second", Prefix = "PrefixSecond", Value = "http://opcfoundation.org/Second" },
                new Namespace { Name = "Third", Prefix = "PrefixThird", Value = "http://opcfoundation.org/Third" }
            ];
            const string namespaceUri = "http://opcfoundation.org/Third";

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("global::PrefixThird.Namespaces.Third"));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with a namespace that has a null Name property.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_NamespaceWithNullName_ThrowsArgumentException()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = null, Prefix = "TestPrefix", Value = "http://opcfoundation.org/Test" }
            ];
            const string namespaceUri = "http://opcfoundation.org/Test";

            // Act and Assert
            Assert.Throws<ArgumentException>(() => namespaces.GetConstantSymbolForNamespace(namespaceUri));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with a namespace that has a null Prefix property.
        /// Expects the formatted constant symbol string with an empty string for the prefix part.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_NamespaceWithNullPrefix_ReturnsFormattedConstantWithEmptyPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "TestName", Prefix = null, Value = "http://opcfoundation.org/Test" }
            ];
            const string namespaceUri = "http://opcfoundation.org/Test";

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("Namespaces.TestName"));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with a namespace that has both null Name and Prefix properties.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_NamespaceWithNullNameAndPrefix_ThrowsArgumentException()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = null, Prefix = null, Value = "http://opcfoundation.org/Test" }
            ];
            const string namespaceUri = "http://opcfoundation.org/Test";

            // Act and Assert
            Assert.Throws<ArgumentException>(() => namespaces.GetConstantSymbolForNamespace(namespaceUri));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with a namespace that has an empty Name property.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_NamespaceWithEmptyName_ThrowsArgumentException()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = string.Empty, Prefix = "TestPrefix", Value = "http://opcfoundation.org/Test" }
            ];
            const string namespaceUri = "http://opcfoundation.org/Test";

            // Act and Assert
            Assert.Throws<ArgumentException>(() => namespaces.GetConstantSymbolForNamespace(namespaceUri));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with a namespace that has an empty Prefix property.
        /// Expects the formatted constant symbol string with an empty string for the prefix part.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_NamespaceWithEmptyPrefix_ReturnsFormattedConstantWithEmptyPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "TestName", Prefix = string.Empty, Value = "http://opcfoundation.org/Test" }
            ];
            const string namespaceUri = "http://opcfoundation.org/Test";

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("Namespaces.TestName"));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with special characters in Name and Prefix.
        /// Expects the formatted constant symbol string with special characters preserved.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_NamespaceWithSpecialCharacters_ReturnsFormattedConstantWithSpecialCharacters()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "Test-Name_123", Prefix = "Prefix$Special", Value = "http://opcfoundation.org/Test" }
            ];
            const string namespaceUri = "http://opcfoundation.org/Test";

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("global::Prefix$Special.Namespaces.Test-Name_123"));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with very long Name and Prefix values.
        /// Expects the formatted constant symbol string with the full long values.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_NamespaceWithVeryLongValues_ReturnsFormattedConstantWithLongValues()
        {
            // Arrange
            string longName = new('N', 1000);
            string longPrefix = new('P', 1000);
            Namespace[] namespaces =
            [
                new Namespace { Name = longName, Prefix = longPrefix, Value = "http://opcfoundation.org/Test" }
            ];
            const string namespaceUri = "http://opcfoundation.org/Test";

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo($"global::{longPrefix}.Namespaces.{longName}"));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with exact case match for namespace URI.
        /// Expects the formatted constant symbol string to be returned.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_ExactCaseMatch_ReturnsFormattedConstant()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "TestName", Prefix = "TestPrefix", Value = "http://opcfoundation.org/Test" }
            ];
            const string namespaceUri = "http://opcfoundation.org/Test";

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("global::TestPrefix.Namespaces.TestName"));
        }

        /// <summary>
        /// Tests GetConstantSymbolForNamespace with different case for namespace URI.
        /// Expects null to be returned since string comparison is case-sensitive.
        /// </summary>
        [Test]
        public void GetConstantSymbolForNamespace_DifferentCase_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Name = "TestName", Prefix = "TestPrefix", Value = "http://opcfoundation.org/Test" }
            ];
            const string namespaceUri = "http://opcfoundation.org/TEST";

            // Act
            string result = namespaces.GetConstantSymbolForNamespace(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that GetEventNotifierString returns the correct string when SupportsEvents is true.
        /// Input: ObjectTypeDesign with SupportsEvents set to true.
        /// Expected: Returns "global::Opc.Ua.EventNotifiers.SubscribeToEvents".
        /// </summary>
        [Test]
        public void GetEventNotifierString_SupportsEventsTrue_ReturnsSubscribeToEvents()
        {
            // Arrange
            var objectType = new ObjectTypeDesign
            {
                SupportsEvents = true
            };

            // Act
            string result = objectType.GetEventNotifierAsCode();

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.EventNotifiers.SubscribeToEvents"));
        }

        /// <summary>
        /// Tests that GetEventNotifierString returns the correct string when SupportsEvents is false.
        /// Input: ObjectTypeDesign with SupportsEvents set to false.
        /// Expected: Returns "global::Opc.Ua.EventNotifiers.None".
        /// </summary>
        [Test]
        public void GetEventNotifierString_SupportsEventsFalse_ReturnsNone()
        {
            // Arrange
            var objectType = new ObjectTypeDesign
            {
                SupportsEvents = false
            };

            // Act
            string result = objectType.GetEventNotifierAsCode();

            // Assert
            Assert.That(result, Is.EqualTo("global::Opc.Ua.EventNotifiers.None"));
        }

        /// <summary>
        /// Tests that GetEventNotifierString returns the correct string for both true and false values using parameterization.
        /// Input: ObjectTypeDesign with SupportsEvents set to the test case value.
        /// Expected: Returns the corresponding EventNotifiers string value.
        /// </summary>
        [TestCase(true, "global::Opc.Ua.EventNotifiers.SubscribeToEvents")]
        [TestCase(false, "global::Opc.Ua.EventNotifiers.None")]
        public void GetEventNotifierString_VariousSupportsEventsValues_ReturnsExpectedString(bool supportsEvents, string expectedResult)
        {
            // Arrange
            var objectType = new ObjectTypeDesign
            {
                SupportsEvents = supportsEvents
            };

            // Act
            string result = objectType.GetEventNotifierAsCode();

            // Assert
            Assert.That(result, Is.EqualTo(expectedResult));
        }

        /// <summary>
        /// Tests that GetEventNotifierString throws NullReferenceException when objectType is null.
        /// Input: null ObjectTypeDesign.
        /// Expected: Throws NullReferenceException.
        /// </summary>
        [Test]
        public void GetEventNotifierString_NullObjectType_ThrowsArgumentNullException()
        {
            // Arrange
            ObjectTypeDesign objectType = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => objectType.GetEventNotifierAsCode());
        }

        /// <summary>
        /// Tests that IsDotNetReferenceType returns true when ValueRank is Array.
        /// Since arrays are reference types in .NET, this should return true regardless of the underlying data type.
        /// </summary>
        [Test]
        public void IsDotNetReferenceType_ValueRankArray_ReturnsTrue()
        {
            // Arrange
            var dataType = new DataTypeDesign();
            const ValueRank valueRank = ValueRank.Array;

            // Act
            bool result = dataType.IsDotNetReferenceType(valueRank);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsDotNetReferenceType returns true when ValueRank is ScalarOrArray.
        /// When valueRank is not Scalar, the method should return true.
        /// </summary>
        [Test]
        public void IsDotNetReferenceType_ValueRankScalarOrArray_ReturnsTrue()
        {
            // Arrange
            var dataType = new DataTypeDesign();
            const ValueRank valueRank = ValueRank.ScalarOrArray;

            // Act
            bool result = dataType.IsDotNetReferenceType(valueRank);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsDotNetReferenceType returns true when ValueRank is OneOrMoreDimensions.
        /// When valueRank is not Scalar, the method should return true.
        /// </summary>
        [Test]
        public void IsDotNetReferenceType_ValueRankOneOrMoreDimensions_ReturnsTrue()
        {
            // Arrange
            var dataType = new DataTypeDesign();
            const ValueRank valueRank = ValueRank.OneOrMoreDimensions;

            // Act
            bool result = dataType.IsDotNetReferenceType(valueRank);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsDotNetReferenceType returns true when ValueRank is ScalarOrOneDimension.
        /// When valueRank is not Scalar, the method should return true.
        /// </summary>
        [Test]
        public void IsDotNetReferenceType_ValueRankScalarOrOneDimension_ReturnsTrue()
        {
            // Arrange
            var dataType = new DataTypeDesign();
            const ValueRank valueRank = ValueRank.ScalarOrOneDimension;

            // Act
            bool result = dataType.IsDotNetReferenceType(valueRank);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsDotNetReferenceType returns true when ValueRank is Any.
        /// When valueRank is not Scalar, the method should return true.
        /// </summary>
        [Test]
        public void IsDotNetReferenceType_ValueRankAny_ReturnsTrue()
        {
            // Arrange
            var dataType = new DataTypeDesign();
            const ValueRank valueRank = ValueRank.Any;

            // Act
            bool result = dataType.IsDotNetReferenceType(valueRank);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsDotNetReferenceType returns true when ValueRank is Scalar and BasicDataType is String.
        /// String is a reference type in .NET, so this should return true.
        /// </summary>
        [Test]
        public void IsDotNetReferenceType_ScalarStringType_ReturnsTrue()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                BasicDataType = BasicDataType.String
            };
            const ValueRank valueRank = ValueRank.Scalar;

            // Act
            bool result = dataType.IsDotNetReferenceType(valueRank);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that IsDotNetReferenceType throws or null DataTypeDesign parameter.
        /// </summary>
        [Test]
        public void IsDotNetReferenceType_NullDataType_ThrowsArgumentNullException()
        {
            // Arrange
            DataTypeDesign dataType = null;
            const ValueRank valueRank = ValueRank.Scalar;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => dataType.IsDotNetReferenceType(valueRank));
        }

        /// <summary>
        /// Tests that IsDotNetReferenceType returns false when ValueRank is Scalar and the data type
        /// would be treated as a .NET value type based on the BasicDataType determination.
        /// This test covers the case where the BasicDataType cannot be determined and the recursive
        /// check is performed.
        /// </summary>
        [Test]
        public void IsDotNetReferenceType_ScalarValueType_ReturnsFalse()
        {
            // Arrange
            var baseDataType = new DataTypeDesign();
            var baseTypeName = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/");
            baseDataType.SymbolicName = baseTypeName;

            var derivedDataType = new DataTypeDesign
            {
                BaseType = baseTypeName
            };

            const ValueRank valueRank = ValueRank.Scalar;

            // Act
            bool result = derivedDataType.IsDotNetReferenceType(valueRank);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsDotNetReferenceType returns appropriate result for various undefined/invalid ValueRank values.
        /// Tests boundary conditions by casting integer values to ValueRank enum.
        /// </summary>
        [TestCase((ValueRank)(-1), ExpectedResult = true)]
        [TestCase((ValueRank)100, ExpectedResult = true)]
        [TestCase((ValueRank)int.MaxValue, ExpectedResult = true)]
        [TestCase((ValueRank)int.MinValue, ExpectedResult = true)]
        public bool IsDotNetReferenceType_InvalidValueRankValues_ReturnsTrue(ValueRank valueRank)
        {
            // Arrange
            var dataType = new DataTypeDesign();

            // Act & Assert
            return dataType.IsDotNetReferenceType(valueRank);
        }

        /// <summary>
        /// Tests that IsDotNetReferenceType returns false for optionset data types with Scalar ValueRank.
        /// OptionSet types are treated as enumerations which are value types in .NET.
        /// </summary>
        [Test]
        public void IsDotNetReferenceType_ScalarOptionSetType_ReturnsFalse()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                IsOptionSet = true
            };
            const ValueRank valueRank = ValueRank.Scalar;

            // Act
            bool result = dataType.IsDotNetReferenceType(valueRank);

            // Assert
            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Tests that IsDotNetReferenceType returns true for optionset data types with Array ValueRank.
        /// Even though OptionSet is a value type, arrays are reference types in .NET.
        /// </summary>
        [Test]
        public void IsDotNetReferenceType_ArrayOptionSetType_ReturnsTrue()
        {
            // Arrange
            var dataType = new DataTypeDesign
            {
                IsOptionSet = true
            };
            const ValueRank valueRank = ValueRank.Array;

            // Act
            bool result = dataType.IsDotNetReferenceType(valueRank);

            // Assert
            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Tests that GetMergedInstance returns the original instance when the instance has no parent.
        /// </summary>
        [Test]
        public void GetMergedInstance_InstanceWithNoParent_ReturnsOriginalInstance()
        {
            // Arrange
            var mockInstance = new InstanceDesign
            {
                Parent = null
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance returns the original instance when the parent is not a root node.
        /// </summary>
        [Test]
        public void GetMergedInstance_ParentIsNotRoot_ReturnsOriginalInstance()
        {
            // Arrange
            var mockGrandParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = null
            };

            var mockParent = new NodeDesign
            {
                Parent = mockGrandParent
            };

            var mockInstance = new InstanceDesign
            {
                Parent = mockParent
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance returns the original instance when the root parent has null Hierarchy.
        /// </summary>
        [Test]
        public void GetMergedInstance_RootParentHasNullHierarchy_ReturnsOriginalInstance()
        {
            // Arrange
            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = null
            };

            var symbolicId = new XmlQualifiedName("TestName", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance uses the full name as relative path when SymbolicId.Name contains no underscore.
        /// </summary>
        [Test]
        public void GetMergedInstance_SymbolicIdNameWithoutUnderscore_UsesFullNameAsRelativePath()
        {
            // Arrange
            var mockHierarchy = new Hierarchy();

            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var symbolicId = new XmlQualifiedName("TestName", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance extracts substring after first underscore when SymbolicId.Name contains underscore.
        /// </summary>
        [Test]
        public void GetMergedInstance_SymbolicIdNameWithUnderscore_ExtractsSubstringAfterFirstUnderscore()
        {
            // Arrange
            var mockHierarchy = new Hierarchy();

            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var symbolicId = new XmlQualifiedName("Prefix_TestName", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance handles multiple underscores and only splits on the first one.
        /// </summary>
        [Test]
        public void GetMergedInstance_SymbolicIdNameWithMultipleUnderscores_SplitsOnFirstUnderscore()
        {
            // Arrange
            var mockHierarchy = new Hierarchy();

            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var symbolicId = new XmlQualifiedName("Prefix_Test_Name_Test", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance returns the original instance when relative path is not found in Hierarchy.Nodes.
        /// </summary>
        [Test]
        public void GetMergedInstance_RelativePathNotFoundInHierarchyNodes_ReturnsOriginalInstance()
        {
            // Arrange
            var mockHierarchy = new Hierarchy();
            mockHierarchy.Nodes.Add("OtherPath", new HierarchyNode());

            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var symbolicId = new XmlQualifiedName("Prefix_TestName", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance returns the original instance when HierarchyNode.Instance is null.
        /// </summary>
        [Test]
        public void GetMergedInstance_HierarchyNodeInstanceIsNull_ReturnsOriginalInstance()
        {
            // Arrange
            var hierarchyNode = new HierarchyNode { Instance = null };
            var mockHierarchy = new Hierarchy();
            mockHierarchy.Nodes.Add("TestName", hierarchyNode);

            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var symbolicId = new XmlQualifiedName("Prefix_TestName", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance returns the original instance when HierarchyNode.Instance is not InstanceDesign.
        /// </summary>
        [Test]
        public void GetMergedInstance_HierarchyNodeInstanceIsNotInstanceDesign_ReturnsOriginalInstance()
        {
            // Arrange
            var mockNodeDesign = new NodeDesign();
            var hierarchyNode = new HierarchyNode { Instance = mockNodeDesign };

            var mockHierarchy = new Hierarchy();
            mockHierarchy.Nodes.Add("TestName", hierarchyNode);

            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var symbolicId = new XmlQualifiedName("Prefix_TestName", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance returns the merged instance when HierarchyNode.Instance is InstanceDesign.
        /// </summary>
        [Test]
        public void GetMergedInstance_HierarchyNodeInstanceIsInstanceDesign_ReturnsMergedInstance()
        {
            // Arrange
            var mockMergedInstance = new InstanceDesign();
            var hierarchyNode = new HierarchyNode { Instance = mockMergedInstance };

            var mockHierarchy = new Hierarchy();
            mockHierarchy.Nodes.Add("TestName", hierarchyNode);

            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var symbolicId = new XmlQualifiedName("Prefix_TestName", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockMergedInstance));
            Assert.That(result, Is.Not.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance handles empty SymbolicId.Name correctly.
        /// </summary>
        [Test]
        public void GetMergedInstance_EmptySymbolicIdName_ReturnsOriginalInstance()
        {
            // Arrange
            var mockHierarchy = new Hierarchy();

            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var symbolicId = new XmlQualifiedName(string.Empty, "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance handles SymbolicId.Name with only underscore correctly.
        /// </summary>
        [Test]
        public void GetMergedInstance_SymbolicIdNameOnlyUnderscore_ReturnsOriginalInstance()
        {
            // Arrange
            var mockHierarchy = new Hierarchy();

            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var symbolicId = new XmlQualifiedName("_", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance handles SymbolicId.Name starting with underscore correctly.
        /// </summary>
        [Test]
        public void GetMergedInstance_SymbolicIdNameStartingWithUnderscore_ExtractsSubstringAfterUnderscore()
        {
            // Arrange
            var mockMergedInstance = new InstanceDesign();
            var hierarchyNode = new HierarchyNode { Instance = mockMergedInstance };

            var mockHierarchy = new Hierarchy();
            mockHierarchy.Nodes.Add("TestName", hierarchyNode);

            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var symbolicId = new XmlQualifiedName("_TestName", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockMergedInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance traverses multiple parent levels correctly.
        /// </summary>
        [Test]
        public void GetMergedInstance_MultipleParentLevels_ChecksRootParentOnly()
        {
            // Arrange
            var mockMergedInstance = new InstanceDesign();
            var hierarchyNode = new HierarchyNode { Instance = mockMergedInstance };

            var mockHierarchy = new Hierarchy();
            mockHierarchy.Nodes.Add("TestName", hierarchyNode);

            var mockGrandParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var mockParent = new NodeDesign
            {
                Parent = mockGrandParent,
                Hierarchy = null
            };

            var symbolicId = new XmlQualifiedName("Prefix_TestName", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockMergedInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance handles case where SymbolicId.Name ends with underscore.
        /// </summary>
        [Test]
        public void GetMergedInstance_SymbolicIdNameEndsWithUnderscore_UsesEmptyRelativePath()
        {
            // Arrange
            var mockMergedInstance = new InstanceDesign();
            var hierarchyNode = new HierarchyNode { Instance = mockMergedInstance };

            var mockHierarchy = new Hierarchy();
            mockHierarchy.Nodes.Add(string.Empty, hierarchyNode);

            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var symbolicId = new XmlQualifiedName("Prefix_", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockMergedInstance));
        }

        /// <summary>
        /// Tests that GetMergedInstance returns original instance when Hierarchy.Nodes dictionary is empty.
        /// </summary>
        [Test]
        public void GetMergedInstance_HierarchyNodesEmpty_ReturnsOriginalInstance()
        {
            // Arrange
            var mockHierarchy = new Hierarchy();
            var mockParent = new NodeDesign
            {
                Parent = null,
                Hierarchy = mockHierarchy
            };

            var symbolicId = new XmlQualifiedName("Prefix_TestName", "http://test.com");
            var mockInstance = new InstanceDesign
            {
                Parent = mockParent,
                SymbolicId = symbolicId
            };

            // Act
            InstanceDesign result = mockInstance.GetMergedInstance();

            // Assert
            Assert.That(result, Is.SameAs(mockInstance));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with null namespaceUri parameter.
        /// Should return null when namespaceUri is null.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_NullNamespaceUri_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://test.org/UA/", XmlPrefix = "test" }
            ];

            // Act
            string result = namespaces.GetXmlNamespacePrefix(null);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with null namespaces array.
        /// Should return null when called on a null array.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_NullNamespaces_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces = null;
            const string namespaceUri = "http://test.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with empty namespaces array.
        /// Should return null when no namespaces are present.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_EmptyNamespaces_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces = [];
            const string namespaceUri = "http://test.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix when matching namespace has XmlPrefix set.
        /// Should return the XmlPrefix value when it's not null or empty.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_MatchingNamespaceWithXmlPrefix_ReturnsXmlPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://opcfoundation.org/UA/", XmlPrefix = "opc" },
                new Namespace { Value = "http://test.org/UA/", XmlPrefix = "test" },
                new Namespace { Value = "http://example.org/UA/", XmlPrefix = "ex" }
            ];
            const string namespaceUri = "http://test.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("test"));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix when matching namespace has null XmlPrefix.
        /// Should return formatted prefix "s{index}" when XmlPrefix is null.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_MatchingNamespaceWithNullXmlPrefix_ReturnsFormattedPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://opcfoundation.org/UA/", XmlPrefix = "opc" },
                new Namespace { Value = "http://test.org/UA/", XmlPrefix = null },
                new Namespace { Value = "http://example.org/UA/", XmlPrefix = "ex" }
            ];
            const string namespaceUri = "http://test.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("s1"));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix when matching namespace has empty XmlPrefix.
        /// Should return formatted prefix "s{index}" when XmlPrefix is empty.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_MatchingNamespaceWithEmptyXmlPrefix_ReturnsFormattedPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://opcfoundation.org/UA/", XmlPrefix = "opc" },
                new Namespace { Value = "http://test.org/UA/", XmlPrefix = string.Empty },
                new Namespace { Value = "http://example.org/UA/", XmlPrefix = "ex" }
            ];
            const string namespaceUri = "http://test.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("s1"));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with matching namespace at different array indices.
        /// Should return correct formatted prefix "s{index}" corresponding to the array position.
        /// </summary>
        [TestCase(0, "s0")]
        [TestCase(1, "s1")]
        [TestCase(2, "s2")]
        [TestCase(5, "s5")]
        [TestCase(10, "s10")]
        public void GetXmlNamespacePrefix_MatchingNamespaceAtDifferentIndices_ReturnsCorrectFormattedPrefix(int index, string expected)
        {
            // Arrange
            var namespaces = new Namespace[index + 1];
            for (int i = 0; i < namespaces.Length; i++)
            {
                namespaces[i] = new Namespace
                {
                    Value = $"http://namespace{i}.org/UA/",
                    XmlPrefix = null
                };
            }
            string namespaceUri = $"http://namespace{index}.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix when no matching namespace is found.
        /// Should return null when the namespaceUri doesn't match any namespace Value.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_NoMatchingNamespace_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://opcfoundation.org/UA/", XmlPrefix = "opc" },
                new Namespace { Value = "http://test.org/UA/", XmlPrefix = "test" },
                new Namespace { Value = "http://example.org/UA/", XmlPrefix = "ex" }
            ];
            const string namespaceUri = "http://notfound.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with empty string namespaceUri.
        /// Should handle empty string correctly and return matching result or null.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_EmptyStringNamespaceUri_ReturnsCorrectResult()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = string.Empty, XmlPrefix = "empty" },
                new Namespace { Value = "http://test.org/UA/", XmlPrefix = "test" }
            ];
            string namespaceUri = string.Empty;

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("empty"));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with empty string namespaceUri when no match exists.
        /// Should return null when empty string doesn't match any namespace.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_EmptyStringNamespaceUriNoMatch_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://test.org/UA/", XmlPrefix = "test" }
            ];
            string namespaceUri = string.Empty;

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with whitespace-only namespaceUri.
        /// Should handle whitespace string correctly and return matching result or null.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_WhitespaceNamespaceUri_ReturnsCorrectResult()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "   ", XmlPrefix = "whitespace" },
                new Namespace { Value = "http://test.org/UA/", XmlPrefix = "test" }
            ];
            const string namespaceUri = "   ";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("whitespace"));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix when first namespace matches.
        /// Should return correct result when match is at index 0.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_FirstNamespaceMatches_ReturnsCorrectResult()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://first.org/UA/", XmlPrefix = "first" },
                new Namespace { Value = "http://second.org/UA/", XmlPrefix = "second" },
                new Namespace { Value = "http://third.org/UA/", XmlPrefix = "third" }
            ];
            const string namespaceUri = "http://first.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("first"));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix when last namespace matches.
        /// Should return correct result when match is at the last index.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_LastNamespaceMatches_ReturnsCorrectResult()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://first.org/UA/", XmlPrefix = "first" },
                new Namespace { Value = "http://second.org/UA/", XmlPrefix = "second" },
                new Namespace { Value = "http://third.org/UA/", XmlPrefix = "third" }
            ];
            const string namespaceUri = "http://third.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("third"));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with single namespace in array with XmlPrefix.
        /// Should handle single-element array correctly.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_SingleNamespaceWithXmlPrefix_ReturnsXmlPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://test.org/UA/", XmlPrefix = "test" }
            ];
            const string namespaceUri = "http://test.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("test"));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with single namespace in array without XmlPrefix.
        /// Should return "s0" for first element when XmlPrefix is null or empty.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_SingleNamespaceWithoutXmlPrefix_ReturnsFormattedPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://test.org/UA/", XmlPrefix = null }
            ];
            const string namespaceUri = "http://test.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("s0"));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with case-sensitive namespace URI comparison.
        /// Should not match if case differs as string comparison is case-sensitive.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_CaseSensitiveComparison_ReturnsNull()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://test.org/UA/", XmlPrefix = "test" }
            ];
            const string namespaceUri = "http://TEST.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with special characters in namespaceUri.
        /// Should handle special characters correctly in matching.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_SpecialCharactersInNamespaceUri_ReturnsCorrectResult()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://test.org/UA/special!@#$%", XmlPrefix = "special" }
            ];
            const string namespaceUri = "http://test.org/UA/special!@#$%";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo("special"));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with very long namespaceUri.
        /// Should handle very long strings correctly.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_VeryLongNamespaceUri_ReturnsCorrectResult()
        {
            // Arrange
            string longUri = "http://test.org/UA/" + new string('a', 10000);
            Namespace[] namespaces =
            [
                new Namespace { Value = longUri, XmlPrefix = "long" }
            ];

            // Act
            string result = namespaces.GetXmlNamespacePrefix(longUri);

            // Assert
            Assert.That(result, Is.EqualTo("long"));
        }

        /// <summary>
        /// Tests GetXmlNamespacePrefix with XmlPrefix containing whitespace.
        /// Should return XmlPrefix as-is even if it contains whitespace.
        /// </summary>
        [Test]
        public void GetXmlNamespacePrefix_XmlPrefixWithWhitespace_ReturnsXmlPrefix()
        {
            // Arrange
            Namespace[] namespaces =
            [
                new Namespace { Value = "http://test.org/UA/", XmlPrefix = " test " }
            ];
            const string namespaceUri = "http://test.org/UA/";

            // Act
            string result = namespaces.GetXmlNamespacePrefix(namespaceUri);

            // Assert
            Assert.That(result, Is.EqualTo(" test "));
        }
    }
}
