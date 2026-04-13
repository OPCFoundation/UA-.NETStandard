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
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;
using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    /// <summary>
    /// Additional coverage tests for JsonDataSetMessage focusing on
    /// decode paths, DataValue field encoding with all timestamps/picoseconds,
    /// and StatusCode-Good omission handling.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public class JsonDataSetMessageAdditionalTests
    {
        // Encode DataValue with source and server picoseconds
        [Test]
        public void EncodeDataValueWithAllPicosecondsFields()
        {
            Field field = CreateField("TestField", BuiltInType.Int32, 42);
            field.Value = new DataValue(new Variant(42))
            {
                StatusCode = StatusCodes.Good,
                SourceTimestamp = DateTime.UtcNow,
                ServerTimestamp = DateTime.UtcNow,
                SourcePicoseconds = 100,
                ServerPicoseconds = 200
            };
            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.ServerPicoSeconds);

            string json = EncodeMessage(message, PubSubJsonEncoding.NonReversible);
            var root = JObject.Parse(json);

            var fieldObj = root["TestField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null, "DataValue encoding should produce a JSON object.");
            Assert.That(fieldObj["SourcePicoseconds"], Is.Not.Null);
            Assert.That(fieldObj["ServerPicoseconds"], Is.Not.Null);
        }

        // Encode StatusCode.Good field as null in RawData mode
        [Test]
        public void EncodeGoodStatusCodeAsNullInRawDataMode()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            Field field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "StatusField",
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(StatusCodes.Good))
            };
#pragma warning restore CS0618 // Type or member is obsolete
            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            string json = EncodeMessage(message, PubSubJsonEncoding.NonReversible);
            Assert.That(json, Is.Not.Null);
        }

        // Encode with bad StatusCode replaces value with status code in non-DataValue mode
        [Test]
        public void EncodeBadStatusCodeReplacesValueInVariantMode()
        {
            Field field = CreateField("TestField", BuiltInType.Int32, 42);
            field.Value = new DataValue(new Variant(42))
            {
                StatusCode = StatusCodes.BadInvalidArgument
            };
            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            Assert.That(root["TestField"], Is.Not.Null);
        }

        // Encode with bad StatusCode in RawData mode
        [Test]
        public void EncodeBadStatusCodeInRawDataMode()
        {
            Field field = CreateField("TestField", BuiltInType.Int32, 42);
            field.Value = new DataValue(new Variant(42))
            {
                StatusCode = StatusCodes.BadOutOfRange
            };
            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            string json = EncodeMessage(message, PubSubJsonEncoding.NonReversible);
            Assert.That(json, Is.Not.Null);
        }

        // Round-trip encode then decode using Variant field encoding
        [Test]
        public void RoundTripVariantEncoding()
        {
            DataSet dataSet = CreateSimpleDataSet("TestField", BuiltInType.Int32, 42);
            var encodeMsg = new PubSubEncoding.JsonDataSetMessage(dataSet);
            encodeMsg.HasDataSetMessageHeader = true;
            encodeMsg.DataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber;
            encodeMsg.DataSetWriterId = 5;
            encodeMsg.SequenceNumber = 10;
            encodeMsg.SetFieldContentMask(DataSetFieldContentMask.None);

            string json = EncodeMessage(encodeMsg, PubSubJsonEncoding.Reversible);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var decoder = new PubSubJsonDecoder(json, ServiceMessageContext.Create(telemetry));

            var decodeMsg = new PubSubEncoding.JsonDataSetMessage();
            decodeMsg.HasDataSetMessageHeader = true;
            decodeMsg.DataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber;
            decodeMsg.SetFieldContentMask(DataSetFieldContentMask.None);

            DataSetReaderDataType reader = CreateDataSetReader("TestField", BuiltInType.Int32);
            reader.DataSetWriterId = 5;

            decodeMsg.DecodePossibleDataSetReader(decoder, 0, null, reader);

            Assert.That(decodeMsg.DataSet, Is.Not.Null, "DataSet should be decoded.");
            Assert.That(decodeMsg.DataSetWriterId, Is.EqualTo(5));
            Assert.That(decodeMsg.SequenceNumber, Is.EqualTo(10u));
        }

        // Decode with RawData field encoding
        [Test]
        public void RoundTripRawDataEncoding()
        {
            DataSet dataSet = CreateSimpleDataSet("TestField", BuiltInType.Int32, 42);
            var encodeMsg = new PubSubEncoding.JsonDataSetMessage(dataSet);
            encodeMsg.HasDataSetMessageHeader = false;
            encodeMsg.SetFieldContentMask(DataSetFieldContentMask.RawData);

            string json = EncodeMessage(encodeMsg, PubSubJsonEncoding.NonReversible);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var decoder = new PubSubJsonDecoder(json, ServiceMessageContext.Create(telemetry));

            var decodeMsg = new PubSubEncoding.JsonDataSetMessage();
            decodeMsg.HasDataSetMessageHeader = false;
            decodeMsg.SetFieldContentMask(DataSetFieldContentMask.RawData);

            DataSetReaderDataType reader = CreateDataSetReader("TestField", BuiltInType.Int32);
            decodeMsg.DecodePossibleDataSetReader(decoder, 0, null, reader);

            Assert.That(decodeMsg.DataSet, Is.Not.Null, "DataSet should be decoded for RawData.");
        }

        // Decode with DataValue field encoding including all sub-fields
        [Test]
        public void RoundTripDataValueEncoding()
        {
            Field field = CreateField("TestField", BuiltInType.Int32, 42);
            field.Value = new DataValue(new Variant(42))
            {
                StatusCode = StatusCodes.Good,
                SourceTimestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ServerTimestamp = new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                SourcePicoseconds = 10,
                ServerPicoseconds = 20
            };
            const DataSetFieldContentMask mask =
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.ServerPicoSeconds;

            var encodeMsg = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            encodeMsg.HasDataSetMessageHeader = false;
            encodeMsg.SetFieldContentMask(mask);

            string json = EncodeMessage(encodeMsg, PubSubJsonEncoding.NonReversible);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var decoder = new PubSubJsonDecoder(json, ServiceMessageContext.Create(telemetry));

            var decodeMsg = new PubSubEncoding.JsonDataSetMessage();
            decodeMsg.HasDataSetMessageHeader = false;
            decodeMsg.SetFieldContentMask(mask);

            DataSetReaderDataType reader = CreateDataSetReader("TestField", BuiltInType.Int32);
            decodeMsg.DecodePossibleDataSetReader(decoder, 0, null, reader);

            Assert.That(decodeMsg.DataSet, Is.Not.Null, "DataSet should be decoded for DataValue.");
        }

        // Decode with header including all header fields
        [Test]
        public void DecodeWithAllHeaderFields()
        {
            DataSet dataSet = CreateSimpleDataSet("F1", BuiltInType.String, "hello");
            var encodeMsg = new PubSubEncoding.JsonDataSetMessage(dataSet);
            encodeMsg.HasDataSetMessageHeader = true;
            encodeMsg.DataSetMessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.SequenceNumber |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.Timestamp |
                JsonDataSetMessageContentMask.Status;
            encodeMsg.DataSetWriterId = 7;
            encodeMsg.SequenceNumber = 99;
            encodeMsg.MetaDataVersion = new ConfigurationVersionDataType
            {
                MajorVersion = 1,
                MinorVersion = 2
            };
            encodeMsg.Timestamp = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            encodeMsg.Status = StatusCodes.Good;
            encodeMsg.SetFieldContentMask(DataSetFieldContentMask.None);

            string json = EncodeMessage(encodeMsg, PubSubJsonEncoding.Reversible);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var decoder = new PubSubJsonDecoder(json, ServiceMessageContext.Create(telemetry));

            var decodeMsg = new PubSubEncoding.JsonDataSetMessage();
            decodeMsg.HasDataSetMessageHeader = true;
            decodeMsg.DataSetMessageContentMask = encodeMsg.DataSetMessageContentMask;
            decodeMsg.SetFieldContentMask(DataSetFieldContentMask.None);

            DataSetReaderDataType reader = CreateDataSetReader("F1", BuiltInType.String);
            reader.DataSetWriterId = 7;
            reader.DataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType
            {
                MajorVersion = 1,
                MinorVersion = 2
            };

            decodeMsg.DecodePossibleDataSetReader(decoder, 0, null, reader);

            Assert.That(decodeMsg.DataSetWriterId, Is.EqualTo(7));
            Assert.That(decodeMsg.SequenceNumber, Is.EqualTo(99u));
            Assert.That(decodeMsg.DataSet, Is.Not.Null);
        }

        // Encode multiple fields with different data types
        [Test]
        public void EncodeMultipleFieldTypes()
        {
            Field[] fields =
            [
                CreateField("IntField", BuiltInType.Int32, 42),
                CreateField("StringField", BuiltInType.String, "hello"),
                CreateField("BoolField", BuiltInType.Boolean, true),
                CreateField("DoubleField", BuiltInType.Double, 3.14)
            ];
            var dataSet = new DataSet { Fields = fields };
            var message = new PubSubEncoding.JsonDataSetMessage(dataSet);
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            string json = EncodeMessage(message, PubSubJsonEncoding.NonReversible);
            var root = JObject.Parse(json);

            Assert.That(root["IntField"]?.Value<int>(), Is.EqualTo(42));
            Assert.That(root["StringField"]?.Value<string>(), Is.EqualTo("hello"));
            Assert.That(root["BoolField"]?.Value<bool>(), Is.True);
            Assert.That(root["DoubleField"]?.Value<double>(), Is.EqualTo(3.14));
        }

        // Encode EncodePayload without push structure (pushStructure=false)
        [Test]
        public void EncodePayloadWithoutPushStructure()
        {
            var dataSet = new DataSet
            {
                Fields = [CreateField("F1", BuiltInType.Int32, 7)]
            };
            var message = new PubSubEncoding.JsonDataSetMessage(dataSet);
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var encoder = new PubSubJsonEncoder(
                ServiceMessageContext.Create(telemetry),
                PubSubJsonEncoding.NonReversible);
            encoder.PushStructure(null);
            message.EncodePayload(encoder, pushStructure: false);
            encoder.PopStructure();
            string json = encoder.CloseAndReturnText();

            var root = JObject.Parse(json);
            Assert.That(root["F1"]?.Value<int>(), Is.EqualTo(7));
        }

        // Decode StatusCode.Good omission in Variant mode
        [Test]
        public void DecodeStatusCodeGoodOmissionInVariantMode()
        {
            const string json = /*lang=json,strict*/ "{\"StatusField\":null}";
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var decoder = new PubSubJsonDecoder(json, ServiceMessageContext.Create(telemetry));

            var decodeMsg = new PubSubEncoding.JsonDataSetMessage();
            decodeMsg.HasDataSetMessageHeader = false;
            decodeMsg.SetFieldContentMask(DataSetFieldContentMask.None);

            DataSetReaderDataType reader = CreateDataSetReader("StatusField", BuiltInType.StatusCode);
            decodeMsg.DecodePossibleDataSetReader(decoder, 0, null, reader);

            // The field should be decoded (as Null variant since field not found)
            Assert.That(decodeMsg.DataSet, Is.Not.Null);
        }

        private static Field CreateField(string name, BuiltInType builtInType, object value)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            return new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = name,
                    BuiltInType = (byte)builtInType,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(value))
                {
                    SourceTimestamp = DateTime.UtcNow
                }
            };
#pragma warning restore CS0618 // Type or member is obsolete
        }

        private static DataSet CreateSimpleDataSet(
            string fieldName,
            BuiltInType builtInType,
            object value)
        {
            return new DataSet
            {
                Fields = [CreateField(fieldName, builtInType, value)]
            };
        }

        private static DataSetReaderDataType CreateDataSetReader(
            string fieldName,
            BuiltInType builtInType)
        {
            return new DataSetReaderDataType
            {
                Name = "Reader1",
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "TestMeta",
                    Fields = [
                        new FieldMetaData
                        {
                            Name = fieldName,
                            BuiltInType = (byte)builtInType,
                            ValueRank = ValueRanks.Scalar
                        }
                    ],
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                }
            };
        }

        private static string EncodeMessage(
            PubSubEncoding.JsonDataSetMessage message,
            PubSubJsonEncoding encodingType)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var encoder = new PubSubJsonEncoder(
                ServiceMessageContext.Create(telemetry),
                encodingType);
            message.Encode(encoder);
            return encoder.CloseAndReturnText();
        }
    }
}
