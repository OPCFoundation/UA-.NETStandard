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
    public class PubSubJsonDecoderAdditionalTests
    {
        private ServiceMessageContext m_context;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
        }

        [Test]
        public void DecodeDataSetNetworkMessageWithHeader()
        {
            const string json = /*lang=json,strict*/ """
{
                "MessageId": "msg-1",
                "MessageType": "ua-data",
                "PublisherId": "TestPub",
                "Messages": [
                    {
                        "Payload": {
                            "Temperature": 22.5
                        }
                    }
                ]
            }
""";

            var reader = new DataSetReaderDataType
            {
                Enabled = true,
                Name = "Reader1",
                PublisherId = new Variant("TestPub"),
                DataSetWriterId = 0,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS1",
                    Fields =
                    [
                        new FieldMetaData
                        {
                            Name = "Temperature",
                            BuiltInType = (byte)BuiltInType.Double,
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

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);
            networkMessage.Decode(m_context, messageBytes, [reader]);

            Assert.That(networkMessage.MessageId, Is.EqualTo("msg-1"));
            Assert.That(networkMessage.PublisherId, Is.EqualTo("TestPub"));
        }

        [Test]
        public void DecodeMetaDataNetworkMessage()
        {
            const string json = /*lang=json,strict*/ """
{
                "MessageId": "meta-1",
                "MessageType": "ua-metadata",
                "PublisherId": "MetaPub",
                "DataSetWriterId": 10,
                "MetaData": {
                    "Name": "TestMetaData",
                    "Fields": [],
                    "ConfigurationVersion": {
                        "MajorVersion": 1,
                        "MinorVersion": 0
                    }
                }
            }
""";

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);
            networkMessage.Decode(m_context, messageBytes, []);

            Assert.That(networkMessage.MessageType, Is.EqualTo("ua-metadata"));
            Assert.That(networkMessage.PublisherId, Is.EqualTo("MetaPub"));
            Assert.That(networkMessage.DataSetWriterId, Is.EqualTo((ushort)10));
        }

        [Test]
        public void DecodeNetworkMessageWithNullReadersDoesNotThrow()
        {
            const string json = /*lang=json,strict*/ """
{
                "MessageId": "msg-2",
                "MessageType": "ua-data",
                "PublisherId": "Pub"
            }
""";

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);

            Assert.DoesNotThrow(() =>
                networkMessage.Decode(m_context, messageBytes, null));
        }

        [Test]
        public void DecodeNetworkMessageWithEmptyReadersDoesNotThrow()
        {
            const string json = /*lang=json,strict*/ """
{
                "MessageId": "msg-3",
                "MessageType": "ua-data"
            }
""";

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);

            Assert.DoesNotThrow(() =>
                networkMessage.Decode(
                    m_context, messageBytes, []));
        }

        [Test]
        public void DecodeNetworkMessageWithInvalidMessageType()
        {
            const string json = /*lang=json,strict*/ """
{
                "MessageId": "msg-inv",
                "MessageType": "ua-invalid"
            }
""";

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);

            Assert.DoesNotThrow(() =>
                networkMessage.Decode(
                    m_context, messageBytes, []));
        }

        [Test]
        public void DecodeNetworkMessageWithDataSetClassId()
        {
            var classId = Guid.NewGuid();
            string json = $$"""
{
                "MessageId": "msg-cls",
                "MessageType": "ua-data",
                "PublisherId": "Pub",
                "DataSetClassId": "{{classId}}"
            }
""";

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);
            networkMessage.Decode(m_context, messageBytes, []);

            Assert.That(networkMessage.DataSetClassId, Is.EqualTo(classId.ToString()));
        }

        [Test]
        public void ReadByteStringReturnsCorrectValue()
        {
            string base64 = Convert.ToBase64String([1, 2, 3]);
            string json = $"{{\"Data\": \"{base64}\"}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ByteString result = decoder.ReadByteString("Data");
            Assert.That(result.Length, Is.EqualTo(3));
        }

        [Test]
        public void ReadVariantWithReversibleEncodingReturnsVariant()
        {
            // OPC UA reversible encoding uses {"Type": <id>, "Body": <value>}
            const string json = /*lang=json,strict*/ "{\"Val\": {\"Type\": 6, \"Body\": 42}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Variant result = decoder.ReadVariant("Val");
            Assert.That(result, Is.Not.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithPlainJsonReturnsNullVariant()
        {
            // Plain JSON values without OPC UA type info return Variant.Null
            const string json = /*lang=json,strict*/ "{\"Val\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Variant result = decoder.ReadVariant("Val");
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadVariantWithNullReturnsNull()
        {
            const string json = /*lang=json,strict*/ "{\"Val\": null}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Variant result = decoder.ReadVariant("Val");
            Assert.That(result, Is.EqualTo(Variant.Null));
        }

        [Test]
        public void ReadPushArrayNavigatesIntoArrayElement()
        {
            const string json = /*lang=json,strict*/ "{\"Items\": [{\"Name\": \"First\"}, {\"Name\": \"Second\"}]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            bool pushed = decoder.PushArray("Items", 0);
            Assert.That(pushed, Is.True);

            string name = decoder.ReadString("Name");
            Assert.That(name, Is.EqualTo("First"));
            decoder.Pop();
        }

        [Test]
        public void ReadPushArrayWithSecondElementNavigatesCorrectly()
        {
            const string json = /*lang=json,strict*/ "{\"Items\": [{\"Name\": \"First\"}, {\"Name\": \"Second\"}]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            bool pushed = decoder.PushArray("Items", 1);
            Assert.That(pushed, Is.True);

            string name = decoder.ReadString("Name");
            Assert.That(name, Is.EqualTo("Second"));
            decoder.Pop();
        }

        [Test]
        public void ReadStatusCodeReturnsValue()
        {
            const string json = /*lang=json,strict*/ "{\"Status\": 0}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            StatusCode result = decoder.ReadStatusCode("Status");
            Assert.That(result.Code, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public void ReadMissingStringReturnsNull()
        {
            const string json = /*lang=json,strict*/ "{\"Other\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            string result = decoder.ReadString("Missing");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void ReadMissingBooleanReturnsDefault()
        {
            const string json = /*lang=json,strict*/ "{\"Other\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            bool result = decoder.ReadBoolean("Missing");
            Assert.That(result, Is.False);
        }

        [Test]
        public void ReadMissingDoubleReturnsDefault()
        {
            const string json = /*lang=json,strict*/ "{\"Other\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            double result = decoder.ReadDouble("Missing");
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void ReadMissingFloatReturnsDefault()
        {
            const string json = /*lang=json,strict*/ "{\"Other\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            float result = decoder.ReadFloat("Missing");
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void ReadMissingByteReturnsDefault()
        {
            const string json = "{}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            byte result = decoder.ReadByte("Missing");
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void ReadMissingUInt16ReturnsDefault()
        {
            const string json = "{}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ushort result = decoder.ReadUInt16("Missing");
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void ReadMissingUInt64ReturnsDefault()
        {
            const string json = "{}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ulong result = decoder.ReadUInt64("Missing");
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void ReadMissingInt64ReturnsDefault()
        {
            const string json = "{}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            long result = decoder.ReadInt64("Missing");
            Assert.That(result, Is.Zero);
        }

        [Test]
        public void HasFieldReturnsTrueForExistingField()
        {
            const string json = /*lang=json,strict*/ "{\"Exists\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            bool exists = decoder.HasField("Exists");
            Assert.That(exists, Is.True);
        }

        [Test]
        public void HasFieldReturnsFalseForMissingField()
        {
            const string json = /*lang=json,strict*/ "{\"Exists\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            bool exists = decoder.HasField("Missing");
            Assert.That(exists, Is.False);
        }

        [Test]
        public void EncodingTypeIsJson()
        {
            const string json = "{}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Assert.That(decoder.EncodingType, Is.EqualTo(EncodingType.Json));
        }

        [Test]
        public void UpdateNamespaceTablePropertyIsAccessible()
        {
            const string json = "{}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            decoder.UpdateNamespaceTable = true;
            Assert.That(decoder.UpdateNamespaceTable, Is.True);
        }

        [Test]
        public void PushAndPopNamespaceDoesNotThrow()
        {
            const string json = "{}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Assert.DoesNotThrow(() =>
            {
                decoder.PushNamespace("http://test.org");
                decoder.PopNamespace();
            });
        }

        [Test]
        public void ReadMultiplePrimitivesFromSameJson()
        {
            const string json = /*lang=json,strict*/ """
{
                "IntVal": 10,
                "DoubleVal": 3.14,
                "BoolVal": true,
                "StrVal": "test"
            }
""";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Assert.That(decoder.ReadInt32("IntVal"), Is.EqualTo(10));
            Assert.That(
                decoder.ReadDouble("DoubleVal"), Is.EqualTo(3.14).Within(0.001));
            Assert.That(decoder.ReadBoolean("BoolVal"), Is.True);
            Assert.That(decoder.ReadString("StrVal"), Is.EqualTo("test"));
        }

        [Test]
        public void PushStructureThenPopReturnsToParent()
        {
            const string json = /*lang=json,strict*/ "{\"Parent\": {\"Child\": 99}, \"Sibling\": 100}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            decoder.PushStructure("Parent");
            int child = decoder.ReadInt32("Child");
            decoder.Pop();

            int sibling = decoder.ReadInt32("Sibling");
            Assert.That(child, Is.EqualTo(99));
            Assert.That(sibling, Is.EqualTo(100));
        }

        [Test]
        public void DecodeMessageFromBufferWithValidJson()
        {
            const string json = "{}";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(json);

            Assert.DoesNotThrow(
                () => PubSubJsonDecoder.DecodeMessage<ReadResponse>(
                    buffer, m_context));
        }

#pragma warning disable CS0618 // Type or member is obsolete
        [Test]
        public void RoundTripEncodeDecodeDataSetMessage()
        {
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

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var writerGroup = new WriterGroupDataType
            {
                Name = "RoundTripWG",
                WriterGroupId = 1,
                Enabled = true
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [message],
                null);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.PublisherId);
            networkMessage.PublisherId = "RTPub";

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("Temperature"));
            Assert.That(json, Does.Contain("RTPub"));

            var reader = new DataSetReaderDataType
            {
                Enabled = true,
                Name = "RTReader",
                PublisherId = new Variant("RTPub"),
                DataSetWriterId = 0,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS",
                    Fields =
                    [
                        new FieldMetaData
                        {
                            Name = "Temperature",
                            BuiltInType = (byte)BuiltInType.Double,
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

            var decodedMessage = new PubSubEncoding.JsonNetworkMessage();
            decodedMessage.Decode(m_context, encoded, [reader]);

            Assert.That(decodedMessage.PublisherId, Is.EqualTo("RTPub"));
        }
#pragma warning restore CS0618 // Type or member is obsolete

        [Test]
        public void DecodeNetworkMessageWithNullPublisherIdReaderMatchesAll()
        {
            const string json = /*lang=json,strict*/ """
{
                "MessageId": "msg-np",
                "MessageType": "ua-data",
                "PublisherId": "AnyPub",
                "Messages": [
                    {
                        "Payload": {
                            "Value": 42
                        }
                    }
                ]
            }
""";

            var reader = new DataSetReaderDataType
            {
                Enabled = true,
                Name = "NullPubReader",
                PublisherId = Variant.Null,
                DataSetWriterId = 0,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS",
                    Fields =
                    [
                        new FieldMetaData
                        {
                            Name = "Value",
                            BuiltInType = (byte)BuiltInType.Int32,
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

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);
            networkMessage.Decode(m_context, messageBytes, [reader]);

            Assert.That(networkMessage.PublisherId, Is.EqualTo("AnyPub"));
        }

        [Test]
        public void DecodeNetworkMessageWithMismatchedPublisherIdIgnoresReader()
        {
            const string json = /*lang=json,strict*/ """
{
                "MessageId": "msg-mm",
                "MessageType": "ua-data",
                "PublisherId": "PubA",
                "Messages": [
                    {
                        "Payload": {
                            "Value": 42
                        }
                    }
                ]
            }
""";

            var reader = new DataSetReaderDataType
            {
                Enabled = true,
                Name = "MismatchReader",
                PublisherId = new Variant("PubB"),
                DataSetWriterId = 0,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS",
                    Fields =
                    [
                        new FieldMetaData
                        {
                            Name = "Value",
                            BuiltInType = (byte)BuiltInType.Int32,
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

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);
            networkMessage.Decode(m_context, messageBytes, [reader]);

            Assert.That(networkMessage.DataSetMessages.Count, Is.Zero);
        }

        [Test]
        public void DecodeMetaDataMessageWithDataSetWriterId()
        {
            const string json = /*lang=json,strict*/ """
{
                "MessageId": "meta-dw",
                "MessageType": "ua-metadata",
                "PublisherId": "MetaPub",
                "DataSetWriterId": 25,
                "MetaData": {
                    "Name": "MD1",
                    "Fields": [
                        {
                            "Name": "F1",
                            "BuiltInType": 6,
                            "ValueRank": -1
                        }
                    ],
                    "ConfigurationVersion": {
                        "MajorVersion": 2,
                        "MinorVersion": 1
                    }
                }
            }
""";

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);
            networkMessage.Decode(m_context, messageBytes, []);

            Assert.That(networkMessage.DataSetWriterId, Is.EqualTo((ushort)25));
            Assert.That(networkMessage.IsMetaDataMessage, Is.True);
        }

        [Test]
        public void DecodeMetaDataMessageMissingDataSetWriterId()
        {
            const string json = /*lang=json,strict*/ """
{
                "MessageId": "meta-no-dw",
                "MessageType": "ua-metadata",
                "PublisherId": "MetaPub",
                "MetaData": {
                    "Name": "MD2",
                    "Fields": [],
                    "ConfigurationVersion": {
                        "MajorVersion": 1,
                        "MinorVersion": 0
                    }
                }
            }
""";

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);

            Assert.DoesNotThrow(() =>
                networkMessage.Decode(
                    m_context, messageBytes, []));
        }

        [Test]
        public void ReadStructureWithNoMatchingFieldReturnsDefault()
        {
            const string json = /*lang=json,strict*/ "{\"A\": {\"B\": 1}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            decoder.PushStructure("A");
            int val = decoder.ReadInt32("NonExistent");
            decoder.Pop();

            Assert.That(val, Is.Zero);
        }

        [Test]
        public void ReadArrayFromJsonProducesCorrectCount()
        {
            const string json = /*lang=json,strict*/ "{\"Items\": [1, 2, 3, 4, 5]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<int> items = decoder.ReadInt32Array("Items");
            Assert.That(items.Count, Is.EqualTo(5));
        }

        [Test]
        public void ReadEmptyObjectDoesNotThrow()
        {
            const string json = "{}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Assert.DoesNotThrow(() =>
            {
                decoder.ReadInt32("Any");
                decoder.ReadString("Any");
                decoder.ReadBoolean("Any");
                decoder.ReadDouble("Any");
            });
        }

        [Test]
        public void ReadNestedArrayOfObjects()
        {
            const string json = /*lang=json,strict*/ """
{
                "Groups": [
                    {"Id": 1, "Name": "First"},
                    {"Id": 2, "Name": "Second"}
                ]
            }
""";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            bool pushed = decoder.PushArray("Groups", 1);
            Assert.That(pushed, Is.True);

            int id = decoder.ReadInt32("Id");
            string name = decoder.ReadString("Name");
            decoder.Pop();

            Assert.That(id, Is.EqualTo(2));
            Assert.That(name, Is.EqualTo("Second"));
        }

        [Test]
        public void CloseDecoderMultipleTimesDoesNotThrow()
        {
            var decoder = new PubSubJsonDecoder("{}", m_context);
            decoder.Close();
            Assert.DoesNotThrow(decoder.Close);
        }

        [Test]
        public void CloseWithCheckEofDoesNotThrow()
        {
            var decoder = new PubSubJsonDecoder("{}", m_context);
            Assert.DoesNotThrow(() => decoder.Close(false));
        }
    }
}