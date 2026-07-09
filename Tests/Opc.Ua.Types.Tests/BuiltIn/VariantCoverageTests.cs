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

namespace Opc.Ua.Types.Tests.BuiltIn
{
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariantCoverageTests
    {
        [Test]
        public void ConvertToDateTimeFromStringParsesXmlDateTime()
        {
            const string text = "2024-06-15T12:00:00";
            Variant result = new Variant(text).ConvertToDateTime();
            var expected = (DateTimeUtc)System.Xml.XmlConvert.ToDateTime(
                text, System.Xml.XmlDateTimeSerializationMode.Unspecified);
            Assert.That(result.GetDateTime(), Is.EqualTo(expected));
        }

        [Test]
        public void ConvertToDateTimeReturnsSelfForDateTime()
        {
            Variant result = new Variant(Dt(3)).ConvertToDateTime();
            Assert.That(result.GetDateTime(), Is.EqualTo(Dt(3)));
        }

        [Test]
        public void ConvertToDateTimeFromUnsupportedTypeThrows()
        {
            Assert.Throws<InvalidCastException>(() => new Variant(5).ConvertToDateTime());
        }

        [Test]
        public void ConvertToGuidFromString()
        {
            Variant result = new Variant("00000000-0000-0000-0000-000000000009").ConvertToGuid();
            Assert.That(
                result.GetGuid(),
                Is.EqualTo(new Uuid(new Guid("00000000-0000-0000-0000-000000000009"))));
        }

        [Test]
        public void ConvertToGuidFromByteString()
        {
            var guid = new Guid("00000000-0000-0000-0000-000000000009");
            Variant result = new Variant(ByteString.From(guid.ToByteArray())).ConvertToGuid();
            Assert.That(result.GetGuid(), Is.EqualTo(new Uuid(guid)));
        }

        [Test]
        public void ConvertToGuidReturnsSelfForGuid()
        {
            Uuid uid = Uid(4);
            Variant result = new Variant(uid).ConvertToGuid();
            Assert.That(result.GetGuid(), Is.EqualTo(uid));
        }

        [Test]
        public void ConvertToGuidFromUnsupportedTypeThrows()
        {
            Assert.Throws<InvalidCastException>(() => new Variant(5).ConvertToGuid());
        }

        [Test]
        public void ConvertToByteStringFromHexString()
        {
            Variant result = new Variant("0A0B").ConvertToByteString();
            bool matches = result == new Variant(ByteString.From(0x0A, 0x0B));
            Assert.That(matches, Is.True);
        }

        [Test]
        public void ConvertToByteStringFromGuid()
        {
            var guid = new Guid("00000000-0000-0000-0000-000000000009");
            Variant result = new Variant(new Uuid(guid)).ConvertToByteString();
            bool matches = result == new Variant(ByteString.From(guid.ToByteArray()));
            Assert.That(matches, Is.True);
        }

        [Test]
        public void ConvertToByteStringReturnsSelfForByteString()
        {
            var bytes = ByteString.From(1, 2, 3);
            Variant result = new Variant(bytes).ConvertToByteString();
            bool matches = result == new Variant(bytes);
            Assert.That(matches, Is.True);
        }

        [Test]
        public void ConvertToByteStringFromUnsupportedTypeThrows()
        {
            Assert.Throws<InvalidCastException>(() => new Variant(5).ConvertToByteString());
        }

        [Test]
        public void ConvertToXmlElementReturnsSelfForXmlElement()
        {
            XmlElement xml = Xml("7");
            Variant result = new Variant(xml).ConvertToXmlElement();
            Assert.That(result.GetXmlElement().OuterXml, Is.EqualTo(xml.OuterXml));
        }

        [Test]
        public void ConvertToXmlElementFromUnsupportedTypeThrows()
        {
            Assert.Throws<InvalidCastException>(() => new Variant(5).ConvertToXmlElement());
        }

        [Test]
        public void ConvertToNodeIdFromString()
        {
            Variant result = new Variant("ns=1;i=5").ConvertToNodeId();
            Assert.That(result.GetNodeId(), Is.EqualTo(new NodeId(5, 1)));
        }

        [Test]
        public void ConvertToNodeIdFromExpandedNodeId()
        {
            var nodeId = new NodeId(5, 1);
            var expanded = (ExpandedNodeId)nodeId;
            Variant result = new Variant(expanded).ConvertToNodeId();
            Assert.That(result.GetNodeId(), Is.EqualTo(nodeId));
        }

        [Test]
        public void ConvertToNodeIdReturnsSelfForNodeId()
        {
            var nodeId = new NodeId(7, 2);
            Variant result = new Variant(nodeId).ConvertToNodeId();
            Assert.That(result.GetNodeId(), Is.EqualTo(nodeId));
        }

        [Test]
        public void ConvertToNodeIdFromUnsupportedTypeThrows()
        {
            Assert.Throws<InvalidCastException>(() => new Variant(5).ConvertToNodeId());
        }

        [Test]
        public void ConvertToExpandedNodeIdFromString()
        {
            Variant result = new Variant("nsu=http://test;s=X").ConvertToExpandedNodeId();
            Assert.That(
                result.GetExpandedNodeId(),
                Is.EqualTo(ExpandedNodeId.Parse("nsu=http://test;s=X")));
        }

        [Test]
        public void ConvertToExpandedNodeIdFromNodeId()
        {
            var nodeId = new NodeId(5, 1);
            Variant result = new Variant(nodeId).ConvertToExpandedNodeId();
            Assert.That(result.GetExpandedNodeId(), Is.EqualTo((ExpandedNodeId)nodeId));
        }

        [Test]
        public void ConvertToExpandedNodeIdReturnsSelfForExpandedNodeId()
        {
            ExpandedNodeId expanded = ENId("Self");
            Variant result = new Variant(expanded).ConvertToExpandedNodeId();
            Assert.That(result.GetExpandedNodeId(), Is.EqualTo(expanded));
        }

        [Test]
        public void ConvertToExpandedNodeIdFromUnsupportedTypeThrows()
        {
            Assert.Throws<InvalidCastException>(() => new Variant(5).ConvertToExpandedNodeId());
        }

        [Test]
        public void ConvertToStatusCodeFromUInt16ShiftsLeft()
        {
            Variant result = new Variant((ushort)0x1234).ConvertToStatusCode();
            Assert.That(result.GetStatusCode().Code, Is.EqualTo(0x12340000u));
        }

        [Test]
        public void ConvertToStatusCodeFromInt32()
        {
            Variant result = new Variant(0x40000000).ConvertToStatusCode();
            Assert.That(result.GetStatusCode().Code, Is.EqualTo(0x40000000u));
        }

        [Test]
        public void ConvertToStatusCodeFromUInt32()
        {
            Variant result = new Variant(0x80000000u).ConvertToStatusCode();
            Assert.That(result.GetStatusCode().Code, Is.EqualTo(0x80000000u));
        }

        [Test]
        public void ConvertToStatusCodeFromInt64()
        {
            Variant result = new Variant(0x80000000L).ConvertToStatusCode();
            Assert.That(result.GetStatusCode().Code, Is.EqualTo(0x80000000u));
        }

        [Test]
        public void ConvertToStatusCodeFromUInt64()
        {
            Variant result = new Variant(0x80000000UL).ConvertToStatusCode();
            Assert.That(result.GetStatusCode().Code, Is.EqualTo(0x80000000u));
        }

        [Test]
        public void ConvertToStatusCodeFromDecimalString()
        {
            Variant result = new Variant("1073741824").ConvertToStatusCode();
            Assert.That(result.GetStatusCode().Code, Is.EqualTo(0x40000000u));
        }

        [Test]
        public void ConvertToStatusCodeFromHexString()
        {
            Variant result = new Variant("0x80000000").ConvertToStatusCode();
            Assert.That(result.GetStatusCode().Code, Is.EqualTo(0x80000000u));
        }

        [Test]
        public void ConvertToStatusCodeReturnsSelfForStatusCode()
        {
            Variant result = new Variant(new StatusCode(0x80000000u)).ConvertToStatusCode();
            Assert.That(result.GetStatusCode().Code, Is.EqualTo(0x80000000u));
        }

        [Test]
        public void ConvertToStatusCodeFromUnsupportedTypeThrows()
        {
            Assert.Throws<InvalidCastException>(() => new Variant(1.5f).ConvertToStatusCode());
        }

        [Test]
        public void ConvertToQualifiedNameFromString()
        {
            Variant result = new Variant("1:name").ConvertToQualifiedName();
            Assert.That(result.GetQualifiedName(), Is.EqualTo(QualifiedName.Parse("1:name")));
        }

        [Test]
        public void ConvertToQualifiedNameReturnsSelfForQualifiedName()
        {
            var qualifiedName = new QualifiedName("name", 1);
            Variant result = new Variant(qualifiedName).ConvertToQualifiedName();
            Assert.That(result.GetQualifiedName(), Is.EqualTo(qualifiedName));
        }

        [Test]
        public void ConvertToQualifiedNameFromUnsupportedTypeThrows()
        {
            Assert.Throws<InvalidCastException>(() => new Variant(5).ConvertToQualifiedName());
        }

        [Test]
        public void ConvertToLocalizedTextFromString()
        {
            Variant result = new Variant("hello").ConvertToLocalizedText();
            Assert.That(result.GetLocalizedText().Text, Is.EqualTo("hello"));
        }

        [Test]
        public void ConvertToLocalizedTextReturnsSelfForLocalizedText()
        {
            Variant result = new Variant(new LocalizedText("en", "value")).ConvertToLocalizedText();
            Assert.That(result.GetLocalizedText().Text, Is.EqualTo("value"));
        }

        [Test]
        public void ConvertToLocalizedTextFromUnsupportedTypeThrows()
        {
            Assert.Throws<InvalidCastException>(() => new Variant(5).ConvertToLocalizedText());
        }

        [Test]
        public void ConvertToDispatchesToTargetType()
        {
            Assert.Multiple(() =>
            {
                Assert.That(ConvertedType(new Variant(5), BuiltInType.Boolean), Is.EqualTo(BuiltInType.Boolean));
                Assert.That(ConvertedType(new Variant(5), BuiltInType.SByte), Is.EqualTo(BuiltInType.SByte));
                Assert.That(ConvertedType(new Variant(5), BuiltInType.Byte), Is.EqualTo(BuiltInType.Byte));
                Assert.That(ConvertedType(new Variant(5), BuiltInType.Int16), Is.EqualTo(BuiltInType.Int16));
                Assert.That(ConvertedType(new Variant(5), BuiltInType.UInt16), Is.EqualTo(BuiltInType.UInt16));
                Assert.That(ConvertedType(new Variant(5), BuiltInType.UInt32), Is.EqualTo(BuiltInType.UInt32));
                Assert.That(ConvertedType(new Variant(5), BuiltInType.Int64), Is.EqualTo(BuiltInType.Int64));
                Assert.That(ConvertedType(new Variant(5), BuiltInType.UInt64), Is.EqualTo(BuiltInType.UInt64));
                Assert.That(ConvertedType(new Variant(5), BuiltInType.Float), Is.EqualTo(BuiltInType.Float));
                Assert.That(ConvertedType(new Variant(5), BuiltInType.Double), Is.EqualTo(BuiltInType.Double));
                Assert.That(ConvertedType(new Variant(5), BuiltInType.String), Is.EqualTo(BuiltInType.String));
                Assert.That(ConvertedType(new Variant(5), BuiltInType.StatusCode), Is.EqualTo(BuiltInType.StatusCode));
                Assert.That(
                    ConvertedType(new Variant("2024-01-02T03:04:05"), BuiltInType.DateTime),
                    Is.EqualTo(BuiltInType.DateTime));
                Assert.That(
                    ConvertedType(new Variant("00000000-0000-0000-0000-000000000001"), BuiltInType.Guid),
                    Is.EqualTo(BuiltInType.Guid));
                Assert.That(
                    ConvertedType(new Variant("0A0B"), BuiltInType.ByteString),
                    Is.EqualTo(BuiltInType.ByteString));
                Assert.That(
                    ConvertedType(new Variant("ns=1;i=5"), BuiltInType.NodeId),
                    Is.EqualTo(BuiltInType.NodeId));
                Assert.That(
                    ConvertedType(new Variant("nsu=http://test;s=X"), BuiltInType.ExpandedNodeId),
                    Is.EqualTo(BuiltInType.ExpandedNodeId));
                Assert.That(
                    ConvertedType(new Variant("1:name"), BuiltInType.QualifiedName),
                    Is.EqualTo(BuiltInType.QualifiedName));
                Assert.That(
                    ConvertedType(new Variant("hello"), BuiltInType.LocalizedText),
                    Is.EqualTo(BuiltInType.LocalizedText));
                Assert.That(
                    ConvertedType(new Variant("<a>1</a>"), BuiltInType.XmlElement),
                    Is.EqualTo(BuiltInType.XmlElement));
            });
        }

        [Test]
        public void TryGetDecimalFromNumericScalars()
        {
            Assert.Multiple(() =>
            {
                AssertDecimal(new Variant((sbyte)5), 5m);
                AssertDecimal(new Variant((byte)5), 5m);
                AssertDecimal(new Variant((short)5), 5m);
                AssertDecimal(new Variant((ushort)5), 5m);
                AssertDecimal(new Variant(5), 5m);
                AssertDecimal(new Variant(5u), 5m);
                AssertDecimal(new Variant(5L), 5m);
                AssertDecimal(new Variant(5UL), 5m);
                AssertDecimal(new Variant(2.5f), new decimal(2.5f));
                AssertDecimal(new Variant(2.5d), new decimal(2.5d));
            });
        }

        [Test]
        public void TryGetDecimalFromInt32ArrayBits()
        {
            int[] bits = decimal.GetBits(123.45m);
            var variant = new Variant(ArrayOf.Wrapped(bits));
            bool ok = variant.TryGetDecimal(out decimal value);
            Assert.That(ok, Is.True);
            Assert.That(value, Is.EqualTo(123.45m));
        }

        [Test]
        public void TryGetDecimalReturnsFalseForNonNumericString()
        {
            bool ok = new Variant("abc").TryGetDecimal(out decimal value);
            Assert.That(ok, Is.False);
            Assert.That(value, Is.Zero);
        }

        [Test]
        public void TryGetDecimalReturnsFalseForBoolean()
        {
            bool ok = new Variant(true).TryGetDecimal(out decimal value);
            Assert.That(ok, Is.False);
            Assert.That(value, Is.Zero);
        }

        [Test]
        public void CopyClonesScalarExtensionObject()
        {
            var body = new Argument();
            Variant copy = new Variant(new ExtensionObject(body)).Copy();
            bool ok = copy.GetExtensionObject().TryGetValue(out Argument copiedBody);
            bool sameInstance = ReferenceEquals(copiedBody, body);
            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.True);
                Assert.That(sameInstance, Is.False);
            });
        }

        [Test]
        public void CopyClonesScalarDataValue()
        {
            Variant copy = new Variant(new DataValue(11)).Copy();
            Assert.That(copy.GetDataValue().WrappedValue.GetInt32(), Is.EqualTo(11));
        }

        [Test]
        public void CopyReturnsSameForValueType()
        {
            Variant copy = new Variant(42).Copy();
            Assert.That(copy.GetInt32(), Is.EqualTo(42));
        }

        [Test]
        public void CopyClonesExtensionObjectArray()
        {
            var body = new Argument();
            Variant copy = new Variant(ArrayOf.Wrapped(new ExtensionObject(body))).Copy();
            bool ok = copy.GetExtensionObjectArray().Span[0].TryGetValue(out Argument copiedBody);
            bool sameInstance = ReferenceEquals(copiedBody, body);
            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.True);
                Assert.That(sameInstance, Is.False);
            });
        }

        [Test]
        public void CopyClonesDataValueArray()
        {
            Variant copy = new Variant(ArrayOf.Wrapped(new DataValue(1), new DataValue(2))).Copy();
            ArrayOf<DataValue> copied = copy.GetDataValueArray();
            Assert.Multiple(() =>
            {
                Assert.That(copied.Span[0].WrappedValue.GetInt32(), Is.EqualTo(1));
                Assert.That(copied.Span[1].WrappedValue.GetInt32(), Is.EqualTo(2));
            });
        }

        [Test]
        public void CopyClonesVariantArray()
        {
            Variant copy = new Variant(ArrayOf.Wrapped(new Variant(7), new Variant(8))).Copy();
            ArrayOf<Variant> copied = copy.GetVariantArray();
            Assert.Multiple(() =>
            {
                Assert.That(copied.Span[0].GetInt32(), Is.EqualTo(7));
                Assert.That(copied.Span[1].GetInt32(), Is.EqualTo(8));
            });
        }

        [Test]
        public void CopyClonesExtensionObjectMatrix()
        {
            var body = new Argument();
            MatrixOf<ExtensionObject> matrix = Matrix(new ExtensionObject(body), new ExtensionObject(new Argument()));
            Variant copy = new Variant(matrix).Copy();
            bool ok = copy.Expand().Span[0].GetExtensionObject().TryGetValue(out Argument copiedBody);
            bool sameInstance = ReferenceEquals(copiedBody, body);
            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.True);
                Assert.That(sameInstance, Is.False);
            });
        }

        [Test]
        public void CopyClonesDataValueMatrix()
        {
            MatrixOf<DataValue> matrix = Matrix(new DataValue(1), new DataValue(2));
            Variant copy = new Variant(matrix).Copy();
            ArrayOf<Variant> expanded = copy.Expand();
            Assert.Multiple(() =>
            {
                Assert.That(expanded.Span[0].GetDataValue().WrappedValue.GetInt32(), Is.EqualTo(1));
                Assert.That(expanded.Span[1].GetDataValue().WrappedValue.GetInt32(), Is.EqualTo(2));
            });
        }

        [Test]
        public void BitwiseAndCombinesIntegerScalars()
        {
            Variant result = new Variant(0b1100) & new Variant(0b1010);
            Assert.That(result.GetInt32(), Is.EqualTo(0b1000));
        }

        [Test]
        public void BitwiseOrCombinesIntegerScalars()
        {
            Variant result = new Variant(0b1100) | new Variant(0b1010);
            Assert.That(result.GetInt32(), Is.EqualTo(0b1110));
        }

        [Test]
        public void BitwiseAndCombinesBooleanScalars()
        {
            Variant result = new Variant(true) & new Variant(false);
            Assert.That(result.GetBoolean(), Is.False);
        }

        [Test]
        public void BitwiseOrCombinesBooleanScalars()
        {
            Variant result = new Variant(false) | new Variant(true);
            Assert.That(result.GetBoolean(), Is.True);
        }

        [Test]
        public void BitwiseAndCombinesAllIntegerTypes()
        {
            Assert.Multiple(() =>
            {
                Assert.That((new Variant((byte)0b1100) & new Variant((byte)0b1010)).GetInt32(), Is.EqualTo(0b1000));
                Assert.That((new Variant((sbyte)0b0110) & new Variant((sbyte)0b0011)).GetInt32(), Is.EqualTo(0b0010));
                Assert.That((new Variant((short)0b1100) & new Variant((short)0b1010)).GetInt32(), Is.EqualTo(0b1000));
                Assert.That((new Variant((ushort)0b1100) & new Variant((ushort)0b1010)).GetInt32(), Is.EqualTo(0b1000));
                Assert.That((new Variant(0b1100u) & new Variant(0b1010u)).GetUInt32(), Is.EqualTo(0b1000u));
                Assert.That((new Variant(0b1100L) & new Variant(0b1010L)).GetInt64(), Is.EqualTo(0b1000L));
                Assert.That((new Variant(0b1100UL) & new Variant(0b1010UL)).GetUInt64(), Is.EqualTo(0b1000UL));
                Assert.That(
                    (new Variant(new EnumValue(0b1100)) & new Variant(new EnumValue(0b1010))).GetInt32(),
                    Is.EqualTo(0b1000));
            });
        }

        [Test]
        public void BitwiseOrCombinesAllIntegerTypes()
        {
            Assert.Multiple(() =>
            {
                Assert.That((new Variant((byte)0b1100) | new Variant((byte)0b1010)).GetInt32(), Is.EqualTo(0b1110));
                Assert.That((new Variant((sbyte)0b0110) | new Variant((sbyte)0b0011)).GetInt32(), Is.EqualTo(0b0111));
                Assert.That((new Variant((short)0b1100) | new Variant((short)0b1010)).GetInt32(), Is.EqualTo(0b1110));
                Assert.That((new Variant((ushort)0b1100) | new Variant((ushort)0b1010)).GetInt32(), Is.EqualTo(0b1110));
                Assert.That((new Variant(0b1100u) | new Variant(0b1010u)).GetUInt32(), Is.EqualTo(0b1110u));
                Assert.That((new Variant(0b1100L) | new Variant(0b1010L)).GetInt64(), Is.EqualTo(0b1110L));
                Assert.That((new Variant(0b1100UL) | new Variant(0b1010UL)).GetUInt64(), Is.EqualTo(0b1110UL));
                Assert.That(
                    (new Variant(new EnumValue(0b1100)) | new Variant(new EnumValue(0b1010))).GetInt32(),
                    Is.EqualTo(0b1110));
            });
        }

        [Test]
        public void BitwiseAndWithNonScalarReturnsNull()
        {
            Variant result = new Variant(ArrayOf.Wrapped(1, 2)) & new Variant(3);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void BitwiseOrWithUnsupportedScalarReturnsNull()
        {
            Variant result = new Variant(1.5f) | new Variant(2.5f);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void CompareToOrdersScalarTypes()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new Variant(false).CompareTo(new Variant(true)), Is.Negative);
                Assert.That(new Variant((sbyte)1).CompareTo(new Variant((sbyte)2)), Is.Negative);
                Assert.That(new Variant((byte)2).CompareTo(new Variant((byte)1)), Is.Positive);
                Assert.That(new Variant((short)1).CompareTo(new Variant((short)2)), Is.Negative);
                Assert.That(new Variant((ushort)2).CompareTo(new Variant((ushort)3)), Is.Negative);
                Assert.That(new Variant(1).CompareTo(new Variant(2)), Is.Negative);
                Assert.That(new Variant(2u).CompareTo(new Variant(1u)), Is.Positive);
                Assert.That(new Variant(1L).CompareTo(new Variant(2L)), Is.Negative);
                Assert.That(new Variant(2UL).CompareTo(new Variant(3UL)), Is.Negative);
                Assert.That(new Variant(1.0f).CompareTo(new Variant(2.0f)), Is.Negative);
                Assert.That(new Variant(1.0d).CompareTo(new Variant(2.0d)), Is.Negative);
                Assert.That(new Variant(Dt(1)).CompareTo(new Variant(Dt(2))), Is.Negative);
                Assert.That(new Variant(new EnumValue(1)).CompareTo(new Variant(new EnumValue(2))), Is.Negative);
                Assert.That(new Variant("a").CompareTo(new Variant("b")), Is.Negative);
            });
        }

        [Test]
        public void CompareToEqualScalarsReturnsZero()
        {
            Assert.That(new Variant(5).CompareTo(new Variant(5)), Is.Zero);
        }

        [Test]
        public void CompareToNullVariantsReturnsZero()
        {
            Assert.That(new Variant().CompareTo(new Variant()), Is.Zero);
        }

        [Test]
        public void CompareToNullVariantAgainstValueUsesDefault()
        {
            Assert.That(new Variant().CompareTo(new Variant(5)), Is.Negative);
        }

        [Test]
        public void CompareToIncomparableReturnsMinValue()
        {
            var left = new Variant(new ExtensionObject(new Argument()));
            var right = new Variant(new ExtensionObject(new Argument()));
            Assert.That(left.CompareTo(right), Is.EqualTo(int.MinValue));
        }

        [Test]
        public void RelationalOperatorsCompareScalars()
        {
            bool less = new Variant(1) < new Variant(2);
            bool lessOrEqual = new Variant(2) <= new Variant(2);
            bool greater = new Variant(3) > new Variant(2);
            bool greaterOrEqual = new Variant(2) >= new Variant(2);
            Assert.Multiple(() =>
            {
                Assert.That(less, Is.True);
                Assert.That(lessOrEqual, Is.True);
                Assert.That(greater, Is.True);
                Assert.That(greaterOrEqual, Is.True);
            });
        }

        [Test]
        public void ValueEqualsComparesUnderlyingValues()
        {
            bool sameValue = new Variant(5).ValueEquals(new Variant(5));
            bool bothNull = new Variant().ValueEquals(new Variant());
            bool differentValue = new Variant(5).ValueEquals(new Variant(6));
            bool sameReferenceValue = new Variant("x").ValueEquals(new Variant("x"));
            bool nullVersusReference = new Variant().ValueEquals(new Variant("x"));
            bool referenceVersusNull = new Variant("x").ValueEquals(new Variant());
            Assert.Multiple(() =>
            {
                Assert.That(sameValue, Is.True);
                Assert.That(bothNull, Is.True);
                Assert.That(differentValue, Is.False);
                Assert.That(sameReferenceValue, Is.True);
                Assert.That(nullVersusReference, Is.False);
                Assert.That(referenceVersusNull, Is.False);
            });
        }

        [Test]
        public void EqualityOperatorsMatchScalarValues()
        {
            DateTimeUtc dateTime = Dt(1);
            Uuid uid = Uid(1);
            var bytes = ByteString.From(1, 2);
            XmlElement xml = Xml("1");
            var nodeId = new NodeId(10, 1);
            ExpandedNodeId expanded = ENId("Node");
            var status = new StatusCode(0x80000000u);
            var qualifiedName = new QualifiedName("name", 1);
            var localizedText = new LocalizedText("en", "value");
            var extension = new ExtensionObject(new Argument());
            var dataValue = new DataValue(1);
            var enumValue = new EnumValue(9);

            Assert.Multiple(() =>
            {
                AssertOperatorMatches(new Variant(true) == true, new Variant(true) != true);
                AssertOperatorMatches(new Variant((sbyte)-5) == (sbyte)-5, new Variant((sbyte)-5) != (sbyte)-5);
                AssertOperatorMatches(new Variant((byte)5) == (byte)5, new Variant((byte)5) != (byte)5);
                AssertOperatorMatches(new Variant((short)-6) == (short)-6, new Variant((short)-6) != (short)-6);
                AssertOperatorMatches(new Variant((ushort)6) == (ushort)6, new Variant((ushort)6) != (ushort)6);
                AssertOperatorMatches(new Variant(-7) == -7, new Variant(-7) != -7);
                AssertOperatorMatches(new Variant(7u) == 7u, new Variant(7u) != 7u);
                AssertOperatorMatches(new Variant(-8L) == -8L, new Variant(-8L) != -8L);
                AssertOperatorMatches(new Variant(8UL) == 8UL, new Variant(8UL) != 8UL);
                AssertOperatorMatches(new Variant(1.5f) == 1.5f, new Variant(1.5f) != 1.5f);
                AssertOperatorMatches(new Variant(2.5d) == 2.5d, new Variant(2.5d) != 2.5d);
                AssertOperatorMatches(new Variant("opc") == "opc", new Variant("opc") != "opc");
                AssertOperatorMatches(new Variant(enumValue) == enumValue, new Variant(enumValue) != enumValue);
                AssertOperatorMatches(new Variant(dateTime) == dateTime, new Variant(dateTime) != dateTime);
                AssertOperatorMatches(new Variant(uid) == uid, new Variant(uid) != uid);
                AssertOperatorMatches(new Variant(bytes) == bytes, new Variant(bytes) != bytes);
                AssertOperatorMatches(new Variant(xml) == xml, new Variant(xml) != xml);
                AssertOperatorMatches(new Variant(nodeId) == nodeId, new Variant(nodeId) != nodeId);
                AssertOperatorMatches(new Variant(expanded) == expanded, new Variant(expanded) != expanded);
                AssertOperatorMatches(new Variant(status) == status, new Variant(status) != status);
                AssertOperatorMatches(
                    new Variant(qualifiedName) == qualifiedName,
                    new Variant(qualifiedName) != qualifiedName);
                AssertOperatorMatches(
                    new Variant(localizedText) == localizedText,
                    new Variant(localizedText) != localizedText);
                AssertOperatorMatches(new Variant(extension) == extension, new Variant(extension) != extension);
                AssertOperatorMatches(new Variant(dataValue) == dataValue, new Variant(dataValue) != dataValue);
                AssertOperatorMatches(new Variant(3) == new Variant(3), new Variant(3) != new Variant(3));
            });
        }

        [Test]
        public void EqualityOperatorsMatchArrayValues()
        {
            XmlElement xml = Xml("1");
            var extension = new ExtensionObject(new Argument());
            var dataValue = new DataValue(1);

            var boolArray = ArrayOf.Wrapped(true, false);
            var sbyteArray = ArrayOf.Wrapped((sbyte)-1, (sbyte)1);
            var byteArray = ArrayOf.Wrapped((byte)1, (byte)2);
            var shortArray = ArrayOf.Wrapped((short)-2, (short)2);
            var ushortArray = ArrayOf.Wrapped((ushort)2, (ushort)3);
            var intArray = ArrayOf.Wrapped(-3, 3);
            var uintArray = ArrayOf.Wrapped(3u, 4u);
            var longArray = ArrayOf.Wrapped(-4L, 4L);
            var ulongArray = ArrayOf.Wrapped(4UL, 5UL);
            var floatArray = ArrayOf.Wrapped(1.0f, 2.0f);
            var doubleArray = ArrayOf.Wrapped(1.0d, 2.0d);
            var stringArray = ArrayOf.Wrapped("a", "b");
            var enumArray = ArrayOf.Wrapped(new EnumValue(1), new EnumValue(2));
            var dateTimeArray = ArrayOf.Wrapped(Dt(1), Dt(2));
            var guidArray = ArrayOf.Wrapped(Uid(1), Uid(2));
            var byteStringArray = ArrayOf.Wrapped(ByteString.From(1), ByteString.From(2));
            var xmlArray = ArrayOf.Wrapped(xml, xml);
            var nodeIdArray = ArrayOf.Wrapped(new NodeId(1), new NodeId(2));
            var expandedArray = ArrayOf.Wrapped(ENId("A"), ENId("B"));
            var statusArray = ArrayOf.Wrapped(new StatusCode(1u), new StatusCode(2u));
            var qualifiedArray =
                ArrayOf.Wrapped(new QualifiedName("a", 1), new QualifiedName("b", 1));
            var localizedArray =
                ArrayOf.Wrapped(new LocalizedText("en", "a"), new LocalizedText("en", "b"));
            var extensionArray = ArrayOf.Wrapped(extension, extension);
            var dataValueArray = ArrayOf.Wrapped(dataValue, dataValue);
            var variantArray = ArrayOf.Wrapped(new Variant(1), new Variant(2));

            Assert.Multiple(() =>
            {
                AssertOperatorMatches(new Variant(boolArray) == boolArray, new Variant(boolArray) != boolArray);
                AssertOperatorMatches(new Variant(sbyteArray) == sbyteArray, new Variant(sbyteArray) != sbyteArray);
                AssertOperatorMatches(new Variant(byteArray) == byteArray, new Variant(byteArray) != byteArray);
                AssertOperatorMatches(new Variant(shortArray) == shortArray, new Variant(shortArray) != shortArray);
                AssertOperatorMatches(new Variant(ushortArray) == ushortArray, new Variant(ushortArray) != ushortArray);
                AssertOperatorMatches(new Variant(intArray) == intArray, new Variant(intArray) != intArray);
                AssertOperatorMatches(new Variant(uintArray) == uintArray, new Variant(uintArray) != uintArray);
                AssertOperatorMatches(new Variant(longArray) == longArray, new Variant(longArray) != longArray);
                AssertOperatorMatches(new Variant(ulongArray) == ulongArray, new Variant(ulongArray) != ulongArray);
                AssertOperatorMatches(new Variant(floatArray) == floatArray, new Variant(floatArray) != floatArray);
                AssertOperatorMatches(new Variant(doubleArray) == doubleArray, new Variant(doubleArray) != doubleArray);
                AssertOperatorMatches(new Variant(stringArray) == stringArray, new Variant(stringArray) != stringArray);
                AssertOperatorMatches(new Variant(enumArray) == enumArray, new Variant(enumArray) != enumArray);
                AssertOperatorMatches(
                    new Variant(dateTimeArray) == dateTimeArray, new Variant(dateTimeArray) != dateTimeArray);
                AssertOperatorMatches(new Variant(guidArray) == guidArray, new Variant(guidArray) != guidArray);
                AssertOperatorMatches(
                    new Variant(byteStringArray) == byteStringArray, new Variant(byteStringArray) != byteStringArray);
                AssertOperatorMatches(new Variant(xmlArray) == xmlArray, new Variant(xmlArray) != xmlArray);
                AssertOperatorMatches(new Variant(nodeIdArray) == nodeIdArray, new Variant(nodeIdArray) != nodeIdArray);
                AssertOperatorMatches(
                    new Variant(expandedArray) == expandedArray, new Variant(expandedArray) != expandedArray);
                AssertOperatorMatches(new Variant(statusArray) == statusArray, new Variant(statusArray) != statusArray);
                AssertOperatorMatches(
                    new Variant(qualifiedArray) == qualifiedArray, new Variant(qualifiedArray) != qualifiedArray);
                AssertOperatorMatches(
                    new Variant(localizedArray) == localizedArray, new Variant(localizedArray) != localizedArray);
                AssertOperatorMatches(
                    new Variant(extensionArray) == extensionArray, new Variant(extensionArray) != extensionArray);
                AssertOperatorMatches(
                    new Variant(dataValueArray) == dataValueArray, new Variant(dataValueArray) != dataValueArray);
                AssertOperatorMatches(
                    new Variant(variantArray) == variantArray, new Variant(variantArray) != variantArray);
            });
        }

        [Test]
        public void EqualityOperatorsMatchMatrixValues()
        {
            XmlElement xml = Xml("1");
            var extension = new ExtensionObject(new Argument());
            var dataValue = new DataValue(1);

            MatrixOf<bool> boolMatrix = Matrix(true, false);
            MatrixOf<sbyte> sbyteMatrix = Matrix((sbyte)-1, (sbyte)1);
            MatrixOf<byte> byteMatrix = Matrix((byte)1, (byte)2);
            MatrixOf<short> shortMatrix = Matrix((short)-2, (short)2);
            MatrixOf<ushort> ushortMatrix = Matrix((ushort)2, (ushort)3);
            MatrixOf<int> intMatrix = Matrix(-3, 3);
            MatrixOf<uint> uintMatrix = Matrix(3u, 4u);
            MatrixOf<long> longMatrix = Matrix(-4L, 4L);
            MatrixOf<ulong> ulongMatrix = Matrix(4UL, 5UL);
            MatrixOf<float> floatMatrix = Matrix(1.0f, 2.0f);
            MatrixOf<double> doubleMatrix = Matrix(1.0d, 2.0d);
            MatrixOf<string> stringMatrix = Matrix("a", "b");
            MatrixOf<EnumValue> enumMatrix = Matrix(new EnumValue(1), new EnumValue(2));
            MatrixOf<DateTimeUtc> dateTimeMatrix = Matrix(Dt(1), Dt(2));
            MatrixOf<Uuid> guidMatrix = Matrix(Uid(1), Uid(2));
            MatrixOf<ByteString> byteStringMatrix = Matrix(ByteString.From(1), ByteString.From(2));
            MatrixOf<XmlElement> xmlMatrix = Matrix(xml, xml);
            MatrixOf<NodeId> nodeIdMatrix = Matrix(new NodeId(1), new NodeId(2));
            MatrixOf<ExpandedNodeId> expandedMatrix = Matrix(ENId("A"), ENId("B"));
            MatrixOf<StatusCode> statusMatrix = Matrix(new StatusCode(1u), new StatusCode(2u));
            MatrixOf<QualifiedName> qualifiedMatrix = Matrix(new QualifiedName("a", 1), new QualifiedName("b", 1));
            MatrixOf<LocalizedText> localizedMatrix =
                Matrix(new LocalizedText("en", "a"), new LocalizedText("en", "b"));
            MatrixOf<ExtensionObject> extensionMatrix = Matrix(extension, extension);
            MatrixOf<DataValue> dataValueMatrix = Matrix(dataValue, dataValue);
            MatrixOf<Variant> variantMatrix = Matrix(new Variant(1), new Variant(2));

            Assert.Multiple(() =>
            {
                AssertOperatorMatches(new Variant(boolMatrix) == boolMatrix, new Variant(boolMatrix) != boolMatrix);
                AssertOperatorMatches(new Variant(sbyteMatrix) == sbyteMatrix, new Variant(sbyteMatrix) != sbyteMatrix);
                AssertOperatorMatches(new Variant(byteMatrix) == byteMatrix, new Variant(byteMatrix) != byteMatrix);
                AssertOperatorMatches(new Variant(shortMatrix) == shortMatrix, new Variant(shortMatrix) != shortMatrix);
                AssertOperatorMatches(
                    new Variant(ushortMatrix) == ushortMatrix, new Variant(ushortMatrix) != ushortMatrix);
                AssertOperatorMatches(new Variant(intMatrix) == intMatrix, new Variant(intMatrix) != intMatrix);
                AssertOperatorMatches(new Variant(uintMatrix) == uintMatrix, new Variant(uintMatrix) != uintMatrix);
                AssertOperatorMatches(new Variant(longMatrix) == longMatrix, new Variant(longMatrix) != longMatrix);
                AssertOperatorMatches(new Variant(ulongMatrix) == ulongMatrix, new Variant(ulongMatrix) != ulongMatrix);
                AssertOperatorMatches(new Variant(floatMatrix) == floatMatrix, new Variant(floatMatrix) != floatMatrix);
                AssertOperatorMatches(
                    new Variant(doubleMatrix) == doubleMatrix, new Variant(doubleMatrix) != doubleMatrix);
                AssertOperatorMatches(
                    new Variant(stringMatrix) == stringMatrix, new Variant(stringMatrix) != stringMatrix);
                AssertOperatorMatches(new Variant(enumMatrix) == enumMatrix, new Variant(enumMatrix) != enumMatrix);
                AssertOperatorMatches(
                    new Variant(dateTimeMatrix) == dateTimeMatrix, new Variant(dateTimeMatrix) != dateTimeMatrix);
                AssertOperatorMatches(new Variant(guidMatrix) == guidMatrix, new Variant(guidMatrix) != guidMatrix);
                AssertOperatorMatches(
                    new Variant(byteStringMatrix) == byteStringMatrix,
                    new Variant(byteStringMatrix) != byteStringMatrix);
                AssertOperatorMatches(new Variant(xmlMatrix) == xmlMatrix, new Variant(xmlMatrix) != xmlMatrix);
                AssertOperatorMatches(
                    new Variant(nodeIdMatrix) == nodeIdMatrix, new Variant(nodeIdMatrix) != nodeIdMatrix);
                AssertOperatorMatches(
                    new Variant(expandedMatrix) == expandedMatrix, new Variant(expandedMatrix) != expandedMatrix);
                AssertOperatorMatches(
                    new Variant(statusMatrix) == statusMatrix, new Variant(statusMatrix) != statusMatrix);
                AssertOperatorMatches(
                    new Variant(qualifiedMatrix) == qualifiedMatrix, new Variant(qualifiedMatrix) != qualifiedMatrix);
                AssertOperatorMatches(
                    new Variant(localizedMatrix) == localizedMatrix, new Variant(localizedMatrix) != localizedMatrix);
                AssertOperatorMatches(
                    new Variant(extensionMatrix) == extensionMatrix, new Variant(extensionMatrix) != extensionMatrix);
                AssertOperatorMatches(
                    new Variant(dataValueMatrix) == dataValueMatrix, new Variant(dataValueMatrix) != dataValueMatrix);
                AssertOperatorMatches(
                    new Variant(variantMatrix) == variantMatrix, new Variant(variantMatrix) != variantMatrix);
            });
        }

        [TestCaseSource(nameof(EqualsObjectCases))]
        public void EqualsObjectMatchesTypedValue(Variant variant, object value, bool expected)
        {
            bool result = variant.Equals(value);
            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(ExpandCollapseArrayCases))]
        public void ExpandThenCollapseRoundTripsArray(Variant original, int expectedCount)
        {
            ArrayOf<Variant> expanded = original.Expand();
            Assert.That(expanded.Count, Is.EqualTo(expectedCount));
            var collapsed = Variant.Collapse(expanded);
            bool matches = collapsed == original;
            Assert.That(matches, Is.True);
        }

        [TestCaseSource(nameof(ExpandMatrixCases))]
        public void ExpandMatrixLiftsElements(Variant matrixVariant, Variant[] expectedElements)
        {
            ArrayOf<Variant> expanded = matrixVariant.Expand();
            Assert.That(expanded.ToArray(), Is.EqualTo(expectedElements));
        }

        [Test]
        public void CollapseEmptyReturnsNullVariant()
        {
            ArrayOf<Variant> items = [];
            var result = Variant.Collapse(items);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void CollapseSingleReturnsElement()
        {
            ArrayOf<Variant> items = [new Variant(42)];
            var result = Variant.Collapse(items);
            Assert.That(result.GetInt32(), Is.EqualTo(42));
        }

        [Test]
        public void CollapseMixedTypesReturnsVariantArray()
        {
            ArrayOf<Variant> items = [new Variant(1), new Variant("two")];
            var result = Variant.Collapse(items);
            Assert.Multiple(() =>
            {
                Assert.That(result.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Variant));
                Assert.That(result.TypeInfo.IsArray, Is.True);
            });
        }

        private static IEnumerable<TestCaseData> EqualsObjectCases
        {
            get
            {
                XmlElement xml = Xml("1");
                var extension = new ExtensionObject(new Argument());
                var dataValue = new DataValue(1);

                yield return new TestCaseData(new Variant(true), true, true)
                    .SetName("EqualsObjectScalarBoolean");
                yield return new TestCaseData(new Variant((sbyte)-5), (sbyte)-5, true)
                    .SetName("EqualsObjectScalarSByte");
                yield return new TestCaseData(new Variant((byte)5), (byte)5, true)
                    .SetName("EqualsObjectScalarByte");
                yield return new TestCaseData(new Variant((short)-6), (short)-6, true)
                    .SetName("EqualsObjectScalarInt16");
                yield return new TestCaseData(new Variant((ushort)6), (ushort)6, true)
                    .SetName("EqualsObjectScalarUInt16");
                yield return new TestCaseData(new Variant(-7), -7, true)
                    .SetName("EqualsObjectScalarInt32");
                yield return new TestCaseData(new Variant(7u), 7u, true)
                    .SetName("EqualsObjectScalarUInt32");
                yield return new TestCaseData(new Variant(-8L), -8L, true)
                    .SetName("EqualsObjectScalarInt64");
                yield return new TestCaseData(new Variant(8UL), 8UL, true)
                    .SetName("EqualsObjectScalarUInt64");
                yield return new TestCaseData(new Variant(1.5f), 1.5f, true)
                    .SetName("EqualsObjectScalarFloat");
                yield return new TestCaseData(new Variant(2.5d), 2.5d, true)
                    .SetName("EqualsObjectScalarDouble");
                yield return new TestCaseData(new Variant("opc"), "opc", true)
                    .SetName("EqualsObjectScalarString");
                yield return new TestCaseData(new Variant(new EnumValue(9)), new EnumValue(9), true)
                    .SetName("EqualsObjectScalarEnumeration");
                yield return new TestCaseData(new Variant(Dt(1)), Dt(1), true)
                    .SetName("EqualsObjectScalarDateTime");
                yield return new TestCaseData(new Variant(Uid(1)), Uid(1), true)
                    .SetName("EqualsObjectScalarGuid");
                yield return new TestCaseData(new Variant(ByteString.From(1, 2)), ByteString.From(1, 2), true)
                    .SetName("EqualsObjectScalarByteString");
                yield return new TestCaseData(new Variant(xml), xml, true)
                    .SetName("EqualsObjectScalarXmlElement");
                yield return new TestCaseData(new Variant(new NodeId(10, 1)), new NodeId(10, 1), true)
                    .SetName("EqualsObjectScalarNodeId");
                yield return new TestCaseData(new Variant(ENId("Node")), ENId("Node"), true)
                    .SetName("EqualsObjectScalarExpandedNodeId");
                yield return new TestCaseData(new Variant(new StatusCode(3u)), new StatusCode(3u), true)
                    .SetName("EqualsObjectScalarStatusCode");
                yield return new TestCaseData(
                    new Variant(new QualifiedName("name", 1)), new QualifiedName("name", 1), true)
                    .SetName("EqualsObjectScalarQualifiedName");
                yield return new TestCaseData(
                    new Variant(new LocalizedText("en", "value")), new LocalizedText("en", "value"), true)
                    .SetName("EqualsObjectScalarLocalizedText");
                yield return new TestCaseData(new Variant(extension), extension, true)
                    .SetName("EqualsObjectScalarExtensionObject");
                yield return new TestCaseData(new Variant(dataValue), dataValue, true)
                    .SetName("EqualsObjectScalarDataValue");

                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(true, false)), ArrayOf.Wrapped(true, false), true)
                    .SetName("EqualsObjectArrayBoolean");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped((sbyte)-1, (sbyte)1)),
                    ArrayOf.Wrapped((sbyte)-1, (sbyte)1), true)
                    .SetName("EqualsObjectArraySByte");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped((byte)1, (byte)2)), ArrayOf.Wrapped((byte)1, (byte)2), true)
                    .SetName("EqualsObjectArrayByte");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped((short)-2, (short)2)),
                    ArrayOf.Wrapped((short)-2, (short)2), true)
                    .SetName("EqualsObjectArrayInt16");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped((ushort)2, (ushort)3)),
                    ArrayOf.Wrapped((ushort)2, (ushort)3), true)
                    .SetName("EqualsObjectArrayUInt16");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(-3, 3)), ArrayOf.Wrapped(-3, 3), true)
                    .SetName("EqualsObjectArrayInt32");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(3u, 4u)), ArrayOf.Wrapped(3u, 4u), true)
                    .SetName("EqualsObjectArrayUInt32");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(-4L, 4L)), ArrayOf.Wrapped(-4L, 4L), true)
                    .SetName("EqualsObjectArrayInt64");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(4UL, 5UL)), ArrayOf.Wrapped(4UL, 5UL), true)
                    .SetName("EqualsObjectArrayUInt64");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(1.0f, 2.0f)), ArrayOf.Wrapped(1.0f, 2.0f), true)
                    .SetName("EqualsObjectArrayFloat");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(1.0d, 2.0d)), ArrayOf.Wrapped(1.0d, 2.0d), true)
                    .SetName("EqualsObjectArrayDouble");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped("a", "b")), ArrayOf.Wrapped("a", "b"), true)
                    .SetName("EqualsObjectArrayString");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(new EnumValue(1), new EnumValue(2))),
                    ArrayOf.Wrapped(new EnumValue(1), new EnumValue(2)), true)
                    .SetName("EqualsObjectArrayEnumeration");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(Dt(1), Dt(2))), ArrayOf.Wrapped(Dt(1), Dt(2)), true)
                    .SetName("EqualsObjectArrayDateTime");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(Uid(1), Uid(2))), ArrayOf.Wrapped(Uid(1), Uid(2)), true)
                    .SetName("EqualsObjectArrayGuid");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(ByteString.From(1), ByteString.From(2))),
                    ArrayOf.Wrapped(ByteString.From(1), ByteString.From(2)), true)
                    .SetName("EqualsObjectArrayByteString");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(xml, xml)), ArrayOf.Wrapped(xml, xml), true)
                    .SetName("EqualsObjectArrayXmlElement");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(new NodeId(1), new NodeId(2))),
                    ArrayOf.Wrapped(new NodeId(1), new NodeId(2)), true)
                    .SetName("EqualsObjectArrayNodeId");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(ENId("A"), ENId("B"))),
                    ArrayOf.Wrapped(ENId("A"), ENId("B")), true)
                    .SetName("EqualsObjectArrayExpandedNodeId");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(new StatusCode(1u), new StatusCode(2u))),
                    ArrayOf.Wrapped(new StatusCode(1u), new StatusCode(2u)), true)
                    .SetName("EqualsObjectArrayStatusCode");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(new QualifiedName("a", 1), new QualifiedName("b", 1))),
                    ArrayOf.Wrapped(new QualifiedName("a", 1), new QualifiedName("b", 1)), true)
                    .SetName("EqualsObjectArrayQualifiedName");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(new LocalizedText("en", "a"), new LocalizedText("en", "b"))),
                    ArrayOf.Wrapped(new LocalizedText("en", "a"), new LocalizedText("en", "b")), true)
                    .SetName("EqualsObjectArrayLocalizedText");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(extension, extension)),
                    ArrayOf.Wrapped(extension, extension), true)
                    .SetName("EqualsObjectArrayExtensionObject");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(dataValue, dataValue)),
                    ArrayOf.Wrapped(dataValue, dataValue), true)
                    .SetName("EqualsObjectArrayDataValue");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(new Variant(1), new Variant(2))),
                    ArrayOf.Wrapped(new Variant(1), new Variant(2)), true)
                    .SetName("EqualsObjectArrayVariant");

                yield return new TestCaseData(new Variant(Matrix(true, false)), Matrix(true, false), true)
                    .SetName("EqualsObjectMatrixBoolean");
                yield return new TestCaseData(
                    new Variant(Matrix((sbyte)-1, (sbyte)1)), Matrix((sbyte)-1, (sbyte)1), true)
                    .SetName("EqualsObjectMatrixSByte");
                yield return new TestCaseData(
                    new Variant(Matrix((byte)1, (byte)2)), Matrix((byte)1, (byte)2), true)
                    .SetName("EqualsObjectMatrixByte");
                yield return new TestCaseData(
                    new Variant(Matrix((short)-2, (short)2)), Matrix((short)-2, (short)2), true)
                    .SetName("EqualsObjectMatrixInt16");
                yield return new TestCaseData(
                    new Variant(Matrix((ushort)2, (ushort)3)), Matrix((ushort)2, (ushort)3), true)
                    .SetName("EqualsObjectMatrixUInt16");
                yield return new TestCaseData(new Variant(Matrix(-3, 3)), Matrix(-3, 3), true)
                    .SetName("EqualsObjectMatrixInt32");
                yield return new TestCaseData(new Variant(Matrix(3u, 4u)), Matrix(3u, 4u), true)
                    .SetName("EqualsObjectMatrixUInt32");
                yield return new TestCaseData(new Variant(Matrix(-4L, 4L)), Matrix(-4L, 4L), true)
                    .SetName("EqualsObjectMatrixInt64");
                yield return new TestCaseData(new Variant(Matrix(4UL, 5UL)), Matrix(4UL, 5UL), true)
                    .SetName("EqualsObjectMatrixUInt64");
                yield return new TestCaseData(new Variant(Matrix(1.0f, 2.0f)), Matrix(1.0f, 2.0f), true)
                    .SetName("EqualsObjectMatrixFloat");
                yield return new TestCaseData(new Variant(Matrix(1.0d, 2.0d)), Matrix(1.0d, 2.0d), true)
                    .SetName("EqualsObjectMatrixDouble");
                yield return new TestCaseData(new Variant(Matrix("a", "b")), Matrix("a", "b"), true)
                    .SetName("EqualsObjectMatrixString");
                yield return new TestCaseData(
                    new Variant(Matrix(new EnumValue(1), new EnumValue(2))),
                    Matrix(new EnumValue(1), new EnumValue(2)), true)
                    .SetName("EqualsObjectMatrixEnumeration");
                yield return new TestCaseData(new Variant(Matrix(Dt(1), Dt(2))), Matrix(Dt(1), Dt(2)), true)
                    .SetName("EqualsObjectMatrixDateTime");
                yield return new TestCaseData(new Variant(Matrix(Uid(1), Uid(2))), Matrix(Uid(1), Uid(2)), true)
                    .SetName("EqualsObjectMatrixGuid");
                yield return new TestCaseData(
                    new Variant(Matrix(ByteString.From(1), ByteString.From(2))),
                    Matrix(ByteString.From(1), ByteString.From(2)), true)
                    .SetName("EqualsObjectMatrixByteString");
                yield return new TestCaseData(new Variant(Matrix(xml, xml)), Matrix(xml, xml), true)
                    .SetName("EqualsObjectMatrixXmlElement");
                yield return new TestCaseData(
                    new Variant(Matrix(new NodeId(1), new NodeId(2))),
                    Matrix(new NodeId(1), new NodeId(2)), true)
                    .SetName("EqualsObjectMatrixNodeId");
                yield return new TestCaseData(
                    new Variant(Matrix(ENId("A"), ENId("B"))), Matrix(ENId("A"), ENId("B")), true)
                    .SetName("EqualsObjectMatrixExpandedNodeId");
                yield return new TestCaseData(
                    new Variant(Matrix(new StatusCode(1u), new StatusCode(2u))),
                    Matrix(new StatusCode(1u), new StatusCode(2u)), true)
                    .SetName("EqualsObjectMatrixStatusCode");
                yield return new TestCaseData(
                    new Variant(Matrix(new QualifiedName("a", 1), new QualifiedName("b", 1))),
                    Matrix(new QualifiedName("a", 1), new QualifiedName("b", 1)), true)
                    .SetName("EqualsObjectMatrixQualifiedName");
                yield return new TestCaseData(
                    new Variant(Matrix(new LocalizedText("en", "a"), new LocalizedText("en", "b"))),
                    Matrix(new LocalizedText("en", "a"), new LocalizedText("en", "b")), true)
                    .SetName("EqualsObjectMatrixLocalizedText");
                yield return new TestCaseData(
                    new Variant(Matrix(extension, extension)), Matrix(extension, extension), true)
                    .SetName("EqualsObjectMatrixExtensionObject");
                yield return new TestCaseData(
                    new Variant(Matrix(dataValue, dataValue)), Matrix(dataValue, dataValue), true)
                    .SetName("EqualsObjectMatrixDataValue");

                yield return new TestCaseData(new Variant(5), "5", false)
                    .SetName("EqualsObjectMismatchedTypeIsFalse");
                yield return new TestCaseData(new Variant(), null, true)
                    .SetName("EqualsObjectNullMatchesNullVariant");
                yield return new TestCaseData(new Variant(5), null, false)
                    .SetName("EqualsObjectNullDoesNotMatchValue");
                yield return new TestCaseData(new Variant(5), new object(), false)
                    .SetName("EqualsObjectUnrelatedTypeIsFalse");
            }
        }

        private static IEnumerable<TestCaseData> ExpandCollapseArrayCases
        {
            get
            {
                XmlElement xml = Xml("1");
                var extension = new ExtensionObject(new Argument());
                var dataValue = new DataValue(1);

                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(true, false)), 2)
                    .SetName("ExpandCollapseArrayBoolean");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped((sbyte)-1, (sbyte)2)), 2)
                    .SetName("ExpandCollapseArraySByte");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped((byte)1, (byte)2)), 2)
                    .SetName("ExpandCollapseArrayByte");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped((short)-2, (short)2)), 2)
                    .SetName("ExpandCollapseArrayInt16");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped((ushort)2, (ushort)3)), 2)
                    .SetName("ExpandCollapseArrayUInt16");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(-3, 3)), 2)
                    .SetName("ExpandCollapseArrayInt32");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(3u, 4u)), 2)
                    .SetName("ExpandCollapseArrayUInt32");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(-4L, 4L)), 2)
                    .SetName("ExpandCollapseArrayInt64");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(4UL, 5UL)), 2)
                    .SetName("ExpandCollapseArrayUInt64");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(1.0f, 2.0f)), 2)
                    .SetName("ExpandCollapseArrayFloat");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(1.0d, 2.0d)), 2)
                    .SetName("ExpandCollapseArrayDouble");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped("a", "b")), 2)
                    .SetName("ExpandCollapseArrayString");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(Dt(1), Dt(2))), 2)
                    .SetName("ExpandCollapseArrayDateTime");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(Uid(1), Uid(2))), 2)
                    .SetName("ExpandCollapseArrayGuid");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(ByteString.From(1), ByteString.From(2))), 2)
                    .SetName("ExpandCollapseArrayByteString");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(xml, xml)), 2)
                    .SetName("ExpandCollapseArrayXmlElement");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(new NodeId(1), new NodeId(2))), 2)
                    .SetName("ExpandCollapseArrayNodeId");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(ENId("A"), ENId("B"))), 2)
                    .SetName("ExpandCollapseArrayExpandedNodeId");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(new StatusCode(1u), new StatusCode(2u))), 2)
                    .SetName("ExpandCollapseArrayStatusCode");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(new QualifiedName("a", 1), new QualifiedName("b", 1))), 2)
                    .SetName("ExpandCollapseArrayQualifiedName");
                yield return new TestCaseData(
                    new Variant(ArrayOf.Wrapped(new LocalizedText("en", "a"), new LocalizedText("en", "b"))), 2)
                    .SetName("ExpandCollapseArrayLocalizedText");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(extension, extension)), 2)
                    .SetName("ExpandCollapseArrayExtensionObject");
                yield return new TestCaseData(new Variant(ArrayOf.Wrapped(dataValue, dataValue)), 2)
                    .SetName("ExpandCollapseArrayDataValue");
            }
        }

        private static IEnumerable<TestCaseData> ExpandMatrixCases
        {
            get
            {
                XmlElement xml = Xml("1");
                var extension = new ExtensionObject(new Argument());
                var dataValue = new DataValue(1);

                yield return new TestCaseData(
                    new Variant(Matrix(10, 20)),
                    new[] { new Variant(10), new Variant(20) })
                    .SetName("ExpandMatrixInt32");
                yield return new TestCaseData(
                    new Variant(Matrix(true, false)),
                    new[] { new Variant(true), new Variant(false) })
                    .SetName("ExpandMatrixBoolean");
                yield return new TestCaseData(
                    new Variant(Matrix((sbyte)-1, (sbyte)2)),
                    new[] { new Variant((sbyte)-1), new Variant((sbyte)2) })
                    .SetName("ExpandMatrixSByte");
                yield return new TestCaseData(
                    new Variant(Matrix((byte)1, (byte)2)),
                    new[] { new Variant((byte)1), new Variant((byte)2) })
                    .SetName("ExpandMatrixByte");
                yield return new TestCaseData(
                    new Variant(Matrix((short)-2, (short)2)),
                    new[] { new Variant((short)-2), new Variant((short)2) })
                    .SetName("ExpandMatrixInt16");
                yield return new TestCaseData(
                    new Variant(Matrix((ushort)2, (ushort)3)),
                    new[] { new Variant((ushort)2), new Variant((ushort)3) })
                    .SetName("ExpandMatrixUInt16");
                yield return new TestCaseData(
                    new Variant(Matrix(3u, 4u)),
                    new[] { new Variant(3u), new Variant(4u) })
                    .SetName("ExpandMatrixUInt32");
                yield return new TestCaseData(
                    new Variant(Matrix(-4L, 4L)),
                    new[] { new Variant(-4L), new Variant(4L) })
                    .SetName("ExpandMatrixInt64");
                yield return new TestCaseData(
                    new Variant(Matrix(4UL, 5UL)),
                    new[] { new Variant(4UL), new Variant(5UL) })
                    .SetName("ExpandMatrixUInt64");
                yield return new TestCaseData(
                    new Variant(Matrix(1.0f, 2.0f)),
                    new[] { new Variant(1.0f), new Variant(2.0f) })
                    .SetName("ExpandMatrixFloat");
                yield return new TestCaseData(
                    new Variant(Matrix(1.0d, 2.0d)),
                    new[] { new Variant(1.0d), new Variant(2.0d) })
                    .SetName("ExpandMatrixDouble");
                yield return new TestCaseData(
                    new Variant(Matrix("a", "b")),
                    new[] { new Variant("a"), new Variant("b") })
                    .SetName("ExpandMatrixString");
                yield return new TestCaseData(
                    new Variant(Matrix(Dt(1), Dt(2))),
                    new[] { new Variant(Dt(1)), new Variant(Dt(2)) })
                    .SetName("ExpandMatrixDateTime");
                yield return new TestCaseData(
                    new Variant(Matrix(Uid(1), Uid(2))),
                    new[] { new Variant(Uid(1)), new Variant(Uid(2)) })
                    .SetName("ExpandMatrixGuid");
                yield return new TestCaseData(
                    new Variant(Matrix(ByteString.From(1), ByteString.From(2))),
                    new[] { new Variant(ByteString.From(1)), new Variant(ByteString.From(2)) })
                    .SetName("ExpandMatrixByteString");
                yield return new TestCaseData(
                    new Variant(Matrix(xml, xml)),
                    new[] { new Variant(xml), new Variant(xml) })
                    .SetName("ExpandMatrixXmlElement");
                yield return new TestCaseData(
                    new Variant(Matrix(new NodeId(1), new NodeId(2))),
                    new[] { new Variant(new NodeId(1)), new Variant(new NodeId(2)) })
                    .SetName("ExpandMatrixNodeId");
                yield return new TestCaseData(
                    new Variant(Matrix(ENId("A"), ENId("B"))),
                    new[] { new Variant(ENId("A")), new Variant(ENId("B")) })
                    .SetName("ExpandMatrixExpandedNodeId");
                yield return new TestCaseData(
                    new Variant(Matrix(new StatusCode(1u), new StatusCode(2u))),
                    new[] { new Variant(new StatusCode(1u)), new Variant(new StatusCode(2u)) })
                    .SetName("ExpandMatrixStatusCode");
                yield return new TestCaseData(
                    new Variant(Matrix(new QualifiedName("a", 1), new QualifiedName("b", 1))),
                    new[] { new Variant(new QualifiedName("a", 1)), new Variant(new QualifiedName("b", 1)) })
                    .SetName("ExpandMatrixQualifiedName");
                yield return new TestCaseData(
                    new Variant(Matrix(new LocalizedText("en", "a"), new LocalizedText("en", "b"))),
                    new[] { new Variant(new LocalizedText("en", "a")), new Variant(new LocalizedText("en", "b")) })
                    .SetName("ExpandMatrixLocalizedText");
                yield return new TestCaseData(
                    new Variant(Matrix(extension, extension)),
                    new[] { new Variant(extension), new Variant(extension) })
                    .SetName("ExpandMatrixExtensionObject");
                yield return new TestCaseData(
                    new Variant(Matrix(dataValue, dataValue)),
                    new[] { new Variant(dataValue), new Variant(dataValue) })
                    .SetName("ExpandMatrixDataValue");
            }
        }

        private static void AssertOperatorMatches(bool equalsResult, bool notEqualsResult)
        {
            Assert.That(equalsResult, Is.True);
            Assert.That(notEqualsResult, Is.False);
        }

        private static void AssertDecimal(Variant variant, decimal expected)
        {
            bool ok = variant.TryGetDecimal(out decimal value);
            Assert.That(ok, Is.True);
            Assert.That(value, Is.EqualTo(expected));
        }

        private static BuiltInType ConvertedType(Variant variant, BuiltInType target)
        {
            return variant.ConvertTo(target).TypeInfo.BuiltInType;
        }

        private static MatrixOf<T> Matrix<T>(T first, T second)
        {
            return new T[,] { { first, second } }.ToMatrixOf();
        }

        private static DateTimeUtc Dt(int day)
        {
            return (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, day), DateTimeKind.Utc);
        }

        private static Uuid Uid(int value)
        {
            return new Uuid(new Guid($"00000000-0000-0000-0000-{value:D12}"));
        }

        private static ExpandedNodeId ENId(string identifier)
        {
            return ExpandedNodeId.Parse("nsu=http://test;s=" + identifier);
        }

        private static XmlElement Xml(string value)
        {
            return XmlElement.From("<a>" + value + "</a>");
        }
    }
}
