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
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Coverage tests for the <see cref="TypeInfo"/> struct.
    /// </summary>
    [TestFixture]
    [Category("TypeInfo")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class TypeInfoCoverageTests
    {
        #region Constructor Tests

        [Test]
        public void ConstructorWithValueRankOutOfRangeThrows()
        {
            // Covers lines 57-58: ArgumentOutOfRangeException for value rank > short.MaxValue
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

        #endregion

        #region GetHashCode / Equals / Operators Tests

        [Test]
        public void GetHashCodeForUnknownReturnsZero()
        {
            // Covers lines 110-113: IsUnknown returns 0
            var unknown = TypeInfo.Unknown;
            Assert.That(unknown.GetHashCode(), Is.EqualTo(0));
        }

        [Test]
        public void GetHashCodeForKnownTypeReturnsNonZero()
        {
            // Covers lines 115-116: HashCode.Combine path
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(typeInfo.GetHashCode(), Is.Not.EqualTo(0));
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
            // Covers line 123: null => IsUnknown
            var unknown = TypeInfo.Unknown;
            Assert.That(unknown.Equals((object)null), Is.True);
        }

        [Test]
        public void EqualsObjectWithNullReturnsFalseForKnown()
        {
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(typeInfo.Equals((object)null), Is.False);
        }

        [Test]
        public void EqualsObjectWithTypeInfoDelegatesToTypedEquals()
        {
            // Covers line 124: TypeInfo typeInfo => Equals(typeInfo)
            var a = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            object b = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(a.Equals(b), Is.True);
        }

        [Test]
        public void EqualsObjectWithOtherTypeReturnsFalse()
        {
            // Covers line 125: _ => base.Equals(obj)
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(typeInfo.Equals("not a TypeInfo"), Is.False);
        }

        [Test]
        public void EqualityOperatorReturnsTrueForEqualValues()
        {
            // Covers lines 140-142: op_Equality
            var a = new TypeInfo(BuiltInType.Double, ValueRanks.OneDimension);
            var b = new TypeInfo(BuiltInType.Double, ValueRanks.OneDimension);
            Assert.That(a == b, Is.True);
        }

        [Test]
        public void InequalityOperatorReturnsTrueForDifferentValues()
        {
            var a = new TypeInfo(BuiltInType.Double, ValueRanks.Scalar);
            var b = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(a != b, Is.True);
        }

        #endregion

        #region ToString Tests

        [Test]
        public void ToStringScalarReturnsTypeName()
        {
            // Covers lines 152-154: ToString() delegates to ToString(null, null)
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
            // Covers line 179: throw FormatException for non-null format
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            Assert.That(
                () => typeInfo.ToString("X", null),
                Throws.TypeOf<FormatException>());
        }

        #endregion

        #region WithBuiltInType / WithValueRank Tests

        [Test]
        public void WithBuiltInTypeReturnsNewTypeInfoWithSameValueRank()
        {
            // Covers lines 189-191
            var original = new TypeInfo(BuiltInType.Int32, ValueRanks.OneDimension);
            var changed = original.WithBuiltInType(BuiltInType.Double);
            Assert.That(changed.BuiltInType, Is.EqualTo(BuiltInType.Double));
            Assert.That(changed.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void WithValueRankReturnsNewTypeInfoWithSameBuiltInType()
        {
            // Covers lines 200-202
            var original = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            var changed = original.WithValueRank(ValueRanks.OneDimension);
            Assert.That(changed.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(changed.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        #endregion

        #region IsNumericType Tests

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
            // Covers lines 904-916
            Assert.That(TypeInfo.IsNumericType(type), Is.EqualTo(expected));
        }

        #endregion

        #region IsValueType Tests

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
            // Covers lines 926-938
            Assert.That(TypeInfo.IsValueType(type), Is.EqualTo(expected));
        }

        #endregion

        #region IsEncodingNullableType Tests

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
            // Covers lines 949-961
            Assert.That(TypeInfo.IsEncodingNullableType(type), Is.EqualTo(expected));
        }

        #endregion

        #region GetBuiltInType(NodeId) Tests

        [Test]
        public void GetBuiltInTypeWithNullNodeIdReturnsNull()
        {
            // Covers lines 844-845: null/non-ns0 path
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
            // Covers line 851: DataTypes.UtcTime
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(294u)), Is.EqualTo(BuiltInType.DateTime));
        }

        [Test]
        public void GetBuiltInTypeForByteStringSubtypesReturnsByteString()
        {
            // Covers line 861: DataTypes.ApplicationInstanceCertificate etc.
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(311u)), Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.Image = 30
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(30u)), Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.ImageBMP = 2000
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(2000u)), Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.ImageGIF = 2001
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(2001u)), Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.ImageJPG = 2002
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(2002u)), Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.ImagePNG = 2003
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(2003u)), Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.AudioDataType = 16307
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(16307u)), Is.EqualTo(BuiltInType.ByteString));
            // DataTypes.ContinuationPoint = 521
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(521u)), Is.EqualTo(BuiltInType.ByteString));
        }

        [Test]
        public void GetBuiltInTypeForSessionAuthTokenReturnsNodeId()
        {
            // Covers line 864: DataTypes.SessionAuthenticationToken = 388
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(388u)), Is.EqualTo(BuiltInType.NodeId));
        }

        [Test]
        public void GetBuiltInTypeForDurationReturnsDouble()
        {
            // Covers line 867: DataTypes.Duration = 290
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(290u)), Is.EqualTo(BuiltInType.Double));
        }

        [Test]
        public void GetBuiltInTypeForUInt32SubtypesReturnsUInt32()
        {
            // Covers line 873: DataTypes.IntegerId, Index, VersionTime, Counter
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(288u)), Is.EqualTo(BuiltInType.UInt32)); // IntegerId
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(17588u)), Is.EqualTo(BuiltInType.UInt32)); // Index
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(20998u)), Is.EqualTo(BuiltInType.UInt32)); // VersionTime
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(289u)), Is.EqualTo(BuiltInType.UInt32)); // Counter
        }

        [Test]
        public void GetBuiltInTypeForBitFieldMaskReturnsUInt64()
        {
            // Covers line 876: DataTypes.BitFieldMaskDataType = 11737
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(11737u)), Is.EqualTo(BuiltInType.UInt64));
        }

        [Test]
        public void GetBuiltInTypeForStringSubtypesReturnsString()
        {
            // Covers line 886: DateString, DecimalString, etc.
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(12881u)), Is.EqualTo(BuiltInType.String)); // DateString
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(12878u)), Is.EqualTo(BuiltInType.String)); // DecimalString
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(12879u)), Is.EqualTo(BuiltInType.String)); // DurationString
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(295u)), Is.EqualTo(BuiltInType.String)); // LocaleId
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(12877u)), Is.EqualTo(BuiltInType.String)); // NormalizedString
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(291u)), Is.EqualTo(BuiltInType.String)); // NumericRange
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(12880u)), Is.EqualTo(BuiltInType.String)); // TimeString
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(23751u)), Is.EqualTo(BuiltInType.String)); // UriString
        }

        [Test]
        public void GetBuiltInTypeForUnknownHighIdReturnsNull()
        {
            // Covers lines 888-890: id > DiagnosticInfo and not Enumeration
            Assert.That(TypeInfo.GetBuiltInType(new NodeId(9999u)), Is.EqualTo(BuiltInType.Null));
        }

        #endregion

        #region GetBuiltInType(NodeId, ITypeTable) Tests

        [Test]
        public void GetBuiltInTypeWithTypeTreeResolvesViaSuperType()
        {
            // Covers lines 972-996: type tree loop
            var mockTypeTree = new Mock<ITypeTable>();
            // Custom type 1000 -> supertype Int32 (id=6)
            mockTypeTree
                .Setup(t => t.FindSuperType(new NodeId(1000u)))
                .Returns(new NodeId(6u));

            var result = TypeInfo.GetBuiltInType(new NodeId(1000u), mockTypeTree.Object);
            Assert.That(result, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public void GetBuiltInTypeWithNullTypeTreeReturnsNull()
        {
            // Covers lines 987-989: typeTree == null break
            var result = TypeInfo.GetBuiltInType(new NodeId(1000u), null);
            Assert.That(result, Is.EqualTo(BuiltInType.Null));
        }

        [Test]
        public void GetBuiltInTypeWithTypeTreeReturnsNullWhenSuperTypeIsNull()
        {
            var mockTypeTree = new Mock<ITypeTable>();
            mockTypeTree
                .Setup(t => t.FindSuperType(It.IsAny<NodeId>()))
                .Returns(NodeId.Null);

            var result = TypeInfo.GetBuiltInType(new NodeId(1000u), mockTypeTree.Object);
            Assert.That(result, Is.EqualTo(BuiltInType.Null));
        }

        #endregion

        #region GetBuiltInTypeAsync Tests

        [Test]
        public async Task GetBuiltInTypeAsyncWithKnownTypeReturnsDirectly()
        {
            // Covers lines 1012-1022
            var result = await TypeInfo.GetBuiltInTypeAsync(
                new NodeId(6u), null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public async Task GetBuiltInTypeAsyncWithTypeTreeResolvesViaSuperType()
        {
            // Covers lines 1026-1032
            var mockTypeTree = new Mock<ITypeTable>();
            mockTypeTree
                .Setup(t => t.FindSuperTypeAsync(new NodeId(1000u), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new NodeId(6u));

            var result = await TypeInfo.GetBuiltInTypeAsync(
                new NodeId(1000u), mockTypeTree.Object, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public async Task GetBuiltInTypeAsyncWithNullTypeTreeReturnsNull()
        {
            // Covers lines 1026-1028: typeTree == null break
            var result = await TypeInfo.GetBuiltInTypeAsync(
                new NodeId(1000u), null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(BuiltInType.Null));
        }

        [Test]
        public async Task GetBuiltInTypeAsyncWithNullNodeIdReturnsNull()
        {
            var result = await TypeInfo.GetBuiltInTypeAsync(
                NodeId.Null, null, CancellationToken.None).ConfigureAwait(false);
            Assert.That(result, Is.EqualTo(BuiltInType.Null));
        }

        #endregion

        #region GetDataTypeId(TypeInfo) Tests

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
            // Covers lines 765-831: switch statement for GetDataTypeId(TypeInfo)
            var typeInfo = new TypeInfo(type, ValueRanks.Scalar);
            var result = TypeInfo.GetDataTypeId(typeInfo);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsNull, Is.False);
        }

        [Test]
        public void GetDataTypeIdForNullTypeReturnsNullNodeId()
        {
            var typeInfo = new TypeInfo(BuiltInType.Null, ValueRanks.Scalar);
            var result = TypeInfo.GetDataTypeId(typeInfo);
            Assert.That(result.IsNull, Is.True);
        }

        #endregion

        #region GetDataTypeId(Variant) Tests

        [Test]
        public void GetDataTypeIdForNullVariantReturnsNullNodeId()
        {
            // Covers lines 734-737
            var result = TypeInfo.GetDataTypeId(Variant.Null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void GetDataTypeIdForIntVariantReturnsInt32DataType()
        {
            // Covers line 748
            var variant = new Variant(42);
            var result = TypeInfo.GetDataTypeId(variant);
            Assert.That(result, Is.EqualTo(new NodeId(6u))); // DataTypes.Int32
        }

        #endregion

        #region GetSystemType(ExpandedNodeId, IEncodeableTypeLookup) Tests

        [Test]
        public void GetSystemTypeWithNullExpandedNodeIdReturnsNull()
        {
            // Covers lines 1044-1047
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(ExpandedNodeId.Null, mockFactory.Object);
            Assert.That(result, Is.Null);
        }

        [TestCase(1u, typeof(bool))]       // Boolean
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
            // Covers lines 1057-1152: switch cases
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var expandedNodeId = new ExpandedNodeId(id);
            var result = TypeInfo.GetSystemType(expandedNodeId, mockFactory.Object);
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void GetSystemTypeForDateTimeExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(13u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(DateTimeUtc)));
        }

        [Test]
        public void GetSystemTypeForGuidExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(14u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(Uuid)));
        }

        [Test]
        public void GetSystemTypeForByteStringExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(15u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(ByteString)));
        }

        [Test]
        public void GetSystemTypeForXmlElementExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(16u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(XmlElement)));
        }

        [Test]
        public void GetSystemTypeForNodeIdExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(17u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(NodeId)));
        }

        [Test]
        public void GetSystemTypeForExpandedNodeIdType()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(18u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(ExpandedNodeId)));
        }

        [Test]
        public void GetSystemTypeForStatusCodeExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(19u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(StatusCode)));
        }

        [Test]
        public void GetSystemTypeForDiagnosticInfoExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(25u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(DiagnosticInfo)));
        }

        [Test]
        public void GetSystemTypeForQualifiedNameExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(20u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(QualifiedName)));
        }

        [Test]
        public void GetSystemTypeForLocalizedTextExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(21u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(LocalizedText)));
        }

        [Test]
        public void GetSystemTypeForDataValueExpandedNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(23u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(DataValue)));
        }

        [Test]
        public void GetSystemTypeForBaseDataTypeReturnsVariant()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(24u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(Variant)));
        }

        [Test]
        public void GetSystemTypeForStructureReturnsExtensionObject()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(22u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(ExtensionObject)));
        }

        [Test]
        public void GetSystemTypeForNumberReturnsVariant()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(26u), mockFactory.Object), Is.EqualTo(typeof(Variant)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(27u), mockFactory.Object), Is.EqualTo(typeof(Variant)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(28u), mockFactory.Object), Is.EqualTo(typeof(Variant)));
        }

        [Test]
        public void GetSystemTypeForEnumerationReturnsInt()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(29u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(int)));
        }

        [Test]
        public void GetSystemTypeForUtcTimeSubtypeReturnsDateTimeUtc()
        {
            // Covers UtcTime goto case
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(294u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(DateTimeUtc)));
        }

        [Test]
        public void GetSystemTypeForByteStringSubtypes()
        {
            // Covers ApplicationInstanceCertificate, AudioDataType, ContinuationPoint,
            // Image, ImageBMP, ImageGIF, ImageJPG, ImagePNG goto cases
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(311u), mockFactory.Object), Is.EqualTo(typeof(ByteString)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(16307u), mockFactory.Object), Is.EqualTo(typeof(ByteString)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(521u), mockFactory.Object), Is.EqualTo(typeof(ByteString)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(30u), mockFactory.Object), Is.EqualTo(typeof(ByteString)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(2000u), mockFactory.Object), Is.EqualTo(typeof(ByteString)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(2001u), mockFactory.Object), Is.EqualTo(typeof(ByteString)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(2002u), mockFactory.Object), Is.EqualTo(typeof(ByteString)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(2003u), mockFactory.Object), Is.EqualTo(typeof(ByteString)));
        }

        [Test]
        public void GetSystemTypeForSessionAuthTokenReturnsNodeId()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(388u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(NodeId)));
        }

        [Test]
        public void GetSystemTypeForDurationReturnsDouble()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(290u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(double)));
        }

        [Test]
        public void GetSystemTypeForUInt32Subtypes()
        {
            // IntegerId, Index, VersionTime, Counter
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(288u), mockFactory.Object), Is.EqualTo(typeof(uint)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(17588u), mockFactory.Object), Is.EqualTo(typeof(uint)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(20998u), mockFactory.Object), Is.EqualTo(typeof(uint)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(289u), mockFactory.Object), Is.EqualTo(typeof(uint)));
        }

        [Test]
        public void GetSystemTypeForBitFieldMaskReturnsULong()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            var result = TypeInfo.GetSystemType(new ExpandedNodeId(11737u), mockFactory.Object);
            Assert.That(result, Is.EqualTo(typeof(ulong)));
        }

        [Test]
        public void GetSystemTypeForStringSubtypes()
        {
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(12881u), mockFactory.Object), Is.EqualTo(typeof(string)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(12878u), mockFactory.Object), Is.EqualTo(typeof(string)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(12879u), mockFactory.Object), Is.EqualTo(typeof(string)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(295u), mockFactory.Object), Is.EqualTo(typeof(string)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(12877u), mockFactory.Object), Is.EqualTo(typeof(string)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(291u), mockFactory.Object), Is.EqualTo(typeof(string)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(12880u), mockFactory.Object), Is.EqualTo(typeof(string)));
            Assert.That(TypeInfo.GetSystemType(new ExpandedNodeId(23751u), mockFactory.Object), Is.EqualTo(typeof(string)));
        }

        [Test]
        public void GetSystemTypeForNonNs0FallsBackToFactory()
        {
            // Covers lines 1050-1054: non-ns0 path -> factory.GetSystemType
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            mockFactory
                .Setup(f => f.TryGetEncodeableType(
                    It.IsAny<ExpandedNodeId>(),
                    out It.Ref<IEncodeableType>.IsAny))
                .Returns(false);

            var nonNs0 = new ExpandedNodeId(1u, 2);
            var result = TypeInfo.GetSystemType(nonNs0, mockFactory.Object);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetSystemTypeForUnknownIdFallsBackToFactory()
        {
            // Covers default case
            var mockFactory = new Mock<IEncodeableTypeLookup>();
            mockFactory
                .Setup(f => f.TryGetEncodeableType(
                    It.IsAny<ExpandedNodeId>(),
                    out It.Ref<IEncodeableType>.IsAny))
                .Returns(false);

            var unknownId = new ExpandedNodeId(99999u);
            var result = TypeInfo.GetSystemType(unknownId, mockFactory.Object);
            Assert.That(result, Is.Null);
        }

        #endregion

        #region GetSystemType(BuiltInType, int valueRank) Tests

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
            // Covers lines 1513-1578: scalar switch
            Assert.That(TypeInfo.GetSystemType(type, ValueRanks.Scalar), Is.EqualTo(expected));
        }

        [Test]
        public void GetSystemTypeScalarDateTimeReturnsDateTimeUtc()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.DateTime, ValueRanks.Scalar), Is.EqualTo(typeof(DateTimeUtc)));
        }

        [Test]
        public void GetSystemTypeScalarGuidReturnsUuid()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Guid, ValueRanks.Scalar), Is.EqualTo(typeof(Uuid)));
        }

        [Test]
        public void GetSystemTypeScalarByteStringReturnsByteString()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.ByteString, ValueRanks.Scalar), Is.EqualTo(typeof(ByteString)));
        }

        [Test]
        public void GetSystemTypeScalarXmlElementReturnsXmlElement()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.XmlElement, ValueRanks.Scalar), Is.EqualTo(typeof(XmlElement)));
        }

        [Test]
        public void GetSystemTypeScalarNodeIdReturnsNodeId()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.NodeId, ValueRanks.Scalar), Is.EqualTo(typeof(NodeId)));
        }

        [Test]
        public void GetSystemTypeScalarExpandedNodeIdReturnsExpandedNodeId()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.ExpandedNodeId, ValueRanks.Scalar), Is.EqualTo(typeof(ExpandedNodeId)));
        }

        [Test]
        public void GetSystemTypeScalarLocalizedTextReturnsLocalizedText()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.LocalizedText, ValueRanks.Scalar), Is.EqualTo(typeof(LocalizedText)));
        }

        [Test]
        public void GetSystemTypeScalarQualifiedNameReturnsQualifiedName()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.QualifiedName, ValueRanks.Scalar), Is.EqualTo(typeof(QualifiedName)));
        }

        [Test]
        public void GetSystemTypeScalarStatusCodeReturnsStatusCode()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.StatusCode, ValueRanks.Scalar), Is.EqualTo(typeof(StatusCode)));
        }

        [Test]
        public void GetSystemTypeScalarDiagnosticInfoReturnsDiagnosticInfo()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.DiagnosticInfo, ValueRanks.Scalar), Is.EqualTo(typeof(DiagnosticInfo)));
        }

        [Test]
        public void GetSystemTypeScalarDataValueReturnsDataValue()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.DataValue, ValueRanks.Scalar), Is.EqualTo(typeof(DataValue)));
        }

        [Test]
        public void GetSystemTypeScalarVariantReturnsVariant()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Variant, ValueRanks.Scalar), Is.EqualTo(typeof(Variant)));
        }

        [Test]
        public void GetSystemTypeScalarExtensionObjectReturnsExtensionObject()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.ExtensionObject, ValueRanks.Scalar), Is.EqualTo(typeof(ExtensionObject)));
        }

        [Test]
        public void GetSystemTypeScalarNumberReturnsVariant()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Number, ValueRanks.Scalar), Is.EqualTo(typeof(Variant)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Integer, ValueRanks.Scalar), Is.EqualTo(typeof(Variant)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.UInteger, ValueRanks.Scalar), Is.EqualTo(typeof(Variant)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Null, ValueRanks.Scalar), Is.EqualTo(typeof(Variant)));
        }

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
        [TestCase(BuiltInType.Enumeration, typeof(int[]))]
        public void GetSystemTypeOneDimensionReturnsArrayType(BuiltInType type, Type expected)
        {
            // Covers lines 1580-1645: OneDimension switch
            Assert.That(TypeInfo.GetSystemType(type, ValueRanks.OneDimension), Is.EqualTo(expected));
        }

        [Test]
        public void GetSystemTypeOneDimensionVariantReturnsVariantArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Variant, ValueRanks.OneDimension), Is.EqualTo(typeof(Variant[])));
        }

        [Test]
        public void GetSystemTypeOneDimensionExtensionObjectReturnsArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.ExtensionObject, ValueRanks.OneDimension), Is.EqualTo(typeof(ExtensionObject[])));
        }

        [Test]
        public void GetSystemTypeOneDimensionNullReturnsVariant()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Null, ValueRanks.OneDimension), Is.EqualTo(typeof(Variant)));
        }

        [Test]
        public void GetSystemTypeOneDimensionNumberReturnsVariantArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Number, ValueRanks.OneDimension), Is.EqualTo(typeof(Variant[])));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Integer, ValueRanks.OneDimension), Is.EqualTo(typeof(Variant[])));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.UInteger, ValueRanks.OneDimension), Is.EqualTo(typeof(Variant[])));
        }

        [Test]
        public void GetSystemTypeTwoDimensionsReturnsMdArray()
        {
            // Covers lines 1647-1712: TwoDimensions switch
            var result = TypeInfo.GetSystemType(BuiltInType.Int32, ValueRanks.TwoDimensions);
            Assert.That(result, Is.EqualTo(typeof(int).MakeArrayType(2)));
        }

        [Test]
        public void GetSystemTypeTwoDimensionsForAllBuiltInTypes()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Boolean, 2), Is.EqualTo(typeof(bool).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.SByte, 2), Is.EqualTo(typeof(sbyte).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Byte, 2), Is.EqualTo(typeof(byte).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Int16, 2), Is.EqualTo(typeof(short).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.UInt16, 2), Is.EqualTo(typeof(ushort).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Int32, 2), Is.EqualTo(typeof(int).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.UInt32, 2), Is.EqualTo(typeof(uint).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Int64, 2), Is.EqualTo(typeof(long).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.UInt64, 2), Is.EqualTo(typeof(ulong).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Float, 2), Is.EqualTo(typeof(float).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Double, 2), Is.EqualTo(typeof(double).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.String, 2), Is.EqualTo(typeof(string).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.DateTime, 2), Is.EqualTo(typeof(DateTimeUtc).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Guid, 2), Is.EqualTo(typeof(Uuid).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.ByteString, 2), Is.EqualTo(typeof(ByteString).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.XmlElement, 2), Is.EqualTo(typeof(XmlElement).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.NodeId, 2), Is.EqualTo(typeof(NodeId).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.ExpandedNodeId, 2), Is.EqualTo(typeof(ExpandedNodeId).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.LocalizedText, 2), Is.EqualTo(typeof(LocalizedText).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.QualifiedName, 2), Is.EqualTo(typeof(QualifiedName).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.StatusCode, 2), Is.EqualTo(typeof(StatusCode).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.DiagnosticInfo, 2), Is.EqualTo(typeof(DiagnosticInfo).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.DataValue, 2), Is.EqualTo(typeof(DataValue).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Variant, 2), Is.EqualTo(typeof(Variant).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.ExtensionObject, 2), Is.EqualTo(typeof(ExtensionObject).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Enumeration, 2), Is.EqualTo(typeof(int).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Number, 2), Is.EqualTo(typeof(Variant).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Integer, 2), Is.EqualTo(typeof(Variant).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.UInteger, 2), Is.EqualTo(typeof(Variant).MakeArrayType(2)));
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Null, 2), Is.EqualTo(typeof(Variant)));
        }

        [Test]
        public void GetSystemTypeNegativeValueRankReturnsVariant()
        {
            // Covers lines 1714-1717: else branch for negative value rank (not Scalar=-1)
            var result = TypeInfo.GetSystemType(BuiltInType.Int32, ValueRanks.Any);
            Assert.That(result, Is.EqualTo(typeof(Variant)));
        }

        [Test]
        public void GetSystemTypeOneDimensionDateTimeReturnsDateTimeArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.DateTime, ValueRanks.OneDimension), Is.EqualTo(typeof(DateTime[])));
        }

        [Test]
        public void GetSystemTypeOneDimensionGuidReturnsUuidArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.Guid, ValueRanks.OneDimension), Is.EqualTo(typeof(Uuid[])));
        }

        [Test]
        public void GetSystemTypeOneDimensionByteStringReturnsByteStringArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.ByteString, ValueRanks.OneDimension), Is.EqualTo(typeof(ByteString[])));
        }

        [Test]
        public void GetSystemTypeOneDimensionXmlElementReturnsXmlElementArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.XmlElement, ValueRanks.OneDimension), Is.EqualTo(typeof(XmlElement[])));
        }

        [Test]
        public void GetSystemTypeOneDimensionNodeIdReturnsNodeIdArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.NodeId, ValueRanks.OneDimension), Is.EqualTo(typeof(NodeId[])));
        }

        [Test]
        public void GetSystemTypeOneDimensionExpandedNodeIdReturnsArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.ExpandedNodeId, ValueRanks.OneDimension), Is.EqualTo(typeof(ExpandedNodeId[])));
        }

        [Test]
        public void GetSystemTypeOneDimensionLocalizedTextReturnsArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.LocalizedText, ValueRanks.OneDimension), Is.EqualTo(typeof(LocalizedText[])));
        }

        [Test]
        public void GetSystemTypeOneDimensionQualifiedNameReturnsArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.QualifiedName, ValueRanks.OneDimension), Is.EqualTo(typeof(QualifiedName[])));
        }

        [Test]
        public void GetSystemTypeOneDimensionStatusCodeReturnsArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.StatusCode, ValueRanks.OneDimension), Is.EqualTo(typeof(StatusCode[])));
        }

        [Test]
        public void GetSystemTypeOneDimensionDiagnosticInfoReturnsArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.DiagnosticInfo, ValueRanks.OneDimension), Is.EqualTo(typeof(DiagnosticInfo[])));
        }

        [Test]
        public void GetSystemTypeOneDimensionDataValueReturnsArray()
        {
            Assert.That(TypeInfo.GetSystemType(BuiltInType.DataValue, ValueRanks.OneDimension), Is.EqualTo(typeof(DataValue[])));
        }

        #endregion

        #region Construct Tests

        [Test]
        public void ConstructFromNullObjectReturnsUnknown()
        {
            // Covers lines 1738-1739
            var result = TypeInfo.Construct((object)null);
            Assert.That(result.IsUnknown, Is.True);
        }

        [Test]
        public void ConstructFromNullTypeReturnsUnknown()
        {
            // Covers lines 1763-1764
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
            // byte[] should map to ByteString scalar
            var result = TypeInfo.Construct(typeof(byte[]));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.ByteString));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void ConstructFromEnumReturnsEnumeration()
        {
            // Covers lines 1807-1809: IsEnum path
            var result = TypeInfo.Construct(typeof(NodeClass));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void ConstructFromEnumArrayReturnsEnumerationArray()
        {
            // Covers lines 1883-1885: enum array path
            var result = TypeInfo.Construct(typeof(NodeClass[]));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void ConstructFromMultiDimArrayReturnsCorrectRank()
        {
            // Covers lines 1891-1910: multi-dimensional array handling
            var result = TypeInfo.Construct(typeof(int[,]));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(result.ValueRank, Is.EqualTo(2));
        }

        [Test]
        public void ConstructFromUnknownTypeReturnsUnknown()
        {
            // Covers line 1857: unknown type returns Unknown
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

        #endregion

        #region GetDefaultVariantValue Tests

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
            // Covers lines 1943-2008: full switch in GetDefaultVariantValue(BuiltInType)
            // Should not throw for any valid built-in type
            Assert.That(() => TypeInfo.GetDefaultVariantValue(type), Throws.Nothing);
        }

        [Test]
        public void GetDefaultVariantValueWithNodeIdAndScalarRank()
        {
            // Covers lines 2018-2020: overload delegating to three-arg version
            var result = TypeInfo.GetDefaultVariantValue(new NodeId(6u), ValueRanks.Scalar);
            Assert.That(result.AsBoxedObject(), Is.Not.Null);
        }

        [Test]
        public void GetDefaultVariantValueWithNodeIdNonScalarReturnsDefault()
        {
            // Covers lines 2031-2033: valueRank != Scalar returns default
            var result = TypeInfo.GetDefaultVariantValue(new NodeId(6u), ValueRanks.OneDimension, null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void GetDefaultVariantValueWithNullNodeIdUsesTypeTree()
        {
            // Covers lines 2036-2040: null/non-ns0 path goes to GetDefaultValueInternal
            var result = TypeInfo.GetDefaultVariantValue(NodeId.Null, ValueRanks.Scalar, null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void GetDefaultVariantValueForBuiltInTypeNodeIds()
        {
            // Covers lines 2043-2046: id <= DiagnosticInfo
            var result = TypeInfo.GetDefaultVariantValue(new NodeId(1u), ValueRanks.Scalar, null);
            // Boolean default is false
            Assert.That(() => result.IsNull, Throws.Nothing);
        }

        [Test]
        public void GetDefaultVariantValueForKnownUaTypes()
        {
            // Covers lines 2050-2072: Duration, UtcTime, Counter, IntegerId, etc.
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
            // Covers lines 2075-2083: GetDefaultValueInternal local function
            var mockTypeTree = new Mock<ITypeTable>();
            mockTypeTree
                .Setup(t => t.FindSuperType(It.IsAny<NodeId>()))
                .Returns(new NodeId(6u)); // resolves to Int32

            var result = TypeInfo.GetDefaultVariantValue(new NodeId(50000u), ValueRanks.Scalar, mockTypeTree.Object);
            Assert.That(result.IsNull, Is.False);
        }

        #endregion

        #region GetDefaultValue Tests

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
            // Covers lines 2163-2220: full switch in GetDefaultValue(BuiltInType)
            Assert.That(() => TypeInfo.GetDefaultValue(type), Throws.Nothing);
        }

        [Test]
        public void GetDefaultValueWithNodeIdAndScalarRank()
        {
            // Covers lines 2230-2232: overload delegating
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(6u), ValueRanks.Scalar), Throws.Nothing);
        }

        [Test]
        public void GetDefaultValueWithNodeIdNonScalarReturnsNull()
        {
            // Covers lines 2243-2245: valueRank != Scalar returns default
            var result = TypeInfo.GetDefaultValue(new NodeId(6u), ValueRanks.OneDimension, null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetDefaultValueWithNullNodeIdReturnsNull()
        {
            // Covers lines 2248-2252
            var result = TypeInfo.GetDefaultValue(NodeId.Null, ValueRanks.Scalar, null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetDefaultValueForBuiltInTypeNodeIds()
        {
            // Covers lines 2255-2258: id <= DiagnosticInfo
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(1u), ValueRanks.Scalar, null), Throws.Nothing);
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(6u), ValueRanks.Scalar, null), Throws.Nothing);
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(11u), ValueRanks.Scalar, null), Throws.Nothing);
        }

        [Test]
        public void GetDefaultValueForKnownUaTypes()
        {
            // Covers lines 2262-2284
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(290u), ValueRanks.Scalar, null), Throws.Nothing); // Duration
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(294u), ValueRanks.Scalar, null), Throws.Nothing); // UtcTime
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(289u), ValueRanks.Scalar, null), Throws.Nothing); // Counter
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(288u), ValueRanks.Scalar, null), Throws.Nothing); // IntegerId
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(26u), ValueRanks.Scalar, null), Throws.Nothing);  // Number
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(28u), ValueRanks.Scalar, null), Throws.Nothing);  // UInteger
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(27u), ValueRanks.Scalar, null), Throws.Nothing);  // Integer
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(256u), ValueRanks.Scalar, null), Throws.Nothing); // IdType
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(257u), ValueRanks.Scalar, null), Throws.Nothing); // NodeClass
            Assert.That(() => TypeInfo.GetDefaultValue(new NodeId(29u), ValueRanks.Scalar, null), Throws.Nothing);  // Enumeration
        }

        [Test]
        public void GetDefaultValueForUnknownIdWithTypeTree()
        {
            // Covers lines 2287-2295: GetDefaultValueInternal local function
            var mockTypeTree = new Mock<ITypeTable>();
            mockTypeTree
                .Setup(t => t.FindSuperType(It.IsAny<NodeId>()))
                .Returns(new NodeId(6u)); // resolves to Int32

            var result = TypeInfo.GetDefaultValue(new NodeId(50000u), ValueRanks.Scalar, mockTypeTree.Object);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void GetDefaultValueForUnknownIdWithNullTypeTreeReturnsNull()
        {
            // Covers lines 2287-2295 where builtInType == Null
            var result = TypeInfo.GetDefaultValue(new NodeId(50000u), ValueRanks.Scalar, null);
            Assert.That(result, Is.Null);
        }

        #endregion

        #region CreateArray Tests

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
        [TestCase(BuiltInType.DateTime, typeof(DateTime[]))]
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
            // Covers lines 2315-2386: one dimensional array creation
            var array = TypeInfo.CreateArray(type, 3);
            Assert.That(array, Is.Not.Null);
            Assert.That(array.GetType(), Is.EqualTo(expectedArrayType));
            Assert.That(array.Length, Is.EqualTo(3));
        }

        [Test]
        public void CreateMultiDimensionalArrayReturnsCorrectType()
        {
            // Covers lines 2388-2456: higher dimension arrays
            var array = TypeInfo.CreateArray(BuiltInType.Int32, 2, 3);
            Assert.That(array, Is.Not.Null);
            Assert.That(array.Rank, Is.EqualTo(2));
            Assert.That(array.GetLength(0), Is.EqualTo(2));
            Assert.That(array.GetLength(1), Is.EqualTo(3));
        }

        [Test]
        public void CreateMultiDimensionalArrayForAllTypes()
        {
            // Covers the multi-dimensional switch (lines 2388-2456)
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

            foreach (var type in types)
            {
                var array = TypeInfo.CreateArray(type, 2, 3);
                Assert.That(array, Is.Not.Null, $"Failed for type {type}");
                Assert.That(array.Rank, Is.EqualTo(2), $"Wrong rank for type {type}");
            }
        }

        #endregion

        #region GetDataTypeId(Type) Tests

        [Test]
        public void GetDataTypeIdForSystemTypeReturnsExpectedNodeId()
        {
            // Covers lines 2466-2513
            Assert.That(TypeInfo.GetDataTypeId(typeof(int)), Is.EqualTo(new NodeId(6u)));
            Assert.That(TypeInfo.GetDataTypeId(typeof(bool)), Is.EqualTo(new NodeId(1u)));
            Assert.That(TypeInfo.GetDataTypeId(typeof(double)), Is.EqualTo(new NodeId(11u)));
            Assert.That(TypeInfo.GetDataTypeId(typeof(string)), Is.EqualTo(new NodeId(12u)));
        }

        [Test]
        public void GetDataTypeIdForEnumReturnsEnumeration()
        {
            // Covers lines 2471-2476: enum path
            var result = TypeInfo.GetDataTypeId(typeof(NodeClass));
            Assert.That(result, Is.EqualTo(new NodeId(29u)));
        }

        [Test]
        public void GetDataTypeIdForEnumArrayReturnsEnumeration()
        {
            // Covers lines 2473-2476: enum array path
            var result = TypeInfo.GetDataTypeId(typeof(NodeClass[]));
            Assert.That(result, Is.EqualTo(new NodeId(29u)));
        }

        #endregion

        #region GetValueRank Tests

        [Test]
        public void GetValueRankForScalarReturnsScalar()
        {
            // Covers lines 2522-2539
            Assert.That(TypeInfo.GetValueRank(typeof(int)), Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void GetValueRankForOneDimensionArrayReturnsOneDimension()
        {
            Assert.That(TypeInfo.GetValueRank(typeof(int[])), Is.EqualTo(ValueRanks.OneDimension));
        }

        [Test]
        public void GetValueRankForEnumReturnsScalar()
        {
            // Covers lines 2525-2535: enum path
            Assert.That(TypeInfo.GetValueRank(typeof(NodeClass)), Is.EqualTo(ValueRanks.Scalar));
        }

        [Test]
        public void GetValueRankForEnumArrayReturnsOneDimension()
        {
            // Covers lines 2527-2532: enum array path
            Assert.That(TypeInfo.GetValueRank(typeof(NodeClass[])), Is.EqualTo(ValueRanks.OneDimension));
        }

        #endregion

        #region GetXmlName Tests

        [Test]
        public void GetXmlNameForNullTypeReturnsNull()
        {
            // Covers lines 2552-2553
            Assert.That(TypeInfo.GetXmlName((Type)null), Is.Null);
        }

        [Test]
        public void GetXmlNameForTypeWithDataContractReturnsXmlQualifiedName()
        {
            // Covers lines 2556-2574: DataContractAttribute path
            var result = TypeInfo.GetXmlName(typeof(LocalizedText));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.Not.Empty);
        }

        [Test]
        public void GetXmlNameForTypeWithoutDataContractReturnsFullName()
        {
            // Covers line 2598: fallback to FullName
            var result = TypeInfo.GetXmlName(typeof(int));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo(typeof(int).FullName));
        }

        [Test]
        public void GetXmlNameForObjectWithNullReturnsNull()
        {
            // Covers line 2616: null object path -> GetXmlName(null?.GetType()) -> null
            var result = TypeInfo.GetXmlName(null, null);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void GetXmlNameForNonDynamicObjectReturnsTypeName()
        {
            // Covers line 2616: non-IDynamicComplexTypeInstance path
            var result = TypeInfo.GetXmlName(42, null);
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void GetXmlNameForCollectionDataContractTypeReturnsXmlQualifiedName()
        {
            // Covers lines 2586-2594: CollectionDataContractAttribute path
            var result = TypeInfo.GetXmlName(typeof(BooleanCollection));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo("ListOfBoolean"));
        }

        [Test]
        public void GetXmlNameForDynamicComplexTypeInstanceReturnsXmlName()
        {
            // Covers lines 2608-2613: IDynamicComplexTypeInstance path
            var mockDynamic = new Mock<IDynamicComplexTypeInstance>();
            var expectedXmlName = new XmlQualifiedName("TestType", "http://test.org");
            mockDynamic
                .Setup(d => d.GetXmlName(It.IsAny<IServiceMessageContext>()))
                .Returns(expectedXmlName);

            var result = TypeInfo.GetXmlName(mockDynamic.Object, null);
            Assert.That(result, Is.EqualTo(expectedXmlName));
        }

        [Test]
        public void GetXmlNameForDynamicComplexTypeInstanceWithNullXmlNameFallsBack()
        {
            // Covers lines 2608-2615: IDynamicComplexTypeInstance path where GetXmlName returns null
            var mockDynamic = new Mock<IDynamicComplexTypeInstance>();
            mockDynamic
                .Setup(d => d.GetXmlName(It.IsAny<IServiceMessageContext>()))
                .Returns((XmlQualifiedName)null);

            var result = TypeInfo.GetXmlName(mockDynamic.Object, null);
            Assert.That(result, Is.Not.Null);
        }

        #endregion

        #region Instance GetDataTypeId Tests

        [Test]
        public void InstanceGetDataTypeIdForNullBuiltInTypeReturnsNullNodeId()
        {
            // Covers lines 1477-1479
            var typeInfo = new TypeInfo(BuiltInType.Null, ValueRanks.Scalar);
            var result = typeInfo.GetDataTypeId(Variant.Null, null, null);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void InstanceGetDataTypeIdForNonExtensionObjectReturnsBuiltInTypeNodeId()
        {
            // Covers line 1502
            var typeInfo = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);
            var result = typeInfo.GetDataTypeId(new Variant(42), null, null);
            Assert.That(result, Is.EqualTo(new NodeId(6u)));
        }

        #endregion

        #region Scalars / Arrays / OneOrMoreDimensions Static Fields Tests

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
            Assert.That(TypeInfo.Arrays.Boolean.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
            Assert.That(TypeInfo.Arrays.Boolean.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
            Assert.That(TypeInfo.Arrays.Int32.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(TypeInfo.Arrays.Double.BuiltInType, Is.EqualTo(BuiltInType.Double));
        }

        [Test]
        public void OneOrMoreDimensionsStaticFieldsAreCorrect()
        {
            // Covers OneOrMoreDimensions class static constructor (lines 573-724)
            Assert.That(TypeInfo.OneOrMoreDimensions.Boolean.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
            Assert.That(TypeInfo.OneOrMoreDimensions.Boolean.ValueRank, Is.EqualTo(ValueRanks.OneOrMoreDimensions));
            Assert.That(TypeInfo.OneOrMoreDimensions.SByte.BuiltInType, Is.EqualTo(BuiltInType.SByte));
            Assert.That(TypeInfo.OneOrMoreDimensions.Byte.BuiltInType, Is.EqualTo(BuiltInType.Byte));
            Assert.That(TypeInfo.OneOrMoreDimensions.Int16.BuiltInType, Is.EqualTo(BuiltInType.Int16));
            Assert.That(TypeInfo.OneOrMoreDimensions.UInt16.BuiltInType, Is.EqualTo(BuiltInType.UInt16));
            Assert.That(TypeInfo.OneOrMoreDimensions.Int32.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(TypeInfo.OneOrMoreDimensions.UInt32.BuiltInType, Is.EqualTo(BuiltInType.UInt32));
            Assert.That(TypeInfo.OneOrMoreDimensions.Int64.BuiltInType, Is.EqualTo(BuiltInType.Int64));
            Assert.That(TypeInfo.OneOrMoreDimensions.UInt64.BuiltInType, Is.EqualTo(BuiltInType.UInt64));
            Assert.That(TypeInfo.OneOrMoreDimensions.Float.BuiltInType, Is.EqualTo(BuiltInType.Float));
            Assert.That(TypeInfo.OneOrMoreDimensions.Double.BuiltInType, Is.EqualTo(BuiltInType.Double));
            Assert.That(TypeInfo.OneOrMoreDimensions.String.BuiltInType, Is.EqualTo(BuiltInType.String));
            Assert.That(TypeInfo.OneOrMoreDimensions.DateTime.BuiltInType, Is.EqualTo(BuiltInType.DateTime));
            Assert.That(TypeInfo.OneOrMoreDimensions.Guid.BuiltInType, Is.EqualTo(BuiltInType.Guid));
            Assert.That(TypeInfo.OneOrMoreDimensions.ByteString.BuiltInType, Is.EqualTo(BuiltInType.ByteString));
            Assert.That(TypeInfo.OneOrMoreDimensions.XmlElement.BuiltInType, Is.EqualTo(BuiltInType.XmlElement));
            Assert.That(TypeInfo.OneOrMoreDimensions.NodeId.BuiltInType, Is.EqualTo(BuiltInType.NodeId));
            Assert.That(TypeInfo.OneOrMoreDimensions.ExpandedNodeId.BuiltInType, Is.EqualTo(BuiltInType.ExpandedNodeId));
            Assert.That(TypeInfo.OneOrMoreDimensions.StatusCode.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
            Assert.That(TypeInfo.OneOrMoreDimensions.QualifiedName.BuiltInType, Is.EqualTo(BuiltInType.QualifiedName));
            Assert.That(TypeInfo.OneOrMoreDimensions.LocalizedText.BuiltInType, Is.EqualTo(BuiltInType.LocalizedText));
            Assert.That(TypeInfo.OneOrMoreDimensions.Variant.BuiltInType, Is.EqualTo(BuiltInType.Variant));
            Assert.That(TypeInfo.OneOrMoreDimensions.DataValue.BuiltInType, Is.EqualTo(BuiltInType.DataValue));
            Assert.That(TypeInfo.OneOrMoreDimensions.DiagnosticInfo.BuiltInType, Is.EqualTo(BuiltInType.DiagnosticInfo));
            Assert.That(TypeInfo.OneOrMoreDimensions.ExtensionObject.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));
            Assert.That(TypeInfo.OneOrMoreDimensions.Enumeration.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
        }

        #endregion

        #region Properties Tests

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

        #endregion

        #region Construct(Variant) Obsolete Test

        [Test]
#pragma warning disable CS0618 // Type or member is obsolete
        public void ConstructFromVariantReturnsTypeInfo()
        {
            // Covers lines 1725-1727: Construct(Variant)
            var variant = new Variant(42);
            var result = TypeInfo.Construct(variant);
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(result.ValueRank, Is.EqualTo(ValueRanks.Scalar));
        }
#pragma warning restore CS0618

        #endregion

        #region Construct(Type) - Collection and Generic Paths

        [Test]
        public void ConstructFromObjectTypeReturnsVariant()
        {
            // Covers the "Object" case in GetBuiltInType(string)
            var result = TypeInfo.Construct(typeof(object));
            Assert.That(result.BuiltInType, Is.EqualTo(BuiltInType.Variant));
        }

        [Test]
        public void ConstructFromSingleTypeReturnsFloat()
        {
            // Covers the "Single" alias in GetBuiltInType(string)
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

        #endregion
    }
}
