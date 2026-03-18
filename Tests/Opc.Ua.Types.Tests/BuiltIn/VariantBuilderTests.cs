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

namespace Opc.Ua.Types.Tests.BuiltIn
{
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariantBuilderTests
    {
        /// <summary>
        /// Asserts that WithValue followed by GetValue returns the original value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
#pragma warning disable CA1859 // Use concrete types when possible for improved performance
        private static void AssertRoundTrip<T>(IVariantBuilder<T> builder, T input)
#pragma warning restore CA1859 // Use concrete types when possible for improved performance
        {
            Variant variant = builder.WithValue(input);
            T result = builder.GetValue(variant);
            Assert.That(result, Is.EqualTo(input));
        }

        private static readonly VariantBuilder s_builder = new VariantBuilder();
        private static readonly Guid s_testGuid = new Guid("12345678-1234-1234-1234-123456789012");

        /// <summary>
        /// Test enum for EnumerationBuilder tests.
        /// </summary>
        private enum TestColor
        {
            Red = 0,
            Green = 1,
            Blue = 2,
            Alpha = 3
        }

        [Test]
        public void WithValueAndGetValueBool()
        {
            AssertRoundTrip(s_builder, true);
        }

        [Test]
        public void WithValueAndGetValueBoolFalse()
        {
            AssertRoundTrip(s_builder, false);
        }

        [Test]
        public void WithValueAndGetValueSByte()
        {
            AssertRoundTrip(s_builder, (sbyte)-5);
        }

        [Test]
        public void WithValueAndGetValueByte()
        {
            AssertRoundTrip(s_builder, (byte)42);
        }

        [Test]
        public void WithValueAndGetValueInt16()
        {
            AssertRoundTrip(s_builder, (short)-1000);
        }

        [Test]
        public void WithValueAndGetValueUInt16()
        {
            AssertRoundTrip(s_builder, (ushort)1000);
        }

        [Test]
        public void WithValueAndGetValueInt32()
        {
            AssertRoundTrip(s_builder, 12345);
        }

        [Test]
        public void WithValueAndGetValueUInt32()
        {
            AssertRoundTrip(s_builder, 12345u);
        }

        [Test]
        public void WithValueAndGetValueInt64()
        {
            AssertRoundTrip(s_builder, 123456789L);
        }

        [Test]
        public void WithValueAndGetValueUInt64()
        {
            AssertRoundTrip(s_builder, 123456789uL);
        }

        [Test]
        public void WithValueAndGetValueFloat()
        {
            AssertRoundTrip(s_builder, 3.14f);
        }

        [Test]
        public void WithValueAndGetValueDouble()
        {
            AssertRoundTrip(s_builder, 3.14159);
        }

        [Test]
        public void WithValueAndGetValueString()
        {
            AssertRoundTrip(s_builder, "hello");
        }

        [Test]
        public void WithValueAndGetValueDateTimeUtc()
        {
            var input = new DateTimeUtc(2024, 6, 15, 12, 30, 0);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueUuid()
        {
            var input = new Uuid(s_testGuid);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueByteString()
        {
            var input = ByteString.From(new byte[] { 1, 2, 3 });
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueXmlElement()
        {
            var input = XmlElement.From("<test>value</test>");
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueNodeId()
        {
            var input = new NodeId(42);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueExpandedNodeId()
        {
            var input = new ExpandedNodeId(42);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueStatusCode()
        {
            var input = new StatusCode(0x80010000u);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueQualifiedName()
        {
            var input = new QualifiedName("test");
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueLocalizedText()
        {
            var input = new LocalizedText("en", "test");
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueExtensionObject()
        {
            ExtensionObject input = ExtensionObject.Null;
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueDataValue()
        {
            IVariantBuilder<DataValue> builder = s_builder;
            var input = new DataValue(Variant.From(42));
            Variant variant = builder.WithValue(input);
            DataValue result = builder.GetValue(variant);
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.EqualTo(input));
        }

        [Test]
        public void WithValueBoolProducesCorrectType()
        {
            IVariantBuilder<bool> builder = s_builder;
            Variant variant = builder.WithValue(true);
            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
        }

        [Test]
        public void WithValueInt32ProducesCorrectType()
        {
            IVariantBuilder<int> builder = s_builder;
            Variant variant = builder.WithValue(42);
            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public void WithValueDoubleProducesCorrectType()
        {
            IVariantBuilder<double> builder = s_builder;
            Variant variant = builder.WithValue(3.14);
            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Double));
        }

        [Test]
        public void WithValueStringProducesCorrectType()
        {
            IVariantBuilder<string> builder = s_builder;
            Variant variant = builder.WithValue("hello");
            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.String));
        }

        [Test]
        public void WithValueAndGetValueSByteMinMax()
        {
            AssertRoundTrip(s_builder, sbyte.MinValue);
            AssertRoundTrip(s_builder, sbyte.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueByteMinMax()
        {
            AssertRoundTrip(s_builder, byte.MinValue);
            AssertRoundTrip(s_builder, byte.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueInt16MinMax()
        {
            AssertRoundTrip(s_builder, short.MinValue);
            AssertRoundTrip(s_builder, short.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueUInt16MinMax()
        {
            AssertRoundTrip(s_builder, ushort.MinValue);
            AssertRoundTrip(s_builder, ushort.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueInt32MinMax()
        {
            AssertRoundTrip(s_builder, int.MinValue);
            AssertRoundTrip(s_builder, int.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueUInt32MinMax()
        {
            AssertRoundTrip(s_builder, uint.MinValue);
            AssertRoundTrip(s_builder, uint.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueInt64MinMax()
        {
            AssertRoundTrip(s_builder, long.MinValue);
            AssertRoundTrip(s_builder, long.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueUInt64MinMax()
        {
            AssertRoundTrip(s_builder, ulong.MinValue);
            AssertRoundTrip(s_builder, ulong.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueFloatSpecial()
        {
            AssertRoundTrip(s_builder, float.MinValue);
            AssertRoundTrip(s_builder, float.MaxValue);
            AssertRoundTrip(s_builder, 0f);
        }

        [Test]
        public void WithValueAndGetValueDoubleSpecial()
        {
            AssertRoundTrip(s_builder, double.MinValue);
            AssertRoundTrip(s_builder, double.MaxValue);
            AssertRoundTrip(s_builder, 0.0);
        }

        [Test]
        public void WithValueAndGetValueBoolArray()
        {
            AssertRoundTrip(s_builder, ArrayOf.Wrapped(true, false));
        }

        [Test]
        public void WithValueAndGetValueSByteArray()
        {
            AssertRoundTrip(s_builder, ArrayOf.Wrapped((sbyte)-1, (sbyte)1));
        }

        [Test]
        public void WithValueAndGetValueByteArray()
        {
            AssertRoundTrip(s_builder, ArrayOf.Wrapped((byte)0, (byte)255));
        }

        [Test]
        public void WithValueAndGetValueInt16Array()
        {
            AssertRoundTrip(s_builder, ArrayOf.Wrapped((short)-100, (short)100));
        }

        [Test]
        public void WithValueAndGetValueUInt16Array()
        {
            AssertRoundTrip(s_builder, ArrayOf.Wrapped((ushort)0, (ushort)1000));
        }

        [Test]
        public void WithValueAndGetValueInt32Array()
        {
            AssertRoundTrip(s_builder, ArrayOf.Wrapped(-3, 3));
        }

        [Test]
        public void WithValueAndGetValueUInt32Array()
        {
            AssertRoundTrip(s_builder, ArrayOf.Wrapped(0u, 42u));
        }

        [Test]
        public void WithValueAndGetValueInt64Array()
        {
            AssertRoundTrip(s_builder, ArrayOf.Wrapped(-100L, 100L));
        }

        [Test]
        public void WithValueAndGetValueUInt64Array()
        {
            AssertRoundTrip(s_builder, ArrayOf.Wrapped(0uL, 100uL));
        }

        [Test]
        public void WithValueAndGetValueFloatArray()
        {
            AssertRoundTrip(s_builder, ArrayOf.Wrapped(1.1f, 2.2f));
        }

        [Test]
        public void WithValueAndGetValueDoubleArray()
        {
            AssertRoundTrip(s_builder, ArrayOf.Wrapped(1.1, 2.2));
        }

        [Test]
        public void WithValueAndGetValueStringArray()
        {
            AssertRoundTrip(s_builder, ArrayOf.Wrapped("a", "b"));
        }

        [Test]
        public void WithValueAndGetValueDateTimeUtcArray()
        {
            var dt1 = new DateTimeUtc(2024, 1, 1);
            var dt2 = new DateTimeUtc(2024, 12, 31);
            AssertRoundTrip(s_builder, ArrayOf.Wrapped(dt1, dt2));
        }

        [Test]
        public void WithValueAndGetValueUuidArray()
        {
            var uuid1 = new Uuid(s_testGuid);
            var uuid2 = new Uuid(Guid.Empty);
            AssertRoundTrip(s_builder, ArrayOf.Wrapped(uuid1, uuid2));
        }

        [Test]
        public void WithValueAndGetValueByteStringArray()
        {
            var bs1 = ByteString.From(new byte[] { 1, 2 });
            var bs2 = ByteString.From(new byte[] { 3, 4 });
            AssertRoundTrip(s_builder, ArrayOf.Wrapped(bs1, bs2));
        }

        [Test]
        public void WithValueAndGetValueXmlElementArray()
        {
            var xml1 = XmlElement.From("<a/>");
            var xml2 = XmlElement.From("<b/>");
            AssertRoundTrip(s_builder, ArrayOf.Wrapped(xml1, xml2));
        }

        [Test]
        public void WithValueAndGetValueNodeIdArray()
        {
            AssertRoundTrip(s_builder,
                ArrayOf.Wrapped(new NodeId(1), new NodeId(2)));
        }

        [Test]
        public void WithValueAndGetValueExpandedNodeIdArray()
        {
            AssertRoundTrip(s_builder,
                ArrayOf.Wrapped(new ExpandedNodeId(1), new ExpandedNodeId(2)));
        }

        [Test]
        public void WithValueAndGetValueStatusCodeArray()
        {
            AssertRoundTrip(s_builder,
                ArrayOf.Wrapped(new StatusCode(0u), new StatusCode(0x80000000u)));
        }

        [Test]
        public void WithValueAndGetValueQualifiedNameArray()
        {
            AssertRoundTrip(s_builder,
                ArrayOf.Wrapped(new QualifiedName("a"), new QualifiedName("b")));
        }

        [Test]
        public void WithValueAndGetValueLocalizedTextArray()
        {
            AssertRoundTrip(s_builder,
                ArrayOf.Wrapped(new LocalizedText("en", "a"), new LocalizedText("en", "b")));
        }

        [Test]
        public void WithValueAndGetValueExtensionObjectArray()
        {
            AssertRoundTrip(s_builder,
                ArrayOf.Wrapped(ExtensionObject.Null, ExtensionObject.Null));
        }

        [Test]
        public void WithValueAndGetValueDataValueArray()
        {
            var dv1 = new DataValue(Variant.From(1));
            var dv2 = new DataValue(Variant.From(2));
            IVariantBuilder<ArrayOf<DataValue>> builder = s_builder;
            var input = ArrayOf.Wrapped(dv1, dv2);
            Variant variant = builder.WithValue(input);
            ArrayOf<DataValue> result = builder.GetValue(variant);
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void WithValueAndGetValueVariantArray()
        {
            var input = ArrayOf.Wrapped(Variant.From(42), Variant.From("test"));
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueBoolMatrix()
        {
            MatrixOf<bool> input = ArrayOf.Wrapped(true, false, true, false).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueSByteMatrix()
        {
            MatrixOf<sbyte> input = ArrayOf.Wrapped((sbyte)-1, (sbyte)0, (sbyte)1, (sbyte)2).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueByteMatrix()
        {
            MatrixOf<byte> input = ArrayOf.Wrapped((byte)0, (byte)1, (byte)2, (byte)3).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueInt16Matrix()
        {
            MatrixOf<short> input = ArrayOf.Wrapped((short)1, (short)2, (short)3, (short)4).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueUInt16Matrix()
        {
            MatrixOf<ushort> input = ArrayOf.Wrapped((ushort)1, (ushort)2, (ushort)3, (ushort)4).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueInt32Matrix()
        {
            MatrixOf<int> input = ArrayOf.Wrapped(1, 2, 3, 4).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueUInt32Matrix()
        {
            MatrixOf<uint> input = ArrayOf.Wrapped(1u, 2u, 3u, 4u).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueInt64Matrix()
        {
            MatrixOf<long> input = ArrayOf.Wrapped(1L, 2L, 3L, 4L).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueUInt64Matrix()
        {
            MatrixOf<ulong> input = ArrayOf.Wrapped(1uL, 2uL, 3uL, 4uL).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueFloatMatrix()
        {
            MatrixOf<float> input = ArrayOf.Wrapped(1.0f, 2.0f, 3.0f, 4.0f).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueDoubleMatrix()
        {
            MatrixOf<double> input = ArrayOf.Wrapped(1.0, 2.0, 3.0, 4.0).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueStringMatrix()
        {
            MatrixOf<string> input = ArrayOf.Wrapped("a", "b", "c", "d").ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueDateTimeUtcMatrix()
        {
            var dt = new DateTimeUtc(2024, 6, 15);
            MatrixOf<DateTimeUtc> input = ArrayOf.Wrapped(dt, dt, dt, dt).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueUuidMatrix()
        {
            var uuid = new Uuid(s_testGuid);
            MatrixOf<Uuid> input = ArrayOf.Wrapped(uuid, uuid, uuid, uuid).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueByteStringMatrix()
        {
            var bs = ByteString.From(new byte[] { 1, 2 });
            MatrixOf<ByteString> input = ArrayOf.Wrapped(bs, bs, bs, bs).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueXmlElementMatrix()
        {
            var xml = XmlElement.From("<test/>");
            MatrixOf<XmlElement> input = ArrayOf.Wrapped(xml, xml, xml, xml).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueNodeIdMatrix()
        {
            MatrixOf<NodeId> input = ArrayOf.Wrapped(
                new NodeId(1), new NodeId(2),
                new NodeId(3), new NodeId(4)).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueExpandedNodeIdMatrix()
        {
            MatrixOf<ExpandedNodeId> input = ArrayOf.Wrapped(
                new ExpandedNodeId(1), new ExpandedNodeId(2),
                new ExpandedNodeId(3), new ExpandedNodeId(4)).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueStatusCodeMatrix()
        {
            MatrixOf<StatusCode> input = ArrayOf.Wrapped(
                new StatusCode(0u), new StatusCode(1u),
                new StatusCode(2u), new StatusCode(3u)).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueQualifiedNameMatrix()
        {
            MatrixOf<QualifiedName> input = ArrayOf.Wrapped(
                new QualifiedName("a"), new QualifiedName("b"),
                new QualifiedName("c"), new QualifiedName("d")).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueLocalizedTextMatrix()
        {
            MatrixOf<LocalizedText> input = ArrayOf.Wrapped(
                new LocalizedText("a"), new LocalizedText("b"),
                new LocalizedText("c"), new LocalizedText("d")).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueExtensionObjectMatrix()
        {
            MatrixOf<ExtensionObject> input = ArrayOf.Wrapped(
                ExtensionObject.Null, ExtensionObject.Null,
                ExtensionObject.Null, ExtensionObject.Null).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueDataValueMatrix()
        {
            IVariantBuilder<MatrixOf<DataValue>> builder = s_builder;
            MatrixOf<DataValue> input = ArrayOf.Wrapped(
                new DataValue(Variant.From(1)), new DataValue(Variant.From(2)),
                new DataValue(Variant.From(3)), new DataValue(Variant.From(4))).ToMatrix(2, 2);
            Variant variant = builder.WithValue(input);
            MatrixOf<DataValue> result = builder.GetValue(variant);
            Assert.That(result.Count, Is.EqualTo(4));
            Assert.That(result.Dimensions, Has.Length.EqualTo(2));
        }

        [Test]
        public void WithValueAndGetValueVariantMatrix()
        {
            MatrixOf<Variant> input = ArrayOf.Wrapped(
                Variant.From(1), Variant.From(2),
                Variant.From(3), Variant.From(4)).ToMatrix(2, 2);
            AssertRoundTrip(s_builder, input);
        }

        [Test]
        public void EnumerationBuilderScalarRoundTrip()
        {
            IVariantBuilder<TestColor> builder = new EnumerationBuilder<TestColor>();
            Variant variant = builder.WithValue(TestColor.Green);
            TestColor result = builder.GetValue(variant);
            Assert.That(result, Is.EqualTo(TestColor.Green));
        }

        [Test]
        public void EnumerationBuilderArrayRoundTrip()
        {
            IVariantBuilder<ArrayOf<TestColor>> builder = new EnumerationBuilder<TestColor>();
            var input = ArrayOf.Wrapped(TestColor.Red, TestColor.Green, TestColor.Blue);
            Variant variant = builder.WithValue(input);
            ArrayOf<TestColor> result = builder.GetValue(variant);
            Assert.That(result, Is.EqualTo(input));
        }

        [Test]
        public void EnumerationBuilderMatrixRoundTrip()
        {
            IVariantBuilder<MatrixOf<TestColor>> builder = new EnumerationBuilder<TestColor>();
            MatrixOf<TestColor> input = ArrayOf.Wrapped(
                TestColor.Red, TestColor.Green,
                TestColor.Blue, TestColor.Alpha).ToMatrix(2, 2);
            Variant variant = builder.WithValue(input);
            MatrixOf<TestColor> result = builder.GetValue(variant);
            Assert.That(result, Is.EqualTo(input));
        }

        [Test]
        public void StructureBuilderScalarRoundTrip()
        {
            IVariantBuilder<Argument> builder = new StructureBuilder<Argument>();
            var input = new Argument { Name = "test" };
            Variant variant = builder.WithValue(input);
            Argument result = builder.GetValue(variant);
            Assert.That(result, Is.SameAs(input));
        }

        [Test]
        public void StructureBuilderArrayRoundTrip()
        {
            IVariantBuilder<ArrayOf<Argument>> builder = new StructureBuilder<Argument>();
            var input = ArrayOf.Wrapped(
                new Argument { Name = "a" },
                new Argument { Name = "b" });
            Variant variant = builder.WithValue(input);
            ArrayOf<Argument> result = builder.GetValue(variant);
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void StructureBuilderMatrixRoundTrip()
        {
            IVariantBuilder<MatrixOf<Argument>> builder = new StructureBuilder<Argument>();
            MatrixOf<Argument> input = ArrayOf.Wrapped(
                new Argument { Name = "a" },
                new Argument { Name = "b" },
                new Argument { Name = "c" },
                new Argument { Name = "d" }).ToMatrix(2, 2);
            Variant variant = builder.WithValue(input);
            MatrixOf<Argument> result = builder.GetValue(variant);
            Assert.That(result.Count, Is.EqualTo(4));
            Assert.That(result.Dimensions, Has.Length.EqualTo(2));
        }
    }
}
