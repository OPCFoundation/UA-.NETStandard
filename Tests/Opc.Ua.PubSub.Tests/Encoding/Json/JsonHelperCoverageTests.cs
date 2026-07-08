/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
 *
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
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Tests;

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Focused unit tests for helper classes inside
    /// <c>Opc.Ua.PubSub.Encoding.Json</c> that aren't directly covered
    /// by the higher-level encoder/decoder round-trip fixtures. These
    /// exercise null-argument guards, the
    /// <see cref="PubSubFieldEncoding.DataValue"/> field path, metadata
    /// driven field-name resolution and the <see cref="JsonBufferWriter"/>
    /// resize / disposal / boundary semantics.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class JsonHelperCoverageTests
    {
        [Test]
        [TestSpec("7.2.5")]
        public void WriteVariantPropertyRejectsNullArgs()
        {
            using var buffer = new MemoryStream();
            using var writer = new Utf8JsonWriter(buffer);
            writer.WriteStartObject();
            IServiceMessageContext ctx = ServiceMessageContext.CreateEmpty(null!);

            Assert.That(() => JsonVariantEncoder.WriteVariantProperty(
                null!, "x", new Variant(1), JsonEncodingMode.Verbose, ctx),
                Throws.ArgumentNullException);
            Assert.That(() => JsonVariantEncoder.WriteVariantProperty(
                writer, null!, new Variant(1), JsonEncodingMode.Verbose, ctx),
                Throws.ArgumentNullException);
            Assert.That(() => JsonVariantEncoder.WriteVariantProperty(
                writer, "x", new Variant(1), JsonEncodingMode.Verbose, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        [TestSpec("7.2.5")]
        public void WriteVariantPropertyEmitsNullForNullVariant()
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                JsonVariantEncoder.WriteVariantProperty(
                    writer, "x", Variant.Null,
                    JsonEncodingMode.Verbose,
                    ServiceMessageContext.CreateEmpty(null!));
                writer.WriteEndObject();
            }
            string text = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            Assert.That(text, Is.EqualTo("{\"x\":null}"));
        }

        [Test]
        [TestSpec("7.2.5")]
        public void WriteDataValuePropertyRejectsNullArgs()
        {
            using var buffer = new MemoryStream();
            using var writer = new Utf8JsonWriter(buffer);
            writer.WriteStartObject();
            IServiceMessageContext ctx = ServiceMessageContext.CreateEmpty(null!);
            DataValue dv = new(new Variant(1));

            Assert.That(() => JsonVariantEncoder.WriteDataValueProperty(
                null!, "x", dv, JsonEncodingMode.Verbose, ctx),
                Throws.ArgumentNullException);
            Assert.That(() => JsonVariantEncoder.WriteDataValueProperty(
                writer, null!, dv, JsonEncodingMode.Verbose, ctx),
                Throws.ArgumentNullException);
            Assert.That(() => JsonVariantEncoder.WriteDataValueProperty(
                writer, "x", dv, JsonEncodingMode.Verbose, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        [TestSpec("7.2.5")]
        public void WriteDataValuePropertyEmitsNullForNullDataValue()
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                JsonVariantEncoder.WriteDataValueProperty(
                    writer, "x", DataValue.Null,
                    JsonEncodingMode.Verbose,
                    ServiceMessageContext.CreateEmpty(null!));
                writer.WriteEndObject();
            }
            string text = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            Assert.That(text, Is.EqualTo("{\"x\":null}"));
        }

        [Test]
        [TestCase(JsonEncodingMode.Verbose)]
        [TestCase(JsonEncodingMode.Compact)]
        [TestCase(JsonEncodingMode.RawData)]
        [TestSpec("7.2.5")]
        public void WriteDataValuePropertyEmitsObjectForEveryMode(JsonEncodingMode mode)
        {
            using var buffer = new MemoryStream();
            DataValue dv = new(
                new Variant(123),
                StatusCodes.Good,
                new DateTimeUtc(2026, 1, 1, 0, 0, 0),
                DateTimeUtc.MinValue);
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                JsonVariantEncoder.WriteDataValueProperty(
                    writer, "v", dv, mode,
                    ServiceMessageContext.CreateEmpty(null!));
                writer.WriteEndObject();
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            JsonElement payload = document.RootElement.GetProperty("v");
            Assert.That(payload.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(payload.TryGetProperty("Value", out _), Is.True);
        }

        [Test]
        [TestSpec("7.2.5.4")]
        public void EncodeFieldsRejectsNullArgs()
        {
            using var buffer = new MemoryStream();
            using var writer = new Utf8JsonWriter(buffer);
            writer.WriteStartObject();
            IServiceMessageContext ctx = ServiceMessageContext.CreateEmpty(null!);

            Assert.That(() => JsonFieldEncoder.EncodeFields(
                null!, JsonTestUtilities.CreateFields(),
                null, JsonEncodingMode.Verbose, ctx),
                Throws.ArgumentNullException);
            Assert.That(() => JsonFieldEncoder.EncodeFields(
                writer, JsonTestUtilities.CreateFields(),
                null, JsonEncodingMode.Verbose, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        [TestSpec("7.2.5.4")]
        public void EncodeFieldsResolvesNameFromMetaDataAndAutoIndex()
        {
            DataSetField[] fields =
            [
                new DataSetField { Value = new Variant(1) },
                new DataSetField { Value = new Variant(2) },
                new DataSetField { Value = new Variant(3) },
                new DataSetField { Value = new Variant(4) }
            ];
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                JsonFieldEncoder.EncodeFields(
                    writer, fields, meta,
                    JsonEncodingMode.Verbose,
                    ServiceMessageContext.CreateEmpty(null!));
                writer.WriteEndObject();
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            JsonElement payload = document.RootElement.GetProperty("Payload");
            Assert.That(payload.TryGetProperty("BoolField", out _), Is.True);
            Assert.That(payload.TryGetProperty("IntField", out _), Is.True);
            Assert.That(payload.TryGetProperty("StringField", out _), Is.True);
            Assert.That(payload.TryGetProperty("Field3", out _), Is.True);
        }

        [Test]
        [TestSpec("7.2.5.4")]
        public void EncodeFieldsHandlesDataValueAndRawDataEncodings()
        {
            DataSetField[] fields =
            [
                new DataSetField
                {
                    Name = "raw",
                    Value = new Variant(7),
                    Encoding = PubSubFieldEncoding.RawData
                },
                new DataSetField
                {
                    Name = "dv",
                    Value = new Variant(8),
                    Encoding = PubSubFieldEncoding.DataValue,
                    StatusCode = StatusCodes.Good,
                    SourceTimestamp = new DateTimeUtc(
                        2026, 1, 1, 0, 0, 0)
                }
            ];
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                JsonFieldEncoder.EncodeFields(
                    writer, fields, null,
                    JsonEncodingMode.Verbose,
                    ServiceMessageContext.CreateEmpty(null!));
                writer.WriteEndObject();
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            JsonElement payload = document.RootElement.GetProperty("Payload");
            Assert.That(payload.GetProperty("raw").ValueKind,
                Is.EqualTo(JsonValueKind.Number));
            JsonElement dv = payload.GetProperty("dv");
            Assert.That(dv.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(dv.TryGetProperty("Value", out _), Is.True);
        }

        [Test]
        [TestSpec("7.2.5.5")]
        public void WriteMetaDataRejectsNullArgs()
        {
            using var buffer = new MemoryStream();
            using var writer = new Utf8JsonWriter(buffer);
            writer.WriteStartObject();
            IServiceMessageContext ctx = ServiceMessageContext.CreateEmpty(null!);
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData();

            Assert.That(() => JsonMetaDataEncoder.WriteMetaData(
                null!, "M", meta, JsonEncodingMode.Verbose, ctx),
                Throws.ArgumentNullException);
            Assert.That(() => JsonMetaDataEncoder.WriteMetaData(
                writer, null!, meta, JsonEncodingMode.Verbose, ctx),
                Throws.ArgumentNullException);
            Assert.That(() => JsonMetaDataEncoder.WriteMetaData(
                writer, "M", null!, JsonEncodingMode.Verbose, ctx),
                Throws.ArgumentNullException);
            Assert.That(() => JsonMetaDataEncoder.WriteMetaData(
                writer, "M", meta, JsonEncodingMode.Verbose, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        [TestSpec("7.2.5.5")]
        public void WriteMetaDataProducesObject()
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                JsonMetaDataEncoder.WriteMetaData(
                    writer, "M",
                    JsonTestUtilities.CreateMetaData(),
                    JsonEncodingMode.Verbose,
                    ServiceMessageContext.CreateEmpty(null!));
                writer.WriteEndObject();
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            Assert.That(document.RootElement.GetProperty("M").ValueKind,
                Is.EqualTo(JsonValueKind.Object));
        }

        [Test]
        [TestSpec("7.2.5")]
        public void JsonBufferWriterAdvanceRejectsNegative()
        {
            using var writer = new JsonBufferWriter(64);
            Assert.That(() => writer.Advance(-1),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        [TestSpec("7.2.5")]
        public void JsonBufferWriterAdvanceRejectsOverflow()
        {
            using var writer = new JsonBufferWriter(16);
            int spanLength = writer.GetSpan(16).Length;
            Assert.That(spanLength, Is.GreaterThanOrEqualTo(16));
            Assert.That(() => writer.Advance(spanLength + 1),
                Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        [TestSpec("7.2.5")]
        public void JsonBufferWriterGetSpanRejectsNegativeSizeHint()
        {
            using var writer = new JsonBufferWriter(16);
            Assert.That(() => writer.GetSpan(-1),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        [TestSpec("7.2.5")]
        public void JsonBufferWriterGrowsToFitLargePayload()
        {
            using var writer = new JsonBufferWriter(8);
            Memory<byte> memory = writer.GetMemory(4096);
            Assert.That(memory.Length, Is.GreaterThanOrEqualTo(4096));
            memory.Span[..4096].Fill(0x41);
            writer.Advance(4096);
            Assert.That(writer.WrittenCount, Is.EqualTo(4096));
            Assert.That(writer.WrittenSpan.Length, Is.EqualTo(4096));
            Assert.That(writer.WrittenMemory.Length, Is.EqualTo(4096));
            byte[] copied = writer.GetWritten();
            Assert.That(copied, Has.Length.EqualTo(4096));
            Assert.That(copied[0], Is.EqualTo((byte)0x41));
        }

        [Test]
        [TestSpec("7.2.5")]
        public void JsonBufferWriterUsesDefaultCapacityForZeroOrNegative()
        {
            using var writer = new JsonBufferWriter(-5);
            Span<byte> span = writer.GetSpan();
            Assert.That(span.Length, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        [TestSpec("7.2.5")]
        public void JsonBufferWriterDisposeIsIdempotent()
        {
            var writer = new JsonBufferWriter(32);
            writer.Dispose();
            Assert.That(writer.Dispose, Throws.Nothing);
        }

        [Test]
        [TestSpec("7.2.5.4")]
        public void DecodeFieldsRejectsNullContext()
        {
            using var document = JsonDocument.Parse("{}");
            Assert.That(() => JsonFieldDecoder.DecodeFields(
                document.RootElement, null,
                JsonEncodingMode.Verbose, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        [TestSpec("7.2.5.4")]
        public void DecodeFieldsReturnsEmptyForNonObjectPayload()
        {
            using var document = JsonDocument.Parse("[1,2,3]");
            var fields = JsonFieldDecoder.DecodeFields(
                document.RootElement, null,
                JsonEncodingMode.Verbose,
                ServiceMessageContext.CreateEmpty(null!));
            Assert.That(fields, Is.Empty);
        }

        [Test]
        [TestSpec("7.2.5.4")]
        public void DecodeFieldsRecognisesDataValueEnvelope()
        {
            const string json = """
                {
                    "field": {
                        "Value": { "Type": 6, "Body": 42 },
                        "Status": 0,
                        "SourceTimestamp": "2026-01-01T00:00:00Z"
                    }
                }
                """;
            using var document = JsonDocument.Parse(json);
            var fields = JsonFieldDecoder.DecodeFields(
                document.RootElement, null,
                JsonEncodingMode.Verbose,
                ServiceMessageContext.CreateEmpty(null!));
            Assert.That(fields.Count, Is.EqualTo(1));
            Assert.That(fields[0].Encoding,
                Is.EqualTo(PubSubFieldEncoding.DataValue));
        }

        [Test]
        [TestSpec("7.2.5.4")]
        public void DecodeFieldsRecognisesPlainValueObject()
        {
            const string nonDataValueObject = """
                {
                    "field": { "Type": 6, "Body": 42 }
                }
                """;
            using var document = JsonDocument.Parse(nonDataValueObject);
            var fields = JsonFieldDecoder.DecodeFields(
                document.RootElement, null,
                JsonEncodingMode.Verbose,
                ServiceMessageContext.CreateEmpty(null!));
            Assert.That(fields.Count, Is.EqualTo(1));
            Assert.That(fields[0].Encoding,
                Is.EqualTo(PubSubFieldEncoding.Variant));
        }

        [Test]
        [TestSpec("7.2.5")]
        public void DecodeVariantHandlesNullElement()
        {
            using var document = JsonDocument.Parse("null");
            Variant value = JsonVariantDecoder.DecodeVariant(
                document.RootElement,
                JsonEncodingMode.Verbose,
                null,
                ServiceMessageContext.CreateEmpty(null!));
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        [TestSpec("7.2.5")]
        public void DecodeVariantRejectsNullContext()
        {
            using var document = JsonDocument.Parse("1");
            Assert.That(() => JsonVariantDecoder.DecodeVariant(
                document.RootElement,
                JsonEncodingMode.Verbose,
                null, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        [TestSpec("7.2.5")]
        public void DecodeDataValueRejectsNullContext()
        {
            using var document = JsonDocument.Parse("{}");
            Assert.That(() => JsonVariantDecoder.DecodeDataValue(
                document.RootElement, null!),
                Throws.ArgumentNullException);
        }

        [Test]
        [TestSpec("7.2.5")]
        public void DecodeDataValueReturnsNullForNullElement()
        {
            using var document = JsonDocument.Parse("null");
            DataValue value = JsonVariantDecoder.DecodeDataValue(
                document.RootElement,
                ServiceMessageContext.CreateEmpty(null!));
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        [TestSpec("7.2.5")]
        public void DecodeVariantCompactWithoutTypeInfoReturnsNull()
        {
            using var document = JsonDocument.Parse("42");
            Variant value = JsonVariantDecoder.DecodeVariant(
                document.RootElement,
                JsonEncodingMode.Compact,
                null,
                ServiceMessageContext.CreateEmpty(null!));
            Assert.That(value.IsNull, Is.True);
        }

        [Test]
        [TestSpec("7.2.5")]
        public void DecodeVariantRawDataWithoutTypeInfoReturnsNull()
        {
            using var document = JsonDocument.Parse("42");
            Variant value = JsonVariantDecoder.DecodeVariant(
                document.RootElement,
                JsonEncodingMode.RawData,
                null,
                ServiceMessageContext.CreateEmpty(null!));
            Assert.That(value.IsNull, Is.True);
        }
    }
}
