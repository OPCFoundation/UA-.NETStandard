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
using Opc.Ua.WotCon.Server;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Tests for <see cref="WotVariantHelper.ToVariant"/> — the type-switch
    /// helper that maps WoT provider scalar / array values to OPC UA
    /// <see cref="Variant"/>s without going through the obsolete
    /// reflection-based constructor for the spec-listed types (Table 14)
    /// and their array counterparts.
    /// </summary>
    [TestFixture]
    [Category("WotCon")]
    public sealed class WotVariantHelperTests
    {
        [Test]
        public void ToVariantOfNullReturnsVariantNull()
            => Assert.That(WotVariantHelper.ToVariant(null).IsNull, Is.True);

        [Test]
        public void ToVariantOfExistingVariantReturnsItUnchanged()
        {
            var input = new Variant(42);
            Assert.That(WotVariantHelper.ToVariant(input), Is.EqualTo(input));
        }

        // Scalars — every spec Table 14 primitive plus the ones the
        // simulated and reference providers are most likely to emit.
        [Test]
        public void ToVariantOfBooleanProducesBooleanVariant()
            => AssertVariant(WotVariantHelper.ToVariant(true), BuiltInType.Boolean, ValueRanks.Scalar);

        [Test]
        public void ToVariantOfSByteProducesSByteVariant()
            => AssertVariant(WotVariantHelper.ToVariant((sbyte)-1), BuiltInType.SByte, ValueRanks.Scalar);

        [Test]
        public void ToVariantOfByteProducesByteVariant()
            => AssertVariant(WotVariantHelper.ToVariant((byte)1), BuiltInType.Byte, ValueRanks.Scalar);

        [Test]
        public void ToVariantOfShortProducesInt16Variant()
            => AssertVariant(WotVariantHelper.ToVariant((short)-1), BuiltInType.Int16, ValueRanks.Scalar);

        [Test]
        public void ToVariantOfUShortProducesUInt16Variant()
            => AssertVariant(WotVariantHelper.ToVariant((ushort)1), BuiltInType.UInt16, ValueRanks.Scalar);

        [Test]
        public void ToVariantOfIntProducesInt32Variant()
            => AssertVariant(WotVariantHelper.ToVariant(-1), BuiltInType.Int32, ValueRanks.Scalar);

        [Test]
        public void ToVariantOfUIntProducesUInt32Variant()
            => AssertVariant(WotVariantHelper.ToVariant(1u), BuiltInType.UInt32, ValueRanks.Scalar);

        [Test]
        public void ToVariantOfLongProducesInt64Variant()
            => AssertVariant(WotVariantHelper.ToVariant(-1L), BuiltInType.Int64, ValueRanks.Scalar);

        [Test]
        public void ToVariantOfULongProducesUInt64Variant()
            => AssertVariant(WotVariantHelper.ToVariant(1UL), BuiltInType.UInt64, ValueRanks.Scalar);

        [Test]
        public void ToVariantOfFloatProducesFloatVariant()
            => AssertVariant(WotVariantHelper.ToVariant(1.5f), BuiltInType.Float, ValueRanks.Scalar);

        [Test]
        public void ToVariantOfDoubleProducesDoubleVariant()
            => AssertVariant(WotVariantHelper.ToVariant(1.5), BuiltInType.Double, ValueRanks.Scalar);

        [Test]
        public void ToVariantOfStringProducesStringVariant()
            => AssertVariant(WotVariantHelper.ToVariant("hello"), BuiltInType.String, ValueRanks.Scalar);

        [Test]
        public void ToVariantOfDateTimeProducesDateTimeVariant()
        {
            Variant v = WotVariantHelper.ToVariant(DateTime.UtcNow);
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.DateTime));
        }

        [Test]
        public void ToVariantOfGuidProducesGuidVariant()
        {
            Variant v = WotVariantHelper.ToVariant(Guid.NewGuid());
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Guid));
        }

        [Test]
        public void ToVariantOfUuidProducesGuidVariant()
        {
            Variant v = WotVariantHelper.ToVariant(new Uuid(Guid.NewGuid()));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Guid));
        }

        [Test]
        public void ToVariantOfByteArrayProducesByteStringVariant()
        {
            byte[] payload = new byte[] { 1, 2, 3 };
            Variant v = WotVariantHelper.ToVariant(payload);
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ByteString));
        }

        [Test]
        public void ToVariantOfByteStringProducesByteStringVariant()
        {
            Variant v = WotVariantHelper.ToVariant(ByteString.From(new byte[] { 7 }));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ByteString));
        }

        [Test]
        public void ToVariantOfXmlElementProducesXmlElementVariant()
        {
            // WotVariantHelper handles the Opc.Ua.XmlElement built-in type
            // (System.Xml.XmlElement falls through to the reflection
            // fallback, which is the documented behaviour for non-spec
            // types).
            Opc.Ua.XmlElement element = (Opc.Ua.XmlElement)"<root/>";
            Variant v = WotVariantHelper.ToVariant(element);
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.XmlElement));
        }

        [Test]
        public void ToVariantOfNodeIdProducesNodeIdVariant()
        {
            Variant v = WotVariantHelper.ToVariant(new NodeId(7u, 2));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.NodeId));
        }

        [Test]
        public void ToVariantOfExpandedNodeIdProducesExpandedNodeIdVariant()
        {
            Variant v = WotVariantHelper.ToVariant(new ExpandedNodeId(7u, "urn:test"));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExpandedNodeId));
        }

        [Test]
        public void ToVariantOfQualifiedNameProducesQualifiedNameVariant()
        {
            Variant v = WotVariantHelper.ToVariant(new QualifiedName("name", 2));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.QualifiedName));
        }

        [Test]
        public void ToVariantOfLocalizedTextProducesLocalizedTextVariant()
        {
            Variant v = WotVariantHelper.ToVariant(new LocalizedText("text"));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.LocalizedText));
        }

        [Test]
        public void ToVariantOfStatusCodeProducesStatusCodeVariant()
        {
            Variant v = WotVariantHelper.ToVariant((StatusCode)StatusCodes.Good);
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
        }

        // Arrays — every spec Table 14 array shape plus the
        // DateTime[] → ArrayOf<DateTimeUtc> conversion path.
        [Test]
        public void ToVariantOfBoolArrayProducesBooleanArrayVariant()
            => AssertVariant(WotVariantHelper.ToVariant(new[] { true, false }), BuiltInType.Boolean, ValueRanks.OneDimension);

        [Test]
        public void ToVariantOfSByteArrayProducesSByteArrayVariant()
            => AssertVariant(WotVariantHelper.ToVariant(new sbyte[] { -1, 1 }), BuiltInType.SByte, ValueRanks.OneDimension);

        [Test]
        public void ToVariantOfShortArrayProducesInt16ArrayVariant()
            => AssertVariant(WotVariantHelper.ToVariant(new short[] { -1, 1 }), BuiltInType.Int16, ValueRanks.OneDimension);

        [Test]
        public void ToVariantOfUShortArrayProducesUInt16ArrayVariant()
            => AssertVariant(WotVariantHelper.ToVariant(new ushort[] { 1, 2 }), BuiltInType.UInt16, ValueRanks.OneDimension);

        [Test]
        public void ToVariantOfIntArrayProducesInt32ArrayVariant()
            => AssertVariant(WotVariantHelper.ToVariant(new[] { -1, 1 }), BuiltInType.Int32, ValueRanks.OneDimension);

        [Test]
        public void ToVariantOfUIntArrayProducesUInt32ArrayVariant()
            => AssertVariant(WotVariantHelper.ToVariant(new[] { 1u, 2u }), BuiltInType.UInt32, ValueRanks.OneDimension);

        [Test]
        public void ToVariantOfLongArrayProducesInt64ArrayVariant()
            => AssertVariant(WotVariantHelper.ToVariant(new[] { -1L, 1L }), BuiltInType.Int64, ValueRanks.OneDimension);

        [Test]
        public void ToVariantOfULongArrayProducesUInt64ArrayVariant()
            => AssertVariant(WotVariantHelper.ToVariant(new[] { 1UL, 2UL }), BuiltInType.UInt64, ValueRanks.OneDimension);

        [Test]
        public void ToVariantOfFloatArrayProducesFloatArrayVariant()
            => AssertVariant(WotVariantHelper.ToVariant(new[] { 1.5f }), BuiltInType.Float, ValueRanks.OneDimension);

        [Test]
        public void ToVariantOfDoubleArrayProducesDoubleArrayVariant()
            => AssertVariant(WotVariantHelper.ToVariant(new[] { 1.5 }), BuiltInType.Double, ValueRanks.OneDimension);

        [Test]
        public void ToVariantOfStringArrayProducesStringArrayVariant()
            => AssertVariant(WotVariantHelper.ToVariant(new[] { "a", "b" }), BuiltInType.String, ValueRanks.OneDimension);

        [Test]
        public void ToVariantOfDateTimeArrayProducesDateTimeArrayVariant()
        {
            Variant v = WotVariantHelper.ToVariant(new[] { DateTime.UtcNow });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.DateTime));
            Assert.That(v.TypeInfo.ValueRank, Is.EqualTo(ValueRanks.OneDimension));
        }

        // Fallback — unknown types still produce a Variant via the
        // reflection-based constructor (suppressed AOT warnings). We just
        // assert the call did not throw.
        [Test]
        public void ToVariantOfUnknownReferenceTypeFallsBackThroughReflection()
        {
            var owned = new object();
            _ = WotVariantHelper.ToVariant(owned);
            Assert.Pass();
        }

        private static void AssertVariant(Variant v, BuiltInType expectedType, int expectedRank)
        {
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(expectedType));
            Assert.That(v.TypeInfo.ValueRank, Is.EqualTo(expectedRank));
        }
    }
}
