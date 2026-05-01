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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Xml;
using NUnit.Framework;

#pragma warning disable CA2263 // Prefer generic overload when type is known
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable NUnit4002 // Use Specific constraint
#pragma warning disable UA_NETStandard_1

namespace Opc.Ua.Types.Tests.BuiltIn
{
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariantTests
    {
        public sealed record VariantDescriptor(
            string Name,
            Func<object> ValueFactory,
            Type ValueType,
            TypeInfo TypeInfo,
            string GetMethodName)
        {
            public object CreateValue()
            {
                return ValueFactory();
            }

            public override string ToString()
            {
                return Name;
            }
        }

        private static IEnumerable<TestCaseData> ScalarConstructorCases
        {
            get
            {
                yield return CreateConstructorCase(() => true,
                    TypeInfo.Scalars.Boolean);
                yield return CreateConstructorCase(() => (sbyte)-1,
                    TypeInfo.Scalars.SByte);
                yield return CreateConstructorCase(() => (byte)1,
                    TypeInfo.Scalars.Byte);
                yield return CreateConstructorCase(() => (short)-2,
                    TypeInfo.Scalars.Int16);
                yield return CreateConstructorCase(() => (ushort)2,
                    TypeInfo.Scalars.UInt16);
                yield return CreateConstructorCase(() => -3,
                    TypeInfo.Scalars.Int32);
                yield return CreateConstructorCase(() => 3u,
                    TypeInfo.Scalars.UInt32);
                yield return CreateConstructorCase(() => -4L,
                    TypeInfo.Scalars.Int64);
                yield return CreateConstructorCase(() => 4UL,
                    TypeInfo.Scalars.UInt64);
                yield return CreateConstructorCase(() => 1.25f,
                    TypeInfo.Scalars.Float);
                yield return CreateConstructorCase(() => 2.25d,
                    TypeInfo.Scalars.Double);
                yield return CreateConstructorCase(() => "opc",
                    TypeInfo.Scalars.String);
                yield return CreateConstructorCase(() => (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 2, 3, 4, 5), DateTimeKind.Utc),
                    TypeInfo.Scalars.DateTime);
                yield return CreateConstructorCase(() => Uuid.NewUuid(),
                    TypeInfo.Scalars.Guid);
                yield return CreateConstructorCase(() => ByteString.From(1, 2, 3),
                    TypeInfo.Scalars.ByteString);
                yield return CreateConstructorCase(() => CreateXmlElement("Scalar"),
                    TypeInfo.Scalars.XmlElement);
                yield return CreateConstructorCase(() => new NodeId(10, 1),
                    TypeInfo.Scalars.NodeId);
                yield return CreateConstructorCase(() => ExpandedNodeId.Parse("nsu=Test;s=Node"),
                    TypeInfo.Scalars.ExpandedNodeId);
                yield return CreateConstructorCase(() => new StatusCode(123u),
                    TypeInfo.Scalars.StatusCode);
                yield return CreateConstructorCase(() => new QualifiedName("name", 2),
                    TypeInfo.Scalars.QualifiedName);
                yield return CreateConstructorCase(() => new LocalizedText("en", "text"),
                    TypeInfo.Scalars.LocalizedText);
                yield return CreateConstructorCase(() => new ExtensionObject(new Argument()),
                    TypeInfo.Scalars.ExtensionObject);
                yield return CreateConstructorCase(() => new DataValue(5),
                    TypeInfo.Scalars.DataValue);
            }
        }

        private static IEnumerable<TestCaseData> ArrayConstructorCases
        {
            get
            {
                yield return CreateConstructorCase(() => Array(true, false),
                    TypeInfo.Arrays.Boolean);
                yield return CreateConstructorCase(() => Array((sbyte)-1, (sbyte)1),
                    TypeInfo.Arrays.SByte);
                yield return CreateConstructorCase(() => Array((short)-2, (short)2),
                    TypeInfo.Arrays.Int16);
                yield return CreateConstructorCase(() => Array((ushort)2, (ushort)4),
                    TypeInfo.Arrays.UInt16);
                yield return CreateConstructorCase(() => Array(-3, 3),
                    TypeInfo.Arrays.Int32);
                yield return CreateConstructorCase(() => Array(3u, 4u),
                    TypeInfo.Arrays.UInt32);
                yield return CreateConstructorCase(() => Array(-4L, 4L),
                    TypeInfo.Arrays.Int64);
                yield return CreateConstructorCase(() => Array(4UL, 5UL),
                    TypeInfo.Arrays.UInt64);
                yield return CreateConstructorCase(() => Array(1.0f, 2.0f),
                    TypeInfo.Arrays.Float);
                yield return CreateConstructorCase(() => Array(1.0d, 2.0d),
                    TypeInfo.Arrays.Double);
                yield return CreateConstructorCase(() => Array("a", "b"),
                    TypeInfo.Arrays.String);
                yield return CreateConstructorCase(() => Array(
                        (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 2, 1), DateTimeKind.Utc),
                        (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2025, 2, 1), DateTimeKind.Utc)),
                    TypeInfo.Arrays.DateTime);
                yield return CreateConstructorCase(() => Array(
                        new Uuid(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee1")),
                        new Uuid(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee2"))),
                    TypeInfo.Arrays.Guid);
                yield return CreateConstructorCase(() => Array(ByteString.From(1), ByteString.From(2, 3)),
                    TypeInfo.Arrays.ByteString);
                yield return CreateConstructorCase(() => Array(CreateXmlElement("A"), CreateXmlElement("B")),
                    TypeInfo.Arrays.XmlElement);
                yield return CreateConstructorCase(() => Array(new NodeId(1), new NodeId(2, 1)),
                    TypeInfo.Arrays.NodeId);
                yield return CreateConstructorCase(() => Array(
                        ExpandedNodeId.Parse("nsu=Test;s=One"),
                        ExpandedNodeId.Parse("nsu=Test;s=Two")),
                    TypeInfo.Arrays.ExpandedNodeId);
                yield return CreateConstructorCase(() => Array(
                        new StatusCode(1u),
                        new StatusCode(2u)),
                    TypeInfo.Arrays.StatusCode);
                yield return CreateConstructorCase(() => Array(
                        new QualifiedName("q1", 1),
                        new QualifiedName("q2", 2)),
                    TypeInfo.Arrays.QualifiedName);
                yield return CreateConstructorCase(() => Array(
                        new LocalizedText("en", "a"),
                        new LocalizedText("de", "b")),
                    TypeInfo.Arrays.LocalizedText);
                yield return CreateConstructorCase(() => Array(
                        new ExtensionObject(new Argument()),
                        new ExtensionObject(new Argument())),
                    TypeInfo.Arrays.ExtensionObject);
                yield return CreateConstructorCase(() => Array(new DataValue(1), new DataValue(2)),
                    TypeInfo.Arrays.DataValue);
                yield return CreateConstructorCase(() => Array(new Variant(1), new Variant("two")),
                    TypeInfo.Arrays.Variant);
            }
        }

        private static IEnumerable<TestCaseData> ScalarDescriptorCases
        {
            get
            {
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarBoolean",
                        () => true,
                        typeof(bool),
                        TypeInfo.Scalars.Boolean,
                        nameof(Variant.GetBoolean)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarSByte",
                        () => (sbyte)-1,
                        typeof(sbyte),
                        TypeInfo.Scalars.SByte,
                        nameof(Variant.GetSByte)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarByte",
                        () => (byte)1,
                        typeof(byte),
                        TypeInfo.Scalars.Byte,
                        nameof(Variant.GetByte)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarInt16",
                        () => (short)-2,
                        typeof(short),
                        TypeInfo.Scalars.Int16,
                        nameof(Variant.GetInt16)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarUInt16",
                        () => (ushort)2,
                        typeof(ushort),
                        TypeInfo.Scalars.UInt16,
                        nameof(Variant.GetUInt16)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarInt32",
                        () => -3,
                        typeof(int),
                        TypeInfo.Scalars.Int32,
                        nameof(Variant.GetInt32)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarUInt32",
                        () => 3u,
                        typeof(uint),
                        TypeInfo.Scalars.UInt32,
                        nameof(Variant.GetUInt32)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarInt64",
                        () => -4L,
                        typeof(long),
                        TypeInfo.Scalars.Int64,
                        nameof(Variant.GetInt64)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarUInt64",
                        () => 4UL,
                        typeof(ulong),
                        TypeInfo.Scalars.UInt64,
                        nameof(Variant.GetUInt64)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarFloat",
                        () => 1.25f,
                        typeof(float),
                        TypeInfo.Scalars.Float,
                        nameof(Variant.GetFloat)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarDouble",
                        () => 2.25d,
                        typeof(double),
                        TypeInfo.Scalars.Double,
                        nameof(Variant.GetDouble)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarString",
                        () => "opc",
                        typeof(string),
                        TypeInfo.Scalars.String,
                        nameof(Variant.GetString)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarDateTime",
                        () => (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 2), DateTimeKind.Utc),
                        typeof(DateTimeUtc),
                        TypeInfo.Scalars.DateTime,
                        nameof(Variant.GetDateTime)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarGuid",
                        () => Uuid.NewUuid(),
                        typeof(Uuid),
                        TypeInfo.Scalars.Guid,
                        nameof(Variant.GetGuid)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarByteString",
                        () => ByteString.From(1, 2),
                        typeof(ByteString),
                        TypeInfo.Scalars.ByteString,
                        nameof(Variant.GetByteString)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarXmlElement",
                        () => CreateXmlElement("Scalar"),
                        typeof(XmlElement),
                        TypeInfo.Scalars.XmlElement,
                        nameof(Variant.GetXmlElement)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarNodeId",
                        () => new NodeId(10, 1),
                        typeof(NodeId),
                        TypeInfo.Scalars.NodeId,
                        nameof(Variant.GetNodeId)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarExpandedNodeId",
                        () => ExpandedNodeId.Parse("nsu=Test;s=Node"),
                        typeof(ExpandedNodeId),
                        TypeInfo.Scalars.ExpandedNodeId,
                        nameof(Variant.GetExpandedNodeId)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarStatusCode",
                        () => new StatusCode(2u),
                        typeof(StatusCode),
                        TypeInfo.Scalars.StatusCode,
                        nameof(Variant.GetStatusCode)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarQualifiedName",
                        () => new QualifiedName("name", 1),
                        typeof(QualifiedName),
                        TypeInfo.Scalars.QualifiedName,
                        nameof(Variant.GetQualifiedName)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarLocalizedText",
                        () => new LocalizedText("en", "value"),
                        typeof(LocalizedText),
                        TypeInfo.Scalars.LocalizedText,
                        nameof(Variant.GetLocalizedText)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarExtensionObject",
                        () => new ExtensionObject(new Argument()),
                        typeof(ExtensionObject),
                        TypeInfo.Scalars.ExtensionObject,
                        nameof(Variant.GetExtensionObject)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ScalarDataValue",
                        () => new DataValue(1),
                        typeof(DataValue),
                        TypeInfo.Scalars.DataValue,
                        nameof(Variant.GetDataValue)));
            }
        }

        private static IEnumerable<TestCaseData> ArrayDescriptorCases
        {
            get
            {
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayBoolean",
                        () => ArrayOf.Wrapped(true, false),
                        typeof(ArrayOf<bool>),
                        TypeInfo.Arrays.Boolean,
                        nameof(Variant.GetBooleanArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArraySByte",
                        () => ArrayOf.Wrapped((sbyte)-1, (sbyte)1),
                        typeof(ArrayOf<sbyte>),
                        TypeInfo.Arrays.SByte,
                        nameof(Variant.GetSByteArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayInt16",
                        () => ArrayOf.Wrapped((short)-2, (short)2),
                        typeof(ArrayOf<short>),
                        TypeInfo.Arrays.Int16,
                        nameof(Variant.GetInt16Array)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayUInt16",
                        () => ArrayOf.Wrapped((ushort)2, (ushort)3),
                        typeof(ArrayOf<ushort>),
                        TypeInfo.Arrays.UInt16,
                        nameof(Variant.GetUInt16Array)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayInt32",
                        () => ArrayOf.Wrapped(-3, 3),
                        typeof(ArrayOf<int>),
                        TypeInfo.Arrays.Int32,
                        nameof(Variant.GetInt32Array)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayUInt32",
                        () => ArrayOf.Wrapped(3u, 4u),
                        typeof(ArrayOf<uint>),
                        TypeInfo.Arrays.UInt32,
                        nameof(Variant.GetUInt32Array)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayInt64",
                        () => ArrayOf.Wrapped(-4L, 4L),
                        typeof(ArrayOf<long>),
                        TypeInfo.Arrays.Int64,
                        nameof(Variant.GetInt64Array)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayUInt64",
                        () => ArrayOf.Wrapped(4UL, 5UL),
                        typeof(ArrayOf<ulong>),
                        TypeInfo.Arrays.UInt64,
                        nameof(Variant.GetUInt64Array)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayFloat",
                        () => ArrayOf.Wrapped(1.0f, 2.0f),
                        typeof(ArrayOf<float>),
                        TypeInfo.Arrays.Float,
                        nameof(Variant.GetFloatArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayDouble",
                        () => ArrayOf.Wrapped(1.0d, 2.0d),
                        typeof(ArrayOf<double>),
                        TypeInfo.Arrays.Double,
                        nameof(Variant.GetDoubleArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayString",
                        () => ArrayOf.Wrapped("a", "b"),
                        typeof(ArrayOf<string>),
                        TypeInfo.Arrays.String,
                        nameof(Variant.GetStringArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayDateTime",
                        () => ArrayOf.Wrapped(
                            (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 2, 1), DateTimeKind.Utc),
                            (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2025, 2, 1), DateTimeKind.Utc)),
                        typeof(ArrayOf<DateTimeUtc>),
                        TypeInfo.Arrays.DateTime,
                        nameof(Variant.GetDateTimeArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayGuid",
                        () => ArrayOf.Wrapped(
                            new Uuid(Guid.Parse("bbbbbbbb-cccc-dddd-eeee-fffffffffff1")),
                            new Uuid(Guid.Parse("bbbbbbbb-cccc-dddd-eeee-fffffffffff2"))),
                        typeof(ArrayOf<Uuid>),
                        TypeInfo.Arrays.Guid,
                        nameof(Variant.GetGuidArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayByteString",
                        () => ArrayOf.Wrapped(ByteString.From([1]), ByteString.From([2])),
                        typeof(ArrayOf<ByteString>),
                        TypeInfo.Arrays.ByteString,
                        nameof(Variant.GetByteStringArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayXmlElement",
                        () => ArrayOf.Wrapped(CreateXmlElement("A"), CreateXmlElement("B")),
                        typeof(ArrayOf<XmlElement>),
                        TypeInfo.Arrays.XmlElement,
                        nameof(Variant.GetXmlElementArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayNodeId",
                        () => ArrayOf.Wrapped(new NodeId(1), new NodeId(2, 1)),
                        typeof(ArrayOf<NodeId>),
                        TypeInfo.Arrays.NodeId,
                        nameof(Variant.GetNodeIdArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayExpandedNodeId",
                        () => ArrayOf.Wrapped(ExpandedNodeId.Parse("nsu=Test;s=One"), ExpandedNodeId.Parse("nsu=Test;s=Two")),
                        typeof(ArrayOf<ExpandedNodeId>),
                        TypeInfo.Arrays.ExpandedNodeId,
                        nameof(Variant.GetExpandedNodeIdArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayStatusCode",
                        () => ArrayOf.Wrapped(new StatusCode(1u), new StatusCode(2u)),
                        typeof(ArrayOf<StatusCode>),
                        TypeInfo.Arrays.StatusCode,
                        nameof(Variant.GetStatusCodeArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayQualifiedName",
                        () => ArrayOf.Wrapped(new QualifiedName("q1", 1), new QualifiedName("q2", 2)),
                        typeof(ArrayOf<QualifiedName>),
                        TypeInfo.Arrays.QualifiedName,
                        nameof(Variant.GetQualifiedNameArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayLocalizedText",
                        () => ArrayOf.Wrapped(new LocalizedText("en", "a"), new LocalizedText("de", "b")),
                        typeof(ArrayOf<LocalizedText>),
                        TypeInfo.Arrays.LocalizedText,
                        nameof(Variant.GetLocalizedTextArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayExtensionObject",
                        () => ArrayOf.Wrapped(new ExtensionObject(new Argument()), new ExtensionObject(new Argument())),
                        typeof(ArrayOf<ExtensionObject>),
                        TypeInfo.Arrays.ExtensionObject,
                        nameof(Variant.GetExtensionObjectArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayDataValue",
                        () => ArrayOf.Wrapped(new DataValue(1), new DataValue(2)),
                        typeof(ArrayOf<DataValue>),
                        TypeInfo.Arrays.DataValue,
                        nameof(Variant.GetDataValueArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayVariant",
                        () => ArrayOf.Wrapped(new Variant(1), new Variant("two")),
                        typeof(ArrayOf<Variant>),
                        TypeInfo.Arrays.Variant,
                        nameof(Variant.GetVariantArray)));
            }
        }

        [TestCaseSource(nameof(ScalarConstructorCases))]
        public void ScalarConstructor_SetsExpectedTypeInfo(Func<object> valueFactory, TypeInfo expectedTypeInfo)
        {
            object value = valueFactory();
            var variant = new Variant(value);

            AssertTypeInfo(expectedTypeInfo, variant.TypeInfo);
            AssertValueEquality(value, variant.Value);
        }

        [TestCaseSource(nameof(ArrayConstructorCases))]
        public void ArrayConstructor_SetsExpectedTypeInfo(Func<object> valueFactory, TypeInfo expectedTypeInfo)
        {
            object value = valueFactory();
            var variant = new Variant((Array)value);

            AssertTypeInfo(expectedTypeInfo, variant.TypeInfo);
            AssertValueEquality(value, variant.Value);
        }

        [Test]
        public void MultiDimensionalArrayConstructor_BecomesMatrix()
        {
            int[,] data = new int[2, 2]
            {
                { 1, 2 },
                { 3, 4 }
            };
            var variant = new Variant(data);

            Assert.That(variant.TypeInfo.ValueRank, Is.EqualTo(2));
            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(variant.AsBoxedObject(), Is.TypeOf<MatrixOf<int>>());
            var matrix = (MatrixOf<int>)variant;
            Assert.That(data.Cast<int>().ToArray(), Is.EquivalentTo(matrix.Span.ToArray()));
        }

        [Test]
        public void MatrixConstructor_PreservesMatrixTypeInfo()
        {
            MatrixOf<int> matrix = ArrayOf.Wrapped(1, 2, 3, 4).ToMatrix([2, 2]);
            var variant = new Variant(matrix);

            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(variant.TypeInfo.ValueRank, Is.EqualTo(2));
            Assert.That(variant.GetInt32Matrix(), Is.EqualTo(matrix));
        }

        [Test]
        public void FromEnumerationArray_ReturnsEnumerationArray()
        {
            TestEnum[] values = [TestEnum.Zero, TestEnum.One];
            var variant = Variant.From(values);

            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(variant.GetEnumerationArray<TestEnum>(), Is.EqualTo(values));
            Assert.That(variant.GetInt32Array(), Is.EqualTo(
                values.Select(v => Convert.ToInt32(v, CultureInfo.InvariantCulture)).ToArray()));
        }

        [Test]
        public void FromIntArray_ReturnsEnumerationArray()
        {
            ArrayOf<TestEnum> values = [TestEnum.Zero, TestEnum.One];
            var asInt32 = ArrayOf.Wrapped(0, 1);
            var variant = Variant.From(asInt32);

            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(variant.GetEnumerationArray<TestEnum>(), Is.EqualTo(values));
            Assert.That(variant.GetInt32Array(), Is.EqualTo(asInt32));
        }

        [Test]
        public void FromEnumeration_CoercesInt32Value()
        {
            const TestEnum value = TestEnum.Two;
            var variant = Variant.From(value);

            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(variant.GetInt32(), Is.EqualTo(Convert.ToInt32(value, CultureInfo.InvariantCulture)));
            Assert.That(variant.GetEnumeration<TestEnum>(), Is.EqualTo(value));
            Assert.That(variant.Value, Is.EqualTo(value));
        }

        [Test]
        public void EnumArrayConstructorWithTypeInfo_CoercesEnumerationArray()
        {
            TestEnum[] values = [TestEnum.Zero, TestEnum.One];
            var variant = Variant.From(values);

            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(variant.GetEnumerationArray<TestEnum>(), Is.EqualTo(values));
            Assert.That(variant.GetInt32Array(), Is.EqualTo(
                values.Select(v => Convert.ToInt32(v, CultureInfo.InvariantCulture)).ToArray()));
        }

        [Test]
        public void VariantConstructsArrayFromEnumerable()
        {
            IList source = new List<int> { 1, 2, 3 };
            var typeInfo = TypeInfo.Create(BuiltInType.Int32, ValueRanks.OneDimension);
#pragma warning disable CS0618 // Type or member is obsolete
            var variant = new Variant(source, typeInfo);

            AssertTypeInfo(typeInfo, variant.TypeInfo);
            Assert.That(source, Is.EquivalentTo((int[])variant.Value));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        [TestCaseSource(nameof(ArrayDescriptorCases))]
        public void TryGetArray_Succeeds(VariantDescriptor descriptor)
        {
            object values = descriptor.CreateValue();
#pragma warning disable CS0618 // Type or member is obsolete
            var variant = new Variant(values);
#pragma warning restore CS0618 // Type or member is obsolete
            MethodInfo method = typeof(Variant).GetMethod(nameof(Variant.TryGetValue), Array(descriptor.ValueType.MakeByRefType()));
            object[] args = Array(CreateDefaultValue(descriptor.ValueType));

            Assert.That(method, Is.Not.Null, $"TryGetValue overload for {descriptor.Name} should exist");
            bool success = (bool)method.Invoke(variant, args);

            Assert.That(success, Is.True);
            AssertValueEquality(values, args[0]);
        }

        [Test]
        [TestCaseSource(nameof(ScalarDescriptorCases))]
        public void GetScalar_ReturnsStoredValue(VariantDescriptor descriptor)
        {
            object value = descriptor.CreateValue();
#pragma warning disable CS0618 // Type or member is obsolete
            var variant = new Variant(value);
#pragma warning restore CS0618 // Type or member is obsolete
            MethodInfo method = typeof(Variant).GetMethod(descriptor.GetMethodName, Array(descriptor.ValueType));
            object[] args = Array(CreateDefaultValue(descriptor.ValueType));

            Assert.That(method, Is.Not.Null, $"Get method {descriptor.GetMethodName} should exist");
            object result = method.Invoke(variant, args);
            AssertValueEquality(value, result);
        }

        [Test]
        [TestCaseSource(nameof(ArrayDescriptorCases))]
        public void GetArray_ReturnsStoredValue(VariantDescriptor descriptor)
        {
            object values = descriptor.CreateValue();
#pragma warning disable CS0618 // Type or member is obsolete
            var variant = new Variant(values);
#pragma warning restore CS0618 // Type or member is obsolete
            MethodInfo method = typeof(Variant).GetMethod(descriptor.GetMethodName, Array(descriptor.ValueType));
            object[] args = Array(CreateDefaultValue(descriptor.ValueType));

            Assert.That(method, Is.Not.Null, $"Get method {descriptor.GetMethodName} should exist");
            object result = method.Invoke(variant, args);
            AssertValueEquality(values, result);
        }

        [Test]
        [TestCaseSource(nameof(ScalarDescriptorCases))]
        public void GetScalar_ReturnsDefaultOnMismatch(VariantDescriptor descriptor)
        {
            object defaultValue = descriptor.CreateValue();
            var variant = new Variant(true);
            MethodInfo method = typeof(Variant).GetMethod(descriptor.GetMethodName, Array(descriptor.ValueType));
            object[] args = Array(CloneValue(defaultValue));

            object result = method.Invoke(variant, args);
            AssertValueEquality(defaultValue, result);
        }

        [Test]
        [TestCaseSource(nameof(ArrayDescriptorCases))]
        public void GetArray_ReturnsDefaultOnMismatch(VariantDescriptor descriptor)
        {
            object defaultValue = descriptor.CreateValue();
            var variant = new Variant(true);
            MethodInfo method = typeof(Variant).GetMethod(descriptor.GetMethodName, Array(descriptor.ValueType));
            object[] args = Array(CloneValue(defaultValue));

            object result = method.Invoke(variant, args);
            AssertValueEquality(defaultValue, result);
        }

        [Test]
        [TestCaseSource(nameof(ScalarDescriptorCases))]
        public void TryGet_Scalar_Succeeds(VariantDescriptor descriptor)
        {
            object value = descriptor.CreateValue();
#pragma warning disable CS0618 // Type or member is obsolete
            var variant = new Variant(value);
#pragma warning restore CS0618 // Type or member is obsolete
            MethodInfo method = typeof(Variant).GetMethods().FirstOrDefault(m =>
            {
                if (m.Name != nameof(Variant.TryGetValue))
                {
                    return false;
                }
                ParameterInfo[] parameters = m.GetParameters();
                if (parameters.Length != 1)
                {
                    return false;
                }
                ParameterInfo param = parameters[0];
                if (!param.IsOut)
                {
                    return false;
                }
                return
                    param.ParameterType == descriptor.ValueType.MakeByRefType() ||
                    param.ParameterType == descriptor.ValueType;
            });
            Assert.That(method, Is.Not.Null, $"TryGetValue Method with {descriptor.ValueType} not found");
            object[] args = new object[1];
            bool success = (bool)method.Invoke(variant, args);
            Assert.That(success, Is.True);
            AssertValueEquality(value, args[0]);
        }

        [Test]
        [TestCaseSource(nameof(ArrayDescriptorCases))]
        public void GenericTryGetArray_Succeeds(VariantDescriptor descriptor)
        {
            object values = descriptor.CreateValue();
#pragma warning disable CS0618 // Type or member is obsolete
            var variant = new Variant(values);
#pragma warning restore CS0618 // Type or member is obsolete
            Type elementType = descriptor.ValueType.GetGenericArguments()[0] ?? descriptor.ValueType;
            MethodInfo method = typeof(Variant).GetMethod(nameof(Variant.TryGetValueArray))
                .MakeGenericMethod(elementType);
            object[] args = Array(CreateDefaultValue(descriptor.ValueType), descriptor.TypeInfo.BuiltInType);

            bool success = (bool)method.Invoke(variant, args);
            Assert.That(success, Is.True);
            AssertValueEquality(values, args[0]);
        }

        [Test]
        public void TryGetString_FailsForWrongBuiltInType()
        {
            var variant = new Variant(1);
            Assert.That(variant.TryGetValue(out string _), Is.False);
        }

        [Test]
        public void GenericTryGetArray_FailsForWrongBuiltInType()
        {
            var variant = new Variant(Array(1, 2));
            MethodInfo method = typeof(Variant).GetMethod(nameof(Variant.TryGetValueArray))
                .MakeGenericMethod(typeof(int));
            object[] args = Array<object>(null, BuiltInType.String);

            bool success = (bool)method.Invoke(variant, args);
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryGetMatrix_ReturnsMatrix()
        {
            MatrixOf<float> matrix = ArrayOf.Wrapped(1f, 2f, 3f, 4f).ToMatrix(2, 2);
            var variant = Variant.From(matrix);
            object[] args = Array<object>(null, BuiltInType.Float);
            MethodInfo method = typeof(Variant).GetMethod(nameof(Variant.TryGetValueMatrix))
                .MakeGenericMethod(typeof(float));

            bool success = (bool)method.Invoke(variant, args);
            Assert.That(success, Is.True);
            Assert.That(args[0], Is.EqualTo(matrix));
        }

        [Test]
        public void TryGetMatrix_FromArray()
        {
            double[,] data = new double[2, 1]
            {
                { 1.0 },
                { 2.0 }
            };
            var variant = new Variant(data);
            object[] args = Array<object>(null, BuiltInType.Double);
            MethodInfo method = typeof(Variant).GetMethod(nameof(Variant.TryGetValueMatrix))
                .MakeGenericMethod(typeof(double));

            bool success = (bool)method.Invoke(variant, args);
            Assert.That(success, Is.True);
            Assert.That(args[0], Is.TypeOf<MatrixOf<double>>());
        }

        [Test]
        public void VariantEqualsVariant_UsesValueSemantics()
        {
            var first = new Variant(Array(1, 2));
            var second = new Variant(Array(1, 2));
            var third = new Variant(Array(1, 3));

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.Not.EqualTo(third));
        }

        [Test]
        public void VariantEqualsVariant_DetectsTypeMismatch()
        {
            var scalar = new Variant(1);
            var floating = new Variant(1.0f);

            Assert.That(scalar, Is.Not.EqualTo(floating));
        }

        [Test]
        public void VariantEqualsObject_HandlesNullAndMismatch()
        {
            var variant = new Variant("value");

            Assert.That(variant, Is.EqualTo((object)"value"));
            Assert.That(variant, Is.Not.EqualTo((object)"other"));
#pragma warning disable NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
            Assert.That(Variant.Null.Equals((object)null));
#pragma warning restore NUnit2010 // Use EqualConstraint for better assertion messages in case of failure
        }

        [Test]
        public void EqualityOperatorWithVariantOperands()
        {
            var left = new Variant(Array(true, false));
            var identical = new Variant(Array(true, false));
            var different = new Variant(Array(false, false));

            Assert.That(left, Is.EqualTo(identical));
            Assert.That(left, Is.EqualTo(identical));
            Assert.That(left, Is.Not.EqualTo(different));
            Assert.That(left, Is.Not.EqualTo(different));
        }

        [Test]
        public void ExplicitConversionThrowsOnMismatch()
        {
            var variant = new Variant(5);

            Assert.Throws<InvalidCastException>(() => _ = (string)variant);
        }

        [Test]
        public void StatusCodeTryGetFallsBackToUInt32()
        {
            var variant = new Variant(123u);

            Assert.That(variant.TryGetValue(out StatusCode status), Is.True);
            Assert.That(status, Is.EqualTo(new StatusCode(123u)));
        }

        [Test]
        public void AsBoxedObjectReturnsDefaultForReferenceTypes()
        {
            var variant = Variant.CreateDefault(TypeInfo.Scalars.NodeId);

            Assert.That(variant.AsBoxedObject(), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void VariantNullBehavesAsExpected()
        {
            Assert.That(Variant.Null.IsNull, Is.True);
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(Variant.Null.Value, Is.Null);
#pragma warning restore CS0618 // Type or member is obsolete
            Assert.That(Variant.Null.GetHashCode(), Is.Zero);
        }

        [Test]
        public void GetHashCodeMatchesUnderlyingValue()
        {
            var variant = new Variant(42);

#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(variant.Value.GetHashCode(), Is.EqualTo(variant.GetHashCode()));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        public void ToStringFormatsByteStringAsHex()
        {
            var variant = new Variant(ByteString.From([0x0A, 0xFF]));

            Assert.That(variant.ToString(), Is.EqualTo("0AFF"));
        }

        [Test]
        public void ToStringFormatsXmlElementOuterXml()
        {
            XmlElement element = CreateXmlElement("Alpha");
            var variant = new Variant(element);

            Assert.That(variant.ToString(), Is.EqualTo(element.OuterXml));
        }

        [Test]
        public void ToStringFormatsArraysWithSeparators()
        {
            var variant = new Variant(Array(1, 2, 3));

            Assert.That(variant.ToString(), Is.EqualTo("[ 1 2 3 ]"));
        }

        [Test]
        public void ToStringRejectsCustomFormats()
        {
            var variant = new Variant(1);

            Assert.Throws<FormatException>(() => variant.ToString("G", null));
        }

        [TestCaseSource(nameof(ScalarDescriptorCases))]
        public void VariantFromScalarProducesEquivalentVariant(VariantDescriptor descriptor)
        {
            object value = descriptor.CreateValue();
            Variant variant = InvokeVariantFrom(value);

            AssertTypeInfo(descriptor.TypeInfo, variant.TypeInfo);
#pragma warning disable CS0618 // Type or member is obsolete
            AssertValueEquality(value, variant.Value);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [TestCaseSource(nameof(ArrayDescriptorCases))]
        public void VariantFromArrayProducesEquivalentVariant(VariantDescriptor descriptor)
        {
            object value = descriptor.CreateValue();
            Variant variant = InvokeVariantFrom(value);

            AssertTypeInfo(descriptor.TypeInfo, variant.TypeInfo);
#pragma warning disable CS0618 // Type or member is obsolete
            AssertValueEquality(value, variant.Value);
#pragma warning restore CS0618 // Type or member is obsolete
        }

        /// <summary>
        /// Initialize Variant from uint with StatusCode TypeInfo.
        /// Tests that a Variant created from uint with StatusCode TypeInfo
        /// can be properly cast to StatusCode.
        /// </summary>
        [Test]
        public void VariantFromUIntWithStatusCodeTypeInfo()
        {
            // Test scalar StatusCode creation from uint
            uint statusCodeValue = (uint)StatusCodes.Good;
#pragma warning disable CS0618 // Type or member is obsolete
            var variant = new Variant(statusCodeValue, TypeInfo.Scalars.StatusCode);

            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
            Assert.That(variant.Value, Is.Not.Null);

            // Cast the Value to StatusCode
            var statusCode = (StatusCode)variant.Value;
            Assert.That(statusCode.Code, Is.EqualTo(StatusCodes.Good));

            // Test with different status code values
            uint badNodeIdValue = (uint)StatusCodes.BadNodeIdInvalid;
            var variant2 = new Variant(badNodeIdValue, TypeInfo.Scalars.StatusCode);

            Assert.That(variant2.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
            var statusCode2 = (StatusCode)variant2.Value;
            Assert.That(statusCode2.Code, Is.EqualTo(StatusCodes.BadNodeIdInvalid));

            // Test with custom status code value
            const uint customValue = 0x80AB0000;
            var variant3 = new Variant(customValue, TypeInfo.Scalars.StatusCode);

            Assert.That(variant3.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
            var statusCode3 = (StatusCode)variant3.Value;
#pragma warning restore CS0618 // Type or member is obsolete
            Assert.That(statusCode3.Code, Is.EqualTo(customValue));
        }

        /// <summary>
        /// Initialize Variant from uint array with StatusCode TypeInfo.
        /// Tests that a Variant created from uint[] with StatusCode TypeInfo
        /// can be properly cast to StatusCode[].
        /// </summary>
        [Test]
        public void VariantFromUIntArrayWithStatusCodeTypeInfo()
        {
            // Test array StatusCode creation from uint[]
            ArrayOf<StatusCode> statusCodeValues =
            [
                StatusCodes.Good,
                StatusCodes.BadNodeIdInvalid,
                StatusCodes.BadUnexpectedError,
                StatusCodes.BadAttributeIdInvalid
            ];

            var variant = Variant.From(statusCodeValues);

            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.That(variant.Value, Is.Not.Null);
            Assert.That(variant.Value, Is.InstanceOf<StatusCode[]>());

            // Cast the Value to StatusCode array
            var statusCodes = (ArrayOf<StatusCode>)variant;
            Assert.That(statusCodes.Count, Is.EqualTo(statusCodeValues.Count));

            for (int i = 0; i < statusCodeValues.Count; i++)
            {
                Assert.That(statusCodes[i], Is.EqualTo(statusCodeValues[i]));
            }

            // Test empty array
            ArrayOf<StatusCode> emptyArray = [];
            var variant2 = new Variant(emptyArray);

            Assert.That(variant2.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
            var emptyStatusCodes = (StatusCode[])variant2.Value;
            Assert.That(emptyStatusCodes, Is.Empty);

            // Test single element array
            ArrayOf<StatusCode> singleElement = [StatusCodes.BadNodeIdInvalid];
            var variant3 = new Variant(singleElement);

            Assert.That(variant3.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
            var singleStatusCode = (StatusCode[])variant3.Value;
#pragma warning restore CS0618 // Type or member is obsolete
            Assert.That(singleStatusCode, Has.Length.EqualTo(1));
            Assert.That(singleStatusCode[0], Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        private static Variant InvokeVariantFrom(object value)
        {
            MethodInfo method = typeof(Variant).GetMethod(nameof(Variant.From), [value.GetType()]);
            return (Variant)method.Invoke(null, [value]);
        }

        private static T[] Array<T>(params T[] items)
        {
            return items;
        }

        private static TestCaseData CreateConstructorCase(
            Func<object> valueFactory,
            TypeInfo typeInfo)
        {
            return new TestCaseData(valueFactory, typeInfo);
        }

        private static TestCaseData CreateDescriptorCase(
            VariantDescriptor descriptor)
        {
            return new TestCaseData(descriptor);
        }

        private static object CreateDefaultValue(Type type)
        {
            if (type.IsValueType)
            {
                return Activator.CreateInstance(type);
            }

            return null;
        }

        private static void AssertTypeInfo(TypeInfo expected, TypeInfo actual)
        {
            Assert.That(actual.BuiltInType, Is.EqualTo(expected.BuiltInType));
            Assert.That(actual.ValueRank, Is.EqualTo(expected.ValueRank));
        }

        private static object CloneValue(object value)
        {
            if (value is Array array)
            {
                var clone = System.Array.CreateInstance(array.GetType().GetElementType(), array.Length);
                for (int i = 0; i < array.Length; i++)
                {
                    clone.SetValue(CloneValue(array.GetValue(i)), i);
                }

                return clone;
            }

            if (value is XmlElement xmlElement)
            {
                return XmlElement.From((System.Xml.XmlElement)xmlElement.AsXmlElement().CloneNode(true));
            }

            if (value is ICloneable cloneable)
            {
                return cloneable.Clone();
            }

            return value;
        }

        private static void AssertValueEquality(object expected, object actual)
        {
            if (expected is null || actual is null)
            {
                Assert.That(actual, Is.EqualTo(expected));
                return;
            }

            if (expected is Array expectedArray && actual is Array actualArray)
            {
                Assert.That(actualArray, Has.Length.EqualTo(expectedArray.Length), "Array lengths differ");
                for (int i = 0; i < expectedArray.Length; i++)
                {
                    AssertValueEquality(expectedArray.GetValue(i), actualArray.GetValue(i));
                }

                return;
            }

            if (expected is XmlElement expectedXml && actual is XmlElement actualXml)
            {
                Assert.That(actualXml.OuterXml, Is.EqualTo(expectedXml.OuterXml));
                return;
            }

            Assert.That(actual, Is.EqualTo(expected));
        }

        private enum TestEnum
        {
            Zero = 0,
            One = 1,
            Two = 2,
            Negative = -1
        }

        private enum ByteEnum : byte
        {
            Zero = 0,
            One = 1
        }

        private enum ShortEnum : short
        {
            Zero = 0,
            One = 1
        }

        private enum LongEnum : long
        {
            Zero = 0,
            One = 1
        }

        private enum UShortEnum : ushort
        {
            Zero = 0,
            One = 1
        }

        private enum UIntEnum : uint
        {
            Zero = 0,
            One = 1
        }

        private enum ULongEnum : ulong
        {
            Zero = 0,
            One = 1
        }

        private enum SByteEnum : sbyte
        {
            Zero = 0,
            One = 1
        }

        [Test]
        public void ExplicitCastToBoolReturnsValue()
        {
            var v = new Variant(true);
            bool result = (bool)v;
            Assert.That(result, Is.True);
        }

        [Test]
        public void ExplicitCastToSByteReturnsValue()
        {
            var v = new Variant((sbyte)-5);
            sbyte result = (sbyte)v;
            Assert.That(result, Is.EqualTo((sbyte)-5));
        }

        [Test]
        public void ExplicitCastToByteReturnsValue()
        {
            var v = new Variant((byte)42);
            byte result = (byte)v;
            Assert.That(result, Is.EqualTo((byte)42));
        }

        [Test]
        public void ExplicitCastToInt16ReturnsValue()
        {
            var v = new Variant((short)-100);
            short result = (short)v;
            Assert.That(result, Is.EqualTo((short)-100));
        }

        [Test]
        public void ExplicitCastToUInt16ReturnsValue()
        {
            var v = new Variant((ushort)200);
            ushort result = (ushort)v;
            Assert.That(result, Is.EqualTo((ushort)200));
        }

        [Test]
        public void ExplicitCastToInt32ReturnsValue()
        {
            var v = new Variant(-300);
            int result = (int)v;
            Assert.That(result, Is.EqualTo(-300));
        }

        [Test]
        public void ExplicitCastToUInt32ReturnsValue()
        {
            var v = new Variant(400u);
            uint result = (uint)v;
            Assert.That(result, Is.EqualTo(400u));
        }

        [Test]
        public void ExplicitCastToInt64ReturnsValue()
        {
            var v = new Variant(-500L);
            long result = (long)v;
            Assert.That(result, Is.EqualTo(-500L));
        }

        [Test]
        public void ExplicitCastToUInt64ReturnsValue()
        {
            var v = new Variant(600UL);
            ulong result = (ulong)v;
            Assert.That(result, Is.EqualTo(600UL));
        }

        [Test]
        public void ExplicitCastToFloatReturnsValue()
        {
            var v = new Variant(1.5f);
            float result = (float)v;
            Assert.That(result, Is.EqualTo(1.5f));
        }

        [Test]
        public void ExplicitCastToDoubleReturnsValue()
        {
            var v = new Variant(2.5d);
            double result = (double)v;
            Assert.That(result, Is.EqualTo(2.5d));
        }

        [Test]
        public void ExplicitCastToStringReturnsValue()
        {
            var v = new Variant("hello");
            string result = (string)v;
            Assert.That(result, Is.EqualTo("hello"));
        }

        [Test]
        public void ExplicitCastToDateTimeUtcReturnsValue()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 6, 15), DateTimeKind.Utc);
            var v = new Variant(dt);
            var result = (DateTimeUtc)v;
            Assert.That(result, Is.EqualTo(dt));
        }

        [Test]
        public void ExplicitCastToUuidReturnsValue()
        {
            var guid = Uuid.NewUuid();
            var v = new Variant(guid);
            var result = (Uuid)v;
            Assert.That(result, Is.EqualTo(guid));
        }

        [Test]
        public void ExplicitCastToByteStringReturnsValue()
        {
            var bs = ByteString.From(1, 2, 3);
            var v = new Variant(bs);
            var result = (ByteString)v;
            Assert.That(result, Is.EqualTo(bs));
        }

        [Test]
        public void ExplicitCastToXmlElementReturnsValue()
        {
            XmlElement xml = CreateXmlElement("Test");
            var v = new Variant(xml);
            var result = (XmlElement)v;
            Assert.That(result, Is.EqualTo(xml));
        }

        [Test]
        public void ExplicitCastToNodeIdReturnsValue()
        {
            var nodeId = new NodeId(10, 1);
            var v = new Variant(nodeId);
            var result = (NodeId)v;
            Assert.That(result, Is.EqualTo(nodeId));
        }

        [Test]
        public void ExplicitCastToExpandedNodeIdReturnsValue()
        {
            var eni = ExpandedNodeId.Parse("nsu=Test;s=Node");
            var v = new Variant(eni);
            var result = (ExpandedNodeId)v;
            Assert.That(result, Is.EqualTo(eni));
        }

        [Test]
        public void ExplicitCastToStatusCodeReturnsValue()
        {
            var sc = new StatusCode(123u);
            var v = new Variant(sc);
            var result = (StatusCode)v;
            Assert.That(result, Is.EqualTo(sc));
        }

        [Test]
        public void ExplicitCastToQualifiedNameReturnsValue()
        {
            var qn = new QualifiedName("name", 2);
            var v = new Variant(qn);
            var result = (QualifiedName)v;
            Assert.That(result, Is.EqualTo(qn));
        }

        [Test]
        public void ExplicitCastToLocalizedTextReturnsValue()
        {
            var lt = new LocalizedText("en", "text");
            var v = new Variant(lt);
            var result = (LocalizedText)v;
            Assert.That(result, Is.EqualTo(lt));
        }

        [Test]
        public void ExplicitCastToExtensionObjectReturnsValue()
        {
            var eo = new ExtensionObject(new Argument());
            var v = new Variant(eo);
            var result = (ExtensionObject)v;
            Assert.That(result.IsNull, Is.False);
        }

        [Test]
        public void ExplicitCastToDataValueReturnsValue()
        {
            var dv = new DataValue(5);
            var v = new Variant(dv);
            var result = (DataValue)v;
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void ExplicitCastToArrayOfBoolReturnsValue()
        {
            ArrayOf<bool> arr = [true, false];
            var v = new Variant(arr);
            var result = (ArrayOf<bool>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfSByteReturnsValue()
        {
            ArrayOf<sbyte> arr = [-1, 1];
            var v = new Variant(arr);
            var result = (ArrayOf<sbyte>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfByteReturnsValue()
        {
            ArrayOf<byte> arr = [1, 2];
            var v = new Variant(arr);
            var result = (ArrayOf<byte>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfInt16ReturnsValue()
        {
            ArrayOf<short> arr = [-1, 1];
            var v = new Variant(arr);
            var result = (ArrayOf<short>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfUInt16ReturnsValue()
        {
            ArrayOf<ushort> arr = [1, 2];
            var v = new Variant(arr);
            var result = (ArrayOf<ushort>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfInt32ReturnsValue()
        {
            ArrayOf<int> arr = [-1, 1];
            var v = new Variant(arr);
            var result = (ArrayOf<int>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfUInt32ReturnsValue()
        {
            ArrayOf<uint> arr = [1u, 2u];
            var v = new Variant(arr);
            var result = (ArrayOf<uint>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfInt64ReturnsValue()
        {
            ArrayOf<long> arr = [-1L, 1L];
            var v = new Variant(arr);
            var result = (ArrayOf<long>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfUInt64ReturnsValue()
        {
            ArrayOf<ulong> arr = [1UL, 2UL];
            var v = new Variant(arr);
            var result = (ArrayOf<ulong>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfFloatReturnsValue()
        {
            ArrayOf<float> arr = [1.0f, 2.0f];
            var v = new Variant(arr);
            var result = (ArrayOf<float>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfDoubleReturnsValue()
        {
            ArrayOf<double> arr = [1.0, 2.0];
            var v = new Variant(arr);
            var result = (ArrayOf<double>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfStringReturnsValue()
        {
            ArrayOf<string> arr = ["a", "b"];
            var v = new Variant(arr);
            var result = (ArrayOf<string>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfDateTimeUtcReturnsValue()
        {
            var dt1 = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 1), DateTimeKind.Utc);
            var dt2 = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2025, 1, 1), DateTimeKind.Utc);
            ArrayOf<DateTimeUtc> arr = [dt1, dt2];
            var v = new Variant(arr);
            var result = (ArrayOf<DateTimeUtc>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfUuidReturnsValue()
        {
            ArrayOf<Uuid> arr = [Uuid.NewUuid(), Uuid.NewUuid()];
            var v = new Variant(arr);
            var result = (ArrayOf<Uuid>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfByteStringReturnsValue()
        {
            ArrayOf<ByteString> arr = [ByteString.From(1), ByteString.From(2)];
            var v = new Variant(arr);
            var result = (ArrayOf<ByteString>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfXmlElementReturnsValue()
        {
            ArrayOf<XmlElement> arr = [CreateXmlElement("A"), CreateXmlElement("B")];
            var v = new Variant(arr);
            var result = (ArrayOf<XmlElement>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfNodeIdReturnsValue()
        {
            ArrayOf<NodeId> arr = [new NodeId(1), new NodeId(2)];
            var v = new Variant(arr);
            var result = (ArrayOf<NodeId>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfExpandedNodeIdReturnsValue()
        {
            ArrayOf<ExpandedNodeId> arr = [
                ExpandedNodeId.Parse("nsu=T;s=A"),
                ExpandedNodeId.Parse("nsu=T;s=B")
            ];
            var v = new Variant(arr);
            var result = (ArrayOf<ExpandedNodeId>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfStatusCodeReturnsValue()
        {
            ArrayOf<StatusCode> arr = [new StatusCode(1u), new StatusCode(2u)];
            var v = new Variant(arr);
            var result = (ArrayOf<StatusCode>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfQualifiedNameReturnsValue()
        {
            ArrayOf<QualifiedName> arr = [new QualifiedName("a"), new QualifiedName("b")];
            var v = new Variant(arr);
            var result = (ArrayOf<QualifiedName>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfLocalizedTextReturnsValue()
        {
            ArrayOf<LocalizedText> arr = [
                new LocalizedText("en", "a"),
                new LocalizedText("de", "b")
            ];
            var v = new Variant(arr);
            var result = (ArrayOf<LocalizedText>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfExtensionObjectReturnsValue()
        {
            ArrayOf<ExtensionObject> arr = [
                new ExtensionObject(new Argument()),
                new ExtensionObject(new Argument())
            ];
            var v = new Variant(arr);
            var result = (ArrayOf<ExtensionObject>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfDataValueReturnsValue()
        {
            ArrayOf<DataValue> arr = [new DataValue(1), new DataValue(2)];
            var v = new Variant(arr);
            var result = (ArrayOf<DataValue>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToArrayOfVariantReturnsValue()
        {
            ArrayOf<Variant> arr = [new Variant(1), new Variant("two")];
            var v = new Variant(arr);
            var result = (ArrayOf<Variant>)v;
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void ExplicitCastToMatrixOfBoolReturnsValue()
        {
            MatrixOf<bool> matrix = new bool[,] { { true, false }, { false, true } };
            var v = new Variant(matrix);
            var result = (MatrixOf<bool>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfSByteReturnsValue()
        {
            MatrixOf<sbyte> matrix = new sbyte[,] { { -1, 1 }, { -2, 2 } };
            var v = new Variant(matrix);
            var result = (MatrixOf<sbyte>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfByteReturnsValue()
        {
            MatrixOf<byte> matrix = new byte[,] { { 1, 2 }, { 3, 4 } };
            var v = new Variant(matrix);
            var result = (MatrixOf<byte>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfInt16ReturnsValue()
        {
            MatrixOf<short> matrix = new short[,] { { -1, 1 }, { -2, 2 } };
            var v = new Variant(matrix);
            var result = (MatrixOf<short>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfUInt16ReturnsValue()
        {
            MatrixOf<ushort> matrix = new ushort[,] { { 1, 2 }, { 3, 4 } };
            var v = new Variant(matrix);
            var result = (MatrixOf<ushort>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfInt32ReturnsValue()
        {
            MatrixOf<int> matrix = new int[,] { { 1, 2 }, { 3, 4 } };
            var v = new Variant(matrix);
            var result = (MatrixOf<int>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfUInt32ReturnsValue()
        {
            MatrixOf<uint> matrix = new uint[,] { { 1, 2 }, { 3, 4 } };
            var v = new Variant(matrix);
            var result = (MatrixOf<uint>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfInt64ReturnsValue()
        {
            MatrixOf<long> matrix = new long[,] { { 1, 2 }, { 3, 4 } };
            var v = new Variant(matrix);
            var result = (MatrixOf<long>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfUInt64ReturnsValue()
        {
            MatrixOf<ulong> matrix = new ulong[,] { { 1, 2 }, { 3, 4 } };
            var v = new Variant(matrix);
            var result = (MatrixOf<ulong>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfFloatReturnsValue()
        {
            MatrixOf<float> matrix = new float[,] { { 1.0f, 2.0f }, { 3.0f, 4.0f } };
            var v = new Variant(matrix);
            var result = (MatrixOf<float>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfDoubleReturnsValue()
        {
            MatrixOf<double> matrix = new double[,] { { 1.0, 2.0 }, { 3.0, 4.0 } };
            var v = new Variant(matrix);
            var result = (MatrixOf<double>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfStringReturnsValue()
        {
            MatrixOf<string> matrix = new string[,] { { "a", "b" }, { "c", "d" } };
            var v = new Variant(matrix);
            var result = (MatrixOf<string>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfDateTimeUtcReturnsValue()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 1), DateTimeKind.Utc);
            MatrixOf<DateTimeUtc> matrix = new DateTimeUtc[,] { { dt, dt }, { dt, dt } };
            var v = new Variant(matrix);
            var result = (MatrixOf<DateTimeUtc>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfUuidReturnsValue()
        {
            var u = Uuid.NewUuid();
            MatrixOf<Uuid> matrix = new Uuid[,] { { u, u }, { u, u } };
            var v = new Variant(matrix);
            var result = (MatrixOf<Uuid>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfByteStringReturnsValue()
        {
            var bs = ByteString.From(1);
            MatrixOf<ByteString> matrix = new ByteString[,] { { bs, bs }, { bs, bs } };
            var v = new Variant(matrix);
            var result = (MatrixOf<ByteString>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfXmlElementReturnsValue()
        {
            XmlElement xml = CreateXmlElement("M");
            MatrixOf<XmlElement> matrix = new XmlElement[,] { { xml, xml }, { xml, xml } };
            var v = new Variant(matrix);
            var result = (MatrixOf<XmlElement>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfNodeIdReturnsValue()
        {
            var nid = new NodeId(1);
            MatrixOf<NodeId> matrix = new NodeId[,] { { nid, nid }, { nid, nid } };
            var v = new Variant(matrix);
            var result = (MatrixOf<NodeId>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfExpandedNodeIdReturnsValue()
        {
            var eni = ExpandedNodeId.Parse("nsu=T;s=A");
            MatrixOf<ExpandedNodeId> matrix = new ExpandedNodeId[,] { { eni, eni }, { eni, eni } };
            var v = new Variant(matrix);
            var result = (MatrixOf<ExpandedNodeId>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfStatusCodeReturnsValue()
        {
            var sc = new StatusCode(1u);
            MatrixOf<StatusCode> matrix = new StatusCode[,] { { sc, sc }, { sc, sc } };
            var v = new Variant(matrix);
            var result = (MatrixOf<StatusCode>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfQualifiedNameReturnsValue()
        {
            var qn = new QualifiedName("q");
            MatrixOf<QualifiedName> matrix = new QualifiedName[,] { { qn, qn }, { qn, qn } };
            var v = new Variant(matrix);
            var result = (MatrixOf<QualifiedName>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfLocalizedTextReturnsValue()
        {
            var lt = new LocalizedText("en", "t");
            MatrixOf<LocalizedText> matrix = new LocalizedText[,] { { lt, lt }, { lt, lt } };
            var v = new Variant(matrix);
            var result = (MatrixOf<LocalizedText>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfExtensionObjectReturnsValue()
        {
            var eo = new ExtensionObject(new Argument());
            MatrixOf<ExtensionObject> matrix = new ExtensionObject[,] { { eo, eo }, { eo, eo } };
            var v = new Variant(matrix);
            var result = (MatrixOf<ExtensionObject>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfDataValueReturnsValue()
        {
            var dv = new DataValue(1);
            MatrixOf<DataValue> matrix = new DataValue[,] { { dv, dv }, { dv, dv } };
            var v = new Variant(matrix);
            var result = (MatrixOf<DataValue>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ExplicitCastToMatrixOfVariantReturnsValue()
        {
            var vr = new Variant(1);
            MatrixOf<Variant> matrix = new Variant[,] { { vr, vr }, { vr, vr } };
            var v = new Variant(matrix);
            var result = (MatrixOf<Variant>)v;
            Assert.That(result.Count, Is.EqualTo(4));
        }

        [Test]
        public void ImplicitFromArrayOfSByteCreatesVariant()
        {
            Variant v = (ArrayOf<sbyte>)[-1, 1];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.SByte));
        }

        [Test]
        public void ImplicitFromArrayOfByteCreatesVariant()
        {
            Variant v = (ArrayOf<byte>)[1, 2];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Byte));
        }

        [Test]
        public void ImplicitFromArrayOfInt16CreatesVariant()
        {
            Variant v = (ArrayOf<short>)[-1, 1];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int16));
        }

        [Test]
        public void ImplicitFromArrayOfUInt16CreatesVariant()
        {
            Variant v = (ArrayOf<ushort>)[1, 2];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt16));
        }

        [Test]
        public void ImplicitFromArrayOfInt32CreatesVariant()
        {
            Variant v = (ArrayOf<int>)[-1, 1];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public void ImplicitFromArrayOfUInt32CreatesVariant()
        {
            Variant v = (ArrayOf<uint>)[1u, 2u];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt32));
        }

        [Test]
        public void ImplicitFromArrayOfInt64CreatesVariant()
        {
            Variant v = (ArrayOf<long>)[-1L, 1L];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int64));
        }

        [Test]
        public void ImplicitFromArrayOfUInt64CreatesVariant()
        {
            Variant v = (ArrayOf<ulong>)[1UL, 2UL];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt64));
        }

        [Test]
        public void ImplicitFromArrayOfFloatCreatesVariant()
        {
            Variant v = (ArrayOf<float>)[1.0f, 2.0f];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Float));
        }

        [Test]
        public void ImplicitFromArrayOfDoubleCreatesVariant()
        {
            Variant v = (ArrayOf<double>)[1.0, 2.0];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Double));
        }

        [Test]
        public void ImplicitFromArrayOfStringCreatesVariant()
        {
            Variant v = (ArrayOf<string>)["a", "b"];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.String));
        }

        [Test]
        public void ImplicitFromArrayOfDateTimeUtcCreatesVariant()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 1), DateTimeKind.Utc);
            Variant v = (ArrayOf<DateTimeUtc>)[dt, dt];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.DateTime));
        }

        [Test]
        public void ImplicitFromArrayOfUuidCreatesVariant()
        {
            Variant v = (ArrayOf<Uuid>)[Uuid.NewUuid(), Uuid.NewUuid()];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Guid));
        }

        [Test]
        public void ImplicitFromArrayOfByteStringCreatesVariant()
        {
            Variant v = (ArrayOf<ByteString>)[ByteString.From(1), ByteString.From(2)];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ByteString));
        }

        [Test]
        public void ImplicitFromArrayOfXmlElementCreatesVariant()
        {
            Variant v = (ArrayOf<XmlElement>)[CreateXmlElement("A"), CreateXmlElement("B")];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.XmlElement));
        }

        [Test]
        public void ImplicitFromArrayOfNodeIdCreatesVariant()
        {
            Variant v = (ArrayOf<NodeId>)[new NodeId(1), new NodeId(2)];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.NodeId));
        }

        [Test]
        public void ImplicitFromArrayOfExpandedNodeIdCreatesVariant()
        {
            Variant v = (ArrayOf<ExpandedNodeId>)[
                ExpandedNodeId.Parse("nsu=T;s=A"),
                ExpandedNodeId.Parse("nsu=T;s=B")
            ];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExpandedNodeId));
        }

        [Test]
        public void ImplicitFromArrayOfStatusCodeCreatesVariant()
        {
            Variant v = (ArrayOf<StatusCode>)[new StatusCode(1u), new StatusCode(2u)];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
        }

        [Test]
        public void ImplicitFromArrayOfQualifiedNameCreatesVariant()
        {
            Variant v = (ArrayOf<QualifiedName>)[new QualifiedName("a"), new QualifiedName("b")];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.QualifiedName));
        }

        [Test]
        public void ImplicitFromArrayOfLocalizedTextCreatesVariant()
        {
            Variant v = (ArrayOf<LocalizedText>)[new LocalizedText("en", "a"), new LocalizedText("de", "b")];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.LocalizedText));
        }

        [Test]
        public void ImplicitFromArrayOfExtensionObjectCreatesVariant()
        {
            Variant v = (ArrayOf<ExtensionObject>)[
                new ExtensionObject(new Argument()),
                new ExtensionObject(new Argument())
            ];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));
        }

        [Test]
        public void ImplicitFromArrayOfDataValueCreatesVariant()
        {
            Variant v = (ArrayOf<DataValue>)[new DataValue(1), new DataValue(2)];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.DataValue));
        }

        [Test]
        public void ImplicitFromArrayOfVariantCreatesVariant()
        {
            Variant v = (ArrayOf<Variant>)[new Variant(1), new Variant("x")];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Variant));
        }

        [Test]
        public void ImplicitFromMatrixOfBoolCreatesVariant()
        {
            Variant v = (MatrixOf<bool>)(new bool[,] { { true, false }, { false, true } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
            Assert.That(v.TypeInfo.ValueRank, Is.EqualTo(2));
        }

        [Test]
        public void ImplicitFromMatrixOfSByteCreatesVariant()
        {
            Variant v = (MatrixOf<sbyte>)(new sbyte[,] { { -1, 1 }, { -2, 2 } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.SByte));
            Assert.That(v.TypeInfo.ValueRank, Is.EqualTo(2));
        }

        [Test]
        public void ImplicitFromMatrixOfByteCreatesVariant()
        {
            Variant v = (MatrixOf<byte>)(new byte[,] { { 1, 2 }, { 3, 4 } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Byte));
        }

        [Test]
        public void ImplicitFromMatrixOfInt16CreatesVariant()
        {
            Variant v = (MatrixOf<short>)(new short[,] { { -1, 1 }, { -2, 2 } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int16));
        }

        [Test]
        public void ImplicitFromMatrixOfUInt16CreatesVariant()
        {
            Variant v = (MatrixOf<ushort>)(new ushort[,] { { 1, 2 }, { 3, 4 } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt16));
        }

        [Test]
        public void ImplicitFromMatrixOfInt32CreatesVariant()
        {
            Variant v = (MatrixOf<int>)(new int[,] { { 1, 2 }, { 3, 4 } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public void ImplicitFromMatrixOfUInt32CreatesVariant()
        {
            Variant v = (MatrixOf<uint>)(new uint[,] { { 1, 2 }, { 3, 4 } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt32));
        }

        [Test]
        public void ImplicitFromMatrixOfInt64CreatesVariant()
        {
            Variant v = (MatrixOf<long>)(new long[,] { { 1, 2 }, { 3, 4 } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int64));
        }

        [Test]
        public void ImplicitFromMatrixOfUInt64CreatesVariant()
        {
            Variant v = (MatrixOf<ulong>)(new ulong[,] { { 1, 2 }, { 3, 4 } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt64));
        }

        [Test]
        public void ImplicitFromMatrixOfFloatCreatesVariant()
        {
            Variant v = (MatrixOf<float>)(new float[,] { { 1.0f, 2.0f }, { 3.0f, 4.0f } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Float));
        }

        [Test]
        public void ImplicitFromMatrixOfDoubleCreatesVariant()
        {
            Variant v = (MatrixOf<double>)(new double[,] { { 1.0, 2.0 }, { 3.0, 4.0 } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Double));
        }

        [Test]
        public void ImplicitFromMatrixOfStringCreatesVariant()
        {
            Variant v = (MatrixOf<string>)(new string[,] { { "a", "b" }, { "c", "d" } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.String));
        }

        [Test]
        public void ImplicitFromMatrixOfDateTimeUtcCreatesVariant()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 1), DateTimeKind.Utc);
            Variant v = (MatrixOf<DateTimeUtc>)(new DateTimeUtc[,] { { dt, dt }, { dt, dt } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.DateTime));
        }

        [Test]
        public void ImplicitFromMatrixOfUuidCreatesVariant()
        {
            var u = Uuid.NewUuid();
            Variant v = (MatrixOf<Uuid>)(new Uuid[,] { { u, u }, { u, u } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Guid));
        }

        [Test]
        public void ImplicitFromMatrixOfByteStringCreatesVariant()
        {
            var bs = ByteString.From(1);
            Variant v = (MatrixOf<ByteString>)(new ByteString[,] { { bs, bs }, { bs, bs } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ByteString));
        }

        [Test]
        public void ImplicitFromMatrixOfXmlElementCreatesVariant()
        {
            XmlElement xml = CreateXmlElement("M");
            Variant v = (MatrixOf<XmlElement>)(new XmlElement[,] { { xml, xml }, { xml, xml } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.XmlElement));
        }

        [Test]
        public void ImplicitFromMatrixOfNodeIdCreatesVariant()
        {
            var nid = new NodeId(1);
            Variant v = (MatrixOf<NodeId>)(new NodeId[,] { { nid, nid }, { nid, nid } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.NodeId));
        }

        [Test]
        public void ImplicitFromMatrixOfExpandedNodeIdCreatesVariant()
        {
            var eni = ExpandedNodeId.Parse("nsu=T;s=A");
            Variant v = (MatrixOf<ExpandedNodeId>)(new ExpandedNodeId[,] { { eni, eni }, { eni, eni } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExpandedNodeId));
        }

        [Test]
        public void ImplicitFromMatrixOfStatusCodeCreatesVariant()
        {
            var sc = new StatusCode(1u);
            Variant v = (MatrixOf<StatusCode>)(new StatusCode[,] { { sc, sc }, { sc, sc } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
        }

        [Test]
        public void ImplicitFromMatrixOfQualifiedNameCreatesVariant()
        {
            var qn = new QualifiedName("q");
            Variant v = (MatrixOf<QualifiedName>)(new QualifiedName[,] { { qn, qn }, { qn, qn } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.QualifiedName));
        }

        [Test]
        public void ImplicitFromMatrixOfLocalizedTextCreatesVariant()
        {
            var lt = new LocalizedText("en", "t");
            Variant v = (MatrixOf<LocalizedText>)(new LocalizedText[,] { { lt, lt }, { lt, lt } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.LocalizedText));
        }

        [Test]
        public void ImplicitFromMatrixOfExtensionObjectCreatesVariant()
        {
            var eo = new ExtensionObject(new Argument());
            Variant v = (MatrixOf<ExtensionObject>)(new ExtensionObject[,] { { eo, eo }, { eo, eo } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.ExtensionObject));
        }

        [Test]
        public void ImplicitFromMatrixOfDataValueCreatesVariant()
        {
            var dv = new DataValue(1);
            Variant v = (MatrixOf<DataValue>)(new DataValue[,] { { dv, dv }, { dv, dv } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.DataValue));
        }

        [Test]
        public void ImplicitFromMatrixOfVariantCreatesVariant()
        {
            var vr = new Variant(1);
            Variant v = (MatrixOf<Variant>)(new Variant[,] { { vr, vr }, { vr, vr } });
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Variant));
        }

        [Test]
        public void EqualityOperatorWithBoolValue()
        {
            var v = new Variant(true);
            Assert.That(v, Is.EqualTo(true));
            Assert.That(v, Is.Not.EqualTo(false));
        }

        [Test]
        public void EqualityOperatorWithSByteValue()
        {
            var v = new Variant((sbyte)-5);
            Assert.That(v, Is.EqualTo((sbyte)-5));
            Assert.That(v, Is.Not.EqualTo((sbyte)0));
        }

        [Test]
        public void EqualityOperatorWithByteValue()
        {
            var v = new Variant((byte)42);
            Assert.That(v, Is.EqualTo((byte)42));
            Assert.That(v, Is.Not.EqualTo((byte)0));
        }

        [Test]
        public void EqualityOperatorWithInt16Value()
        {
            var v = new Variant((short)-100);
            Assert.That(v, Is.EqualTo((short)-100));
        }

        [Test]
        public void EqualityOperatorWithUInt16Value()
        {
            var v = new Variant((ushort)200);
            Assert.That(v, Is.EqualTo((ushort)200));
        }

        [Test]
        public void EqualityOperatorWithInt32Value()
        {
            var v = new Variant(-300);
            Assert.That(v, Is.EqualTo(-300));
        }

        [Test]
        public void EqualityOperatorWithUInt32Value()
        {
            var v = new Variant(400u);
            Assert.That(v, Is.EqualTo(400u));
        }

        [Test]
        public void EqualityOperatorWithInt64Value()
        {
            var v = new Variant(-500L);
            Assert.That(v, Is.EqualTo(-500L));
        }

        [Test]
        public void EqualityOperatorWithUInt64Value()
        {
            var v = new Variant(600UL);
            Assert.That(v, Is.EqualTo(600UL));
        }

        [Test]
        public void EqualityOperatorWithFloatValue()
        {
            var v = new Variant(1.5f);
            Assert.That(v, Is.EqualTo(1.5f));
        }

        [Test]
        public void EqualityOperatorWithDoubleValue()
        {
            var v = new Variant(2.5d);
            Assert.That(v, Is.EqualTo(2.5d));
        }

        [Test]
        public void EqualityOperatorWithStringValue()
        {
            var v = new Variant("hello");
            Assert.That(v, Is.EqualTo("hello"));
            Assert.That(v, Is.Not.EqualTo("world"));
        }

        [Test]
        public void EqualityOperatorWithDateTimeUtcValue()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 6, 15), DateTimeKind.Utc);
            var v = new Variant(dt);
            Assert.That(v, Is.EqualTo(dt));
        }

        [Test]
        public void EqualityOperatorWithUuidValue()
        {
            var guid = Uuid.NewUuid();
            var v = new Variant(guid);
            Assert.That(v, Is.EqualTo(guid));
        }

        [Test]
        public void EqualityOperatorWithByteStringValue()
        {
            var bs = ByteString.From(1, 2, 3);
            var v = new Variant(bs);
            Assert.That(v, Is.EqualTo(bs));
        }

        [Test]
        public void EqualityOperatorWithXmlElementValue()
        {
            XmlElement xml = CreateXmlElement("Test");
            var v = new Variant(xml);
            Assert.That(v, Is.EqualTo(xml));
        }

        [Test]
        public void EqualityOperatorWithNodeIdValue()
        {
            var nid = new NodeId(10, 1);
            var v = new Variant(nid);
            Assert.That(v, Is.EqualTo(nid));
        }

        [Test]
        public void EqualityOperatorWithExpandedNodeIdValue()
        {
            var eni = ExpandedNodeId.Parse("nsu=T;s=Node");
            var v = new Variant(eni);
            Assert.That(v, Is.EqualTo(eni));
        }

        [Test]
        public void EqualityOperatorWithStatusCodeValue()
        {
            var sc = new StatusCode(123u);
            var v = new Variant(sc);
            Assert.That(v, Is.EqualTo(sc));
        }

        [Test]
        public void EqualityOperatorWithQualifiedNameValue()
        {
            var qn = new QualifiedName("name", 2);
            var v = new Variant(qn);
            Assert.That(v, Is.EqualTo(qn));
        }

        [Test]
        public void EqualityOperatorWithLocalizedTextValue()
        {
            var lt = new LocalizedText("en", "text");
            var v = new Variant(lt);
            Assert.That(v, Is.EqualTo(lt));
        }

        [Test]
        public void EqualityOperatorWithExtensionObjectValue()
        {
            var eo = new ExtensionObject(new Argument());
            var v = new Variant(eo);
            Assert.That(v, Is.EqualTo(eo));
        }

        [Test]
        public void EqualityOperatorWithDataValueValue()
        {
            var dv = new DataValue(5);
            var v = new Variant(dv);
            Assert.That(v, Is.EqualTo(dv));
        }

        [Test]
        public void EqualityOperatorWithEnumValue()
        {
            var v = Variant.From(EnumValue.From(TestEnum.One, typeof(TestEnum)));
            Assert.That(v, Is.EqualTo(EnumValue.From(TestEnum.One)));
        }

        [Test]
        public void EqualityOperatorWithArrayOfBool()
        {
            ArrayOf<bool> arr = [true, false];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfSByte()
        {
            ArrayOf<sbyte> arr = [-1, 1];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfByte()
        {
            ArrayOf<byte> arr = [1, 2];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfInt16()
        {
            ArrayOf<short> arr = [-1, 1];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfUInt16()
        {
            ArrayOf<ushort> arr = [1, 2];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfInt32()
        {
            ArrayOf<int> arr = [-1, 1];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfUInt32()
        {
            ArrayOf<uint> arr = [1u, 2u];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfInt64()
        {
            ArrayOf<long> arr = [-1L, 1L];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfUInt64()
        {
            ArrayOf<ulong> arr = [1UL, 2UL];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfFloat()
        {
            ArrayOf<float> arr = [1.0f, 2.0f];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfDouble()
        {
            ArrayOf<double> arr = [1.0, 2.0];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfString()
        {
            ArrayOf<string> arr = ["a", "b"];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfDateTimeUtc()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 1), DateTimeKind.Utc);
            ArrayOf<DateTimeUtc> arr = [dt, dt];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfUuid()
        {
            var u = Uuid.NewUuid();
            ArrayOf<Uuid> arr = [u, u];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfByteString()
        {
            ArrayOf<ByteString> arr = [ByteString.From(1), ByteString.From(2)];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfXmlElement()
        {
            ArrayOf<XmlElement> arr = [CreateXmlElement("A"), CreateXmlElement("B")];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfNodeId()
        {
            ArrayOf<NodeId> arr = [new NodeId(1), new NodeId(2)];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfExpandedNodeId()
        {
            ArrayOf<ExpandedNodeId> arr = [
                ExpandedNodeId.Parse("nsu=T;s=A"),
                ExpandedNodeId.Parse("nsu=T;s=B")
            ];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfStatusCode()
        {
            ArrayOf<StatusCode> arr = [new StatusCode(1u), new StatusCode(2u)];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfQualifiedName()
        {
            ArrayOf<QualifiedName> arr = [new QualifiedName("a"), new QualifiedName("b")];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfLocalizedText()
        {
            ArrayOf<LocalizedText> arr = [new LocalizedText("en", "a"), new LocalizedText("de", "b")];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfExtensionObject()
        {
            ArrayOf<ExtensionObject> arr = [
                new ExtensionObject(new Argument()),
                new ExtensionObject(new Argument())
            ];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfDataValue()
        {
            ArrayOf<DataValue> arr = [new DataValue(1), new DataValue(2)];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithArrayOfVariant()
        {
            ArrayOf<Variant> arr = [new Variant(1), new Variant("x")];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfBool()
        {
            MatrixOf<bool> m = new bool[,] { { true, false }, { false, true } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfSByte()
        {
            MatrixOf<sbyte> m = new sbyte[,] { { -1, 1 }, { -2, 2 } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfByte()
        {
            MatrixOf<byte> m = new byte[,] { { 1, 2 }, { 3, 4 } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfInt16()
        {
            MatrixOf<short> m = new short[,] { { -1, 1 }, { -2, 2 } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfUInt16()
        {
            MatrixOf<ushort> m = new ushort[,] { { 1, 2 }, { 3, 4 } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfInt32()
        {
            MatrixOf<int> m = new int[,] { { 1, 2 }, { 3, 4 } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfUInt32()
        {
            MatrixOf<uint> m = new uint[,] { { 1, 2 }, { 3, 4 } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfInt64()
        {
            MatrixOf<long> m = new long[,] { { 1, 2 }, { 3, 4 } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfUInt64()
        {
            MatrixOf<ulong> m = new ulong[,] { { 1, 2 }, { 3, 4 } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfFloat()
        {
            MatrixOf<float> m = new float[,] { { 1.0f, 2.0f }, { 3.0f, 4.0f } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfDouble()
        {
            MatrixOf<double> m = new double[,] { { 1.0, 2.0 }, { 3.0, 4.0 } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfString()
        {
            MatrixOf<string> m = new string[,] { { "a", "b" }, { "c", "d" } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfDateTimeUtc()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 1), DateTimeKind.Utc);
            MatrixOf<DateTimeUtc> m = new DateTimeUtc[,] { { dt, dt }, { dt, dt } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfUuid()
        {
            var u = Uuid.NewUuid();
            MatrixOf<Uuid> m = new Uuid[,] { { u, u }, { u, u } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfByteString()
        {
            var bs = ByteString.From(1);
            MatrixOf<ByteString> m = new ByteString[,] { { bs, bs }, { bs, bs } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfXmlElement()
        {
            XmlElement xml = CreateXmlElement("E");
            MatrixOf<XmlElement> m = new XmlElement[,] { { xml, xml }, { xml, xml } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfNodeId()
        {
            var nid = new NodeId(1);
            MatrixOf<NodeId> m = new NodeId[,] { { nid, nid }, { nid, nid } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfExpandedNodeId()
        {
            var eni = ExpandedNodeId.Parse("nsu=T;s=A");
            MatrixOf<ExpandedNodeId> m = new ExpandedNodeId[,] { { eni, eni }, { eni, eni } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfStatusCode()
        {
            var sc = new StatusCode(1u);
            MatrixOf<StatusCode> m = new StatusCode[,] { { sc, sc }, { sc, sc } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfQualifiedName()
        {
            var qn = new QualifiedName("q");
            MatrixOf<QualifiedName> m = new QualifiedName[,] { { qn, qn }, { qn, qn } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfLocalizedText()
        {
            var lt = new LocalizedText("en", "t");
            MatrixOf<LocalizedText> m = new LocalizedText[,] { { lt, lt }, { lt, lt } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfExtensionObject()
        {
            var eo = new ExtensionObject(new Argument());
            MatrixOf<ExtensionObject> m = new ExtensionObject[,] { { eo, eo }, { eo, eo } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfDataValue()
        {
            var dv = new DataValue(1);
            MatrixOf<DataValue> m = new DataValue[,] { { dv, dv }, { dv, dv } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void EqualityOperatorWithMatrixOfVariant()
        {
            var vr = new Variant(1);
            MatrixOf<Variant> m = new Variant[,] { { vr, vr }, { vr, vr } };
            var v = new Variant(m);
            Assert.That(v, Is.EqualTo(m));
        }

        [Test]
        public void ConvertToBooleanFromInt32()
        {
            var v = new Variant(1);
            Variant result = v.ConvertToBoolean();
            Assert.That((bool)result, Is.True);
        }

        [Test]
        public void ConvertToBooleanFromString()
        {
            var v = new Variant("true");
            Variant result = v.ConvertToBoolean();
            Assert.That((bool)result, Is.True);
        }

        [Test]
        public void ConvertToBooleanFromSByte()
        {
            var v = new Variant((sbyte)1);
            Variant result = v.ConvertToBoolean();
            Assert.That((bool)result, Is.True);
        }

        [Test]
        public void ConvertToBooleanFromByte()
        {
            var v = new Variant((byte)0);
            Variant result = v.ConvertToBoolean();
            Assert.That((bool)result, Is.False);
        }

        [Test]
        public void ConvertToBooleanFromInt16()
        {
            var v = new Variant((short)1);
            Variant result = v.ConvertToBoolean();
            Assert.That((bool)result, Is.True);
        }

        [Test]
        public void ConvertToBooleanFromUInt16()
        {
            var v = new Variant((ushort)0);
            Variant result = v.ConvertToBoolean();
            Assert.That((bool)result, Is.False);
        }

        [Test]
        public void ConvertToBooleanFromUInt32()
        {
            var v = new Variant(1u);
            Variant result = v.ConvertToBoolean();
            Assert.That((bool)result, Is.True);
        }

        [Test]
        public void ConvertToBooleanFromInt64()
        {
            var v = new Variant(1L);
            Variant result = v.ConvertToBoolean();
            Assert.That((bool)result, Is.True);
        }

        [Test]
        public void ConvertToBooleanFromUInt64()
        {
            var v = new Variant(0UL);
            Variant result = v.ConvertToBoolean();
            Assert.That((bool)result, Is.False);
        }

        [Test]
        public void ConvertToBooleanFromFloat()
        {
            var v = new Variant(1.0f);
            Variant result = v.ConvertToBoolean();
            Assert.That((bool)result, Is.True);
        }

        [Test]
        public void ConvertToBooleanFromDouble()
        {
            var v = new Variant(0.0d);
            Variant result = v.ConvertToBoolean();
            Assert.That((bool)result, Is.False);
        }

        [Test]
        public void ConvertToBooleanReturnsSelfForBool()
        {
            var v = new Variant(true);
            Variant result = v.ConvertToBoolean();
            Assert.That((bool)result, Is.True);
        }

        [Test]
        public void ConvertToSByteFromVariousTypes()
        {
            Assert.That((sbyte)new Variant(true).ConvertToSByte(), Is.EqualTo((sbyte)1));
            Assert.That((sbyte)new Variant((byte)5).ConvertToSByte(), Is.EqualTo((sbyte)5));
            Assert.That((sbyte)new Variant((short)10).ConvertToSByte(), Is.EqualTo((sbyte)10));
            Assert.That((sbyte)new Variant((ushort)10).ConvertToSByte(), Is.EqualTo((sbyte)10));
            Assert.That((sbyte)new Variant(10).ConvertToSByte(), Is.EqualTo((sbyte)10));
            Assert.That((sbyte)new Variant(10u).ConvertToSByte(), Is.EqualTo((sbyte)10));
            Assert.That((sbyte)new Variant(10L).ConvertToSByte(), Is.EqualTo((sbyte)10));
            Assert.That((sbyte)new Variant(10UL).ConvertToSByte(), Is.EqualTo((sbyte)10));
            Assert.That((sbyte)new Variant(10.0f).ConvertToSByte(), Is.EqualTo((sbyte)10));
            Assert.That((sbyte)new Variant(10.0d).ConvertToSByte(), Is.EqualTo((sbyte)10));
            Assert.That((sbyte)new Variant("10").ConvertToSByte(), Is.EqualTo((sbyte)10));
        }

        [Test]
        public void ConvertToSByteReturnsSelfForSByte()
        {
            var v = new Variant((sbyte)-5);
            Variant result = v.ConvertToSByte();
            Assert.That((sbyte)result, Is.EqualTo((sbyte)-5));
        }

        [Test]
        public void ConvertToByteFromVariousTypes()
        {
            Assert.That((byte)new Variant(true).ConvertToByte(), Is.EqualTo((byte)1));
            Assert.That((byte)new Variant((sbyte)5).ConvertToByte(), Is.EqualTo((byte)5));
            Assert.That((byte)new Variant((short)10).ConvertToByte(), Is.EqualTo((byte)10));
            Assert.That((byte)new Variant((ushort)10).ConvertToByte(), Is.EqualTo((byte)10));
            Assert.That((byte)new Variant(10).ConvertToByte(), Is.EqualTo((byte)10));
            Assert.That((byte)new Variant(10u).ConvertToByte(), Is.EqualTo((byte)10));
            Assert.That((byte)new Variant(10L).ConvertToByte(), Is.EqualTo((byte)10));
            Assert.That((byte)new Variant(10UL).ConvertToByte(), Is.EqualTo((byte)10));
            Assert.That((byte)new Variant(10.0f).ConvertToByte(), Is.EqualTo((byte)10));
            Assert.That((byte)new Variant(10.0d).ConvertToByte(), Is.EqualTo((byte)10));
            Assert.That((byte)new Variant("10").ConvertToByte(), Is.EqualTo((byte)10));
        }

        [Test]
        public void ConvertToByteReturnsSelfForByte()
        {
            var v = new Variant((byte)42);
            Variant result = v.ConvertToByte();
            Assert.That((byte)result, Is.EqualTo((byte)42));
        }

        [Test]
        public void ConvertToInt16FromVariousTypes()
        {
            Assert.That((short)new Variant(true).ConvertToInt16(), Is.EqualTo((short)1));
            Assert.That((short)new Variant((sbyte)5).ConvertToInt16(), Is.EqualTo((short)5));
            Assert.That((short)new Variant((byte)5).ConvertToInt16(), Is.EqualTo((short)5));
            Assert.That((short)new Variant(10).ConvertToInt16(), Is.EqualTo((short)10));
            Assert.That((short)new Variant("100").ConvertToInt16(), Is.EqualTo((short)100));
        }

        [Test]
        public void ConvertToUInt16FromVariousTypes()
        {
            Assert.That((ushort)new Variant(true).ConvertToUInt16(), Is.EqualTo((ushort)1));
            Assert.That((ushort)new Variant((byte)5).ConvertToUInt16(), Is.EqualTo((ushort)5));
            Assert.That((ushort)new Variant(10).ConvertToUInt16(), Is.EqualTo((ushort)10));
            Assert.That((ushort)new Variant("100").ConvertToUInt16(), Is.EqualTo((ushort)100));
        }

        [Test]
        public void ConvertToInt32FromVariousTypes()
        {
            Assert.That((int)new Variant(true).ConvertToInt32(), Is.EqualTo(1));
            Assert.That((int)new Variant((sbyte)5).ConvertToInt32(), Is.EqualTo(5));
            Assert.That((int)new Variant((byte)5).ConvertToInt32(), Is.EqualTo(5));
            Assert.That((int)new Variant((short)5).ConvertToInt32(), Is.EqualTo(5));
            Assert.That((int)new Variant((ushort)5).ConvertToInt32(), Is.EqualTo(5));
            Assert.That((int)new Variant(5u).ConvertToInt32(), Is.EqualTo(5));
            Assert.That((int)new Variant(5L).ConvertToInt32(), Is.EqualTo(5));
            Assert.That((int)new Variant(5UL).ConvertToInt32(), Is.EqualTo(5));
            Assert.That((int)new Variant(5.0f).ConvertToInt32(), Is.EqualTo(5));
            Assert.That((int)new Variant(5.0d).ConvertToInt32(), Is.EqualTo(5));
            Assert.That((int)new Variant("5").ConvertToInt32(), Is.EqualTo(5));
        }

        [Test]
        public void ConvertToUInt32FromVariousTypes()
        {
            Assert.That((uint)new Variant(true).ConvertToUInt32(), Is.EqualTo(1u));
            Assert.That((uint)new Variant((byte)5).ConvertToUInt32(), Is.EqualTo(5u));
            Assert.That((uint)new Variant(5).ConvertToUInt32(), Is.EqualTo(5u));
            Assert.That((uint)new Variant("5").ConvertToUInt32(), Is.EqualTo(5u));
        }

        [Test]
        public void ConvertToInt64FromVariousTypes()
        {
            Assert.That((long)new Variant(true).ConvertToInt64(), Is.EqualTo(1L));
            Assert.That((long)new Variant((byte)5).ConvertToInt64(), Is.EqualTo(5L));
            Assert.That((long)new Variant(5).ConvertToInt64(), Is.EqualTo(5L));
            Assert.That((long)new Variant("5").ConvertToInt64(), Is.EqualTo(5L));
        }

        [Test]
        public void ConvertToUInt64FromVariousTypes()
        {
            Assert.That((ulong)new Variant(true).ConvertToUInt64(), Is.EqualTo(1UL));
            Assert.That((ulong)new Variant((byte)5).ConvertToUInt64(), Is.EqualTo(5UL));
            Assert.That((ulong)new Variant(5).ConvertToUInt64(), Is.EqualTo(5UL));
            Assert.That((ulong)new Variant("5").ConvertToUInt64(), Is.EqualTo(5UL));
        }

        [Test]
        public void ConvertToFloatFromVariousTypes()
        {
            Assert.That((float)new Variant(true).ConvertToFloat(), Is.EqualTo(1.0f));
            Assert.That((float)new Variant((byte)5).ConvertToFloat(), Is.EqualTo(5.0f));
            Assert.That((float)new Variant(5).ConvertToFloat(), Is.EqualTo(5.0f));
            Assert.That((float)new Variant(5.0d).ConvertToFloat(), Is.EqualTo(5.0f));
            Assert.That((float)new Variant("5").ConvertToFloat(), Is.EqualTo(5.0f));
        }

        [Test]
        public void ConvertToDoubleFromVariousTypes()
        {
            Assert.That((double)new Variant(true).ConvertToDouble(), Is.EqualTo(1.0d));
            Assert.That((double)new Variant((byte)5).ConvertToDouble(), Is.EqualTo(5.0d));
            Assert.That((double)new Variant(5).ConvertToDouble(), Is.EqualTo(5.0d));
            Assert.That((double)new Variant(5.0f).ConvertToDouble(), Is.EqualTo(5.0d));
            Assert.That((double)new Variant("5").ConvertToDouble(), Is.EqualTo(5.0d));
        }

        [Test]
        public void ConvertToStringFromVariousScalarTypes()
        {
            // Tests ConvertToString for all numeric and complex types
            Assert.That((string)new Variant(true).ConvertToString(), Is.EqualTo("true"));
            Assert.That((string)new Variant((sbyte)-1).ConvertToString(), Is.EqualTo("-1"));
            Assert.That((string)new Variant((byte)42).ConvertToString(), Is.EqualTo("42"));
            Assert.That((string)new Variant((short)-100).ConvertToString(), Is.EqualTo("-100"));
            Assert.That((string)new Variant((ushort)200).ConvertToString(), Is.EqualTo("200"));
            Assert.That((string)new Variant(300).ConvertToString(), Is.EqualTo("300"));
            Assert.That((string)new Variant(400u).ConvertToString(), Is.EqualTo("400"));
            Assert.That((string)new Variant(500L).ConvertToString(), Is.EqualTo("500"));
            Assert.That((string)new Variant(600UL).ConvertToString(), Is.EqualTo("600"));
            Assert.That((string)new Variant(1.5f).ConvertToString(), Is.Not.Null);
            Assert.That((string)new Variant(2.5d).ConvertToString(), Is.Not.Null);
        }

        [Test]
        public void ConvertToStringReturnsSelfForString()
        {
            var v = new Variant("hello");
            Variant result = v.ConvertToString();
            Assert.That((string)result, Is.EqualTo("hello"));
        }

        [Test]
        public void ConvertToStringFromDateTime()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 6, 15, 10, 30, 0), DateTimeKind.Utc);
            var v = new Variant(dt);
            Variant result = v.ConvertToString();
            Assert.That((string)result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ConvertToStringFromGuid()
        {
            var guid = Uuid.NewUuid();
            var v = new Variant(guid);
            Variant result = v.ConvertToString();
            Assert.That((string)result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ConvertToStringFromNodeId()
        {
            var v = new Variant(new NodeId(10, 1));
            Variant result = v.ConvertToString();
            Assert.That((string)result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ConvertToStringFromExpandedNodeId()
        {
            var v = new Variant(ExpandedNodeId.Parse("nsu=T;s=A"));
            Variant result = v.ConvertToString();
            Assert.That((string)result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ConvertToStringFromLocalizedText()
        {
            var v = new Variant(new LocalizedText("en", "mytext"));
            Variant result = v.ConvertToString();
            Assert.That((string)result, Is.EqualTo("mytext"));
        }

        [Test]
        public void ConvertToStringFromQualifiedName()
        {
            var v = new Variant(new QualifiedName("qname", 2));
            Variant result = v.ConvertToString();
            Assert.That((string)result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ConvertToStringFromXmlElement()
        {
            XmlElement xml = CreateXmlElement("Test");
            var v = new Variant(xml);
            Variant result = v.ConvertToString();
            Assert.That((string)result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ConvertToStringFromStatusCode()
        {
            var v = new Variant(new StatusCode(123u));
            Variant result = v.ConvertToString();
            Assert.That((string)result, Is.EqualTo("123"));
        }

        [Test]
        public void ConvertToStringFromExtensionObject()
        {
            var v = new Variant(new ExtensionObject(new Argument()));
            Variant result = v.ConvertToString();
            Assert.That((string)result, Is.Not.Null);
        }

        [Test]
        public void ConvertToStringFromNullReturnsDefault()
        {
            Variant v = default;
            Variant result = v.ConvertTo(BuiltInType.String);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ConvertToReturnsDefaultForNullVariant()
        {
            Variant v = default;
            Variant result = v.ConvertTo(BuiltInType.Int32);
            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ConvertToReturnsSelfForSameType()
        {
            var v = new Variant(42);
            Variant result = v.ConvertTo(BuiltInType.Int32);
            Assert.That((int)result, Is.EqualTo(42));
        }

        [Test]
        public void ConvertToVariantReturnsSelf()
        {
            var v = new Variant(42);
            Variant result = v.ConvertTo(BuiltInType.Variant);
            Assert.That((int)result, Is.EqualTo(42));
        }

        [Test]
        public void ConvertToNumberConvertsToDouble()
        {
            var v = new Variant(42);
            Variant result = v.ConvertTo(BuiltInType.Number);
            Assert.That(result.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Double));
        }

        [Test]
        public void ConvertToIntegerConvertsToInt64()
        {
            var v = new Variant(42);
            Variant result = v.ConvertTo(BuiltInType.Integer);
            Assert.That(result.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int64));
        }

        [Test]
        public void ConvertToUIntegerConvertsToUInt64()
        {
            var v = new Variant(42u);
            Variant result = v.ConvertTo(BuiltInType.UInteger);
            Assert.That(result.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.UInt64));
        }

        [Test]
        public void ConvertToEnumerationConvertsToInt32()
        {
            var v = new Variant((byte)5);
            Variant result = v.ConvertTo(BuiltInType.Enumeration);
            Assert.That(result.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
        }

        [Test]
        public void ConvertToThrowsForUnsupportedCast()
        {
            var v = new Variant(42);
            Assert.Throws<InvalidCastException>(() => v.ConvertTo(BuiltInType.ExtensionObject));
        }

        [Test]
        public void ConvertToThrowsForDataValue()
        {
            var v = new Variant(42);
            Assert.Throws<InvalidCastException>(() => v.ConvertTo(BuiltInType.DataValue));
        }

        [Test]
        public void ConvertToThrowsForNullTarget()
        {
            var v = new Variant(42);
            Assert.Throws<InvalidCastException>(() => v.ConvertTo(BuiltInType.Null));
        }

        [Test]
        public void ConvertToArrayConvertsElementwise()
        {
            ArrayOf<int> arr = [1, 2, 3];
            var v = new Variant(arr);
            Variant result = v.ConvertTo(BuiltInType.Double);
            Assert.That(result.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Double));
            Assert.That(result.TypeInfo.IsArray, Is.True);
        }

        [Test]
        public void ConvertToXmlElementFromString()
        {
            var v = new Variant("<Root>Test</Root>");
            Variant result = v.ConvertTo(BuiltInType.XmlElement);
            Assert.That(result.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.XmlElement));
        }

        [Test]
        public void GetHashCodeForNullVariantReturnsZero()
        {
            Variant v = default;
            Assert.That(v.GetHashCode(), Is.Zero);
        }

        [Test]
        public void GetHashCodeForBooleanScalar()
        {
            var v = new Variant(true);
            // Just ensure it doesn't throw and returns a value
            Assert.That(v.GetHashCode(), Is.Not.Zero);
        }

        [Test]
        public void GetHashCodeForIntegerScalars()
        {
            Assert.That(new Variant((sbyte)1).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant((byte)1).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant((short)1).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant((ushort)1).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant(1).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant(1u).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant(1L).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant(1UL).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant(1.0f).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant(1.0d).GetHashCode(), Is.TypeOf<int>());
        }

        [Test]
        public void GetHashCodeForDateTimeScalar()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 1), DateTimeKind.Utc);
            var v = new Variant(dt);
            Assert.That(v.GetHashCode(), Is.TypeOf<int>());
        }

        [Test]
        public void GetHashCodeForStatusCodeScalar()
        {
            var v = new Variant(new StatusCode(123u));
            Assert.That(v.GetHashCode(), Is.TypeOf<int>());
        }

        [Test]
        public void GetHashCodeForStringScalar()
        {
            var v = new Variant("test");
            Assert.That(v.GetHashCode(), Is.TypeOf<int>());
        }

        [Test]
        public void GetHashCodeForGuidScalar()
        {
            var v = new Variant(Uuid.NewUuid());
            Assert.That(v.GetHashCode(), Is.TypeOf<int>());
        }

        [Test]
        public void GetHashCodeForNodeIdScalar()
        {
            var v = new Variant(new NodeId(10, 1));
            Assert.That(v.GetHashCode(), Is.TypeOf<int>());
        }

        [Test]
        public void GetHashCodeForArrays()
        {
            Assert.That(new Variant([true, false]).GetHashCode(), Is.TypeOf<int>());

            Assert.That(new Variant((ArrayOf<sbyte>)[-1, 1]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant((ArrayOf<byte>)[1, 2]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant((ArrayOf<short>)[-1, 1]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant((ArrayOf<ushort>)[1, 2]).GetHashCode(), Is.TypeOf<int>());

            Assert.That(new Variant([1, 2]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([1u, 2u]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([1L, 2L]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([1UL, 2UL]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([1.0f, 2.0f]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([1.0, 2.0]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant(["a", "b"]).GetHashCode(), Is.TypeOf<int>());
        }

        [Test]
        public void GetHashCodeForComplexArrays()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 1), DateTimeKind.Utc);

            Assert.That(new Variant([dt]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([new StatusCode(1u)]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([Uuid.NewUuid()]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([ByteString.From(1)]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([CreateXmlElement("A")]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([new NodeId(1)]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([ExpandedNodeId.Parse("nsu=T;s=A")]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([new QualifiedName("q")]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([new LocalizedText("en", "t")]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([new ExtensionObject(new Argument())]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([new DataValue(1)]).GetHashCode(), Is.TypeOf<int>());
            Assert.That(new Variant([new Variant(1)]).GetHashCode(), Is.TypeOf<int>());
        }

        [Test]
        public void ValueIsDefaultOrNullForNullVariant()
        {
            Variant v = default;
            Assert.That(v.ValueIsDefaultOrNull, Is.True);
        }

        [Test]
        public void ValueIsDefaultOrNullForDefaultBool()
        {
            var v = new Variant(false);
            Assert.That(v.ValueIsDefaultOrNull, Is.True);
        }

        [Test]
        public void ValueIsDefaultOrNullForNonDefaultBool()
        {
            var v = new Variant(true);
            Assert.That(v.ValueIsDefaultOrNull, Is.False);
        }

        [Test]
        public void ValueIsDefaultOrNullForDefaultInt32()
        {
            var v = new Variant(0);
            Assert.That(v.ValueIsDefaultOrNull, Is.True);
        }

        [Test]
        public void ValueIsDefaultOrNullForNonDefaultInt32()
        {
            var v = new Variant(42);
            Assert.That(v.ValueIsDefaultOrNull, Is.False);
        }

        [Test]
        public void ValueIsDefaultOrNullForDefaultNumericTypes()
        {
            Assert.That(new Variant((sbyte)0).ValueIsDefaultOrNull, Is.True);
            Assert.That(new Variant((byte)0).ValueIsDefaultOrNull, Is.True);
            Assert.That(new Variant((short)0).ValueIsDefaultOrNull, Is.True);
            Assert.That(new Variant((ushort)0).ValueIsDefaultOrNull, Is.True);
            Assert.That(new Variant(0u).ValueIsDefaultOrNull, Is.True);
            Assert.That(new Variant(0L).ValueIsDefaultOrNull, Is.True);
            Assert.That(new Variant(0UL).ValueIsDefaultOrNull, Is.True);
            Assert.That(new Variant(0.0f).ValueIsDefaultOrNull, Is.True);
            Assert.That(new Variant(0.0d).ValueIsDefaultOrNull, Is.True);
        }

        [Test]
        public void ValueIsDefaultOrNullForDefaultDateTime()
        {
            var v = new Variant(default(DateTimeUtc));
            Assert.That(v.ValueIsDefaultOrNull, Is.True);
        }

        [Test]
        public void ValueIsDefaultOrNullForDefaultStatusCode()
        {
            var v = new Variant(new StatusCode(0));
            Assert.That(v.ValueIsDefaultOrNull, Is.True);
        }

        [Test]
        public void ValueIsDefaultOrNullForDefaultGuid()
        {
            var v = new Variant(Uuid.Empty);
            Assert.That(v.ValueIsDefaultOrNull, Is.True);
        }

        [Test]
        public void ValueIsDefaultOrNullForNonDefaultGuid()
        {
            var v = new Variant(Uuid.NewUuid());
            Assert.That(v.ValueIsDefaultOrNull, Is.False);
        }

        [Test]
        public void ValueIsDefaultOrNullForNullString()
        {
            var v = new Variant((string)null);
            // Null string creates a null variant
            Assert.That(v.ValueIsDefaultOrNull, Is.True);
        }

        [Test]
        public void ValueIsDefaultOrNullForNullByteString()
        {
            var v = new Variant(default(ByteString));
            Assert.That(v.ValueIsDefaultOrNull, Is.True);
        }

        [Test]
        public void CreateDefaultReturnsVariantWithSpecifiedTypeInfo()
        {
            var v = Variant.CreateDefault(TypeInfo.Scalars.Int32);
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Int32));
            Assert.That(v.TypeInfo.IsScalar, Is.True);
        }

        [Test]
        public void CreateDefaultArrayReturnsVariantWithArrayTypeInfo()
        {
            var v = Variant.CreateDefault(TypeInfo.Arrays.Double);
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Double));
            Assert.That(v.TypeInfo.IsArray, Is.True);
        }

        [Test]
        public void ToStringForScalarBool()
        {
            var v = new Variant(true);
            Assert.That(v.ToString(), Is.EqualTo("True"));
        }

        [Test]
        public void ToStringForScalarSByte()
        {
            var v = new Variant((sbyte)-5);
            Assert.That(v.ToString(), Is.EqualTo("-5"));
        }

        [Test]
        public void ToStringForScalarByte()
        {
            var v = new Variant((byte)42);
            Assert.That(v.ToString(), Is.EqualTo("42"));
        }

        [Test]
        public void ToStringForScalarInt16()
        {
            var v = new Variant((short)-100);
            Assert.That(v.ToString(), Is.EqualTo("-100"));
        }

        [Test]
        public void ToStringForScalarUInt16()
        {
            var v = new Variant((ushort)200);
            Assert.That(v.ToString(), Is.EqualTo("200"));
        }

        [Test]
        public void ToStringForScalarInt32()
        {
            var v = new Variant(42);
            Assert.That(v.ToString(), Is.EqualTo("42"));
        }

        [Test]
        public void ToStringForScalarUInt32()
        {
            var v = new Variant(42u);
            Assert.That(v.ToString(), Is.EqualTo("42"));
        }

        [Test]
        public void ToStringForScalarInt64()
        {
            var v = new Variant(42L);
            Assert.That(v.ToString(), Is.EqualTo("42"));
        }

        [Test]
        public void ToStringForScalarUInt64()
        {
            var v = new Variant(42UL);
            Assert.That(v.ToString(), Is.EqualTo("42"));
        }

        [Test]
        public void ToStringForScalarFloat()
        {
            var v = new Variant(1.5f);
            Assert.That(v.ToString(), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ToStringForScalarDouble()
        {
            var v = new Variant(2.5d);
            Assert.That(v.ToString(), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ToStringForScalarDateTime()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 6, 15), DateTimeKind.Utc);
            var v = new Variant(dt);
            Assert.That(v.ToString(), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ToStringForScalarStatusCode()
        {
            var v = new Variant(new StatusCode(0));
            Assert.That(v.ToString(), Is.Not.Null);
        }

        [Test]
        public void ToStringForNullVariant()
        {
            Variant v = default;
            Assert.That(v.ToString(), Is.EqualTo("<null>"));
        }

        [Test]
        public void ToStringForScalarEnumeration()
        {
            var v = Variant.From(EnumValue.From(TestEnum.One, typeof(TestEnum)));
            string s = v.ToString();
            Assert.That(s, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void FromEnumerationWithIntEnumType()
        {
            var v = Variant.From(EnumValue.From(TestEnum.One, typeof(TestEnum)));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(v.TypeInfo.IsScalar, Is.True);
        }

        [Test]
        public void FromEnumerationWithByteEnumType()
        {
            var v = Variant.From(EnumValue.From(ByteEnum.One, typeof(ByteEnum)));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
        }

        [Test]
        public void FromEnumerationWithShortEnumType()
        {
            var v = Variant.From(EnumValue.From(ShortEnum.One, typeof(ShortEnum)));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
        }

        [Test]
        public void FromEnumerationWithLongEnumType()
        {
            var v = Variant.From(EnumValue.From(LongEnum.One, typeof(LongEnum)));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
        }

        [Test]
        public void FromEnumerationWithUShortEnumType()
        {
            var v = Variant.From(EnumValue.From(UShortEnum.One, typeof(UShortEnum)));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
        }

        [Test]
        public void FromEnumerationWithUIntEnumType()
        {
            var v = Variant.From(EnumValue.From(UIntEnum.One, typeof(UIntEnum)));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
        }

        [Test]
        public void FromEnumerationWithULongEnumType()
        {
            var v = Variant.From(EnumValue.From(ULongEnum.One, typeof(ULongEnum)));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
        }

        [Test]
        public void FromEnumerationWithSByteEnumType()
        {
            var v = Variant.From(EnumValue.From(SByteEnum.One, typeof(SByteEnum)));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
        }

        [Test]
        public void FromEnumerationWithIntValue()
        {
            var v = Variant.From(EnumValue.From(42, typeof(TestEnum)));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
        }

        [Test]
        public void FromEnumerationWithIntValueNoType()
        {
            var v = Variant.From(EnumValue.From(42));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
        }

        [Test]
        public void FromEnumerationWithIntArrayValue()
        {
            ArrayOf<int> arr = [1, 2, 3];
            var v = Variant.From(EnumValue.From(arr));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(v.TypeInfo.IsArray, Is.True);
        }

        [Test]
        public void FromEnumerationWithNullIntArrayReturnsDefault()
        {
            ArrayOf<int> arr = default;
            var v = Variant.From(EnumValue.From(arr));
            Assert.That(v.ValueIsDefaultOrNull, Is.True);
        }

        [Test]
        public void FromEnumerationWithIntMatrixValue()
        {
            MatrixOf<int> matrix = new int[,] { { 1, 2 }, { 3, 4 } };
            var v = Variant.From(EnumValue.From(matrix));
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(v.TypeInfo.ValueRank, Is.EqualTo(2));
        }

        [Test]
        public void FromEnumerationWithNullMatrixReturnsDefault()
        {
            MatrixOf<int> matrix = default;
            var v = Variant.From(EnumValue.From(matrix));
            Assert.That(v.ValueIsDefaultOrNull, Is.True);
        }

        [Test]
        public void AsBoxedObjectForScalarBoolReturnsBoxedBool()
        {
            var v = new Variant(true);
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.True);
        }

        [Test]
        public void AsBoxedObjectForScalarInt32ReturnsBoxedInt()
        {
            var v = new Variant(42);
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.EqualTo(42));
        }

        [Test]
        public void AsBoxedObjectForNullVariantReturnsNull()
        {
            Variant v = default;
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.Null);
        }

        [Test]
        public void AsBoxedObjectForNodeIdReturnsNodeId()
        {
            var nid = new NodeId(10, 1);
            var v = new Variant(nid);
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.EqualTo(nid));
        }

        [Test]
        public void AsBoxedObjectForExpandedNodeIdReturnsExpandedNodeId()
        {
            var eni = ExpandedNodeId.Parse("nsu=T;s=A");
            var v = new Variant(eni);
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.EqualTo(eni));
        }

        [Test]
        public void AsBoxedObjectForLocalizedTextReturnsLocalizedText()
        {
            var lt = new LocalizedText("en", "t");
            var v = new Variant(lt);
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.EqualTo(lt));
        }

        [Test]
        public void AsBoxedObjectForQualifiedNameReturnsQualifiedName()
        {
            var qn = new QualifiedName("q", 1);
            var v = new Variant(qn);
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.EqualTo(qn));
        }

        [Test]
        public void AsBoxedObjectForExtensionObjectReturnsExtensionObject()
        {
            var eo = new ExtensionObject(new Argument());
            var v = new Variant(eo);
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.Not.Null);
        }

        [Test]
        public void AsBoxedObjectForAllPrimitiveTypes()
        {
            Assert.That(new Variant((sbyte)-1).AsBoxedObject(), Is.EqualTo((sbyte)-1));
            Assert.That(new Variant((byte)1).AsBoxedObject(), Is.EqualTo((byte)1));
            Assert.That(new Variant((short)-1).AsBoxedObject(), Is.EqualTo((short)-1));
            Assert.That(new Variant((ushort)1).AsBoxedObject(), Is.EqualTo((ushort)1));
            Assert.That(new Variant(1u).AsBoxedObject(), Is.EqualTo(1u));
            Assert.That(new Variant(1L).AsBoxedObject(), Is.EqualTo(1L));
            Assert.That(new Variant(1UL).AsBoxedObject(), Is.EqualTo(1UL));
            Assert.That(new Variant(1.5f).AsBoxedObject(), Is.EqualTo(1.5f));
            Assert.That(new Variant(2.5d).AsBoxedObject(), Is.EqualTo(2.5d));
        }

        [Test]
        public void AsBoxedObjectForDateTimeReturnsDateTime()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 1), DateTimeKind.Utc);
            var v = new Variant(dt);
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.EqualTo(dt));
        }

        [Test]
        public void AsBoxedObjectForStatusCodeReturnsStatusCode()
        {
            var v = new Variant(new StatusCode(123u));
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.InstanceOf<StatusCode>());
        }

        [Test]
        public void AsBoxedObjectForEnumerationReturnsEnumValue()
        {
            var v = Variant.From(EnumValue.From(TestEnum.One, typeof(TestEnum)));
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.Not.Null);
        }

        [Test]
        public void AsBoxedObjectForByteEnumerationReturnsEnumValue()
        {
            var v = Variant.From(EnumValue.From(ByteEnum.One, typeof(ByteEnum)));
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.Not.Null);
        }

        [Test]
        public void AsBoxedObjectForShortEnumerationReturnsEnumValue()
        {
            var v = Variant.From(EnumValue.From(ShortEnum.One, typeof(ShortEnum)));
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.Not.Null);
        }

        [Test]
        public void AsBoxedObjectForLongEnumerationReturnsEnumValue()
        {
            var v = Variant.From(EnumValue.From(LongEnum.One, typeof(LongEnum)));
            object boxed = v.AsBoxedObject();
            Assert.That(boxed, Is.Not.Null);
        }

        [Test]
        public void AsBoxedObjectWithLegacyTypesForArrayConvertsToArray()
        {
            ArrayOf<int> arr = [1, 2, 3];
            var v = new Variant(arr);
            object boxed = v.AsBoxedObject(Variant.BoxingBehavior.Legacy);
            Assert.That(boxed, Is.InstanceOf<int[]>());
        }

        [Test]
        public void SerializableVariantDefaultConstructor()
        {
            var sv = new SerializableVariant();
            Assert.That(sv.Value.IsNull, Is.True);
        }

        [Test]
        public void SerializableVariantValueConstructor()
        {
            var v = new Variant(42);
            var sv = new SerializableVariant(v);
            Assert.That((int)sv.Value, Is.EqualTo(42));
        }

        [Test]
        public void SerializableVariantGetValue()
        {
            var v = new Variant(42);
            var sv = new SerializableVariant(v);
            Assert.That(sv.GetValue(), Is.Not.Null);
        }

        [Test]
        public void SerializableVariantEqualsVariant()
        {
            var v = new Variant(42);
            var sv = new SerializableVariant(v);
            Assert.That(sv, Is.EqualTo(v));
        }

        [Test]
        public void SerializableVariantEqualsSerializableVariant()
        {
            var v = new Variant(42);
            var sv1 = new SerializableVariant(v);
            var sv2 = new SerializableVariant(v);
            Assert.That(sv1, Is.EqualTo(sv2));
        }

        [Test]
        public void SerializableVariantEqualsNull()
        {
            var sv = new SerializableVariant(new Variant(42));
#pragma warning disable CA1508 // Avoid dead conditional code
            Assert.That(sv, Is.Not.EqualTo((SerializableVariant)null));
#pragma warning restore CA1508 // Avoid dead conditional code
        }

        [Test]
        public void SerializableVariantEqualsObject()
        {
            var v = new Variant(42);
            var sv = new SerializableVariant(v);
            Assert.That(sv, Is.EqualTo((object)new SerializableVariant(v)));
            Assert.That(sv, Is.EqualTo((object)v));
            Assert.That(sv, Is.EqualTo((object)42));
        }

        [Test]
        public void SerializableVariantGetHashCode()
        {
            var sv = new SerializableVariant(new Variant(42));
            Assert.That(sv.GetHashCode(), Is.TypeOf<int>());
        }

        [Test]
        public void SerializableVariantEqualityOperator()
        {
            var sv1 = new SerializableVariant(new Variant(42));
            var sv2 = new SerializableVariant(new Variant(42));
            Assert.That(sv1, Is.EqualTo(sv2));
            Assert.That(sv1, Is.EqualTo(sv2));
        }

        [Test]
        public void SerializableVariantEqualityWithVariantOperator()
        {
            var sv = new SerializableVariant(new Variant(42));
            var v = new Variant(42);
            Assert.That(sv, Is.EqualTo(v));
            Assert.That(sv, Is.EqualTo(v));
        }

        [Test]
        public void SerializableVariantImplicitFromVariant()
        {
            SerializableVariant sv = new Variant(42);
            Assert.That((int)sv.Value, Is.EqualTo(42));
        }

        [Test]
        public void SerializableVariantImplicitToVariant()
        {
            Variant v = new SerializableVariant(new Variant(42));
            Assert.That((int)v, Is.EqualTo(42));
        }

        [Test]
        public void EqualsTypedOverloadForAllScalarTypes()
        {
            Assert.That(new Variant(true), Is.EqualTo(true));
            Assert.That(new Variant((sbyte)-1), Is.EqualTo((sbyte)-1));
            Assert.That(new Variant((byte)1), Is.EqualTo((byte)1));
            Assert.That(new Variant((short)-1), Is.EqualTo((short)-1));
            Assert.That(new Variant((ushort)1), Is.EqualTo((ushort)1));
            Assert.That(new Variant(42), Is.EqualTo(42));
            Assert.That(new Variant(42u), Is.EqualTo(42u));
            Assert.That(new Variant(42L), Is.EqualTo(42L));
            Assert.That(new Variant(42UL), Is.EqualTo(42UL));
            Assert.That(new Variant(1.5f), Is.EqualTo(1.5f));
            Assert.That(new Variant(2.5d), Is.EqualTo(2.5d));
            Assert.That(new Variant("test"), Is.EqualTo("test"));
        }

        [Test]
        public void EqualsTypedOverloadForComplexTypes()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 1), DateTimeKind.Utc);
            Assert.That(new Variant(dt), Is.EqualTo(dt));

            var guid = Uuid.NewUuid();
            Assert.That(new Variant(guid), Is.EqualTo(guid));

            var bs = ByteString.From(1, 2);
            Assert.That(new Variant(bs), Is.EqualTo(bs));

            XmlElement xml = CreateXmlElement("T");
            Assert.That(new Variant(xml), Is.EqualTo(xml));

            var nid = new NodeId(10, 1);
            Assert.That(new Variant(nid), Is.EqualTo(nid));

            var eni = ExpandedNodeId.Parse("nsu=T;s=A");
            Assert.That(new Variant(eni), Is.EqualTo(eni));

            var sc = new StatusCode(123u);
            Assert.That(new Variant(sc), Is.EqualTo(sc));

            var qn = new QualifiedName("q", 1);
            Assert.That(new Variant(qn), Is.EqualTo(qn));

            var lt = new LocalizedText("en", "t");
            Assert.That(new Variant(lt), Is.EqualTo(lt));
        }

        [Test]
        public void EqualsTypedOverloadForExtensionObjectAndDataValue()
        {
            var eo = new ExtensionObject(new Argument());
            Assert.That(new Variant(eo), Is.EqualTo(eo));

            var dv = new DataValue(42);
            Assert.That(new Variant(dv), Is.EqualTo(dv));
        }

        [Test]
        public void EqualsFloatNanValues()
        {
            var v = new Variant(float.NaN);
            Assert.That(v, Is.EqualTo(float.NaN));
        }

        [Test]
        public void EqualsDoubleNanValues()
        {
            var v = new Variant(double.NaN);
            Assert.That(v, Is.EqualTo(double.NaN));
        }

        [Test]
        public void EqualsTypedOverloadReturnsFalseForTypeMismatch()
        {
            var v = new Variant("hello");
            Assert.That(v, Is.Not.EqualTo(42));
        }

        [Test]
        public void EqualsArrayOfBoolOverload()
        {
            ArrayOf<bool> arr = [true, false];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfSByteOverload()
        {
            ArrayOf<sbyte> arr = [-1, 1];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfByteOverload()
        {
            ArrayOf<byte> arr = [1, 2];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfInt16Overload()
        {
            ArrayOf<short> arr = [-1, 1];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfUInt16Overload()
        {
            ArrayOf<ushort> arr = [1, 2];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfInt32Overload()
        {
            ArrayOf<int> arr = [-1, 1];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfUInt32Overload()
        {
            ArrayOf<uint> arr = [1u, 2u];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfInt64Overload()
        {
            ArrayOf<long> arr = [-1L, 1L];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfUInt64Overload()
        {
            ArrayOf<ulong> arr = [1UL, 2UL];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfFloatOverload()
        {
            ArrayOf<float> arr = [1.0f, 2.0f];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfDoubleOverload()
        {
            ArrayOf<double> arr = [1.0, 2.0];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfStringOverload()
        {
            ArrayOf<string> arr = ["a", "b"];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfComplexTypes()
        {
            var dt = (DateTimeUtc)DateTime.SpecifyKind(new DateTime(2024, 1, 1), DateTimeKind.Utc);
            Assert.That(new Variant([dt]), Is.EqualTo((ArrayOf<DateTimeUtc>)[dt]));

            var u = Uuid.NewUuid();
            Assert.That(new Variant([u]), Is.EqualTo((ArrayOf<Uuid>)[u]));

            Assert.That(new Variant([ByteString.From(1)]), Is.EqualTo((ArrayOf<ByteString>)[ByteString.From(1)]));

            var nid = new NodeId(1);
            Assert.That(new Variant([nid]), Is.EqualTo((ArrayOf<NodeId>)[nid]));

            var sc = new StatusCode(1u);
            Assert.That(new Variant([sc]), Is.EqualTo((ArrayOf<StatusCode>)[sc]));

            var qn = new QualifiedName("q");
            Assert.That(new Variant([qn]), Is.EqualTo((ArrayOf<QualifiedName>)[qn]));

            var lt = new LocalizedText("en", "t");
            Assert.That(new Variant([lt]), Is.EqualTo((ArrayOf<LocalizedText>)[lt]));
        }

        [Test]
        public void EqualsArrayOfExtensionObjectOverload()
        {
            var eo = new ExtensionObject(new Argument());
            ArrayOf<ExtensionObject> arr = [eo];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfDataValueOverload()
        {
            ArrayOf<DataValue> arr = [new DataValue(1)];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsArrayOfVariantOverload()
        {
            ArrayOf<Variant> arr = [new Variant(1), new Variant("x")];
            var v = new Variant(arr);
            Assert.That(v, Is.EqualTo(arr));
        }

        [Test]
        public void EqualsVariantBothNullReturnsTrue()
        {
            Variant a = default;
            Variant b = default;
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void EqualsVariantSameIntValue()
        {
            var a = new Variant(42);
            var b = new Variant(42);
            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void EqualsVariantDifferentTypes()
        {
            var a = new Variant(42);
            var b = new Variant("42");
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void EqualsObjectWithBoxedValue()
        {
            var v = new Variant(42);
            Assert.That(v, Is.EqualTo((object)42));
        }

        [Test]
        public void ExplicitCastFromWrongTypeThrowsInvalidCastException()
        {
            var v = new Variant("not a bool");
            Assert.Throws<InvalidCastException>(() => { bool _ = (bool)v; });
        }

        [Test]
        public void ConvertToBooleanFromUnsupportedTypeThrows()
        {
            var v = new Variant(Uuid.NewUuid());
            Assert.Throws<InvalidCastException>(() => v.ConvertToBoolean());
        }

        [Test]
        public void VariantIsNullForDefaultVariant()
        {
            Variant v = default;
            Assert.That(v.IsNull, Is.True);
        }

        [Test]
        public void VariantIsNotNullForValueVariant()
        {
            var v = new Variant(42);
            Assert.That(v.IsNull, Is.False);
        }

        [Test]
        public void VariantNullStaticField()
        {
            Assert.That(Variant.Null.IsNull, Is.True);
        }

        [Test]
        public void ImplicitFromArrayOfBoolCreatesVariant()
        {
            Variant v = (ArrayOf<bool>)[true, false];
            Assert.That(v.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Boolean));
            Assert.That(v.TypeInfo.IsArray, Is.True);
        }

        private static XmlElement CreateXmlElement(string name)
        {
            var document = new XmlDocument();
            System.Xml.XmlElement element = document.CreateElement(name);
            element.InnerText = name;
            return (XmlElement)element;
        }
    }
}
