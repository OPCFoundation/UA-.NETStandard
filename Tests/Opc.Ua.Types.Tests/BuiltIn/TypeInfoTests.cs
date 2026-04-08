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
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Tests for the <see cref="TypeInfo"/> struct.
    /// </summary>
    [TestFixture]
    [Category("TypeInfo")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TypeInfoTests
    {
        [Test]
        public void ConstructorWithValueRankOutOfRangeThrows()
        {
            Assert.That(
                () => new TypeInfo(BuiltInType.Int32, short.MaxValue + 1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void ConstructorWithValueRankBelowMinThrows()
        {
            Assert.That(
                () => new TypeInfo(BuiltInType.Int32, short.MinValue - 1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void GetHashCodeForUnknownReturnsZero()
        {
            TypeInfo unknown = TypeInfo.Unknown;
            Assert.That(unknown.GetHashCode(), Is.Zero);
        }

        [Test]
        public void GetHashCodeForKnownTypeReturnsNonZero()
        {
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(typeInfo.GetHashCode(), Is.Not.Zero);
        }

        [Test]
        public void GetHashCodeDifferentTypesReturnDifferentHash()
        {
            var int32Scalar = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            var doubleScalar = new TypeInfo(BuiltInType.Double, ValueRanks.Scalar);
            Assert.That(int32Scalar.GetHashCode(), Is.Not.EqualTo(doubleScalar.GetHashCode()));
        }

        [Test]
        public void EqualsObjectWithNullReturnsTrueForUnknown()
        {
            TypeInfo unknown = TypeInfo.Unknown;
#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(unknown.Equals((object)null));
#pragma warning restore IDE0004 // Remove Unnecessary Cast
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
        }

        [Test]
        public void EqualsObjectWithNullReturnsFalseForKnown()
        {
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
#pragma warning disable NUnit4002 // Use Specific constraint
            Assert.That(typeInfo, Is.Not.EqualTo((object)null));
#pragma warning restore NUnit4002 // Use Specific constraint
        }

        [Test]
        public void EqualsObjectWithTypeInfoDelegatesToTypedEquals()
        {
            var a = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            object b = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void EqualsObjectWithOtherTypeReturnsFalse()
        {
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(typeInfo.Equals("not a TypeInfo"), Is.False);
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
        }

        [Test]
        public void EqualityOperatorReturnsTrueForEqualValues()
        {
            var a = new TypeInfo(BuiltInType.Double, ValueRanks.OneDimension);
            var b = new TypeInfo(BuiltInType.Double, ValueRanks.OneDimension);
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void InequalityOperatorReturnsTrueForDifferentValues()
        {
            var a = new TypeInfo(BuiltInType.Double, ValueRanks.Scalar);
            var b = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void ToStringScalarReturnsTypeName()
        {
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(typeInfo.ToString(), Is.EqualTo("Int32"));
        }

        [Test]
        public void ToStringOneDimensionArrayReturnsBrackets()
        {
            var typeInfo = new TypeInfo(BuiltInType.Double, ValueRanks.OneDimension);
            Assert.That(typeInfo.ToString(), Is.EqualTo("Double[]"));
        }

        [Test]
        public void ToStringTwoDimensionArrayReturnsComma()
        {
            var typeInfo = new TypeInfo(BuiltInType.Boolean, ValueRanks.TwoDimensions);
            Assert.That(typeInfo.ToString(), Is.EqualTo("Boolean[,]"));
        }

        [Test]
        public void ToStringOneOrMoreDimensionsReturnsEmptyBrackets()
        {
            var typeInfo = new TypeInfo(BuiltInType.String, ValueRanks.OneOrMoreDimensions);
            Assert.That(typeInfo.ToString(), Is.EqualTo("String[]"));
        }

        [Test]
        public void ToStringWithInvalidFormatThrowsFormatException()
        {
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(
                () => typeInfo.ToString("X", null),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void WithBuiltInTypeReturnsNewTypeInfoWithSameValueRank()
        {
            var original = new TypeInfo(BuiltInType.Int32, ValueRanks.OneDimension);
            TypeInfo changed = original.WithBuiltInType(BuiltInType.Double);
            Assert.That(changed.BuiltInType, Is.EqualTo(BuiltInType.Double));
            Assert.That(changed.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void WithValueRankReturnsNewTypeInfoWithSameBuiltInType()
        {
            var original = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            TypeInfo changed = original.WithValueRank(ValueRanks.OneDimension);
            Assert.That(changed.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(changed.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [TestCase(BuiltInType.SByte, true)]
        [TestCase(BuiltInType.Byte, true)]
        [TestCase(BuiltInType.Int16, true)]
        [TestCase(BuiltInType.UInt16, true)]
        [TestCase(BuiltInType.Int32, true)]
        [TestCase(BuiltInType.UInt32, true)]
        [TestCase(BuiltInType.Int64, true)]
        [TestCase(BuiltInType.UInt64, true)]
        [TestCase(BuiltInType.Float, true)]
        [TestCase(BuiltInType.Double, true)]
        [TestCase(BuiltInType.Number, true)]
        [TestCase(BuiltInType.Integer, true)]
        [TestCase(BuiltInType.UInteger, true)]
        [TestCase(BuiltInType.String, false)]
        [TestCase(BuiltInType.Boolean, false)]
        [TestCase(BuiltInType.DateTime, false)]
        [TestCase(BuiltInType.Null, false)]
        public void IsNumericTypeReturnsExpected(BuiltInType type, bool expected)
        {
            Assert.That(TypeInfo.IsNumericType(type), Is.EqualTo(expected));
        }

        [TestCase(BuiltInType.Boolean, true)]
        [TestCase(BuiltInType.SByte, true)]
        [TestCase(BuiltInType.Double, true)]
        [TestCase(BuiltInType.DateTime, true)]
        [TestCase(BuiltInType.Guid, true)]
        [TestCase(BuiltInType.StatusCode, true)]
        [TestCase(BuiltInType.String, false)]
        [TestCase(BuiltInType.NodeId, false)]
        [TestCase(BuiltInType.Null, false)]
        [TestCase(BuiltInType.Variant, false)]
        public void IsValueTypeReturnsExpected(BuiltInType type, bool expected)
        {
            Assert.That(TypeInfo.IsValueType(type), Is.EqualTo(expected));
        }

        [TestCase(BuiltInType.Boolean, false)]
        [TestCase(BuiltInType.Int32, false)]
        [TestCase(BuiltInType.Double, false)]
        [TestCase(BuiltInType.DataValue, false)]
        [TestCase(BuiltInType.DiagnosticInfo, false)]
        [TestCase(BuiltInType.String, true)]
        [TestCase(BuiltInType.NodeId, true)]
        [TestCase(BuiltInType.DateTime, true)]
        [TestCase(BuiltInType.Guid, true)]
        [TestCase(BuiltInType.Variant, true)]
        public void IsEncodingNullableTypeReturnsExpected(BuiltInType type, bool expected)
        {
            Assert.That(TypeInfo.IsEncodingNullableType(type), Is.EqualTo(expected));
        }

        [Test]
        public void GetBuiltInTypeWithNullNodeIdReturnsNull()
        {
            Assert.That(TypeInfo.GetBuiltInType(NodeId.Null), Is.EqualTo(BuiltInType.Null));
        }

        [Test]
        public void GetBuiltInTypeWithNonZeroNamespaceReturnsNull()
        {
            var nodeId = new NodeId(1u, 5);
            Assert.That(TypeInfo.GetBuiltInType(nodeId), Is.EqualTo(BuiltInType.Null));
        }

        [TestCase(1u, BuiltInType.Boolean)]
        [TestCase(2u, BuiltInType.SByte)]
        [TestCase(3u, BuiltInType.Byte)]
        [TestCase(6u, BuiltInType.Int32)]
        [TestCase(11u, BuiltInType.Double)]
        [TestCase(12u, BuiltInType.String)]
        [TestCase(29u, BuiltInType.Enumeration)]
        public void GetBuiltInTypeForStandardTypesReturnsExpected(uint id, BuiltInType expected)
        {
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(id)), Is.EqualTo(expected));
        }

        [Test]
        public void GetBuiltInTypeForUtcTimeReturnsDateTime()
        {
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(294u)),
                Is.EqualTo(BuiltInType.DateTime));
        }

        [Test]
        public void GetBuiltInTypeForByteStringSubtypesReturnsByteString()
        {
            // DataTypes.ApplicationInstanceCertificate
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(311u)),
                Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.Image = 30
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(30u)),
                Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.ImageBMP = 2000
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(2000u)),
                Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.ImageGIF = 2001
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(2001u)),
                Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.ImageJPG = 2002
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(2002u)),
                Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.ImagePNG = 2003
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(2003u)),
                Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.AudioDataType = 16307
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(16307u)),
                Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.ContinuationPoint = 521
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(521u)),
                Is.EqualTo(BuiltInType.ByteString));
        }

        [Test]
        public void GetBuiltInTypeForSessionAuthTokenReturnsNodeId()
        {
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(388u)),
                Is.EqualTo(BuiltInType.NodeId));
        }

        [Test]
        public void GetBuiltInTypeForDurationReturnsDouble()
        {
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(290u)),
                Is.EqualTo(BuiltInType.Double));
        }

        [Test]
        public void GetBuiltInTypeForUInt32SubtypesReturnsUInt32()
        {
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(288u)),
                Is.EqualTo(BuiltInType.UInt32)); // IntegerId
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(17588u)),
                Is.EqualTo(BuiltInType.UInt32)); // Index
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(20998u)),
                Is.EqualTo(BuiltInType.UInt32)); // VersionTime
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(289u)),
                Is.EqualTo(BuiltInType.UInt32)); // Counter
        }

        [Test]
        public void GetBuiltInTypeForBitFieldMaskReturnsUInt64()
        {
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(11737u)),
                Is.EqualTo(BuiltInType.UInt64));
        }

        [Test]
        public void GetBuiltInTypeForStringSubtypesReturnsString()
        {
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(12881u)),
                Is.EqualTo(BuiltInType.String)); // DateString
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(12878u)),
                Is.EqualTo(BuiltInType.String)); // DecimalString
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(12879u)),
                Is.EqualTo(BuiltInType.String)); // DurationString
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(295u)),
                Is.EqualTo(BuiltInType.String)); // LocaleId
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(12877u)),
                Is.EqualTo(BuiltInType.String)); // NormalizedString
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(291u)),
                Is.EqualTo(BuiltInType.String)); // NumericRange
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(12880u)),
                Is.EqualTo(BuiltInType.String)); // TimeString
            Assert.That(
                TypeInfo.GetBuiltInType(new NodeId(23751u)),
                Is.EqualTo(BuiltInType.String)); // UriString
        }

        [Test]
        public void GetBuiltInTypeForUnknownHighIdReturnsNull()
        {
            Assert.That(
    TypeInfo.GetBuiltInType(new NodeId(9999u)),
    Is.EqualTo(BuiltInType.Null));
        }

        [Test]
        public void GetBuiltInTypeWithTypeTreeResolvesViaSuperType()
        {
            var mockTypeTree = new Mock<ITypeTable>();
            // Custom type 1000 -> supertype Int32 (id=6)
            mockTypeTree
                .Setup(t => t.FindSuperType(new NodeId(1000u)))
                .Returns(new NodeId(6u));

            BuiltInType result = TypeInfo.GetBuiltInType(new NodeId(1000u), mockTypeTree.Object);
            Assert.That(result, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public void GetBuiltInTypeWithNullTypeTreeReturnsNull()
        {
            BuiltInType result = TypeInfo.GetBuiltInType(new NodeId(1000u), null);
            Assert.That(result, Is.EqualTo(BuiltInType.Null));
        }

        [Test]
        public void GetBuiltInTypeWithTypeTreeReturnsNullWhenSuperTypeIsNull()
        {
            var mockTypeTree = new Mock<ITypeTable>();
            mockTypeTree
                .Setup(t => t.FindSuperType(It.IsAny<NodeId>()))
                .Returns(NodeId.Null);

            BuiltInType result =
                TypeInfo.GetBuiltInType(new NodeId(1000u), mockTypeTree.Object);
            Assert.That(result, Is.EqualTo(BuiltInType.Null));
        }

        [Test]
        public async Task GetBuiltInTypeAsyncWithKnownTypeReturnsDirectly()
        {
            BuiltInType result = await TypeInfo.GetBuiltInTypeAsync(
    new NodeId(6u), null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public async Task GetBuiltInTypeAsyncWithTypeTreeResolvesViaSuperType()
        {
            var mockTypeTree = new Mock<ITypeTable>();
            mockTypeTree
                .Setup(t => t.FindSuperTypeAsync(new NodeId(1000u), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NodeId(6u));

            BuiltInType result = await TypeInfo.GetBuiltInTypeAsync(
                new NodeId(1000u), mockTypeTree.Object, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public async Task GetBuiltInTypeAsyncWithNullTypeTreeReturnsNull()
        {
            BuiltInType result = await TypeInfo.GetBuiltInTypeAsync(
    new NodeId(1000u), null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(BuiltInType.Null));
        }

        [Test]
        public async Task GetBuiltInTypeAsyncWithNullNodeIdReturnsNull()
        {
            BuiltInType result = await TypeInfo.GetBuiltInTypeAsync(
                NodeId.Null, null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(BuiltInType.Null));
        }

        [TestCase(BuiltInType.Boolean)]
        [TestCase(BuiltInType.SByte)]
        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.String)]
        [TestCase(BuiltInType.DateTime)]
        [TestCase(BuiltInType.Guid)]
        [TestCase(BuiltInType.ByteString)]
        [TestCase(BuiltInType.XmlElement)]
        [TestCase(BuiltInType.NodeId)]
        [TestCase(BuiltInType.ExpandedNodeId)]
        [TestCase(BuiltInType.StatusCode)]
        [TestCase(BuiltInType.DiagnosticInfo)]
        [TestCase(BuiltInType.QualifiedName)]
        [TestCase(BuiltInType.LocalizedText)]
        [TestCase(BuiltInType.ExtensionObject)]
        [TestCase(BuiltInType.DataValue)]
        [TestCase(BuiltInType.Variant)]
        [TestCase(BuiltInType.Number)]
        [TestCase(BuiltInType.Integer)]
        [TestCase(BuiltInType.UInteger)]
        [TestCase(BuiltInType.Enumeration)]
        public void GetDataTypeIdForAllBuiltInTypesReturnsValidNodeId(BuiltInType type)
        {
            var typeInfo = new TypeInfo(type, ValueRanks.Scalar);
            NodeId result = TypeInfo.GetDataTypeId(typeInfo);
            Assert.That(result.IsNull, Is.False);
        }

        [Test]
        public void GetDataTypeIdForNullTypeReturnsNullNodeId()
        {
            var typeInfo = new TypeInfo(BuiltInType.Null, ValueRanks.Scalar);
            NodeId result = TypeInfo.GetDataTypeId(typeInfo);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void GetDataTypeIdForNullVariantReturnsNullNodeId()
        {
            NodeId result = TypeInfo.GetDataTypeId(Variant.Null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void GetDataTypeIdForIntVariantReturnsInt32DataType()
        {
            var variant = new Variant(42);
            NodeId result = TypeInfo.GetDataTypeId(variant);
            Assert.That(result, Is.EqualTo(new NodeId(6u))); // DataTypes.Int32
        }

        [Test]
        public void GetSystemTypeWithNullExpandedNodeIdReturnsNull()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(ExpandedNodeId.Null, mockFactory.Object);
            Assert.That(result, Is.Null);
        }

        [TestCase(1u, typeof(bool))]        // Boolean
        [TestCase(2u, typeof(sbyte))]       // SByte
        [TestCase(3u, typeof(byte))]        // Byte
        [TestCase(4u, typeof(short))]       // Int16
        [TestCase(5u, typeof(ushort))]      // UInt16
        [TestCase(6u, typeof(int))]         // Int32
        [TestCase(7u, typeof(uint))]        // UInt32
        [TestCase(8u, typeof(long))]        // Int64
        [TestCase(9u, typeof(ulong))]       // UInt64
        [TestCase(10u, typeof(float))]      // Float
        [TestCase(11u, typeof(double))]     // Double
        [TestCase(12u, typeof(string))]     // String
        public void GetSystemTypeForBuiltInExpandedNodeIdReturnsExpected(uint id, Type expected)
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var expandedNodeId = new ExpandedNodeId(id);
            IType result = TypeInfo.GetSystemType(expandedNodeId, mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(expected));
        }

        [Test]
        public void GetSystemTypeForDateTimeExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(13u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(DateTimeUtc)));
        }

        [Test]
        public void GetSystemTypeForGuidExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(14u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(Uuid)));
        }

        [Test]
        public void GetSystemTypeForByteStringExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(15u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(ByteString)));
        }

        [Test]
        public void GetSystemTypeForXmlElementExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(16u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(XmlElement)));
        }

        [Test]
        public void GetSystemTypeForNodeIdExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(17u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(NodeId)));
        }

        [Test]
        public void GetSystemTypeForExpandedNodeIdType()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(18u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(ExpandedNodeId)));
        }

        [Test]
        public void GetSystemTypeForStatusCodeExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(19u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(StatusCode)));
        }

        [Test]
        public void GetSystemTypeForDiagnosticInfoExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(25u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(DiagnosticInfo)));
        }

        [Test]
        public void GetSystemTypeForQualifiedNameExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(20u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(QualifiedName)));
        }

        [Test]
        public void GetSystemTypeForLocalizedTextExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(21u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(LocalizedText)));
        }

        [Test]
        public void GetSystemTypeForDataValueExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(23u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(DataValue)));
        }

        [Test]
        public void GetSystemTypeForBaseDataTypeReturnsVariant()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(24u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(Variant)));
        }

        [Test]
        public void GetSystemTypeForStructureReturnsExtensionObject()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(22u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(ExtensionObject)));
        }

        [Test]
        public void GetSystemTypeForNumberReturnsVariant()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(26u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(Variant)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(27u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(Variant)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(28u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(Variant)));
        }

        [Test]
        public void GetSystemTypeForEnumerationReturnsInt()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(29u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(int)));
        }

        [Test]
        public void GetSystemTypeForUtcTimeSubtypeReturnsDateTimeUtc()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(294u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(DateTimeUtc)));
        }

        [Test]
        public void GetSystemTypeForByteStringSubtypes()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(311u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(ByteString)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(16307u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(ByteString)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(521u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(ByteString)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(30u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(ByteString)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(2000u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(ByteString)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(2001u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(ByteString)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(2002u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(ByteString)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(2003u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(ByteString)));
        }

        [Test]
        public void GetSystemTypeForSessionAuthTokenReturnsNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(388u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(NodeId)));
        }

        [Test]
        public void GetSystemTypeForDurationReturnsDouble()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(290u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(double)));
        }

        [Test]
        public void GetSystemTypeForUInt32Subtypes()
        {
            // IntegerId, Index, VersionTime, Counter
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(288u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(uint)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(17588u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(uint)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(20998u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(uint)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(289u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(uint)));
        }

        [Test]
        public void GetSystemTypeForBitFieldMaskReturnsULong()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            IType result = TypeInfo.GetSystemType(new ExpandedNodeId(11737u), mockFactory.Object);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Type, Is.EqualTo(typeof(ulong)));
        }

        [Test]
        public void GetSystemTypeForStringSubtypes()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(12881u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(string)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(12878u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(string)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(12879u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(string)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(295u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(string)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(12877u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(string)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(291u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(string)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(12880u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(string)));
            Assert.That(
                TypeInfo.GetSystemType(new ExpandedNodeId(23751u), mockFactory.Object)?.Type,
                Is.EqualTo(typeof(string)));
        }

        [Test]
        public void GetSystemTypeForNonNs0FallsBackToFactory()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            mockFactory
                .Setup(f => f.TryGetEncodeableType(
                    It.IsAny<ExpandedNodeId>(),
                    out It.Ref<IEncodeableType>.IsAny))
                .Returns(false);

            var nonNs0 = new ExpandedNodeId(1u, 2);
            IType result = TypeInfo.GetSystemType(nonNs0, mockFactory.Object);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetSystemTypeForUnknownIdFallsBackToFactory()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            mockFactory
                .Setup(f => f.TryGetEncodeableType(
                    It.IsAny<ExpandedNodeId>(),
                    out It.Ref<IEncodeableType>.IsAny))
                .Returns(false);

            var unknownId = new ExpandedNodeId(99999u);
            IType result = TypeInfo.GetSystemType(unknownId, mockFactory.Object);
            Assert.That(result, Is.Null);
        }

        [TestCase(BuiltInType.Boolean, typeof(bool))]
        [TestCase(BuiltInType.SByte, typeof(sbyte))]
        [TestCase(BuiltInType.Byte, typeof(byte))]
        [TestCase(BuiltInType.Int16, typeof(short))]
        [TestCase(BuiltInType.UInt16, typeof(ushort))]
        [TestCase(BuiltInType.Int32, typeof(int))]
        [TestCase(BuiltInType.UInt32, typeof(uint))]
        [TestCase(BuiltInType.Int64, typeof(long))]
        [TestCase(BuiltInType.UInt64, typeof(ulong))]
        [TestCase(BuiltInType.Float, typeof(float))]
        [TestCase(BuiltInType.Double, typeof(double))]
        [TestCase(BuiltInType.String, typeof(string))]
        [TestCase(BuiltInType.Enumeration, typeof(int))]
        public void GetSystemTypeScalarReturnsExpected(BuiltInType type, Type expected)
        {
            Assert.That(
                TypeInfo.GetSystemType(type)?.Type,
                Is.EqualTo(expected));
        }

        [Test]
        public void GetSystemTypeScalarDateTimeReturnsDateTimeUtc()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.DateTime)?.Type,
                Is.EqualTo(typeof(DateTimeUtc)));
        }

        [Test]
        public void GetSystemTypeScalarGuidReturnsUuid()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.Guid)?.Type,
                Is.EqualTo(typeof(Uuid)));
        }

        [Test]
        public void GetSystemTypeScalarByteStringReturnsByteString()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.ByteString)?.Type,
                Is.EqualTo(typeof(ByteString)));
        }

        [Test]
        public void GetSystemTypeScalarXmlElementReturnsXmlElement()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.XmlElement)?.Type,
                Is.EqualTo(typeof(XmlElement)));
        }

        [Test]
        public void GetSystemTypeScalarNodeIdReturnsNodeId()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.NodeId)?.Type,
                Is.EqualTo(typeof(NodeId)));
        }

        [Test]
        public void GetSystemTypeScalarExpandedNodeIdReturnsExpandedNodeId()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.ExpandedNodeId)?.Type,
                Is.EqualTo(typeof(ExpandedNodeId)));
        }

        [Test]
        public void GetSystemTypeScalarLocalizedTextReturnsLocalizedText()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.LocalizedText)?.Type,
                Is.EqualTo(typeof(LocalizedText)));
        }

        [Test]
        public void GetSystemTypeScalarQualifiedNameReturnsQualifiedName()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.QualifiedName)?.Type,
                Is.EqualTo(typeof(QualifiedName)));
        }

        [Test]
        public void GetSystemTypeScalarStatusCodeReturnsStatusCode()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.StatusCode)?.Type,
                Is.EqualTo(typeof(StatusCode)));
        }

        [Test]
        public void GetSystemTypeScalarDiagnosticInfoReturnsDiagnosticInfo()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.DiagnosticInfo)?.Type,
                Is.EqualTo(typeof(DiagnosticInfo)));
        }

        [Test]
        public void GetSystemTypeScalarDataValueReturnsDataValue()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.DataValue)?.Type,
                Is.EqualTo(typeof(DataValue)));
        }

        [Test]
        public void GetSystemTypeScalarVariantReturnsVariant()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.Variant)?.Type,
                Is.EqualTo(typeof(Variant)));
        }

        [Test]
        public void GetSystemTypeScalarExtensionObjectReturnsExtensionObject()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.ExtensionObject)?.Type,
                Is.EqualTo(typeof(ExtensionObject)));
        }

        [Test]
        public void GetSystemTypeScalarNumberReturnsVariant()
        {
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.Number)?.Type,
                Is.EqualTo(typeof(Variant)));
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.Integer)?.Type,
                Is.EqualTo(typeof(Variant)));
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.UInteger)?.Type,
                Is.EqualTo(typeof(Variant)));
            Assert.That(
                TypeInfo.GetSystemType(BuiltInType.Null)?.Type,
                Is.EqualTo(typeof(Variant)));
        }

        [Test]
        public void GetSystemTypeOneDimensionDataValueReturnsArray()
        {
            IBuiltInType systemType = TypeInfo.GetSystemType(BuiltInType.DataValue);
            Assert.That(systemType, Is.Not.Null);
            Assert.That(systemType.Type, Is.EqualTo(typeof(DataValue)));
        }

        [Test]
        public void ConstructFromNullObjectReturnsUnknown()
        {
            var result = TypeInfo.Construct((object)null);
            Assert.That(result.IsUnknown, Is.True);
        }

        [Test]
        public void ConstructFromNullTypeReturnsUnknown()
        {
            var result = TypeInfo.Construct((Type)null);
            Assert.That(result.IsUnknown, Is.True);
        }

        [Test]
        public void ConstructFromInt32ReturnsInt32Scalar()
        {
            var result = TypeInfo.Construct((object)42);
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void ConstructFromBoolReturnsBoolean()
        {
            var result = TypeInfo.Construct((object)true);
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void ConstructFromDoubleArrayReturnsDoubleArray()
        {
            var result = TypeInfo.Construct(typeof(double[]));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Double));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void ConstructFromByteArrayReturnsByteStringScalar()
        {
            var result = TypeInfo.Construct(typeof(byte[]));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.ByteString));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void ConstructFromEnumReturnsEnumeration()
        {
            var result = TypeInfo.Construct(typeof(NodeClass));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void ConstructFromEnumArrayReturnsEnumerationArray()
        {
            var result = TypeInfo.Construct(typeof(NodeClass[]));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void ConstructFromMultiDimArrayReturnsCorrectRank()
        {
            var result = TypeInfo.Construct(typeof(int[,]));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(result.ValueRank, Is.EqualTo(2));
        }

        [Test]
        public void ConstructFromUnknownTypeReturnsUnknown()
        {
            var result = TypeInfo.Construct(typeof(System.IO.Stream));
            Assert.That(result.IsUnknown, Is.True);
        }

        [Test]
        public void ConstructFromVariantTypeReturnsVariant()
        {
            var result = TypeInfo.Construct(typeof(Variant));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Variant));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void ConstructFromExtensionObjectReturnsExtensionObject()
        {
            var result = TypeInfo.Construct(typeof(ExtensionObject));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void ConstructFromStringArrayReturnsStringArray()
        {
            var result = TypeInfo.Construct(typeof(string[]));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.String));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void ConstructFromAllScalarTypes()
        {
            Assert.That(TypeInfo.Construct(typeof(sbyte)).BuiltInType, Is.EqualTo(BuiltInType.SByte));
            Assert.That(TypeInfo.Construct(typeof(byte)).BuiltInType, Is.EqualTo(BuiltInType.Byte));
            Assert.That(TypeInfo.Construct(typeof(short)).BuiltInType, Is.EqualTo(BuiltInType.Int16));
            Assert.That(TypeInfo.Construct(typeof(ushort)).BuiltInType, Is.EqualTo(BuiltInType.UInt16));
            Assert.That(TypeInfo.Construct(typeof(int)).BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(TypeInfo.Construct(typeof(uint)).BuiltInType, Is.EqualTo(BuiltInType.UInt32));
            Assert.That(TypeInfo.Construct(typeof(long)).BuiltInType, Is.EqualTo(BuiltInType.Int64));
            Assert.That(TypeInfo.Construct(typeof(ulong)).BuiltInType, Is.EqualTo(BuiltInType.UInt64));
            Assert.That(TypeInfo.Construct(typeof(float)).BuiltInType, Is.EqualTo(BuiltInType.Float));
            Assert.That(TypeInfo.Construct(typeof(double)).BuiltInType, Is.EqualTo(BuiltInType.Double));
            Assert.That(TypeInfo.Construct(typeof(string)).BuiltInType, Is.EqualTo(BuiltInType.String));
            Assert.That(TypeInfo.Construct(typeof(DateTime)).BuiltInType, Is.EqualTo(BuiltInType.DateTime));
            Assert.That(TypeInfo.Construct(typeof(Uuid)).BuiltInType, Is.EqualTo(BuiltInType.Guid));
            Assert.That(TypeInfo.Construct(typeof(ByteString)).BuiltInType, Is.EqualTo(BuiltInType.ByteString));
            Assert.That(TypeInfo.Construct(typeof(XmlElement)).BuiltInType, Is.EqualTo(BuiltInType.XmlElement));
            Assert.That(TypeInfo.Construct(typeof(NodeId)).BuiltInType, Is.EqualTo(BuiltInType.NodeId));
            Assert.That(TypeInfo.Construct(typeof(ExpandedNodeId)).BuiltInType, Is.EqualTo(BuiltInType.ExpandedNodeId));
            Assert.That(TypeInfo.Construct(typeof(LocalizedText)).BuiltInType, Is.EqualTo(BuiltInType.LocalizedText));
            Assert.That(TypeInfo.Construct(typeof(QualifiedName)).BuiltInType, Is.EqualTo(BuiltInType.QualifiedName));
            Assert.That(TypeInfo.Construct(typeof(StatusCode)).BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
            Assert.That(TypeInfo.Construct(typeof(DiagnosticInfo)).BuiltInType, Is.EqualTo(BuiltInType.DiagnosticInfo));
            Assert.That(TypeInfo.Construct(typeof(DataValue)).BuiltInType, Is.EqualTo(BuiltInType.DataValue));
        }

        [TestCase(BuiltInType.Boolean)]
        [TestCase(BuiltInType.SByte)]
        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.String)]
        [TestCase(BuiltInType.DateTime)]
        [TestCase(BuiltInType.Guid)]
        [TestCase(BuiltInType.ByteString)]
        [TestCase(BuiltInType.XmlElement)]
        [TestCase(BuiltInType.StatusCode)]
        [TestCase(BuiltInType.NodeId)]
        [TestCase(BuiltInType.ExpandedNodeId)]
        [TestCase(BuiltInType.QualifiedName)]
        [TestCase(BuiltInType.LocalizedText)]
        [TestCase(BuiltInType.Variant)]
        [TestCase(BuiltInType.DataValue)]
        [TestCase(BuiltInType.Enumeration)]
        [TestCase(BuiltInType.Number)]
        [TestCase(BuiltInType.Integer)]
        [TestCase(BuiltInType.UInteger)]
        [TestCase(BuiltInType.ExtensionObject)]
        [TestCase(BuiltInType.Null)]
        [TestCase(BuiltInType.DiagnosticInfo)]
        public void GetDefaultVariantValueForAllBuiltInTypes(BuiltInType type)
        {
            // Should not throw for any valid built-in type
            Assert.That(() => TypeInfo.GetDefaultVariantValue(type), Throws.Nothing);
        }

        [Test]
        public void GetDefaultVariantValueWithNodeIdAndScalarRank()
        {
            Variant result = TypeInfo.GetDefaultVariantValue(new NodeId(6u), ValueRanks.Scalar);
            Assert.That(result.AsBoxedObject(), Is.Not.Null);
        }

        [Test]
        public void GetDefaultVariantValueWithNodeIdNonScalarReturnsDefault()
        {
            Variant result = TypeInfo.GetDefaultVariantValue(new NodeId(6u), ValueRanks.OneDimension, null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void GetDefaultVariantValueWithNullNodeIdUsesTypeTree()
        {
            Variant result = TypeInfo.GetDefaultVariantValue(NodeId.Null, ValueRanks.Scalar, null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void GetDefaultVariantValueForBuiltInTypeNodeIds()
        {
            Variant result = TypeInfo.GetDefaultVariantValue(new NodeId(1u), ValueRanks.Scalar, null);
            // Boolean default is false
            Assert.That(() => result.IsNull, Throws.Nothing);
        }

        [Test]
        public void GetDefaultVariantValueForKnownUaTypes()
        {
            Assert.That(() => TypeInfo.GetDefaultVariantValue(new NodeId(290u), ValueRanks.Scalar, null), Throws.Nothing); // Duration
            Assert.That(() => TypeInfo.GetDefaultVariantValue(new NodeId(294u), ValueRanks.Scalar, null), Throws.Nothing); // UtcTime
            Assert.That(() => TypeInfo.GetDefaultVariantValue(new NodeId(289u), ValueRanks.Scalar, null), Throws.Nothing); // Counter
            Assert.That(() => TypeInfo.GetDefaultVariantValue(new NodeId(288u), ValueRanks.Scalar, null), Throws.Nothing); // IntegerId
            Assert.That(() => TypeInfo.GetDefaultVariantValue(new NodeId(26u), ValueRanks.Scalar, null), Throws.Nothing); // Number
            Assert.That(() => TypeInfo.GetDefaultVariantValue(new NodeId(28u), ValueRanks.Scalar, null), Throws.Nothing); // UInteger
            Assert.That(() => TypeInfo.GetDefaultVariantValue(new NodeId(27u), ValueRanks.Scalar, null), Throws.Nothing); // Integer
            Assert.That(() => TypeInfo.GetDefaultVariantValue(new NodeId(256u), ValueRanks.Scalar, null), Throws.Nothing); // IdType
            Assert.That(() => TypeInfo.GetDefaultVariantValue(new NodeId(257u), ValueRanks.Scalar, null), Throws.Nothing); // NodeClass
            Assert.That(() => TypeInfo.GetDefaultVariantValue(new NodeId(29u), ValueRanks.Scalar, null), Throws.Nothing); // Enumeration
        }

        [Test]
        public void GetDefaultVariantValueForUnknownIdWithTypeTree()
        {
            var mockTypeTree = new Mock<ITypeTable>();
            mockTypeTree
                .Setup(t => t.FindSuperType(It.IsAny<NodeId>()))
                .Returns(new NodeId(6u)); // resolves to Int32

            Variant result = TypeInfo.GetDefaultVariantValue(new NodeId(50000u), ValueRanks.Scalar, mockTypeTree.Object);
            Assert.That(result.IsNull, Is.False);
        }

        [TestCase(BuiltInType.Boolean)]
        [TestCase(BuiltInType.SByte)]
        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Double)]
        [TestCase(BuiltInType.DateTime)]
        [TestCase(BuiltInType.Guid)]
        [TestCase(BuiltInType.StatusCode)]
        [TestCase(BuiltInType.NodeId)]
        [TestCase(BuiltInType.ExpandedNodeId)]
        [TestCase(BuiltInType.QualifiedName)]
        [TestCase(BuiltInType.LocalizedText)]
        [TestCase(BuiltInType.Variant)]
        [TestCase(BuiltInType.Enumeration)]
        [TestCase(BuiltInType.Number)]
        [TestCase(BuiltInType.Integer)]
        [TestCase(BuiltInType.UInteger)]
        [TestCase(BuiltInType.ExtensionObject)]
        [TestCase(BuiltInType.DataValue)]
        [TestCase(BuiltInType.Null)]
        [TestCase(BuiltInType.DiagnosticInfo)]
        [TestCase(BuiltInType.String)]
        [TestCase(BuiltInType.ByteString)]
        [TestCase(BuiltInType.XmlElement)]
        public void GetDefaultValueForAllBuiltInTypes(BuiltInType type)
        {
            Assert.That(() => TypeInfo.GetDefaultValue(type), Throws.Nothing);
        }

        [Test]
        public void GetDefaultValueWithNodeIdAndScalarRank()
        {
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(6u), ValueRanks.Scalar), Throws.Nothing);
        }

        [Test]
        public void GetDefaultValueWithNodeIdNonScalarReturnsNull()
        {
            object result = TypeInfo.GetDefaultValue(new NodeId(6u), ValueRanks.OneDimension, null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetDefaultValueWithNullNodeIdReturnsNull()
        {
            object result = TypeInfo.GetDefaultValue(NodeId.Null, ValueRanks.Scalar, null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetDefaultValueForBuiltInTypeNodeIds()
        {
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(1u), ValueRanks.Scalar, null),
                Throws.Nothing);
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(6u), ValueRanks.Scalar, null),
                Throws.Nothing);
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(11u), ValueRanks.Scalar, null),
                Throws.Nothing);
        }

        [Test]
        public void GetDefaultValueForKnownUaTypes()
        {
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(290u), ValueRanks.Scalar, null),
                Throws.Nothing); // Duration
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(294u), ValueRanks.Scalar, null),
                Throws.Nothing); // UtcTime
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(289u), ValueRanks.Scalar, null),
                Throws.Nothing); // Counter
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(288u), ValueRanks.Scalar, null),
                Throws.Nothing); // IntegerId
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(26u), ValueRanks.Scalar, null),
                Throws.Nothing);  // Number
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(28u), ValueRanks.Scalar, null),
                Throws.Nothing);  // UInteger
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(27u), ValueRanks.Scalar, null),
                Throws.Nothing);  // Integer
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(256u), ValueRanks.Scalar, null),
                Throws.Nothing); // IdType
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(257u), ValueRanks.Scalar, null),
                Throws.Nothing); // NodeClass
            Assert.That(
                () => TypeInfo.GetDefaultValue(new NodeId(29u), ValueRanks.Scalar, null),
                Throws.Nothing);  // Enumeration
        }

        [Test]
        public void GetDefaultValueForUnknownIdWithTypeTree()
        {
            var mockTypeTree = new Mock<ITypeTable>();
            mockTypeTree
                .Setup(t => t.FindSuperType(It.IsAny<NodeId>()))
                .Returns(new NodeId(6u)); // resolves to Int32

            object result = TypeInfo.GetDefaultValue(
                new NodeId(50000u), ValueRanks.Scalar, mockTypeTree.Object);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void GetDefaultValueForUnknownIdWithNullTypeTreeReturnsNull()
        {
            object result =
                TypeInfo.GetDefaultValue(new NodeId(50000u), ValueRanks.Scalar, null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void CreateArrayWithNullDimensionsThrows()
        {
            Assert.That(
                () => TypeInfo.CreateArray(BuiltInType.Int32, null),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void CreateArrayWithEmptyDimensionsThrows()
        {
            Assert.That(
                () => TypeInfo.CreateArray(BuiltInType.Int32, Array.Empty<int>()),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [TestCase(BuiltInType.Null, typeof(object[]))]
        [TestCase(BuiltInType.Boolean, typeof(bool[]))]
        [TestCase(BuiltInType.SByte, typeof(sbyte[]))]
        [TestCase(BuiltInType.Byte, typeof(byte[]))]
        [TestCase(BuiltInType.Int16, typeof(short[]))]
        [TestCase(BuiltInType.UInt16, typeof(ushort[]))]
        [TestCase(BuiltInType.Int32, typeof(int[]))]
        [TestCase(BuiltInType.UInt32, typeof(uint[]))]
        [TestCase(BuiltInType.Int64, typeof(long[]))]
        [TestCase(BuiltInType.UInt64, typeof(ulong[]))]
        [TestCase(BuiltInType.Float, typeof(float[]))]
        [TestCase(BuiltInType.Double, typeof(double[]))]
        [TestCase(BuiltInType.String, typeof(string[]))]
        [TestCase(BuiltInType.DateTime, typeof(DateTimeUtc[]))]
        [TestCase(BuiltInType.Guid, typeof(Uuid[]))]
        [TestCase(BuiltInType.ByteString, typeof(ByteString[]))]
        [TestCase(BuiltInType.XmlElement, typeof(XmlElement[]))]
        [TestCase(BuiltInType.StatusCode, typeof(StatusCode[]))]
        [TestCase(BuiltInType.NodeId, typeof(NodeId[]))]
        [TestCase(BuiltInType.ExpandedNodeId, typeof(ExpandedNodeId[]))]
        [TestCase(BuiltInType.QualifiedName, typeof(QualifiedName[]))]
        [TestCase(BuiltInType.LocalizedText, typeof(LocalizedText[]))]
        [TestCase(BuiltInType.Variant, typeof(Variant[]))]
        [TestCase(BuiltInType.DataValue, typeof(DataValue[]))]
        [TestCase(BuiltInType.ExtensionObject, typeof(ExtensionObject[]))]
        [TestCase(BuiltInType.DiagnosticInfo, typeof(DiagnosticInfo[]))]
        [TestCase(BuiltInType.Enumeration, typeof(int[]))]
        [TestCase(BuiltInType.Number, typeof(Variant[]))]
        [TestCase(BuiltInType.Integer, typeof(Variant[]))]
        [TestCase(BuiltInType.UInteger, typeof(Variant[]))]
        public void CreateOneDimensionalArrayReturnsCorrectType(BuiltInType type, Type expectedArrayType)
        {
            Array array = TypeInfo.CreateArray(type, 3);
            Assert.That(array, Is.Not.Null);
            Assert.That(array.GetType(), Is.EqualTo(expectedArrayType));
            Assert.That(array, Has.Length.EqualTo(3));
        }

        [Test]
        public void CreateMultiDimensionalArrayReturnsCorrectType()
        {
            Array array = TypeInfo.CreateArray(BuiltInType.Int32, 2, 3);
            Assert.That(array, Is.Not.Null);
            Assert.That(array.Rank, Is.EqualTo(2));
            Assert.That(array.GetLength(0), Is.EqualTo(2));
            Assert.That(array.GetLength(1), Is.EqualTo(3));
        }

        [Test]
        public void CreateMultiDimensionalArrayForAllTypes()
        {
            BuiltInType[] types =
            [
                BuiltInType.Null, BuiltInType.Boolean, BuiltInType.SByte, BuiltInType.Byte,
                BuiltInType.Int16, BuiltInType.UInt16, BuiltInType.Int32, BuiltInType.UInt32,
                BuiltInType.Int64, BuiltInType.UInt64, BuiltInType.Float, BuiltInType.Double,
                BuiltInType.String, BuiltInType.DateTime, BuiltInType.Guid, BuiltInType.ByteString,
                BuiltInType.XmlElement, BuiltInType.StatusCode, BuiltInType.NodeId,
                BuiltInType.ExpandedNodeId, BuiltInType.QualifiedName, BuiltInType.LocalizedText,
                BuiltInType.Variant, BuiltInType.DataValue, BuiltInType.ExtensionObject,
                BuiltInType.DiagnosticInfo, BuiltInType.Enumeration, BuiltInType.Number,
                BuiltInType.Integer, BuiltInType.UInteger
            ];

            foreach (BuiltInType type in types)
            {
                Array array = TypeInfo.CreateArray(type, 2, 3);
                Assert.That(array, Is.Not.Null, $"Failed for type {type}");
                Assert.That(array.Rank, Is.EqualTo(2), $"Wrong rank for type {type}");
            }
        }

        [Test]
        public void GetDataTypeIdForSystemTypeReturnsExpectedNodeId()
        {
            Assert.That(TypeInfo.GetDataTypeId(typeof(int)), Is.EqualTo(new NodeId(6u)));
            Assert.That(TypeInfo.GetDataTypeId(typeof(bool)), Is.EqualTo(new NodeId(1u)));
            Assert.That(TypeInfo.GetDataTypeId(typeof(double)), Is.EqualTo(new NodeId(11u)));
            Assert.That(TypeInfo.GetDataTypeId(typeof(string)), Is.EqualTo(new NodeId(12u)));
        }

        [Test]
        public void GetDataTypeIdForEnumReturnsEnumeration()
        {
            NodeId result = TypeInfo.GetDataTypeId(typeof(NodeClass));
            Assert.That(result, Is.EqualTo(new NodeId(29u)));
        }

        [Test]
        public void GetDataTypeIdForEnumArrayReturnsEnumeration()
        {
            NodeId result = TypeInfo.GetDataTypeId(typeof(NodeClass[]));
            Assert.That(result, Is.EqualTo(new NodeId(29u)));
        }

        [Test]
        public void GetValueRankForScalarReturnsScalar()
        {
            Assert.That(
                TypeInfo.GetValueRank(typeof(int)),
                Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void GetValueRankForOneDimensionArrayReturnsOneDimension()
        {
            Assert.That(
                TypeInfo.GetValueRank(typeof(int[])),
                Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void GetValueRankForEnumReturnsScalar()
        {
            Assert.That(
                TypeInfo.GetValueRank(typeof(NodeClass)),
                Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void GetValueRankForEnumArrayReturnsOneDimension()
        {
            Assert.That(
                TypeInfo.GetValueRank(typeof(NodeClass[])),
                Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void GetXmlNameForNullTypeReturnsNull()
        {
#pragma warning disable IDE0004 // Remove Unnecessary Cast
            Assert.That(TypeInfo.GetXmlName((Type)null), Is.Null);
#pragma warning restore IDE0004 // Remove Unnecessary Cast
        }

        [Test]
        public void GetXmlNameForTypeWithDataContractReturnsXmlQualifiedName()
        {
            XmlQualifiedName result = TypeInfo.GetXmlName(typeof(LocalizedText));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.Not.Empty);
        }

        [Test]
        public void GetXmlNameForTypeWithoutDataContractReturnsFullName()
        {
            XmlQualifiedName result = TypeInfo.GetXmlName(typeof(int));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo(typeof(int).Name));
            Assert.That(result.Namespace, Is.EqualTo(Namespaces.OpcUaXsd));
        }

        [Test]
        public void GetXmlNameForObjectWithNullReturnsNull()
        {
            XmlQualifiedName result = TypeInfo.GetXmlName(null, null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetXmlNameForNonDynamicObjectReturnsTypeName()
        {
            XmlQualifiedName result = TypeInfo.GetXmlName(42, null);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void GetXmlNameForDynamicComplexTypeInstanceReturnsXmlName()
        {
            var mockDynamic = new Mock<IDynamicComplexTypeInstance>();
            var expectedXmlName = new XmlQualifiedName("TestType", "http://test.org");
            mockDynamic
                .Setup(d => d.GetXmlName(It.IsAny<IServiceMessageContext>()))
                .Returns(expectedXmlName);

            XmlQualifiedName result = TypeInfo.GetXmlName(mockDynamic.Object, null);
            Assert.That(result, Is.EqualTo(expectedXmlName));
        }

        [Test]
        public void GetXmlNameForDynamicComplexTypeInstanceWithNullXmlNameFallsBack()
        {
            var mockDynamic = new Mock<IDynamicComplexTypeInstance>();
            mockDynamic
                .Setup(d => d.GetXmlName(It.IsAny<IServiceMessageContext>()))
                .Returns((XmlQualifiedName)null);

            XmlQualifiedName result = TypeInfo.GetXmlName(mockDynamic.Object, null);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void InstanceGetDataTypeIdForNullBuiltInTypeReturnsNullNodeId()
        {
            var typeInfo = new TypeInfo(BuiltInType.Null, ValueRanks.Scalar);
            NodeId result = typeInfo.GetDataTypeId(Variant.Null, null, null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void InstanceGetDataTypeIdForNonExtensionObjectReturnsBuiltInTypeNodeId()
        {
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            NodeId result = typeInfo.GetDataTypeId(new Variant(42), null, null);
            Assert.That(result, Is.EqualTo(new NodeId(6u)));
        }

        [Test]
        public void ScalarsStaticFieldsAreCorrect()
        {
            Assert.That(TypeInfo.Scalars.Boolean.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
            Assert.That(TypeInfo.Scalars.Boolean.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            Assert.That(TypeInfo.Scalars.Int32.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(TypeInfo.Scalars.Double.BuiltInType, Is.EqualTo(BuiltInType.Double));
            Assert.That(TypeInfo.Scalars.String.BuiltInType, Is.EqualTo(BuiltInType.String));
            Assert.That(TypeInfo.Scalars.Variant.BuiltInType, Is.EqualTo(BuiltInType.Variant));
            Assert.That(TypeInfo.Scalars.ExtensionObject.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));
        }

        [Test]
        public void ArraysStaticFieldsAreCorrect()
        {
            Assert.That(
                TypeInfo.Arrays.Boolean.BuiltInType,
                Is.EqualTo(BuiltInType.Boolean));
            Assert.That(
                TypeInfo.Arrays.Boolean.ValueRank,
                Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(
                TypeInfo.Arrays.Int32.BuiltInType,
                Is.EqualTo(BuiltInType.Int32));
            Assert.That(
                TypeInfo.Arrays.Double.BuiltInType,
                Is.EqualTo(BuiltInType.Double));
        }

        [Test]
        public void OneOrMoreDimensionsStaticFieldsAreCorrect()
        {
            Assert.That(
                TypeInfo.OneOrMoreDimensions.Boolean.BuiltInType,
                Is.EqualTo(BuiltInType.Boolean));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.Boolean.ValueRank,
                Is.EqualTo(ValueRanks.OneOrMoreDimensions));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.SByte.BuiltInType,
                Is.EqualTo(BuiltInType.SByte));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.Byte.BuiltInType,
                Is.EqualTo(BuiltInType.Byte));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.Int16.BuiltInType,
                Is.EqualTo(BuiltInType.Int16));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.UInt16.BuiltInType,
                Is.EqualTo(BuiltInType.UInt16));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.Int32.BuiltInType,
                Is.EqualTo(BuiltInType.Int32));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.UInt32.BuiltInType,
                Is.EqualTo(BuiltInType.UInt32));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.Int64.BuiltInType,
                Is.EqualTo(BuiltInType.Int64));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.UInt64.BuiltInType,
                Is.EqualTo(BuiltInType.UInt64));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.Float.BuiltInType,
                Is.EqualTo(BuiltInType.Float));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.Double.BuiltInType,
                Is.EqualTo(BuiltInType.Double));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.String.BuiltInType,
                Is.EqualTo(BuiltInType.String));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.DateTime.BuiltInType,
                Is.EqualTo(BuiltInType.DateTime));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.Guid.BuiltInType,
                Is.EqualTo(BuiltInType.Guid));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.ByteString.BuiltInType,
                Is.EqualTo(BuiltInType.ByteString));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.XmlElement.BuiltInType,
                Is.EqualTo(BuiltInType.XmlElement));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.NodeId.BuiltInType,
                Is.EqualTo(BuiltInType.NodeId));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.ExpandedNodeId.BuiltInType,
                Is.EqualTo(BuiltInType.ExpandedNodeId));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.StatusCode.BuiltInType,
                Is.EqualTo(BuiltInType.StatusCode));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.QualifiedName.BuiltInType,
                Is.EqualTo(BuiltInType.QualifiedName));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.LocalizedText.BuiltInType,
                Is.EqualTo(BuiltInType.LocalizedText));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.Variant.BuiltInType,
                Is.EqualTo(BuiltInType.Variant));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.DataValue.BuiltInType,
                Is.EqualTo(BuiltInType.DataValue));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.DiagnosticInfo.BuiltInType,
                Is.EqualTo(BuiltInType.DiagnosticInfo));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.ExtensionObject.BuiltInType,
                Is.EqualTo(BuiltInType.ExtensionObject));
            Assert.That(
                TypeInfo.OneOrMoreDimensions.Enumeration.BuiltInType,
                Is.EqualTo(BuiltInType.Enumeration));
        }

        [Test]
        public void IsUnknownReturnsTrueForDefault()
        {
            var typeInfo = default(TypeInfo);
            Assert.That(typeInfo.IsUnknown, Is.True);
        }

        [Test]
        public void IsUnknownReturnsFalseForConstructed()
        {
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(typeInfo.IsUnknown, Is.False);
        }

        [Test]
        public void IsScalarReturnsTrueForScalar()
        {
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(typeInfo.IsScalar, Is.True);
        }

        [Test]
        public void IsArrayReturnsTrueForOneDimension()
        {
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.OneDimension);
            Assert.That(typeInfo.IsArray, Is.True);
        }

        [Test]
        public void IsArrayReturnsTrueForOneOrMoreDimensions()
        {
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.OneOrMoreDimensions);
            Assert.That(typeInfo.IsArray, Is.True);
        }

        [Test]
        public void IsMatrixReturnsTrueForTwoDimensions()
        {
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.TwoDimensions);
            Assert.That(typeInfo.IsMatrix, Is.True);
        }

        [Test]
#pragma warning disable CS0618 // Type or member is obsolete
        public void ConstructFromVariantReturnsTypeInfo()
        {
            var variant = new Variant(42);
            var result = TypeInfo.Construct(variant);
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }
#pragma warning restore CS0618

        [Test]
        public void ConstructFromObjectTypeReturnsVariant()
        {
            var result = TypeInfo.Construct(typeof(object));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Variant));
        }

        [Test]
        public void ConstructFromSingleTypeReturnsFloat()
        {
            var result = TypeInfo.Construct(typeof(float));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Float));
        }

        [Test]
        public void ConstructFromInt32ArrayReturnsInt32Array()
        {
            var result = TypeInfo.Construct(typeof(int[]));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void ConstructFromThreeDimensionalArrayReturnsCorrectRank()
        {
            var result = TypeInfo.Construct(typeof(double[,,]));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Double));
            Assert.That(result.ValueRank, Is.EqualTo(3));
        }

        [Test]
        public void ConstructForListOfIntReturnsIntArray()
        {
            var result = TypeInfo.Construct(typeof(List<int>));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(result.IsArray, Is.True);
        }

        [Test]
        public void ConstructForNonEnumerableGenericTypeReturnsUnknown()
        {
            var result = TypeInfo.Construct(typeof(Task<int>));
            Assert.That(result.IsUnknown, Is.True);
        }
    }
}
