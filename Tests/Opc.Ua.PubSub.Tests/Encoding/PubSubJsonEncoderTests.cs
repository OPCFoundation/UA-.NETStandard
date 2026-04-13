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
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;
using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture(Description = "Coverage tests for PubSubJsonEncoder")]
    [Parallelizable]
    public class PubSubJsonEncoderTests
    {
        private ServiceMessageContext m_context;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
        }

        [Test]
        public void ConstructorWithReversibleEncodingCreatesFunctionalEncoder()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.Reversible));

            encoder.PushStructure("Test");
            encoder.WriteString("Field", "Value");
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ConstructorWithNonReversibleEncodingCreatesFunctionalEncoder()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: false);
            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.NonReversible));

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithStreamCreatesFunctionalEncoder()
        {
            using var stream = new MemoryStream();
            var encoder = new PubSubJsonEncoder(
                m_context,
                useReversibleEncoding: true,
                topLevelIsArray: false,
                stream: stream);

            encoder.PushStructure(null);
            encoder.WriteInt32("Number", 42);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ConstructorWithStreamWriterCreatesFunctionalEncoder()
        {
            using var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            var encoder = new PubSubJsonEncoder(
                m_context,
                useReversibleEncoding: true,
                writer);

            encoder.PushStructure(null);
            encoder.WriteBoolean("Flag", true);
            encoder.PopStructure();

            int length = encoder.Close();
            Assert.That(length, Is.GreaterThan(0));
        }

        [Test]
        public void ConstructorWithEncodingEnumCreatesFunctionalEncoder()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Compact);
            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.Compact));

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithTopLevelArrayCreatesFunctionalEncoder()
        {
            using var encoder = new PubSubJsonEncoder(
                m_context,
                useReversibleEncoding: true,
                topLevelIsArray: true);

            encoder.PushArray(null);
            encoder.PopArray();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Is.Not.Null);
        }

        [Test]
        public void WriteSwitchFieldReversibleWritesSwitchFieldAndSetsValueFieldName()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.PushStructure(null);
            encoder.WriteSwitchField(1, out string fieldName);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("SwitchField"));
            Assert.That(fieldName, Is.EqualTo("Value"));
        }

        [Test]
        public void WriteSwitchFieldCompactWritesSwitchField()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Compact);
            encoder.PushStructure(null);
            encoder.WriteSwitchField(1, out _);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("SwitchField"));
        }

        [Test]
        public void WriteSwitchFieldNonReversibleIsNoOp()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            encoder.PushStructure(null);
            encoder.WriteSwitchField(1, out string fieldName);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Not.Contain("SwitchField"));
            Assert.That(fieldName, Is.Null);
        }

        [Test]
        public void WriteSwitchFieldVerboseIsNoOp()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Verbose);
            encoder.PushStructure(null);
            encoder.WriteSwitchField(1, out string fieldName);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Not.Contain("SwitchField"));
            Assert.That(fieldName, Is.Null);
        }

        [Test]
        public void WriteSwitchFieldCompactWithSuppressArtifactsSkipsWrite()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Compact);
            encoder.SuppressArtifacts = true;
            encoder.PushStructure(null);
            encoder.WriteSwitchField(1, out string fieldName);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Not.Contain("SwitchField"));
        }

        [Test]
        public void WriteEncodingMaskReversibleWritesMask()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            encoder.PushStructure(null);
            encoder.WriteEncodingMask(0x03);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("EncodingMask"));
        }

        [Test]
        public void WriteEncodingMaskCompactWritesMask()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Compact);
            encoder.PushStructure(null);
            encoder.WriteEncodingMask(0x03);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("EncodingMask"));
        }

        [Test]
        public void WriteEncodingMaskNonReversibleIsNoOp()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            encoder.PushStructure(null);
            encoder.WriteEncodingMask(0x03);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Not.Contain("EncodingMask"));
        }

        [Test]
        public void WriteEncodingMaskVerboseIsNoOp()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Verbose);
            encoder.PushStructure(null);
            encoder.WriteEncodingMask(0x03);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Not.Contain("EncodingMask"));
        }

        [Test]
        public void WriteEncodingMaskCompactWithSuppressArtifactsSkipsWrite()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Compact);
            encoder.SuppressArtifacts = true;
            encoder.PushStructure(null);
            encoder.WriteEncodingMask(0x03);
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Not.Contain("EncodingMask"));
        }

        [Test]
        public void SetMappingTablesWithNullsDoesNotThrow()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            Assert.DoesNotThrow(() => encoder.SetMappingTables(null, null));
        }

        [Test]
        public void SetMappingTablesWithValidTablesDoesNotThrow()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            var namespaceTable = new NamespaceTable();
            var serverTable = new StringTable();
            Assert.DoesNotThrow(() => encoder.SetMappingTables(namespaceTable, serverTable));
        }

        [Test]
        public void CloseAndReturnTextReturnsValidJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteString("Key", "Value");
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            Assert.That(result, Does.Contain("Key"));
            Assert.That(result, Does.Contain("Value"));

            var parsed = JObject.Parse(result);
            Assert.That(parsed, Is.Not.Null);
        }

        [Test]
        public void CloseReturnsPositiveLength()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteString("Key", "Value");
            encoder.PopStructure();

            int length = encoder.Close();
            Assert.That(length, Is.GreaterThan(0));
        }

        [Test]
        public void CloseAndReturnTextThrowsForExternalNonMemoryStream()
        {
            string tempFile = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "encoder_test.tmp");
            try
            {
                using var fileStream = new FileStream(
                    tempFile, FileMode.Create, FileAccess.Write);
                using var encoder = new PubSubJsonEncoder(
                    m_context,
                    PubSubJsonEncoding.Reversible,
                    topLevelIsArray: false,
                    stream: fileStream);

                encoder.PushStructure(null);
                encoder.WriteString("Key", "Value");
                encoder.PopStructure();

                Assert.Throws<NotSupportedException>(() => encoder.CloseAndReturnText());
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Test]
        public void PushAndPopStructureProducesValidJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.PushStructure("Inner");
            encoder.WriteInt32("Value", 123);
            encoder.PopStructure();
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            var parsed = JObject.Parse(result);
            Assert.That(parsed["Inner"]?["Value"]?.Value<int>(), Is.EqualTo(123));
        }

        [Test]
        public void PushAndPopArrayProducesValidJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.PushArray("Items");
            encoder.PopArray();
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            var parsed = JObject.Parse(result);
            Assert.That(parsed["Items"], Is.Not.Null);
        }

        [Test]
        public void EncodeDataSetMessageWithRawDataModeProducesJson()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Temperature",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(25.5))
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("Temperature"));
        }

        [Test]
        public void EncodeDataSetMessageWithStatusCodeAndTimestampMask()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Pressure",
                    BuiltInType = (byte)BuiltInType.Float,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant((float)101.3))
                {
                    SourceTimestamp = DateTime.UtcNow,
                    StatusCode = StatusCodes.Good
                }
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("Pressure"));
        }

        [Test]
        public void EncodeDataSetMessageWithVariantMode()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Count",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(42))
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("Count"));
        }

        [Test]
        public void EncodeDataSetMessageWithNonReversibleEncoding()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Active",
                    BuiltInType = (byte)BuiltInType.Boolean,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(true))
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("Active"));
        }

        [Test]
        public void EncodeMultipleFieldsInDataSetMessage()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var fields = new Field[]
            {
                new() {
                    FieldMetaData = new FieldMetaData
                    {
                        Name = "Field1",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    Value = new DataValue(new Variant(1))
                },
                new() {
                    FieldMetaData = new FieldMetaData
                    {
                        Name = "Field2",
                        BuiltInType = (byte)BuiltInType.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    Value = new DataValue(new Variant("Hello"))
                },
                new() {
                    FieldMetaData = new FieldMetaData
                    {
                        Name = "Field3",
                        BuiltInType = (byte)BuiltInType.Double,
                        ValueRank = ValueRanks.Scalar
                    },
                    Value = new DataValue(new Variant(3.14))
                }
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = fields });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Does.Contain("Field1"));
            Assert.That(json, Does.Contain("Field2"));
            Assert.That(json, Does.Contain("Field3"));
        }

        [Test]
        public void EncodeEmptyDataSetMessageProducesJson()
        {
            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = Array.Empty<Field>() });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            message.Encode(encoder);
            string json = encoder.CloseAndReturnText();

            Assert.That(json, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void DisposeEncoderMultipleTimesDoesNotThrow()
        {
            var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.PopStructure();
            encoder.Dispose();
            Assert.DoesNotThrow(encoder.Dispose);
        }

#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void UsingReversibleEncodingTemporarilySwitchesEncoding()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.NonReversible);
            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.NonReversible));

            encoder.PushStructure(null);
            encoder.UsingReversibleEncoding<int>(
                (name, value) =>
                {
                    Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.Reversible));
                    encoder.WriteInt32(name, value);
                },
                "TempField",
                99,
                useReversibleEncoding: true);
            encoder.PopStructure();

            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.NonReversible));
        }
#pragma warning restore CS0618 // Type or member is obsolete

        [Test]
        public void UsingAlternateEncodingTemporarilySwitchesEncoding()
        {
            using var encoder = new PubSubJsonEncoder(m_context, PubSubJsonEncoding.Reversible);
            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.Reversible));

            encoder.PushStructure(null);
            encoder.UsingAlternateEncoding<int>(
                (name, value) =>
                {
                    Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.Compact));
                    encoder.WriteInt32(name, value);
                },
                "AltField",
                42,
                PubSubJsonEncoding.Compact);
            encoder.PopStructure();

            Assert.That(encoder.EncodingToUse, Is.EqualTo(PubSubJsonEncoding.Reversible));
        }

        [Test]
        public void WritePrimitiveTypesProducesValidJson()
        {
            using var encoder = new PubSubJsonEncoder(m_context, useReversibleEncoding: true);
            encoder.PushStructure(null);
            encoder.WriteBoolean("Bool", true);
            encoder.WriteByte("Byte", 255);
            encoder.WriteSByte("SByte", -1);
            encoder.WriteInt16("Int16", -32000);
            encoder.WriteUInt16("UInt16", 65000);
            encoder.WriteInt32("Int32", -100);
            encoder.WriteUInt32("UInt32", 100);
            encoder.WriteInt64("Int64", -999999);
            encoder.WriteUInt64("UInt64", 999999);
            encoder.WriteFloat("Float", 1.5f);
            encoder.WriteDouble("Double", 2.5);
            encoder.WriteString("String", "test");
            encoder.PopStructure();

            string result = encoder.CloseAndReturnText();
            var parsed = JObject.Parse(result);
            Assert.That(parsed["Bool"]?.Value<bool>(), Is.True);
            Assert.That(parsed["String"]?.Value<string>(), Is.EqualTo("test"));
            Assert.That(parsed["Int32"]?.Value<int>(), Is.EqualTo(-100));
        }
    }
}
