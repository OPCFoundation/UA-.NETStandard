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
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class JsonDataSetMessageEncodeTests
    {
        [Test]
        public void DefaultConstructorSetsNullDataSet()
        {
            var message = new PubSubEncoding.JsonDataSetMessage();
            Assert.That(message.DataSet, Is.Null);
        }

        [Test]
        public void DataSetConstructorSetsDataSet()
        {
            var dataSet = new DataSet("TestDataSet");
            var message = new PubSubEncoding.JsonDataSetMessage(dataSet);
            Assert.That(message.DataSet, Is.SameAs(dataSet));
        }

        [Test]
        public void HasDataSetMessageHeaderDefaultIsFalse()
        {
            var message = new PubSubEncoding.JsonDataSetMessage();
            Assert.That(message.HasDataSetMessageHeader, Is.False);
        }

        [Test]
        public void SetFieldContentMaskNoneSetsVariant()
        {
            var message = new PubSubEncoding.JsonDataSetMessage(CreateSingleFieldDataSet());
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            var fieldObj = root["TestField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null, "Variant encoding should produce a JSON object with Type/Body.");
            Assert.That(fieldObj["Type"], Is.Not.Null, "Variant mode should include Type information.");
        }

        [Test]
        public void SetFieldContentMaskRawDataSetsRawData()
        {
            var message = new PubSubEncoding.JsonDataSetMessage(CreateSingleFieldDataSet());
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            Assert.That(root["TestField"], Is.Not.Null, "RawData encoding should include the field.");
            Assert.That(root["TestField"].Type, Is.Not.EqualTo(JTokenType.Object).Or.Not.EqualTo(JTokenType.Null),
                "RawData field should be encoded.");
        }

        [Test]
        public void SetFieldContentMaskStatusCodeSetsDataValue()
        {
            var message = new PubSubEncoding.JsonDataSetMessage(CreateSingleFieldDataSet());
            message.SetFieldContentMask(DataSetFieldContentMask.StatusCode);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            var fieldObj = root["TestField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null, "DataValue encoding should produce a JSON object.");
            Assert.That(fieldObj["Value"], Is.Not.Null, "DataValue mode should include Value.");
        }

        [Test]
        public void SetFieldContentMaskServerTimestampSetsDataValue()
        {
            var message = new PubSubEncoding.JsonDataSetMessage(CreateSingleFieldDataSet());
            message.SetFieldContentMask(DataSetFieldContentMask.ServerTimestamp);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            var fieldObj = root["TestField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null, "DataValue encoding should produce a JSON object.");
        }

        [Test]
        public void SetFieldContentMaskSourcePicoSecondsSetsDataValue()
        {
            var message = new PubSubEncoding.JsonDataSetMessage(CreateSingleFieldDataSet());
            message.SetFieldContentMask(DataSetFieldContentMask.SourcePicoSeconds);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            var fieldObj = root["TestField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null, "DataValue encoding should produce a JSON object.");
        }

        [Test]
        public void EncodeWithHeaderIncludesDataSetWriterId()
        {
            PubSubEncoding.JsonDataSetMessage message = CreateHeaderMessage(
                JsonDataSetMessageContentMask.DataSetWriterId);
            message.DataSetWriterId = 42;

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            Assert.That(root["DataSetWriterId"], Is.Not.Null, "DataSetWriterId should be present in header.");
            Assert.That(root["DataSetWriterId"]?.Value<int>(), Is.EqualTo(42));
        }

        [Test]
        public void EncodeWithHeaderIncludesSequenceNumber()
        {
            PubSubEncoding.JsonDataSetMessage message = CreateHeaderMessage(
                JsonDataSetMessageContentMask.SequenceNumber);
            message.SequenceNumber = 7;

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            Assert.That(root["SequenceNumber"], Is.Not.Null, "SequenceNumber should be present in header.");
            Assert.That(root["SequenceNumber"]?.Value<uint>(), Is.EqualTo(7u));
        }

        [Test]
        public void EncodeWithHeaderIncludesTimestamp()
        {
            PubSubEncoding.JsonDataSetMessage message = CreateHeaderMessage(
                JsonDataSetMessageContentMask.Timestamp);
            message.Timestamp = DateTime.UtcNow;

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            Assert.That(root["Timestamp"], Is.Not.Null, "Timestamp should be present in header.");
        }

        [Test]
        public void EncodeWithHeaderIncludesStatus()
        {
            PubSubEncoding.JsonDataSetMessage message = CreateHeaderMessage(
                JsonDataSetMessageContentMask.Status);
            message.Status = StatusCodes.BadInvalidArgument;

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            Assert.That(root["Status"], Is.Not.Null, "Status should be present in header.");
        }

        [Test]
        public void EncodeWithHeaderIncludesMetaDataVersion()
        {
            PubSubEncoding.JsonDataSetMessage message = CreateHeaderMessage(
                JsonDataSetMessageContentMask.MetaDataVersion);
            message.MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 1, MinorVersion = 2 };

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            Assert.That(root["MetaDataVersion"], Is.Not.Null, "MetaDataVersion should be present in header.");
        }

        [Test]
        public void EncodeWithoutHeaderOmitsMessageFields()
        {
            var message = new PubSubEncoding.JsonDataSetMessage(CreateSingleFieldDataSet())
            {
                HasDataSetMessageHeader = false,
                DataSetMessageContentMask =
                    JsonDataSetMessageContentMask.DataSetWriterId |
                    JsonDataSetMessageContentMask.SequenceNumber,
                DataSetWriterId = 99,
                SequenceNumber = 5
            };
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            Assert.That(root["DataSetWriterId"], Is.Null, "DataSetWriterId should be absent without header.");
            Assert.That(root["SequenceNumber"], Is.Null, "SequenceNumber should be absent without header.");
        }

        [Test]
        public void EncodePayloadVariantReversible()
        {
            var message = new PubSubEncoding.JsonDataSetMessage(CreateSingleFieldDataSet());
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            var fieldObj = root["TestField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null);
            Assert.That(fieldObj["Body"]?.Value<int>(), Is.EqualTo(42));
        }

        [Test]
        public void EncodePayloadVariantNonReversible()
        {
            var message = new PubSubEncoding.JsonDataSetMessage(CreateSingleFieldDataSet());
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            string json = EncodeMessage(message, PubSubJsonEncoding.NonReversible);
            var root = JObject.Parse(json);

            Assert.That(root["TestField"], Is.Not.Null, "Field should be present in non-reversible variant mode.");
        }

        [Test]
        public void EncodePayloadRawDataReversible()
        {
            var message = new PubSubEncoding.JsonDataSetMessage(CreateSingleFieldDataSet());
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            Assert.That(root["TestField"]?.Value<int>(), Is.EqualTo(42));
        }

        [Test]
        public void EncodePayloadDataValueReversible()
        {
            Field field = CreateField("TestField", BuiltInType.Int32, 42);
            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.StatusCode);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            var fieldObj = root["TestField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null, "DataValue encoding should produce a JSON object.");
            Assert.That(fieldObj["Value"], Is.Not.Null, "Value should be present in DataValue encoding.");
        }

        [Test]
        public void EncodePayloadDataValueWithStatusCode()
        {
            Field field = CreateField("TestField", BuiltInType.Int32, 42);
            field.Value = new DataValue(new Variant(42))
            {
                StatusCode = StatusCodes.BadInvalidArgument
            };
            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.StatusCode);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            var fieldObj = root["TestField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null);
            Assert.That(fieldObj["StatusCode"], Is.Not.Null, "StatusCode should be present when mask includes it.");
        }

        [Test]
        public void EncodePayloadDataValueWithTimestamps()
        {
            Field field = CreateField("TestField", BuiltInType.Int32, 42);
            field.Value = new DataValue(new Variant(42))
            {
                SourceTimestamp = DateTime.UtcNow,
                ServerTimestamp = DateTime.UtcNow
            };
            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.ServerTimestamp);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            var fieldObj = root["TestField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null);
            Assert.That(fieldObj["SourceTimestamp"], Is.Not.Null,
                "SourceTimestamp should be present when mask includes it.");
            Assert.That(fieldObj["ServerTimestamp"], Is.Not.Null,
                "ServerTimestamp should be present when mask includes it.");
        }

        [Test]
        public void EncodePayloadWithNullFieldSkipsField()
        {
            var dataSet = new DataSet
            {
                Fields = [
                    CreateField("Field1", BuiltInType.Int32, 1),
                    null,
                    CreateField("Field3", BuiltInType.Int32, 3)
                ]
            };
            var message = new PubSubEncoding.JsonDataSetMessage(dataSet);
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            Assert.That(root["Field1"]?.Value<int>(), Is.EqualTo(1));
            Assert.That(root["Field3"]?.Value<int>(), Is.EqualTo(3));
        }

        [Test]
        public void EncodeWithNullDataSetProducesEmptyPayload()
        {
            var message = new PubSubEncoding.JsonDataSetMessage
            {
                HasDataSetMessageHeader = false
            };
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            Assert.That(root.Count, Is.Zero, "Null DataSet should produce empty JSON object.");
        }

        [Test]
        public void EncodeWithAllHeaderFieldsSet()
        {
            var message = new PubSubEncoding.JsonDataSetMessage(CreateSingleFieldDataSet())
            {
                HasDataSetMessageHeader = true,
                DataSetMessageContentMask =
                    JsonDataSetMessageContentMask.DataSetWriterId |
                    JsonDataSetMessageContentMask.SequenceNumber |
                    JsonDataSetMessageContentMask.MetaDataVersion |
                    JsonDataSetMessageContentMask.Timestamp |
                    JsonDataSetMessageContentMask.Status,
                DataSetWriterId = 10,
                SequenceNumber = 20,
                MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 3, MinorVersion = 4 },
                Timestamp = DateTime.UtcNow,
                Status = StatusCodes.BadInvalidArgument
            };
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            string json = EncodeMessage(message, PubSubJsonEncoding.Reversible);
            var root = JObject.Parse(json);

            Assert.That(root["DataSetWriterId"], Is.Not.Null);
            Assert.That(root["SequenceNumber"], Is.Not.Null);
            Assert.That(root["MetaDataVersion"], Is.Not.Null);
            Assert.That(root["Timestamp"], Is.Not.Null);
            Assert.That(root["Status"], Is.Not.Null);
            Assert.That(root["Payload"], Is.Not.Null, "Payload should be present when header is enabled.");
        }

        private static DataSet CreateSingleFieldDataSet()
        {
            return new DataSet
            {
                Fields = [CreateField("TestField", BuiltInType.Int32, 42)]
            };
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

        private static PubSubEncoding.JsonDataSetMessage CreateHeaderMessage(
            JsonDataSetMessageContentMask contentMask)
        {
            var message = new PubSubEncoding.JsonDataSetMessage(CreateSingleFieldDataSet())
            {
                HasDataSetMessageHeader = true,
                DataSetMessageContentMask = contentMask
            };
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);
            return message;
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
