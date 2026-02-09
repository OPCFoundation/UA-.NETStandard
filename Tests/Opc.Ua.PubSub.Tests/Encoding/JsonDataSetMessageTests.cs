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
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;
using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    /// <summary>
    /// <para>
    /// Tests for JsonDataSetMessage encoding behavior.
    /// Validates correct handling of zero values vs StatusCode.Good per OPC UA Part 6 specification.
    /// </para>
    /// <para>
    /// Note: JsonDataSetMessage currently only supports Reversible and NonReversible encoding modes.
    /// Compact and Verbose encoding modes are not yet supported for PubSub messages because
    /// the encoder throws when trying to modify ForceNamespaceUri property with these modes.
    /// </para>
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public class JsonDataSetMessageTests
    {
        /// <summary>
        /// Regression test: UInt32 value of 0 must not be confused with StatusCode.Good
        /// and must be preserved in DataValue mode with Reversible encoding.
        /// </summary>
        [Test]
        public void EncodeUInt32ZeroPreservesValueInDataValueModeReversible()
        {
            Field field = CreateField("TestField", BuiltInType.UInt32, (uint)0);
            PubSubEncoding.JsonDataSetMessage message = CreateDataValueMessage(field);

            string json = EncodeMessage(message, JsonEncodingType.Reversible);
            JObject fieldObj = GetPayloadField(json, "TestField");

            Assert.That(fieldObj, Is.Not.Null, "Field should be encoded.");
            Assert.That(fieldObj["Value"]?.Value<uint>(), Is.EqualTo(0u),
                "UInt32 zero value must be preserved in Reversible encoding.");
        }

        /// <summary>
        /// Regression test: UInt32 value of 0 must not be confused with StatusCode.Good
        /// and must be preserved in DataValue mode with NonReversible encoding.
        /// </summary>
        [Test]
        public void EncodeUInt32ZeroPreservesValueInDataValueModeNonReversible()
        {
            Field field = CreateField("TestField", BuiltInType.UInt32, (uint)0);
            PubSubEncoding.JsonDataSetMessage message = CreateDataValueMessage(field);

            string json = EncodeMessage(message, JsonEncodingType.NonReversible);
            JObject fieldObj = GetPayloadField(json, "TestField");

            Assert.That(fieldObj, Is.Not.Null, "Field should be encoded.");
            Assert.That(fieldObj["Value"]?.Value<uint>(), Is.EqualTo(0u),
                "UInt32 zero value must be preserved in NonReversible encoding.");
        }

        /// <summary>
        /// Regression test: UInt32 value of 0 must be preserved in RawData mode.
        /// Per OPC 10000-6: RawData uses non-reversible encoding for the value itself.
        /// </summary>
        [Test]
        public void EncodeUInt32ZeroPreservesValueInRawDataModeReversible()
        {
            Field field = CreateField("TestField", BuiltInType.UInt32, (uint)0);

            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            string json = EncodeMessage(message, JsonEncodingType.Reversible);
            var root = JObject.Parse(json);
            JObject payload = (root["Payload"] as JObject) ?? root;

            Assert.That(payload["TestField"]?.Value<uint>(), Is.EqualTo(0u),
                "UInt32 zero value must be preserved in RawData mode with Reversible encoding.");
        }

        /// <summary>
        /// Regression test: UInt32 value of 0 must be preserved in RawData mode with NonReversible encoding.
        /// </summary>
        [Test]
        public void EncodeUInt32ZeroPreservesValueInRawDataModeNonReversible()
        {
            Field field = CreateField("TestField", BuiltInType.UInt32, (uint)0);

            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            string json = EncodeMessage(message, JsonEncodingType.NonReversible);
            var root = JObject.Parse(json);
            JObject payload = (root["Payload"] as JObject) ?? root;

            Assert.That(payload["TestField"]?.Value<uint>(), Is.EqualTo(0u),
                "UInt32 zero value must be preserved in RawData mode with NonReversible encoding.");
        }

        /// <summary>
        /// In Variant mode (FieldContentMask.None), values are encoded with type information.
        /// UInt32 zero should still be preserved as it's a valid value.
        /// Per OPC 10000-6: Variant mode uses reversible encoding with Type/Body structure.
        /// </summary>
        [Test]
        public void EncodeUInt32ZeroPreservesValueInVariantModeReversible()
        {
            Field field = CreateField("TestField", BuiltInType.UInt32, (uint)0);

            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.None); // Variant mode

            string json = EncodeMessage(message, JsonEncodingType.Reversible);
            var root = JObject.Parse(json);
            JObject payload = (root["Payload"] as JObject) ?? root;

            // In Variant mode with Reversible encoding, format is { "Type": 7, "Body": 0 }
            var variantObj = payload["TestField"] as JObject;
            Assert.That(variantObj, Is.Not.Null, "Field should be encoded as Variant object.");
            Assert.That(variantObj["Body"]?.Value<uint>(), Is.EqualTo(0u),
                "UInt32 zero value must be preserved in Variant Body.");
        }

        /// <summary>
        /// Verify that a real StatusCode.Good value results in null/omitted Value
        /// in DataValue mode per spec: "The Code is omitted if the numeric code is 0 (Good)."
        /// </summary>
        [Test]
        public void EncodeStatusCodeGoodResultsInNullValueInDataValueModeReversible()
        {
            Field field = CreateStatusCodeField("StatusField", StatusCodes.Good);
            PubSubEncoding.JsonDataSetMessage message = CreateDataValueMessage(field);

            string json = EncodeMessage(message, JsonEncodingType.Reversible);
            var root = JObject.Parse(json);
            JObject payload = (root["Payload"] as JObject) ?? root;

            var fieldObj = payload["StatusField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null, "Field should be present.");

            // The Value field should be omitted entirely (StatusCode.Good is intentionally nulled)
            Assert.That(fieldObj["Value"], Is.Null,
                "StatusCode.Good should result in omitted Value in Reversible DataValue mode.");
        }

        /// <summary>
        /// Verify that a real StatusCode.Good value results in null/omitted Value
        /// in DataValue mode with NonReversible encoding.
        /// </summary>
        [Test]
        public void EncodeStatusCodeGoodResultsInNullValueInDataValueModeNonReversible()
        {
            Field field = CreateStatusCodeField("StatusField", StatusCodes.Good);
            PubSubEncoding.JsonDataSetMessage message = CreateDataValueMessage(field);

            string json = EncodeMessage(message, JsonEncodingType.NonReversible);
            var root = JObject.Parse(json);
            JObject payload = (root["Payload"] as JObject) ?? root;

            var fieldObj = payload["StatusField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null, "Field should be present.");

            // The Value field should be omitted entirely (StatusCode.Good is intentionally nulled)
            Assert.That(fieldObj["Value"], Is.Null,
                "StatusCode.Good should result in omitted Value in NonReversible DataValue mode.");
        }

        /// <summary>
        /// Verify that a non-Good StatusCode value is preserved in Reversible encoding.
        /// </summary>
        [Test]
        public void EncodeStatusCodeBadPreservesValueReversible()
        {
            Field field = CreateStatusCodeField("StatusField", StatusCodes.BadInvalidArgument);
            PubSubEncoding.JsonDataSetMessage message = CreateDataValueMessage(field);

            string json = EncodeMessage(message, JsonEncodingType.Reversible);
            var root = JObject.Parse(json);
            JObject payload = (root["Payload"] as JObject) ?? root;

            var fieldObj = payload["StatusField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null, "Field should be present.");

            // A bad StatusCode should be encoded
            JToken valueToken = fieldObj["Value"];
            Assert.That(valueToken, Is.Not.Null, "Bad StatusCode value should be present in Reversible encoding.");
        }

        /// <summary>
        /// Verify that a non-Good StatusCode value is preserved in NonReversible encoding.
        /// </summary>
        [Test]
        public void EncodeStatusCodeBadPreservesValueNonReversible()
        {
            Field field = CreateStatusCodeField("StatusField", StatusCodes.BadInvalidArgument);
            PubSubEncoding.JsonDataSetMessage message = CreateDataValueMessage(field);

            string json = EncodeMessage(message, JsonEncodingType.NonReversible);
            var root = JObject.Parse(json);
            JObject payload = (root["Payload"] as JObject) ?? root;

            var fieldObj = payload["StatusField"] as JObject;
            Assert.That(fieldObj, Is.Not.Null, "Field should be present.");

            // A bad StatusCode should be encoded
            JToken valueToken = fieldObj["Value"];
            Assert.That(valueToken, Is.Not.Null, "Bad StatusCode value should be present in NonReversible encoding.");
        }

        private static Field CreateField(string name, BuiltInType builtInType, object value)
        {
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
        }

        private static Field CreateStatusCodeField(string name, uint statusCode)
        {
            return new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = name,
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(new StatusCode(statusCode)))
            };
        }

        private static PubSubEncoding.JsonDataSetMessage CreateDataValueMessage(Field field)
        {
            var message = new PubSubEncoding.JsonDataSetMessage(new DataSet { Fields = [field] });
            // DataValue mode requires at least one of these flags
            message.SetFieldContentMask(
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp);
            return message;
        }

        private static string EncodeMessage(PubSubEncoding.JsonDataSetMessage message, JsonEncodingType encodingType)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var encoder = new JsonEncoder(
                new ServiceMessageContext(telemetry),
                encodingType);
            message.Encode(encoder);
            return encoder.CloseAndReturnText();
        }

        private static JObject GetPayloadField(string json, string fieldName)
        {
            var root = JObject.Parse(json);
            JObject payload = (root["Payload"] as JObject) ?? root;
            return payload?[fieldName] as JObject;
        }
    }
}
