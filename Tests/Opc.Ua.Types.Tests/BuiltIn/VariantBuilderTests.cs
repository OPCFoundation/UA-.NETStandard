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
        #region Helpers

        /// <summary>
        /// Asserts that WithValue followed by GetValue returns the original value.
        /// </summary>
        private static void AssertRoundTrip<T>(IVariantBuilder<T> builder, T input)
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

        #endregion

        #region Scalar WithValue and GetValue

        [Test]
        public void WithValueAndGetValueBool()
        {
            AssertRoundTrip<bool>(s_builder, true);
        }

        [Test]
        public void WithValueAndGetValueBoolFalse()
        {
            AssertRoundTrip<bool>(s_builder, false);
        }

        [Test]
        public void WithValueAndGetValueSByte()
        {
            AssertRoundTrip<sbyte>(s_builder, (sbyte)-5);
        }

        [Test]
        public void WithValueAndGetValueByte()
        {
            AssertRoundTrip<byte>(s_builder, (byte)42);
        }

        [Test]
        public void WithValueAndGetValueInt16()
        {
            AssertRoundTrip<short>(s_builder, (short)-1000);
        }

        [Test]
        public void WithValueAndGetValueUInt16()
        {
            AssertRoundTrip<ushort>(s_builder, (ushort)1000);
        }

        [Test]
        public void WithValueAndGetValueInt32()
        {
            AssertRoundTrip<int>(s_builder, 12345);
        }

        [Test]
        public void WithValueAndGetValueUInt32()
        {
            AssertRoundTrip<uint>(s_builder, 12345u);
        }

        [Test]
        public void WithValueAndGetValueInt64()
        {
            AssertRoundTrip<long>(s_builder, 123456789L);
        }

        [Test]
        public void WithValueAndGetValueUInt64()
        {
            AssertRoundTrip<ulong>(s_builder, 123456789uL);
        }

        [Test]
        public void WithValueAndGetValueFloat()
        {
            AssertRoundTrip<float>(s_builder, 3.14f);
        }

        [Test]
        public void WithValueAndGetValueDouble()
        {
            AssertRoundTrip<double>(s_builder, 3.14159);
        }

        [Test]
        public void WithValueAndGetValueString()
        {
            AssertRoundTrip<string>(s_builder, "hello");
        }

        [Test]
        public void WithValueAndGetValueDateTimeUtc()
        {
            var input = new DateTimeUtc(2024, 6, 15, 12, 30, 0);
            AssertRoundTrip<DateTimeUtc>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueUuid()
        {
            var input = new Uuid(s_testGuid);
            AssertRoundTrip<Uuid>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueByteString()
        {
            var input = ByteString.From(new byte[] { 1, 2, 3 });
            AssertRoundTrip<ByteString>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueXmlElement()
        {
            var input = XmlElement.From("<test>value</test>");
            AssertRoundTrip<XmlElement>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueNodeId()
        {
            var input = new NodeId(42);
            AssertRoundTrip<NodeId>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueExpandedNodeId()
        {
            var input = new ExpandedNodeId(42);
            AssertRoundTrip<ExpandedNodeId>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueStatusCode()
        {
            var input = new StatusCode(0x80010000u);
            AssertRoundTrip<StatusCode>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueQualifiedName()
        {
            var input = new QualifiedName("test");
            AssertRoundTrip<QualifiedName>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueLocalizedText()
        {
            var input = new LocalizedText("en", "test");
            AssertRoundTrip<LocalizedText>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueExtensionObject()
        {
            var input = ExtensionObject.Null;
            AssertRoundTrip<ExtensionObject>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueDataValue()
        {
            IVariantBuilder<DataValue> builder = s_builder;
            var input = new DataValue(Variant.From(42));
            Variant variant = builder.WithValue(input);
            DataValue result = builder.GetValue(variant);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Value, Is.EqualTo(input.Value));
        }

        #endregion

        #region Scalar WithValue Produces Correct BuiltInType

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

        #endregion

        #region Scalar Boundary Values

        [Test]
        public void WithValueAndGetValueSByteMinMax()
        {
            AssertRoundTrip<sbyte>(s_builder, sbyte.MinValue);
            AssertRoundTrip<sbyte>(s_builder, sbyte.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueByteMinMax()
        {
            AssertRoundTrip<byte>(s_builder, byte.MinValue);
            AssertRoundTrip<byte>(s_builder, byte.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueInt16MinMax()
        {
            AssertRoundTrip<short>(s_builder, short.MinValue);
            AssertRoundTrip<short>(s_builder, short.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueUInt16MinMax()
        {
            AssertRoundTrip<ushort>(s_builder, ushort.MinValue);
            AssertRoundTrip<ushort>(s_builder, ushort.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueInt32MinMax()
        {
            AssertRoundTrip<int>(s_builder, int.MinValue);
            AssertRoundTrip<int>(s_builder, int.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueUInt32MinMax()
        {
            AssertRoundTrip<uint>(s_builder, uint.MinValue);
            AssertRoundTrip<uint>(s_builder, uint.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueInt64MinMax()
        {
            AssertRoundTrip<long>(s_builder, long.MinValue);
            AssertRoundTrip<long>(s_builder, long.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueUInt64MinMax()
        {
            AssertRoundTrip<ulong>(s_builder, ulong.MinValue);
            AssertRoundTrip<ulong>(s_builder, ulong.MaxValue);
        }

        [Test]
        public void WithValueAndGetValueFloatSpecial()
        {
            AssertRoundTrip<float>(s_builder, float.MinValue);
            AssertRoundTrip<float>(s_builder, float.MaxValue);
            AssertRoundTrip<float>(s_builder, 0f);
        }

        [Test]
        public void WithValueAndGetValueDoubleSpecial()
        {
            AssertRoundTrip<double>(s_builder, double.MinValue);
            AssertRoundTrip<double>(s_builder, double.MaxValue);
            AssertRoundTrip<double>(s_builder, 0.0);
        }

        #endregion

        #region Array WithValue and GetValue

        [Test]
        public void WithValueAndGetValueBoolArray()
        {
            AssertRoundTrip<ArrayOf<bool>>(s_builder, ArrayOf.Wrapped(true, false));
        }

        [Test]
        public void WithValueAndGetValueSByteArray()
        {
            AssertRoundTrip<ArrayOf<sbyte>>(s_builder, ArrayOf.Wrapped((sbyte)-1, (sbyte)1));
        }

        [Test]
        public void WithValueAndGetValueByteArray()
        {
            AssertRoundTrip<ArrayOf<byte>>(s_builder, ArrayOf.Wrapped((byte)0, (byte)255));
        }

        [Test]
        public void WithValueAndGetValueInt16Array()
        {
            AssertRoundTrip<ArrayOf<short>>(s_builder, ArrayOf.Wrapped((short)-100, (short)100));
        }

        [Test]
        public void WithValueAndGetValueUInt16Array()
        {
            AssertRoundTrip<ArrayOf<ushort>>(s_builder, ArrayOf.Wrapped((ushort)0, (ushort)1000));
        }

        [Test]
        public void WithValueAndGetValueInt32Array()
        {
            AssertRoundTrip<ArrayOf<int>>(s_builder, ArrayOf.Wrapped(-3, 3));
        }

        [Test]
        public void WithValueAndGetValueUInt32Array()
        {
            AssertRoundTrip<ArrayOf<uint>>(s_builder, ArrayOf.Wrapped(0u, 42u));
        }

        [Test]
        public void WithValueAndGetValueInt64Array()
        {
            AssertRoundTrip<ArrayOf<long>>(s_builder, ArrayOf.Wrapped(-100L, 100L));
        }

        [Test]
        public void WithValueAndGetValueUInt64Array()
        {
            AssertRoundTrip<ArrayOf<ulong>>(s_builder, ArrayOf.Wrapped(0uL, 100uL));
        }

        [Test]
        public void WithValueAndGetValueFloatArray()
        {
            AssertRoundTrip<ArrayOf<float>>(s_builder, ArrayOf.Wrapped(1.1f, 2.2f));
        }

        [Test]
        public void WithValueAndGetValueDoubleArray()
        {
            AssertRoundTrip<ArrayOf<double>>(s_builder, ArrayOf.Wrapped(1.1, 2.2));
        }

        [Test]
        public void WithValueAndGetValueStringArray()
        {
            AssertRoundTrip<ArrayOf<string>>(s_builder, ArrayOf.Wrapped("a", "b"));
        }

        [Test]
        public void WithValueAndGetValueDateTimeUtcArray()
        {
            var dt1 = new DateTimeUtc(2024, 1, 1);
            var dt2 = new DateTimeUtc(2024, 12, 31);
            AssertRoundTrip<ArrayOf<DateTimeUtc>>(s_builder, ArrayOf.Wrapped(dt1, dt2));
        }

        [Test]
        public void WithValueAndGetValueUuidArray()
        {
            var uuid1 = new Uuid(s_testGuid);
            var uuid2 = new Uuid(Guid.Empty);
            AssertRoundTrip<ArrayOf<Uuid>>(s_builder, ArrayOf.Wrapped(uuid1, uuid2));
        }

        [Test]
        public void WithValueAndGetValueByteStringArray()
        {
            var bs1 = ByteString.From(new byte[] { 1, 2 });
            var bs2 = ByteString.From(new byte[] { 3, 4 });
            AssertRoundTrip<ArrayOf<ByteString>>(s_builder, ArrayOf.Wrapped(bs1, bs2));
        }

        [Test]
        public void WithValueAndGetValueXmlElementArray()
        {
            var xml1 = XmlElement.From("<a/>");
            var xml2 = XmlElement.From("<b/>");
            AssertRoundTrip<ArrayOf<XmlElement>>(s_builder, ArrayOf.Wrapped(xml1, xml2));
        }

        [Test]
        public void WithValueAndGetValueNodeIdArray()
        {
            AssertRoundTrip<ArrayOf<NodeId>>(s_builder,
                ArrayOf.Wrapped(new NodeId(1), new NodeId(2)));
        }

        [Test]
        public void WithValueAndGetValueExpandedNodeIdArray()
        {
            AssertRoundTrip<ArrayOf<ExpandedNodeId>>(s_builder,
                ArrayOf.Wrapped(new ExpandedNodeId(1), new ExpandedNodeId(2)));
        }

        [Test]
        public void WithValueAndGetValueStatusCodeArray()
        {
            AssertRoundTrip<ArrayOf<StatusCode>>(s_builder,
                ArrayOf.Wrapped(new StatusCode(0u), new StatusCode(0x80000000u)));
        }

        [Test]
        public void WithValueAndGetValueQualifiedNameArray()
        {
            AssertRoundTrip<ArrayOf<QualifiedName>>(s_builder,
                ArrayOf.Wrapped(new QualifiedName("a"), new QualifiedName("b")));
        }

        [Test]
        public void WithValueAndGetValueLocalizedTextArray()
        {
            AssertRoundTrip<ArrayOf<LocalizedText>>(s_builder,
                ArrayOf.Wrapped(new LocalizedText("en", "a"), new LocalizedText("en", "b")));
        }

        [Test]
        public void WithValueAndGetValueExtensionObjectArray()
        {
            AssertRoundTrip<ArrayOf<ExtensionObject>>(s_builder,
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
            AssertRoundTrip<ArrayOf<Variant>>(s_builder, input);
        }

        #endregion

        #region Matrix WithValue and GetValue

        [Test]
        public void WithValueAndGetValueBoolMatrix()
        {
            var input = ArrayOf.Wrapped(true, false, true, false).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<bool>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueSByteMatrix()
        {
            var input = ArrayOf.Wrapped((sbyte)-1, (sbyte)0, (sbyte)1, (sbyte)2).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<sbyte>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueByteMatrix()
        {
            var input = ArrayOf.Wrapped((byte)0, (byte)1, (byte)2, (byte)3).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<byte>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueInt16Matrix()
        {
            var input = ArrayOf.Wrapped((short)1, (short)2, (short)3, (short)4).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<short>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueUInt16Matrix()
        {
            var input = ArrayOf.Wrapped((ushort)1, (ushort)2, (ushort)3, (ushort)4).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<ushort>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueInt32Matrix()
        {
            var input = ArrayOf.Wrapped(1, 2, 3, 4).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<int>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueUInt32Matrix()
        {
            var input = ArrayOf.Wrapped(1u, 2u, 3u, 4u).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<uint>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueInt64Matrix()
        {
            var input = ArrayOf.Wrapped(1L, 2L, 3L, 4L).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<long>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueUInt64Matrix()
        {
            var input = ArrayOf.Wrapped(1uL, 2uL, 3uL, 4uL).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<ulong>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueFloatMatrix()
        {
            var input = ArrayOf.Wrapped(1.0f, 2.0f, 3.0f, 4.0f).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<float>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueDoubleMatrix()
        {
            var input = ArrayOf.Wrapped(1.0, 2.0, 3.0, 4.0).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<double>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueStringMatrix()
        {
            var input = ArrayOf.Wrapped("a", "b", "c", "d").ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<string>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueDateTimeUtcMatrix()
        {
            var dt = new DateTimeUtc(2024, 6, 15);
            var input = ArrayOf.Wrapped(dt, dt, dt, dt).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<DateTimeUtc>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueUuidMatrix()
        {
            var uuid = new Uuid(s_testGuid);
            var input = ArrayOf.Wrapped(uuid, uuid, uuid, uuid).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<Uuid>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueByteStringMatrix()
        {
            var bs = ByteString.From(new byte[] { 1, 2 });
            var input = ArrayOf.Wrapped(bs, bs, bs, bs).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<ByteString>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueXmlElementMatrix()
        {
            var xml = XmlElement.From("<test/>");
            var input = ArrayOf.Wrapped(xml, xml, xml, xml).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<XmlElement>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueNodeIdMatrix()
        {
            var input = ArrayOf.Wrapped(
                new NodeId(1), new NodeId(2),
                new NodeId(3), new NodeId(4)).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<NodeId>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueExpandedNodeIdMatrix()
        {
            var input = ArrayOf.Wrapped(
                new ExpandedNodeId(1), new ExpandedNodeId(2),
                new ExpandedNodeId(3), new ExpandedNodeId(4)).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<ExpandedNodeId>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueStatusCodeMatrix()
        {
            var input = ArrayOf.Wrapped(
                new StatusCode(0u), new StatusCode(1u),
                new StatusCode(2u), new StatusCode(3u)).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<StatusCode>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueQualifiedNameMatrix()
        {
            var input = ArrayOf.Wrapped(
                new QualifiedName("a"), new QualifiedName("b"),
                new QualifiedName("c"), new QualifiedName("d")).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<QualifiedName>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueLocalizedTextMatrix()
        {
            var input = ArrayOf.Wrapped(
                new LocalizedText("a"), new LocalizedText("b"),
                new LocalizedText("c"), new LocalizedText("d")).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<LocalizedText>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueExtensionObjectMatrix()
        {
            var input = ArrayOf.Wrapped(
                ExtensionObject.Null, ExtensionObject.Null,
                ExtensionObject.Null, ExtensionObject.Null).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<ExtensionObject>>(s_builder, input);
        }

        [Test]
        public void WithValueAndGetValueDataValueMatrix()
        {
            IVariantBuilder<MatrixOf<DataValue>> builder = s_builder;
            var input = ArrayOf.Wrapped(
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
            var input = ArrayOf.Wrapped(
                Variant.From(1), Variant.From(2),
                Variant.From(3), Variant.From(4)).ToMatrix(2, 2);
            AssertRoundTrip<MatrixOf<Variant>>(s_builder, input);
        }

        #endregion

        #region EnumerationBuilder

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
            var input = ArrayOf.Wrapped(
                TestColor.Red, TestColor.Green,
                TestColor.Blue, TestColor.Alpha).ToMatrix(2, 2);
            Variant variant = builder.WithValue(input);
            MatrixOf<TestColor> result = builder.GetValue(variant);
            Assert.That(result, Is.EqualTo(input));
        }

        #endregion

        #region StructureBuilder

        [Test]
        public void StructureBuilderScalarRoundTrip()
        {
            IVariantBuilder<Argument> builder = new StructureBuilder<Argument>();
            var input = new Argument() { Name = "test" };
            Variant variant = builder.WithValue(input);
            Argument result = builder.GetValue(variant);
            Assert.That(result, Is.SameAs(input));
        }

        [Test]
        public void StructureBuilderArrayRoundTrip()
        {
            IVariantBuilder<ArrayOf<Argument>> builder = new StructureBuilder<Argument>();
            var input = ArrayOf.Wrapped(
                new Argument() { Name = "a" },
                new Argument() { Name = "b" });
            Variant variant = builder.WithValue(input);
            ArrayOf<Argument> result = builder.GetValue(variant);
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void StructureBuilderMatrixRoundTrip()
        {
            IVariantBuilder<MatrixOf<Argument>> builder = new StructureBuilder<Argument>();
            var input = ArrayOf.Wrapped(
                new Argument() { Name = "a" },
                new Argument() { Name = "b" },
                new Argument() { Name = "c" },
                new Argument() { Name = "d" }).ToMatrix(2, 2);
            Variant variant = builder.WithValue(input);
            MatrixOf<Argument> result = builder.GetValue(variant);
            Assert.That(result.Count, Is.EqualTo(4));
            Assert.That(result.Dimensions, Has.Length.EqualTo(2));
        }

        #endregion
    }
}
