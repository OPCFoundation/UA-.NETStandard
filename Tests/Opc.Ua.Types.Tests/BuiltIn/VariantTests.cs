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

namespace Opc.Ua.Types.Tests.BuiltIn
{
    /// <summary>
    /// Comprehensive tests for the Variant built-in type.
    /// </summary>
    [TestFixture]
    [Category("BuiltInType")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class VariantTests
    {
        private enum EnumValue
        {
            Zero = 0,
            One = 1,
            Two = 2
        }

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
                yield return CreateConstructorCase("ScalarBoolean",
                    () => true, TypeInfo.Scalars.Boolean);
                yield return CreateConstructorCase("ScalarSByte",
                    () => (sbyte)-1, TypeInfo.Scalars.SByte);
                yield return CreateConstructorCase("ScalarByte",
                    () => (byte)1, TypeInfo.Scalars.Byte);
                yield return CreateConstructorCase("ScalarInt16",
                    () => (short)-2, TypeInfo.Scalars.Int16);
                yield return CreateConstructorCase("ScalarUInt16",
                    () => (ushort)2, TypeInfo.Scalars.UInt16);
                yield return CreateConstructorCase("ScalarInt32",
                    () => -3, TypeInfo.Scalars.Int32);
                yield return CreateConstructorCase("ScalarUInt32",
                    () => 3u, TypeInfo.Scalars.UInt32);
                yield return CreateConstructorCase("ScalarInt64",
                    () => -4L, TypeInfo.Scalars.Int64);
                yield return CreateConstructorCase("ScalarUInt64",
                    () => 4UL, TypeInfo.Scalars.UInt64);
                yield return CreateConstructorCase("ScalarFloat",
                    () => 1.25f, TypeInfo.Scalars.Float);
                yield return CreateConstructorCase("ScalarDouble",
                    () => 2.25d, TypeInfo.Scalars.Double);
                yield return CreateConstructorCase("ScalarString",
                    () => "opc", TypeInfo.Scalars.String);
                yield return CreateConstructorCase("ScalarDateTime",
                    () => DateTime.SpecifyKind(new DateTime(2024, 1, 2, 3, 4, 5), DateTimeKind.Utc), TypeInfo.Scalars.DateTime);
                yield return CreateConstructorCase("ScalarGuid",
                    () => Uuid.NewUuid(), TypeInfo.Scalars.Guid);
                yield return CreateConstructorCase("ScalarByteString",
                    () => Bytes(1, 2, 3), TypeInfo.Scalars.ByteString);
                yield return CreateConstructorCase("ScalarXmlElement",
                    () => CreateXmlElement("Scalar"), TypeInfo.Scalars.XmlElement);
                yield return CreateConstructorCase("ScalarNodeId",
                    () => new NodeId(10, 1), TypeInfo.Scalars.NodeId);
                yield return CreateConstructorCase("ScalarExpandedNodeId",
                    () => ExpandedNodeId.Parse("nsu=Test;s=Node"), TypeInfo.Scalars.ExpandedNodeId);
                yield return CreateConstructorCase("ScalarStatusCode",
                    () => new StatusCode(123u), TypeInfo.Scalars.StatusCode);
                yield return CreateConstructorCase("ScalarQualifiedName",
                    () => new QualifiedName("name", 2), TypeInfo.Scalars.QualifiedName);
                yield return CreateConstructorCase("ScalarLocalizedText",
                    () => new LocalizedText("en", "text"), TypeInfo.Scalars.LocalizedText);
                yield return CreateConstructorCase("ScalarExtensionObject",
                    () => new ExtensionObject(new Argument()), TypeInfo.Scalars.ExtensionObject);
                yield return CreateConstructorCase("ScalarDataValue",
                    () => new DataValue(5), TypeInfo.Scalars.DataValue);
            }
        }

        private static IEnumerable<TestCaseData> ArrayConstructorCases
        {
            get
            {
                yield return CreateConstructorCase("ArrayBoolean",
                    () => Array(true, false), TypeInfo.Arrays.Boolean);
                yield return CreateConstructorCase("ArraySByte",
                    () => Array((sbyte)-1, (sbyte)1), TypeInfo.Arrays.SByte);
                yield return CreateConstructorCase("ArrayInt16",
                    () => Array((short)-2, (short)2), TypeInfo.Arrays.Int16);
                yield return CreateConstructorCase("ArrayUInt16",
                    () => Array((ushort)2, (ushort)4), TypeInfo.Arrays.UInt16);
                yield return CreateConstructorCase("ArrayInt32",
                    () => Array(-3, 3), TypeInfo.Arrays.Int32);
                yield return CreateConstructorCase("ArrayUInt32",
                    () => Array(3u, 4u), TypeInfo.Arrays.UInt32);
                yield return CreateConstructorCase("ArrayInt64",
                    () => Array(-4L, 4L), TypeInfo.Arrays.Int64);
                yield return CreateConstructorCase("ArrayUInt64",
                    () => Array(4UL, 5UL), TypeInfo.Arrays.UInt64);
                yield return CreateConstructorCase("ArrayFloat",
                    () => Array(1.0f, 2.0f), TypeInfo.Arrays.Float);
                yield return CreateConstructorCase("ArrayDouble",
                    () => Array(1.0d, 2.0d), TypeInfo.Arrays.Double);
                yield return CreateConstructorCase("ArrayString",
                    () => Array("a", "b"), TypeInfo.Arrays.String);
                yield return CreateConstructorCase("ArrayDateTime",
                    () => Array(
                        DateTime.SpecifyKind(new DateTime(2024, 2, 1), DateTimeKind.Utc),
                        DateTime.SpecifyKind(new DateTime(2025, 2, 1), DateTimeKind.Utc)), TypeInfo.Arrays.DateTime);
                yield return CreateConstructorCase("ArrayGuid",
                    () => Array(
                        new Uuid(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee1")),
                        new Uuid(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee2"))), TypeInfo.Arrays.Guid);
                yield return CreateConstructorCase("ArrayByteString",
                    () => Array(Bytes(1), Bytes(2, 3)), TypeInfo.Arrays.ByteString);
                yield return CreateConstructorCase("ArrayXmlElement",
                    () => Array(CreateXmlElement("A"), CreateXmlElement("B")), TypeInfo.Arrays.XmlElement);
                yield return CreateConstructorCase("ArrayNodeId",
                    () => Array(new NodeId(1), new NodeId(2, 1)), TypeInfo.Arrays.NodeId);
                yield return CreateConstructorCase("ArrayExpandedNodeId",
                    () => Array(
                        ExpandedNodeId.Parse("nsu=Test;s=One"),
                        ExpandedNodeId.Parse("nsu=Test;s=Two")), TypeInfo.Arrays.ExpandedNodeId);
                yield return CreateConstructorCase("ArrayStatusCode",
                    () => Array(
                        new StatusCode(1u),
                        new StatusCode(2u)), TypeInfo.Arrays.StatusCode);
                yield return CreateConstructorCase("ArrayQualifiedName",
                    () => Array(
                        new QualifiedName("q1", 1),
                        new QualifiedName("q2", 2)), TypeInfo.Arrays.QualifiedName);
                yield return CreateConstructorCase("ArrayLocalizedText",
                    () => Array(
                        new LocalizedText("en", "a"),
                        new LocalizedText("de", "b")), TypeInfo.Arrays.LocalizedText);
                yield return CreateConstructorCase("ArrayExtensionObject",
                    () => Array(
                        new ExtensionObject(new Argument()),
                        new ExtensionObject(new Argument())), TypeInfo.Arrays.ExtensionObject);
                yield return CreateConstructorCase("ArrayDataValue",
                    () => Array(new DataValue(1), new DataValue(2)), TypeInfo.Arrays.DataValue);
                yield return CreateConstructorCase("ArrayVariant",
                    () => Array(new Variant(1), new Variant("two")), TypeInfo.Arrays.Variant);
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
                        () => DateTime.SpecifyKind(new DateTime(2024, 1, 2), DateTimeKind.Utc),
                        typeof(DateTime),
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
                        () => Bytes(1, 2),
                        typeof(byte[]),
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
                        () => Array(true, false),
                        typeof(bool[]),
                        TypeInfo.Arrays.Boolean,
                        nameof(Variant.GetBooleanArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArraySByte",
                        () => Array((sbyte)-1, (sbyte)1),
                        typeof(sbyte[]),
                        TypeInfo.Arrays.SByte,
                        nameof(Variant.GetSByteArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayInt16",
                        () => Array((short)-2, (short)2),
                        typeof(short[]),
                        TypeInfo.Arrays.Int16,
                        nameof(Variant.GetInt16Array)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayUInt16",
                        () => Array((ushort)2, (ushort)3),
                        typeof(ushort[]),
                        TypeInfo.Arrays.UInt16,
                        nameof(Variant.GetUInt16Array)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayInt32",
                        () => Array(-3, 3),
                        typeof(int[]),
                        TypeInfo.Arrays.Int32,
                        nameof(Variant.GetInt32Array)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayUInt32",
                        () => Array(3u, 4u),
                        typeof(uint[]),
                        TypeInfo.Arrays.UInt32,
                        nameof(Variant.GetUInt32Array)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayInt64",
                        () => Array(-4L, 4L),
                        typeof(long[]),
                        TypeInfo.Arrays.Int64,
                        nameof(Variant.GetInt64Array)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayUInt64",
                        () => Array(4UL, 5UL),
                        typeof(ulong[]),
                        TypeInfo.Arrays.UInt64,
                        nameof(Variant.GetUInt64Array)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayFloat",
                        () => Array(1.0f, 2.0f),
                        typeof(float[]),
                        TypeInfo.Arrays.Float,
                        nameof(Variant.GetFloatArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayDouble",
                        () => Array(1.0d, 2.0d),
                        typeof(double[]),
                        TypeInfo.Arrays.Double,
                        nameof(Variant.GetDoubleArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayString",
                        () => Array("a", "b"),
                        typeof(string[]),
                        TypeInfo.Arrays.String,
                        nameof(Variant.GetStringArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayDateTime",
                        () => Array(
                            DateTime.SpecifyKind(new DateTime(2024, 2, 1), DateTimeKind.Utc),
                            DateTime.SpecifyKind(new DateTime(2025, 2, 1), DateTimeKind.Utc)),
                        typeof(DateTime[]),
                        TypeInfo.Arrays.DateTime,
                        nameof(Variant.GetDateTimeArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayGuid",
                        () => Array(
                            new Uuid(Guid.Parse("bbbbbbbb-cccc-dddd-eeee-fffffffffff1")),
                            new Uuid(Guid.Parse("bbbbbbbb-cccc-dddd-eeee-fffffffffff2"))),
                        typeof(Uuid[]),
                        TypeInfo.Arrays.Guid,
                        nameof(Variant.GetGuidArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayByteString",
                        () => Array(Bytes(1), Bytes(2)),
                        typeof(byte[][]),
                        TypeInfo.Arrays.ByteString,
                        nameof(Variant.GetByteStringArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayXmlElement",
                        () => Array(CreateXmlElement("A"), CreateXmlElement("B")),
                        typeof(XmlElement[]),
                        TypeInfo.Arrays.XmlElement,
                        nameof(Variant.GetXmlElementArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayNodeId",
                        () => Array(new NodeId(1), new NodeId(2, 1)),
                        typeof(NodeId[]),
                        TypeInfo.Arrays.NodeId,
                        nameof(Variant.GetNodeIdArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayExpandedNodeId",
                        () => Array(ExpandedNodeId.Parse("nsu=Test;s=One"), ExpandedNodeId.Parse("nsu=Test;s=Two")),
                        typeof(ExpandedNodeId[]),
                        TypeInfo.Arrays.ExpandedNodeId,
                        nameof(Variant.GetExpandedNodeIdArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayStatusCode",
                        () => Array(new StatusCode(1u), new StatusCode(2u)),
                        typeof(StatusCode[]),
                        TypeInfo.Arrays.StatusCode,
                        nameof(Variant.GetStatusCodeArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayQualifiedName",
                        () => Array(new QualifiedName("q1", 1), new QualifiedName("q2", 2)),
                        typeof(QualifiedName[]),
                        TypeInfo.Arrays.QualifiedName,
                        nameof(Variant.GetQualifiedNameArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayLocalizedText",
                        () => Array(new LocalizedText("en", "a"), new LocalizedText("de", "b")),
                        typeof(LocalizedText[]),
                        TypeInfo.Arrays.LocalizedText,
                        nameof(Variant.GetLocalizedTextArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayExtensionObject",
                        () => Array(new ExtensionObject(new Argument()), new ExtensionObject(new Argument())),
                        typeof(ExtensionObject[]),
                        TypeInfo.Arrays.ExtensionObject,
                        nameof(Variant.GetExtensionObjectArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayDataValue",
                        () => Array(new DataValue(1), new DataValue(2)),
                        typeof(DataValue[]),
                        TypeInfo.Arrays.DataValue,
                        nameof(Variant.GetDataValueArray)));
                yield return CreateDescriptorCase(
                    new VariantDescriptor(
                        "ArrayVariant",
                        () => Array(new Variant(1), new Variant("two")),
                        typeof(Variant[]),
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
            Assert.That(variant.AsBoxedObject(), Is.TypeOf<Matrix>());
            var matrix = (Matrix)variant.Value;
            Assert.That(data.Cast<int>().ToArray(), Is.EquivalentTo(matrix.Elements.Cast<int>().ToArray()));
        }

        [Test]
        public void MatrixConstructor_PreservesMatrixTypeInfo()
        {
            var matrix = new Matrix(Array(1, 2, 3, 4), BuiltInType.Int32, 2, 2);
            var variant = new Variant(matrix);

            AssertTypeInfo(matrix.TypeInfo, variant.TypeInfo);
            Assert.That(variant.Value, Is.SameAs(matrix));
        }

        [Test]
        public void ObjectConstructorWithTypeInfo_CoercesEnumerationValue()
        {
            const EnumValue value = EnumValue.Two;
            var variant = new Variant(value, TypeInfo.Scalars.Enumeration);

            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(variant.GetInt32(), Is.EqualTo(Convert.ToInt32(value, CultureInfo.InvariantCulture)));
            Assert.That(variant.GetEnumeration<EnumValue>(), Is.EqualTo(value));
            Assert.That(variant.Value, Is.EqualTo(value));
        }

        [Test]
        public void ArrayConstructorWithTypeInfo_CoercesEnumerationArray()
        {
            EnumValue[] values = [EnumValue.Zero, EnumValue.One];
            var typeInfo = TypeInfo.Create(BuiltInType.Enumeration, ValueRanks.OneDimension);
            var variant = new Variant(values, typeInfo);

            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(variant.Value, Is.EqualTo(values));
            Assert.That(variant.GetEnumerationArray<EnumValue>(), Is.EqualTo(values));
            Assert.That(variant.GetInt32Array(), Is.EqualTo(
                values.Select(v => Convert.ToInt32(v, CultureInfo.InvariantCulture)).ToArray()));
        }

        [Test]
        public void FromEnumeration_CoercesInt32Value()
        {
            const EnumValue value = EnumValue.Two;
            var variant = Variant.From(value);

            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(variant.GetInt32(), Is.EqualTo(Convert.ToInt32(value, CultureInfo.InvariantCulture)));
            Assert.That(variant.GetEnumeration<EnumValue>(), Is.EqualTo(value));
            Assert.That(variant.Value, Is.EqualTo(value));
        }

        [Test]
        public void EnumArrayConstructorWithTypeInfo_CoercesEnumerationArray()
        {
            EnumValue[] values = [EnumValue.Zero, EnumValue.One];
            var variant = Variant.From(values);

            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.Enumeration));
            Assert.That(variant.Value, Is.EqualTo(values));
            Assert.That(variant.GetEnumerationArray<EnumValue>(), Is.EqualTo(values));
            Assert.That(variant.GetInt32Array(), Is.EqualTo(
                values.Select(v => Convert.ToInt32(v, CultureInfo.InvariantCulture)).ToArray()));
        }

        [Test]
        public void VariantConstructsArrayFromEnumerable()
        {
            IList source = new List<int> { 1, 2, 3 };
            var typeInfo = TypeInfo.Create(BuiltInType.Int32, ValueRanks.OneDimension);
            var variant = new Variant(source, typeInfo);

            AssertTypeInfo(typeInfo, variant.TypeInfo);
            Assert.That(source, Is.EquivalentTo((int[])variant.Value));
        }

        [Test]
        [TestCaseSource(nameof(ArrayDescriptorCases))]
        public void TryGetArray_Succeeds(VariantDescriptor descriptor)
        {
            var values = (Array)descriptor.CreateValue();
            var variant = new Variant(values);
            MethodInfo method = typeof(Variant).GetMethod(nameof(Variant.TryGet), Array(descriptor.ValueType.MakeByRefType()));
            object[] args = Array(CreateDefaultValue(descriptor.ValueType));

            Assert.That(method, Is.Not.Null, $"TryGet overload for {descriptor.Name} should exist");
            bool success = (bool)method.Invoke(variant, args);

            Assert.That(success, Is.True);
            AssertValueEquality(values, args[0]);
        }

        [Test]
        [TestCaseSource(nameof(ScalarDescriptorCases))]
        public void GetScalar_ReturnsStoredValue(VariantDescriptor descriptor)
        {
            object value = descriptor.CreateValue();
            var variant = new Variant(value);
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
            var values = (Array)descriptor.CreateValue();
            var variant = new Variant(values);
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
            var variant = new Variant(value);
            MethodInfo method = typeof(Variant).GetMethods().FirstOrDefault(m =>
            {
                if (m.Name != nameof(Variant.TryGet))
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
            Assert.That(method, Is.Not.Null, $"TryGet Method with {descriptor.ValueType} not found");
            object[] args = new object[1];
            bool success = (bool)method.Invoke(variant, args);
            Assert.That(success, Is.True);
            AssertValueEquality(value, args[0]);
        }

        [Test]
        [TestCaseSource(nameof(ArrayDescriptorCases))]
        public void GenericTryGetArray_Succeeds(VariantDescriptor descriptor)
        {
            var values = (Array)descriptor.CreateValue();
            var variant = new Variant(values);
            Type elementType = descriptor.ValueType.GetElementType() ?? descriptor.ValueType;
            MethodInfo method = typeof(Variant).GetMethod(nameof(Variant.TryGetArray))
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
            Assert.That(variant.TryGet(out string _), Is.False);
        }

        [Test]
        public void GenericTryGetArray_FailsForWrongBuiltInType()
        {
            var variant = new Variant(Array(1, 2));
            MethodInfo method = typeof(Variant).GetMethod(nameof(Variant.TryGetArray))
                .MakeGenericMethod(typeof(int));
            object[] args = Array<object>(null, BuiltInType.String);

            bool success = (bool)method.Invoke(variant, args);
            Assert.That(success, Is.False);
        }

        [Test]
        public void TryGetMatrix_ReturnsMatrix()
        {
            var matrix = new Matrix(Array(1f, 2f, 3f, 4f), BuiltInType.Float, 2, 2);
            var variant = new Variant(matrix);
            object[] args = Array<object>(null, BuiltInType.Float);
            MethodInfo method = typeof(Variant).GetMethod(nameof(Variant.TryGetMatrix))
                .MakeGenericMethod(typeof(float));

            bool success = (bool)method.Invoke(variant, args);
            Assert.That(success, Is.True);
            Assert.That(args[0], Is.SameAs(matrix));
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
            MethodInfo method = typeof(Variant).GetMethod(nameof(Variant.TryGetMatrix))
                .MakeGenericMethod(typeof(double));

            bool success = (bool)method.Invoke(variant, args);
            Assert.That(success, Is.True);
            Assert.That(args[0], Is.TypeOf<Matrix>());
        }

        [Test]
        public void VariantEqualsVariant_UsesValueSemantics()
        {
            var first = new Variant(Array(1, 2));
            var second = new Variant(Array(1, 2));
            var third = new Variant(Array(1, 3));

            Assert.That(first.Equals(second), Is.True);
            Assert.That(first.Equals(third), Is.False);
        }

        [Test]
        public void VariantEqualsVariant_DetectsTypeMismatch()
        {
            var scalar = new Variant(1);
            var floating = new Variant(1.0f);

            Assert.That(scalar.Equals(floating), Is.False);
        }

        [Test]
        public void VariantEqualsObject_HandlesNullAndMismatch()
        {
            var variant = new Variant("value");

            Assert.That(variant.Equals((object)"value"), Is.True);
            Assert.That(variant.Equals((object)"other"), Is.False);
            Assert.That(Variant.Null.Equals((object)null), Is.True);
        }

        [Test]
        public void EqualityOperatorWithVariantOperands()
        {
            var left = new Variant(Array(true, false));
            var identical = new Variant(Array(true, false));
            var different = new Variant(Array(false, false));

            Assert.That(left == identical, Is.True);
            Assert.That(left != identical, Is.False);
            Assert.That(left == different, Is.False);
            Assert.That(left != different, Is.True);
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

            Assert.That(variant.TryGet(out StatusCode status), Is.True);
            Assert.That(status, Is.EqualTo(new StatusCode(123u)));
        }

        [Test]
        public void AsBoxedObjectReturnsDefaultForReferenceTypes()
        {
            var variant = new Variant(null, TypeInfo.Scalars.NodeId);

            Assert.That(variant.AsBoxedObject(), Is.EqualTo(NodeId.Null));
        }

        [Test]
        public void VariantNullBehavesAsExpected()
        {
            Assert.That(Variant.Null.IsNull, Is.True);
            Assert.That(Variant.Null.Value, Is.Null);
            Assert.That(Variant.Null.GetHashCode(), Is.EqualTo(0));
        }

        [Test]
        public void GetHashCodeMatchesUnderlyingValue()
        {
            var variant = new Variant(42);

            Assert.That(variant.Value.GetHashCode(), Is.EqualTo(variant.GetHashCode()));
        }

        [Test]
        public void ToStringFormatsByteStringAsHex()
        {
            var variant = new Variant(ByteString.From(Bytes(0x0A, 0xFF)));

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

            Assert.That(variant.ToString(), Is.EqualTo("{1|2|3}"));
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
            AssertValueEquality(value, variant.Value);
        }

        [TestCaseSource(nameof(ArrayDescriptorCases))]
        public void VariantFromArrayProducesEquivalentVariant(VariantDescriptor descriptor)
        {
            object value = descriptor.CreateValue();
            Variant variant = InvokeVariantFrom(value);

            AssertTypeInfo(descriptor.TypeInfo, variant.TypeInfo);
            AssertValueEquality(value, variant.Value);
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
            uint[] statusCodeValues =
            [
                (uint)StatusCodes.Good,
                (uint)StatusCodes.BadNodeIdInvalid,
                (uint)StatusCodes.BadUnexpectedError,
                (uint)StatusCodes.BadAttributeIdInvalid
            ];

            var variant = new Variant(statusCodeValues, TypeInfo.Arrays.StatusCode);

            Assert.That(variant.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
            Assert.That(variant.Value, Is.Not.Null);
            Assert.That(variant.Value, Is.InstanceOf<StatusCode[]>());

            // Cast the Value to StatusCode array
            var statusCodes = (StatusCode[])variant.Value;
            Assert.That(statusCodes.Length, Is.EqualTo(statusCodeValues.Length));

            for (int i = 0; i < statusCodeValues.Length; i++)
            {
                Assert.That(statusCodes[i].Code, Is.EqualTo(statusCodeValues[i]));
            }

            // Test empty array
            uint[] emptyArray = [];
            var variant2 = new Variant(emptyArray, TypeInfo.Arrays.StatusCode);

            Assert.That(variant2.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
            var emptyStatusCodes = (StatusCode[])variant2.Value;
            Assert.That(emptyStatusCodes.Length, Is.EqualTo(0));

            // Test single element array
            uint[] singleElement = [(uint)StatusCodes.BadNodeIdInvalid];
            var variant3 = new Variant(singleElement, TypeInfo.Arrays.StatusCode);

            Assert.That(variant3.TypeInfo.BuiltInType, Is.EqualTo(BuiltInType.StatusCode));
            var singleStatusCode = (StatusCode[])variant3.Value;
            Assert.That(singleStatusCode.Length, Is.EqualTo(1));
            Assert.That(singleStatusCode[0].Code, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
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

        private static byte[] Bytes(params byte[] values)
        {
            return values;
        }

        private static TestCaseData CreateConstructorCase(
            string name,
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

        private static XmlElement CreateXmlElement(string name)
        {
            var document = new XmlDocument();
            System.Xml.XmlElement element = document.CreateElement(name);
            element.InnerText = name;
            return (XmlElement)element;
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
                Assert.That(actualArray.Length, Is.EqualTo(expectedArray.Length), "Array lengths differ");
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
    }
}
