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
using System.Buffers;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Tests;
using Opc.Ua.Types;

namespace Opc.Ua.UnitTests
{
    /// <summary>
    /// Unit tests for the <see cref = "JsonParser"/> class.
    /// </summary>
    [TestFixture]
    public class JsonEncodeDecodeTests
    {
        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void WriteAndReadBoolean(bool value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            bool expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteBoolean(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            bool result = reader.ReadBoolean(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(true, 0)]
        [TestCase(false, 4)]
        [TestCase(true, 100)]
        public void WriteAndReadBooleanArray(bool value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteBooleanArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<bool> result = reader.ReadBooleanArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(true, 0)]
        [TestCase(false, 4)]
        [TestCase(true, 100)]
        public void WriteAndReadBooleanValuesVariant(bool value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public void WriteAndReadBooleanVariant(bool value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0)]
        [TestCase(0x3)]
        [TestCase(byte.MaxValue)]
        public void WriteAndReadByte(byte value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            byte expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteByte(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            byte result = reader.ReadByte(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.ByteStrings))]
        public void WriteAndReadByteString(ByteString value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            ByteString expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteByteString(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ByteString result = reader.ReadByteString(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.ByteStringValues))]
        public void WriteAndReadByteStringArray(ByteString value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteByteStringArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<ByteString> result = reader.ReadByteStringArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.ByteStringValues))]
        public void WriteAndReadByteStringValuesVariant(ByteString value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.ByteStrings))]
        public void WriteAndReadByteStringVariant(ByteString value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0, 0)]
        [TestCase(0, 3)]
        [TestCase(0x3, 1)]
        [TestCase(0x3, 100)]
        [TestCase(byte.MaxValue, 5)]
        public void WriteAndReadByteArray(byte value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteByteArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<byte> result = reader.ReadByteArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0, 0)]
        [TestCase(0, 3)]
        [TestCase(0x3, 1)]
        [TestCase(0x3, 100)]
        [TestCase(byte.MaxValue, 5)]
        public void WriteAndReadByteValuesVariant(byte value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArrayOf());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0)]
        [TestCase(0x3)]
        [TestCase(byte.MaxValue)]
        public void WriteAndReadByteVariant(byte value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.DataValues))]
        public void WriteAndReadDataValue(DataValue value)
        {
            TestWriteAndReadDataValue(in value);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.DataValueValues))]
        public void WriteAndReadDataValueArray(DataValue value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteDataValueArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<DataValue> result = reader.ReadDataValueArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.DataValueValues))]
        public void WriteAndReadDataValueValuesVariant(DataValue value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.DataValues))]
        public void WriteAndReadDataValueVariant(DataValue value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        public void WriteAndReadDataValueWithInt()
        {
            var expected = new DataValue(new Variant(12345));
            TestWriteAndReadDataValue(in expected);
        }

        [Test]
        public void WriteAndReadDataValueWithPicoseconds()
        {
            var expected = new DataValue(new Variant(12345), StatusCodes.Good, DateTime.UtcNow, DateTime.UtcNow)
            {
                ServerPicoseconds = 323,
                SourcePicoseconds = 232
            };
            TestWriteAndReadDataValue(in expected);
        }

        [Test]
        public void WriteAndReadDataValueWithString()
        {
            var expected = new DataValue(new Variant("TestTestTestTest" + Uuid.NewUuid()));
            TestWriteAndReadDataValue(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.DateTimes))]
        public void WriteAndReadDateTime(DateTime value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            DateTime expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteDateTime(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            DateTime result = reader.ReadDateTime(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.DateTimeValues))]
        public void WriteAndReadDateTimeArray(DateTime value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteDateTimeArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<DateTime> result = reader.ReadDateTimeArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.DateTimeValues))]
        public void WriteAndReadDateTimeValuesVariant(DateTime value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.DateTimes))]
        public void WriteAndReadDateTimeVariant(DateTime value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.DiagnosticInfos))]
        public void WriteAndReadDiagnosticInfo(DiagnosticInfo value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            DiagnosticInfo expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteDiagnosticInfo(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            DiagnosticInfo result = reader.ReadDiagnosticInfo(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.DiagnosticInfoValues))]
        public void WriteAndReadDiagnosticInfoArray(DiagnosticInfo value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteDiagnosticInfoArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<DiagnosticInfo> result = reader.ReadDiagnosticInfoArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0.0)]
        [TestCase(3.0)]
        [TestCase(3.3)]
        [TestCase(double.MaxValue)]
        [TestCase(-123.3)]
        [TestCase(double.MinValue)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(double.NaN)]
        public void WriteAndReadDouble(double value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            double expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteDouble(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            double result = reader.ReadDouble(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0.0, 0)]
        [TestCase(0.0, 3)]
        [TestCase(4.1, 1)]
        [TestCase(double.MaxValue, 5)]
        [TestCase(-123.0, 2)]
        [TestCase(double.MinValue, 1000)]
        [TestCase(4.1, 100)]
        [TestCase(double.PositiveInfinity, 3)]
        [TestCase(double.NegativeInfinity, 6)]
        [TestCase(double.NaN, 66)]
        public void WriteAndReadDoubleArray(double value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteDoubleArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<double> result = reader.ReadDoubleArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0.0, 0)]
        [TestCase(0.0, 3)]
        [TestCase(4.1, 1)]
        [TestCase(double.MaxValue, 5)]
        [TestCase(-123.0, 2)]
        [TestCase(double.MinValue, 1000)]
        [TestCase(4.1, 100)]
        [TestCase(double.PositiveInfinity, 3)]
        [TestCase(double.NegativeInfinity, 6)]
        [TestCase(double.NaN, 66)]
        public void WriteAndReadDoubleValuesVariant(double value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0.0)]
        [TestCase(3.0)]
        [TestCase(3.3)]
        [TestCase(double.MaxValue)]
        [TestCase(-123.3)]
        [TestCase(double.MinValue)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(double.NaN)]
        public void WriteAndReadDoubleVariant(double value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(StructureType.Structure)]
        [TestCase(StructureType.StructureWithOptionalFields)]
        public void WriteAndReadEnumerated(StructureType value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            StructureType expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteEnumerated(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            StructureType result = reader.ReadEnumerated<StructureType>(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(StructureType.Structure, 0)]
        [TestCase(StructureType.Structure, 1000)]
        [TestCase(StructureType.StructureWithOptionalFields, 4)]
        public void WriteAndReadEnumeratedArrayVerbose(StructureType value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext, JsonEncoderOptions.Verbose))
            {
                writer.WriteEnumeratedArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<StructureType> result = reader.ReadEnumeratedArray<StructureType>(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(StructureType.Structure, 0)]
        [TestCase(StructureType.Structure, 1000)]
        [TestCase(StructureType.StructureWithOptionalFields, 4)]
        public void WriteAndReadEnumeratedArrayCompact(StructureType value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext, JsonEncoderOptions.Compact))
            {
                writer.WriteEnumeratedArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<StructureType> result = reader.ReadEnumeratedArray<StructureType>(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(StructureType.Structure, 0)]
        [TestCase(StructureType.Structure, 1000)]
        [TestCase(StructureType.StructureWithOptionalFields, 4)]
        public void WriteAndReadEnumeratedValuesVariant(StructureType value, int length)
        {
            var expected = Variant.From(Enumerable.Repeat(value, length).ToArrayOf());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(StructureType.Structure)]
        [TestCase(StructureType.StructureWithOptionalFields)]
        public void WriteAndReadEnumeratedVariant(StructureType value)
        {
            var expected = Variant.From(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.ExpandedNodeIds))]
        public void WriteAndReadExpandedNodeId(ExpandedNodeId value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            ExpandedNodeId expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteExpandedNodeId(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ExpandedNodeId result = reader.ReadExpandedNodeId(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.ExpandedNodeIdValues))]
        public void WriteAndReadExpandedNodeIdArray(ExpandedNodeId value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteExpandedNodeIdArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<ExpandedNodeId> result = reader.ReadExpandedNodeIdArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.ExpandedNodeIdValues))]
        public void WriteAndReadExpandedNodeIdValuesVariant(ExpandedNodeId value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.ExpandedNodeIds))]
        public void WriteAndReadExpandedNodeIdVariant(ExpandedNodeId value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.ExtensionObjects))]
        public void WriteAndReadExtensionObject(ExtensionObject value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            ExtensionObject expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteExtensionObject(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ExtensionObject result = reader.ReadExtensionObject(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.ExtensionObjectValues))]
        public void WriteAndReadExtensionObjectArray(ExtensionObject value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteExtensionObjectArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<ExtensionObject> result = reader.ReadExtensionObjectArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.ExtensionObjectValues))]
        public void WriteAndReadExtensionObjectValuesVariant(ExtensionObject value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.ExtensionObjects))]
        public void WriteAndReadExtensionObjectVariant(ExtensionObject value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0f)]
        [TestCase(3f)]
        [TestCase(3.3f)]
        [TestCase(float.MaxValue)]
        [TestCase(-123.3f)]
        [TestCase(float.MinValue)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(float.NaN)]
        public void WriteAndReadFloat(float value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            float expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteFloat(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            float result = reader.ReadFloat(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0f, 0)]
        [TestCase(0f, 3)]
        [TestCase(4.1f, 1)]
        [TestCase(float.MaxValue, 5)]
        [TestCase(-123f, 2)]
        [TestCase(float.MinValue, 1000)]
        [TestCase(4.1f, 100)]
        [TestCase(float.PositiveInfinity, 3)]
        [TestCase(float.NegativeInfinity, 6)]
        [TestCase(float.NaN, 66)]
        public void WriteAndReadFloatArray(float value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteFloatArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<float> result = reader.ReadFloatArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0f, 0)]
        [TestCase(0f, 3)]
        [TestCase(4.1f, 1)]
        [TestCase(float.MaxValue, 5)]
        [TestCase(-123f, 2)]
        [TestCase(float.MinValue, 1000)]
        [TestCase(4.1f, 100)]
        [TestCase(float.PositiveInfinity, 3)]
        [TestCase(float.NegativeInfinity, 6)]
        [TestCase(float.NaN, 66)]
        public void WriteAndReadFloatValuesVariant(float value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0f)]
        [TestCase(3f)]
        [TestCase(3.3f)]
        [TestCase(float.MaxValue)]
        [TestCase(-123.3f)]
        [TestCase(float.MinValue)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(float.NaN)]
        public void WriteAndReadFloatVariant(float value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.Guids))]
        public void WriteAndReadGuid(Uuid value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            Uuid expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteGuid(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            Uuid result = reader.ReadGuid(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.GuidValues))]
        public void WriteAndReadGuidArray(Uuid value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteGuidArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<Uuid> result = reader.ReadGuidArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.GuidValues))]
        public void WriteAndReadGuidValuesVariant(Uuid value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.Guids))]
        public void WriteAndReadGuidVariant(Uuid value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0)]
        [TestCase(0x3)]
        [TestCase(int.MaxValue)]
        [TestCase(-123)]
        [TestCase(int.MinValue)]
        public void WriteAndReadInt(int value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            int expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteInt32(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            int result = reader.ReadInt32(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0, 0)]
        [TestCase(0, 3)]
        [TestCase(0x3, 1)]
        [TestCase(0x3, 100)]
        [TestCase(int.MaxValue, 5)]
        [TestCase(-123, 2)]
        [TestCase(int.MinValue, 1000)]
        public void WriteAndReadIntArray(int value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteInt32Array(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<int> result = reader.ReadInt32Array(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0, 0)]
        [TestCase(0, 3)]
        [TestCase(0x3, 1)]
        [TestCase(0x3, 100)]
        [TestCase(int.MaxValue, 5)]
        [TestCase(-123, 2)]
        [TestCase(int.MinValue, 1000)]
        public void WriteAndReadIntValuesVariant(int value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0)]
        [TestCase(0x3)]
        [TestCase(int.MaxValue)]
        [TestCase(-123)]
        [TestCase(int.MinValue)]
        public void WriteAndReadIntVariant(int value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.LocalizedTexts))]
        public void WriteAndReadLocalizedText(LocalizedText value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            LocalizedText expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteLocalizedText(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            LocalizedText result = reader.ReadLocalizedText(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.LocalizedTextValues))]
        public void WriteAndReadLocalizedTextArray(LocalizedText value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteLocalizedTextArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<LocalizedText> result = reader.ReadLocalizedTextArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.LocalizedTextValues))]
        public void WriteAndReadLocalizedTextValuesVariant(LocalizedText value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.LocalizedTexts))]
        public void WriteAndReadLocalizedTextVariant(LocalizedText value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0u)]
        [TestCase(0x3u)]
        [TestCase(long.MaxValue)]
        [TestCase(-123)]
        [TestCase(long.MinValue)]
        public void WriteAndReadLong(long value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            long expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteInt64(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            long result = reader.ReadInt64(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0, 0)]
        [TestCase(0, 3)]
        [TestCase(0x3, 1)]
        [TestCase(0x3, 100)]
        [TestCase(long.MaxValue, 5)]
        [TestCase(-123, 2)]
        [TestCase(long.MinValue, 1000)]
        public void WriteAndReadLongArray(long value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteInt64Array(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<long> result = reader.ReadInt64Array(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0, 0)]
        [TestCase(0, 3)]
        [TestCase(0x3, 1)]
        [TestCase(0x3, 100)]
        [TestCase(long.MaxValue, 5)]
        [TestCase(-123, 2)]
        [TestCase(long.MinValue, 1000)]
        public void WriteAndReadLongValuesVariant(long value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0u)]
        [TestCase(0x3u)]
        [TestCase(long.MaxValue)]
        [TestCase(-123)]
        [TestCase(long.MinValue)]
        public void WriteAndReadLongVariant(long value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.VariantsWithMatrix))]
        public void WriteAndReadMatrixVariants(Variant value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            Variant expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteVariant(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            Variant result = reader.ReadVariant(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.NodeIds))]
        public void WriteAndReadNodeId(NodeId value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            NodeId expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteNodeId(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            NodeId result = reader.ReadNodeId(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.NodeIdValues))]
        public void WriteAndReadNodeIdArray(NodeId value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteNodeIdArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<NodeId> result = reader.ReadNodeIdArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.NodeIdValues))]
        public void WriteAndReadNodeIdValuesVariant(NodeId value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.NodeIds))]
        public void WriteAndReadNodeIdVariant(NodeId value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.QualifiedNames))]
        public void WriteAndReadQualifiedName(QualifiedName value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            QualifiedName expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteQualifiedName(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            QualifiedName result = reader.ReadQualifiedName(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.QualifiedNameValues))]
        public void WriteAndReadQualifiedNameArray(QualifiedName value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteQualifiedNameArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<QualifiedName> result = reader.ReadQualifiedNameArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.QualifiedNameValues))]
        public void WriteAndReadQualifiedNameValuesVariant(QualifiedName value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.QualifiedNames))]
        public void WriteAndReadQualifiedNameVariant(QualifiedName value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0)]
        [TestCase(0x3)]
        [TestCase(sbyte.MaxValue)]
        [TestCase(-123)]
        [TestCase(sbyte.MinValue)]
        public void WriteAndReadSByte(sbyte value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            sbyte expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteSByte(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            sbyte result = reader.ReadSByte(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0, 0)]
        [TestCase(0, 3)]
        [TestCase(0x3, 1)]
        [TestCase(0x3, 100)]
        [TestCase(sbyte.MaxValue, 5)]
        [TestCase(-123, 2)]
        [TestCase(sbyte.MinValue, 1000)]
        public void WriteAndReadSByteArray(sbyte value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteSByteArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<sbyte> result = reader.ReadSByteArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0, 0)]
        [TestCase(0, 3)]
        [TestCase(0x3, 1)]
        [TestCase(0x3, 100)]
        [TestCase(sbyte.MaxValue, 5)]
        [TestCase(-123, 2)]
        [TestCase(sbyte.MinValue, 1000)]
        public void WriteAndReadSByteValuesVariant(sbyte value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0)]
        [TestCase(0x3)]
        [TestCase(sbyte.MaxValue)]
        [TestCase(-123)]
        [TestCase(sbyte.MinValue)]
        public void WriteAndReadSByteVariant(sbyte value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0)]
        [TestCase(0x3)]
        [TestCase(short.MaxValue)]
        [TestCase(-123)]
        [TestCase(short.MinValue)]
        public void WriteAndReadShort(short value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            short expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteInt16(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            short result = reader.ReadInt16(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0, 0)]
        [TestCase(0, 3)]
        [TestCase(0x3, 1)]
        [TestCase(0x3, 100)]
        [TestCase(short.MaxValue, 5)]
        [TestCase(-123, 2)]
        [TestCase(short.MinValue, 1000)]
        public void WriteAndReadShortArray(short value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteInt16Array(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<short> result = reader.ReadInt16Array(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0, 0)]
        [TestCase(0, 3)]
        [TestCase(0x3, 1)]
        [TestCase(0x3, 100)]
        [TestCase(short.MaxValue, 5)]
        [TestCase(-123, 2)]
        [TestCase(short.MinValue, 1000)]
        public void WriteAndReadShortValuesVariant(short value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0)]
        [TestCase(0x3)]
        [TestCase(short.MaxValue)]
        [TestCase(-123)]
        [TestCase(short.MinValue)]
        public void WriteAndReadShortVariant(short value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.StatusCodes2))]
        public void WriteAndReadStatusCode(StatusCode value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            StatusCode expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteStatusCode(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            StatusCode result = reader.ReadStatusCode(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.StatusCodeValues))]
        public void WriteAndReadStatusCodeArray(StatusCode value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteStatusCodeArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<StatusCode> result = reader.ReadStatusCodeArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.StatusCodeValues))]
        public void WriteAndReadStatusCodeValuesVariant(StatusCode value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.StatusCodes2))]
        public void WriteAndReadStatusCodeVariant(StatusCode value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase("")]
        [TestCase("a b c")]
        [TestCase("dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd")]
        public void WriteAndReadString(string value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            string expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteString(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            string result = reader.ReadString(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase("", 0)]
        [TestCase("", 44)]
        [TestCase("a b c", 3)]
        [TestCase("dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd", 33)]
        public void WriteAndReadStringArray(string value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteStringArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<string> result = reader.ReadStringArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void WriteAndReadEncodeableExtensionObjectNull()
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = new Argument();
            var buffers = new PooledBufferWriter();
            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteEncodeableAsExtensionObject(JsonProperties.Value, expected);
            }
            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            Argument result = reader.ReadEncodeableAsExtensionObject<Argument>(JsonProperties.Value);

            Assert.That(CoreUtils.IsEqual(result, expected), Is.True);
        }

        [Test]
        [TestCase(0u)]
        [TestCase(0x3u)]
        [TestCase(uint.MaxValue)]
        public void WriteAndReadUInt(uint value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            uint expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteUInt32(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            uint result = reader.ReadUInt32(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0u, 0)]
        [TestCase(0u, 3)]
        [TestCase(0x3u, 1)]
        [TestCase(0x3u, 100)]
        [TestCase(uint.MaxValue, 5)]
        public void WriteAndReadUIntArray(uint value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteUInt32Array(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<uint> result = reader.ReadUInt32Array(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0u, 0)]
        [TestCase(0u, 3)]
        [TestCase(0x3u, 1)]
        [TestCase(0x3u, 100)]
        [TestCase(uint.MaxValue, 5)]
        public void WriteAndReadUIntValuesVariant(uint value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0u)]
        [TestCase(0x3u)]
        [TestCase(uint.MaxValue)]
        public void WriteAndReadUIntVariant(uint value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0u)]
        [TestCase(0x3u)]
        [TestCase((ulong)long.MaxValue)]
        [TestCase((ulong)long.MaxValue + 1)]
        [TestCase(ulong.MaxValue)]
        public void WriteAndReadULong(ulong value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            ulong expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteUInt64(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ulong result = reader.ReadUInt64(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0u, 0)]
        [TestCase(0u, 3)]
        [TestCase(0x3u, 1)]
        [TestCase(0x3u, 100)]
        [TestCase(ulong.MaxValue, 5)]
        public void WriteAndReadULongArray(ulong value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteUInt64Array(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<ulong> result = reader.ReadUInt64Array(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(0u, 0)]
        [TestCase(0u, 3)]
        [TestCase(0x3u, 1)]
        [TestCase(0x3u, 100)]
        [TestCase(ulong.MaxValue, 5)]
        public void WriteAndReadULongValuesVariant(ulong value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase(0UL)]
        [TestCase(0x3UL)]
        [TestCase((ulong)long.MaxValue)]
        [TestCase((ulong)long.MaxValue + 1)]
        [TestCase(ulong.MaxValue)]
        public void WriteAndReadULongVariant(ulong value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase((ushort)0)]
        [TestCase((ushort)0x3)]
        [TestCase(ushort.MaxValue)]
        public void WriteAndReadUShort(ushort value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            ushort expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteUInt16(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ushort result = reader.ReadUInt16(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase((ushort)0, 0)]
        [TestCase((ushort)0, 3)]
        [TestCase((ushort)0x3, 1)]
        [TestCase((ushort)0x3, 100)]
        [TestCase(ushort.MaxValue, 5)]
        public void WriteAndReadUShortArray(ushort value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteUInt16Array(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<ushort> result = reader.ReadUInt16Array(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase((ushort)0, 0)]
        [TestCase((ushort)0, 3)]
        [TestCase((ushort)0x3, 1)]
        [TestCase((ushort)0x3, 100)]
        [TestCase(ushort.MaxValue, 5)]
        public void WriteAndReadUShortValuesVariant(ushort value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCase((ushort)0)]
        [TestCase((ushort)0x3)]
        [TestCase(ushort.MaxValue)]
        public void WriteAndReadUShortVariant(ushort value)
        {
            var expected = new Variant(value);
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.Variants))]
        public void WriteAndReadVariant(Variant value)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            Variant expected = value;
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteVariant(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            Variant result = reader.ReadVariant(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.VariantValues))]
        public void WriteAndReadVariantArray(Variant value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = Enumerable.Repeat(value, length).ToArrayOf();
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteVariantArray(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            ArrayOf<Variant> result = reader.ReadVariantArray(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.VariantValues))]
        public void WriteAndReadVariantWithVariantArray(Variant value, int length)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteVariant(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            Variant result = reader.ReadVariant(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.XmlElementValues))]
        public void WriteAndReadXmlElementValuesVariant(XmlElement value, int length)
        {
            var expected = new Variant(Enumerable.Repeat(value, length).ToArray());
            TestWriteAndReadVariant(in expected);
        }

        [Test]
        [TestCaseSource(typeof(BuiltInTypeTestCases), nameof(BuiltInTypeTestCases.XmlElements))]
        public void WriteAndReadXmlElementVariant(XmlElement value)
        {
            TestWriteAndReadVariant(new Variant(value));
        }

        private static void TestWriteAndReadDataValue(in DataValue expected)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteDataValue(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            DataValue result = reader.ReadDataValue(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }

        private static void TestWriteAndReadVariant(in Variant expected)
        {
            ITelemetryContext telemetryContext = NUnitTelemetryContext.Create();
            var messageContext = new ServiceMessageContext(telemetryContext);
            var buffers = new PooledBufferWriter();

            using (var writer = new JsonWriter(buffers, messageContext))
            {
                writer.WriteVariant(JsonProperties.Value, expected);
            }

            using var reader = new JsonParser(buffers.WrittenMemory.ToReadOnlySequence(16), messageContext);
            Variant result = reader.ReadVariant(JsonProperties.Value);

            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
