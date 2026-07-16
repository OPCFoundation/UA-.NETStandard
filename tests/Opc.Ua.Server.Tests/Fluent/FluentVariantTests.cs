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
using Opc.Ua.Server.Fluent;

#nullable enable

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Exhaustive coverage for <see cref="FluentVariant.ToVariant{TValue}(TValue)"/>
    /// — the shared marshalling bridge behind the fluent typed variable
    /// surface. Verifies every built-in OPC UA Variant type (scalar,
    /// <see cref="ArrayOf{T}"/> and <see cref="MatrixOf{T}"/>), enumerations,
    /// null handling, and the unsupported-type failure path all route through
    /// the same <see cref="IVariantBuilder{T}"/> registration the rest of the
    /// stack uses.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    [Parallelizable]
    public class FluentVariantTests
    {
        private static readonly Guid s_guid =
            new("12345678-1234-1234-1234-123456789012");

        /// <summary>
        /// Asserts that <see cref="FluentVariant.ToVariant{TValue}(TValue)"/>
        /// produces exactly what the canonical <see cref="IVariantBuilder{T}"/>
        /// registration on <see cref="VariantBuilder"/> produces for the same
        /// value.
        /// </summary>
        private static void AssertMatchesBuilder<T>(T value)
        {
            var builder = (IVariantBuilder<T>)(object)default(VariantBuilder);
            Variant expected = builder.WithValue(value);
            Variant actual = FluentVariant.ToVariant(value);
            Assert.That(
                actual.TypeInfo.BuiltInType,
                Is.EqualTo(expected.TypeInfo.BuiltInType));
            Assert.That(
                actual.TypeInfo.ValueRank,
                Is.EqualTo(expected.TypeInfo.ValueRank));
            Assert.That(actual, Is.EqualTo(expected));
        }

        private static void AssertArrayMatchesBuilder<T>(params T[] items)
        {
            AssertMatchesBuilder<ArrayOf<T>>(items);
        }

        private static void AssertMatrixMatchesBuilder<T>(T[,] items)
        {
            AssertMatchesBuilder(MatrixOf.From<T>(items));
        }

        [Test]
        public void ScalarBoolean()
        {
            AssertMatchesBuilder(true);
            AssertMatchesBuilder(false);
        }

        [Test]
        public void ScalarSByte()
        {
            AssertMatchesBuilder(sbyte.MinValue);
            AssertMatchesBuilder(sbyte.MaxValue);
        }

        [Test]
        public void ScalarByte()
        {
            AssertMatchesBuilder(byte.MinValue);
            AssertMatchesBuilder(byte.MaxValue);
        }

        [Test]
        public void ScalarInt16()
        {
            AssertMatchesBuilder(short.MinValue);
            AssertMatchesBuilder(short.MaxValue);
        }

        [Test]
        public void ScalarUInt16()
        {
            AssertMatchesBuilder(ushort.MinValue);
            AssertMatchesBuilder(ushort.MaxValue);
        }

        [Test]
        public void ScalarInt32()
        {
            AssertMatchesBuilder(int.MinValue);
            AssertMatchesBuilder(int.MaxValue);
        }

        [Test]
        public void ScalarUInt32()
        {
            AssertMatchesBuilder(uint.MinValue);
            AssertMatchesBuilder(uint.MaxValue);
        }

        [Test]
        public void ScalarInt64()
        {
            AssertMatchesBuilder(long.MinValue);
            AssertMatchesBuilder(long.MaxValue);
        }

        [Test]
        public void ScalarUInt64()
        {
            AssertMatchesBuilder(ulong.MinValue);
            AssertMatchesBuilder(ulong.MaxValue);
        }

        [Test]
        public void ScalarFloat()
        {
            AssertMatchesBuilder(3.14f);
            AssertMatchesBuilder(float.MinValue);
            AssertMatchesBuilder(float.MaxValue);
        }

        [Test]
        public void ScalarDouble()
        {
            AssertMatchesBuilder(3.14159);
            AssertMatchesBuilder(double.MinValue);
            AssertMatchesBuilder(double.MaxValue);
        }

        [Test]
        public void ScalarString()
        {
            AssertMatchesBuilder("hello");
            AssertMatchesBuilder(string.Empty);
        }

        [Test]
        public void ScalarDateTimeUtc()
        {
            AssertMatchesBuilder(new DateTimeUtc(2024, 6, 15, 12, 30, 0));
        }

        [Test]
        public void ScalarUuid()
        {
            AssertMatchesBuilder(new Uuid(s_guid));
        }

        [Test]
        public void ScalarByteString()
        {
            AssertMatchesBuilder(ByteString.From(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void ScalarXmlElement()
        {
            AssertMatchesBuilder(XmlElement.From("<test>value</test>"));
        }

        [Test]
        public void ScalarNodeId()
        {
            AssertMatchesBuilder(new NodeId(42));
        }

        [Test]
        public void ScalarExpandedNodeId()
        {
            AssertMatchesBuilder(new ExpandedNodeId(42));
        }

        [Test]
        public void ScalarStatusCode()
        {
            AssertMatchesBuilder(StatusCodes.BadNoData);
        }

        [Test]
        public void ScalarQualifiedName()
        {
            AssertMatchesBuilder(new QualifiedName("test", 2));
        }

        [Test]
        public void ScalarLocalizedText()
        {
            AssertMatchesBuilder(new LocalizedText("en", "text"));
        }

        [Test]
        public void ScalarExtensionObject()
        {
            AssertMatchesBuilder(ExtensionObject.Null);
        }

        [Test]
        public void ScalarDataValue()
        {
            AssertMatchesBuilder(new DataValue(Variant.From(42)));
        }

        [Test]
        public void ArrayBoolean()
        {
            AssertArrayMatchesBuilder(true, false, true);
        }

        [Test]
        public void ArraySByte()
        {
            AssertArrayMatchesBuilder<sbyte>(-1, 0, 1);
        }

        [Test]
        public void ArrayByte()
        {
            AssertArrayMatchesBuilder<byte>(0, 1, 255);
        }

        [Test]
        public void ArrayInt16()
        {
            AssertArrayMatchesBuilder<short>(-1, 0, 1);
        }

        [Test]
        public void ArrayUInt16()
        {
            AssertArrayMatchesBuilder<ushort>(0, 1, 2);
        }

        [Test]
        public void ArrayInt32()
        {
            AssertArrayMatchesBuilder(1, 2, 3);
        }

        [Test]
        public void ArrayUInt32()
        {
            AssertArrayMatchesBuilder(1u, 2u, 3u);
        }

        [Test]
        public void ArrayInt64()
        {
            AssertArrayMatchesBuilder(1L, 2L, 3L);
        }

        [Test]
        public void ArrayUInt64()
        {
            AssertArrayMatchesBuilder(1uL, 2uL, 3uL);
        }

        [Test]
        public void ArrayFloat()
        {
            AssertArrayMatchesBuilder(1.5f, 2.5f);
        }

        [Test]
        public void ArrayDouble()
        {
            AssertArrayMatchesBuilder(1.5, 2.5);
        }

        [Test]
        public void ArrayString()
        {
            AssertArrayMatchesBuilder("a", "b");
        }

        [Test]
        public void ArrayDateTimeUtc()
        {
            AssertArrayMatchesBuilder(
                new DateTimeUtc(2024, 1, 1, 0, 0, 0),
                new DateTimeUtc(2024, 6, 15, 12, 30, 0));
        }

        [Test]
        public void ArrayUuid()
        {
            AssertArrayMatchesBuilder(new Uuid(s_guid));
        }

        [Test]
        public void ArrayByteString()
        {
            AssertArrayMatchesBuilder(ByteString.From(new byte[] { 1, 2 }));
        }

        [Test]
        public void ArrayXmlElement()
        {
            AssertArrayMatchesBuilder(XmlElement.From("<a/>"));
        }

        [Test]
        public void ArrayNodeId()
        {
            AssertArrayMatchesBuilder(new NodeId(1), new NodeId(2));
        }

        [Test]
        public void ArrayExpandedNodeId()
        {
            AssertArrayMatchesBuilder(new ExpandedNodeId(1));
        }

        [Test]
        public void ArrayStatusCode()
        {
            AssertArrayMatchesBuilder(StatusCodes.Good);
        }

        [Test]
        public void ArrayQualifiedName()
        {
            AssertArrayMatchesBuilder(new QualifiedName("a", 2));
        }

        [Test]
        public void ArrayLocalizedText()
        {
            AssertArrayMatchesBuilder(new LocalizedText("en", "a"));
        }

        [Test]
        public void ArrayExtensionObject()
        {
            AssertArrayMatchesBuilder(ExtensionObject.Null);
        }

        [Test]
        public void ArrayDataValue()
        {
            AssertArrayMatchesBuilder(new DataValue(Variant.From(1)));
        }

        [Test]
        public void ArrayVariant()
        {
            AssertArrayMatchesBuilder(Variant.From(1), Variant.From("a"));
        }

        [Test]
        public void MatrixInt32()
        {
            AssertMatrixMatchesBuilder(new[,] { { 1, 2 }, { 3, 4 } });
        }

        [Test]
        public void MatrixDouble()
        {
            AssertMatrixMatchesBuilder(new[,] { { 1.0, 2.0 }, { 3.0, 4.0 } });
        }

        [Test]
        public void MatrixString()
        {
            AssertMatrixMatchesBuilder(new[,] { { "a", "b" }, { "c", "d" } });
        }

        [Test]
        public void MatrixBoolean()
        {
            AssertMatrixMatchesBuilder(new[,] { { true, false }, { false, true } });
        }

        [Test]
        public void EnumMapsToInt32WireValue()
        {
            Variant v = FluentVariant.ToVariant(TestEnum.Second);
            Assert.That(v, Is.EqualTo(Variant.From(TestEnum.Second)));
        }

        [Test]
        public void NullReferenceValueBecomesVariantNull()
        {
            Variant v = FluentVariant.ToVariant<string>(null!);
            Assert.That(v, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void UnsupportedTypeThrowsBadNotSupported()
        {
            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => FluentVariant.ToVariant(new UnsupportedType()))!;
            Assert.That(
                ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadNotSupported));
        }

        private enum TestEnum
        {
            First = 0,
            Second = 5
        }

        private sealed class UnsupportedType
        {
        }
    }
}
