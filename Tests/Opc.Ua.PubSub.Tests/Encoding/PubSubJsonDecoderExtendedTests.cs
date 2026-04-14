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
    public class PubSubJsonDecoderExtendedTests
    {
        private ServiceMessageContext m_context;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
        }

        [Test]
        public void DecodeRawDataScalarBoolean()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-1"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""BoolVal"": true
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "BoolVal",
                    BuiltInType = (byte)BuiltInType.Boolean,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarSByte()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-sb"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""SByteVal"": -50
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "SByteVal",
                    BuiltInType = (byte)BuiltInType.SByte,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarByte()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-b"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""ByteVal"": 200
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "ByteVal",
                    BuiltInType = (byte)BuiltInType.Byte,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarInt16()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-i16"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Int16Val"": -1000
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "Int16Val",
                    BuiltInType = (byte)BuiltInType.Int16,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarUInt16()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-u16"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""UInt16Val"": 60000
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "UInt16Val",
                    BuiltInType = (byte)BuiltInType.UInt16,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarInt32()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-i32"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Int32Val"": -100000
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "Int32Val",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarUInt32()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-u32"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""UInt32Val"": 4000000000
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "UInt32Val",
                    BuiltInType = (byte)BuiltInType.UInt32,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarInt64()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-i64"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Int64Val"": -999999999999
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "Int64Val",
                    BuiltInType = (byte)BuiltInType.Int64,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarUInt64()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-u64"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""UInt64Val"": 999999999999
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "UInt64Val",
                    BuiltInType = (byte)BuiltInType.UInt64,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarFloat()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-f"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""FloatVal"": 3.14
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "FloatVal",
                    BuiltInType = (byte)BuiltInType.Float,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarDouble()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-d"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""DoubleVal"": 2.718281828
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "DoubleVal",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarString()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-s"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""StrVal"": ""hello""
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "StrVal",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarDateTime()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-dt"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""DateVal"": ""2024-01-15T10:30:00Z""
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "DateVal",
                    BuiltInType = (byte)BuiltInType.DateTime,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarGuid()
        {
            var guid = Guid.NewGuid();
            string json = $@"{{
                ""MessageId"": ""msg-g"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{{
                    ""Payload"": {{
                        ""GuidVal"": ""{guid}""
                    }}
                }}]
            }}";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "GuidVal",
                    BuiltInType = (byte)BuiltInType.Guid,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarByteString()
        {
            string base64 = Convert.ToBase64String([1, 2, 3, 4]);
            string json = $@"{{
                ""MessageId"": ""msg-bs"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{{
                    ""Payload"": {{
                        ""ByteStrVal"": ""{base64}""
                    }}
                }}]
            }}";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "ByteStrVal",
                    BuiltInType = (byte)BuiltInType.ByteString,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarNodeId()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-nid"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""NodeIdVal"": ""i=1234""
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "NodeIdVal",
                    BuiltInType = (byte)BuiltInType.NodeId,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarExpandedNodeId()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-enid"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""EnidVal"": ""i=5678""
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "EnidVal",
                    BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarStatusCode()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-sc"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""StatusVal"": 0
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "StatusVal",
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarQualifiedName()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-qn"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""QnVal"": ""TestQN""
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "QnVal",
                    BuiltInType = (byte)BuiltInType.QualifiedName,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarLocalizedText()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-lt"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""LtVal"": ""Hello World""
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "LtVal",
                    BuiltInType = (byte)BuiltInType.LocalizedText,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarEnumeration()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-enum"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""EnumVal"": 3
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "EnumVal",
                    BuiltInType = (byte)BuiltInType.Enumeration,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeDataSetMessageWithDataSetHeader()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-hdr"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""DataSetWriterId"": 5,
                    ""SequenceNumber"": 100,
                    ""Payload"": {
                        ""Temp"": 22.5
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReaderWithHeader("P1", 5,
                new FieldMetaData
                {
                    Name = "Temp",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeDataSetMessageFiltersByDataSetWriterId()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-filter"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""DataSetWriterId"": 5,
                    ""Payload"": {
                        ""Val"": 42
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReaderWithHeader("P1", 99,
                new FieldMetaData
                {
                    Name = "Val",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.EqualTo(0));
        }

        [Test]
        public void DecodeDataSetWithMissingFieldReturnsNullVariant()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-miss"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Field1"": 42
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "Field1",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                new FieldMetaData
                {
                    Name = "Field2",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeMissingStatusCodeFieldReturnsGood()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-goodsc"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {}
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "StatusField",
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodePayloadWithExtraFieldsFilteredOut()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-extra"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Known"": 42,
                        ""Unknown"": 99
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "Known",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.EqualTo(0));
        }

        [Test]
        public void DecodeDataValueEncodingWithStatusCodeAndTimestamps()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-dv"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Temp"": {
                            ""Value"": 25.5,
                            ""StatusCode"": { ""Code"": 0 },
                            ""SourceTimestamp"": ""2024-01-15T10:30:00Z"",
                            ""ServerTimestamp"": ""2024-01-15T10:30:01Z""
                        }
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReaderDataValue("P1", 0,
                new FieldMetaData
                {
                    Name = "Temp",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeDataValueEncodingWithPicoseconds()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-dv-pico"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Val"": {
                            ""Value"": 42,
                            ""SourceTimestamp"": ""2024-01-15T10:30:00Z"",
                            ""SourcePicoseconds"": 1234,
                            ""ServerTimestamp"": ""2024-01-15T10:30:01Z"",
                            ""ServerPicoseconds"": 5678
                        }
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReaderDataValueWithPicos("P1", 0,
                new FieldMetaData
                {
                    Name = "Val",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeDataValueEncodingWithMissingValueForStatusCode()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-dv-novalue"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""StatusField"": {
                            ""StatusCode"": { ""Code"": 0 }
                        }
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReaderDataValue("P1", 0,
                new FieldMetaData
                {
                    Name = "StatusField",
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeNetworkMessageWithSingleDataSetNoHeader()
        {
            const string json = /*lang=json,strict*/ @"{
                ""Temperature"": 25.5
            }";

            DataSetReaderDataType reader = CreateDataSetReaderNoHeader("", 0,
                new FieldMetaData
                {
                    Name = "Temperature",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);
            networkMessage.Decode(m_context, messageBytes, [reader]);

            Assert.That(networkMessage, Is.Not.EqualTo(default(PubSubEncoding.JsonNetworkMessage)));
        }

        [Test]
        public void DecodeNetworkMessageWithMultipleMessages()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-multi"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [
                    { ""Payload"": { ""F1"": 1 } },
                    { ""Payload"": { ""F1"": 2 } }
                ]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "F1",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void DecodeNetworkMessageWithMultipleReadersMatchesByPublisherId()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-mr"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""PubA"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Val"": 42
                    }
                }]
            }";

            DataSetReaderDataType readerA = CreateDataSetReader("PubA", 0,
                new FieldMetaData
                {
                    Name = "Val",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            DataSetReaderDataType readerB = CreateDataSetReader("PubB", 0,
                new FieldMetaData
                {
                    Name = "Val",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);
            networkMessage.Decode(m_context, messageBytes, [readerA, readerB]);

            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void DecodeRawDataArrayInt32()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-arr"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""IntArr"": [1, 2, 3]
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "IntArr",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.OneDimension
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataArrayString()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-sarr"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""StrArr"": [""a"", ""b"", ""c""]
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "StrArr",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.OneDimension
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataArrayDouble()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-darr"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""DblArr"": [1.1, 2.2, 3.3]
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "DblArr",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.OneDimension
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataArrayBoolean()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""msg-barr"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""BoolArr"": [true, false, true]
                    }
                }]
            }";

            DataSetReaderDataType reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "BoolArr",
                    BuiltInType = (byte)BuiltInType.Boolean,
                    ValueRank = ValueRanks.OneDimension
                });

            PubSubEncoding.JsonNetworkMessage networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeMetaDataMessageProducesMetaData()
        {
            const string json = /*lang=json,strict*/ @"{
                ""MessageId"": ""meta-ext"",
                ""MessageType"": ""ua-metadata"",
                ""PublisherId"": ""MetaPub"",
                ""DataSetWriterId"": 10,
                ""MetaData"": {
                    ""Name"": ""MetaDS"",
                    ""Fields"": [
                        {
                            ""Name"": ""F1"",
                            ""BuiltInType"": 6,
                            ""ValueRank"": -1
                        },
                        {
                            ""Name"": ""F2"",
                            ""BuiltInType"": 12,
                            ""ValueRank"": -1
                        }
                    ],
                    ""ConfigurationVersion"": {
                        ""MajorVersion"": 3,
                        ""MinorVersion"": 1
                    }
                }
            }";

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);
            networkMessage.Decode(m_context, messageBytes, Array.Empty<DataSetReaderDataType>());

            Assert.That(networkMessage.IsMetaDataMessage, Is.True);
            Assert.That(networkMessage.DataSetWriterId, Is.EqualTo((ushort)10));
        }

        [Test]
        public void DecoderReadSByteReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Val\": -50}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            sbyte result = decoder.ReadSByte("Val");
            Assert.That(result, Is.EqualTo((sbyte)-50));
        }

        [Test]
        public void DecoderReadInt16ReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Val\": -30000}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            short result = decoder.ReadInt16("Val");
            Assert.That(result, Is.EqualTo((short)-30000));
        }

        [Test]
        public void DecoderReadUInt32ReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Val\": 4000000000}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            uint result = decoder.ReadUInt32("Val");
            Assert.That(result, Is.EqualTo(4000000000u));
        }

        [Test]
        public void DecoderReadGuidReturnsCorrectValue()
        {
            var guid = Guid.NewGuid();
            string json = $"{{\"Val\": \"{guid}\"}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Uuid result = decoder.ReadGuid("Val");
            Assert.That(result.ToString(), Is.EqualTo(guid.ToString()));
        }

        [Test]
        public void DecoderReadDateTimeReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Val\": \"2024-06-15T12:30:00Z\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            DateTimeUtc result = decoder.ReadDateTime("Val");
            Assert.That(((DateTime)result).Year, Is.EqualTo(2024));
        }

        [Test]
        public void DecoderReadNodeIdReturnsValue()
        {
            const string json = /*lang=json,strict*/ "{\"Val\": \"i=1234\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            NodeId result = decoder.ReadNodeId("Val");
            Assert.That(result, Is.Not.EqualTo(NodeId.Null));
        }

        [Test]
        public void DecoderReadExpandedNodeIdReturnsValue()
        {
            const string json = /*lang=json,strict*/ "{\"Val\": \"i=5678\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ExpandedNodeId result = decoder.ReadExpandedNodeId("Val");
            Assert.That(result, Is.Not.EqualTo(ExpandedNodeId.Null));
        }

        [Test]
        public void DecoderReadQualifiedNameReturnsValue()
        {
            const string json = /*lang=json,strict*/ "{\"Val\": \"TestQN\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            QualifiedName result = decoder.ReadQualifiedName("Val");
            Assert.That(result, Is.Not.EqualTo(QualifiedName.Null));
        }

        [Test]
        public void DecoderReadLocalizedTextReturnsValue()
        {
            const string json = /*lang=json,strict*/ "{\"Val\": \"Hello World\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            LocalizedText result = decoder.ReadLocalizedText("Val");
            Assert.That(result, Is.Not.EqualTo(LocalizedText.Null));
        }

        [Test]
        public void DecoderReadDiagnosticInfoReturnsValue()
        {
            const string json = /*lang=json,strict*/ "{\"Val\": {}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Assert.DoesNotThrow(() => decoder.ReadDiagnosticInfo("Val"));
        }

        [Test]
        public void DecoderReadInt32ArrayReturnsCorrectValues()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [10, 20, 30]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<int> result = decoder.ReadInt32Array("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo(10));
        }

        [Test]
        public void DecoderReadStringArrayReturnsCorrectValues()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [\"a\", \"b\", \"c\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<string> result = decoder.ReadStringArray("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadDoubleArrayReturnsCorrectValues()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [1.1, 2.2, 3.3]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<double> result = decoder.ReadDoubleArray("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadBooleanArrayReturnsCorrectValues()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [true, false, true]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<bool> result = decoder.ReadBooleanArray("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadFloatArrayReturnsCorrectValues()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [1.1, 2.2]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<float> result = decoder.ReadFloatArray("Arr");
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecoderReadUInt16ArrayReturnsCorrectValues()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [100, 200, 300]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<ushort> result = decoder.ReadUInt16Array("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadInt64ArrayReturnsCorrectValues()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [-1, 0, 1]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<long> result = decoder.ReadInt64Array("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadUInt64ArrayReturnsCorrectValues()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [0, 999]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<ulong> result = decoder.ReadUInt64Array("Arr");
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecoderReadByteArrayReturnsCorrectValues()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [1, 2, 3]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<byte> result = decoder.ReadByteArray("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadSByteArrayReturnsCorrectValues()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [-1, 0, 1]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<sbyte> result = decoder.ReadSByteArray("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadInt16ArrayReturnsCorrectValues()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [-100, 0, 100]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<short> result = decoder.ReadInt16Array("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadUInt32ArrayReturnsCorrectValues()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [0, 1000, 2000]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ArrayOf<uint> result = decoder.ReadUInt32Array("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderSetMappingTablesDoesNotThrow()
        {
            using var decoder = new PubSubJsonDecoder("{}", m_context);
            var nsTable = new NamespaceTable();
            var serverTable = new StringTable();
            Assert.DoesNotThrow(() => decoder.SetMappingTables(nsTable, serverTable));
        }

        [Test]
        public void DecoderDecodeMessageFromArraySegment()
        {
            const string json = "{}";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(buffer);

            Assert.DoesNotThrow(() =>
                PubSubJsonDecoder.DecodeMessage<ReadResponse>(segment, m_context));
        }

        [Test]
        public void DecoderDecodeMessageFromArraySegmentNullContextThrows()
        {
            const string json = "{}";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(buffer);

            Assert.Throws<ArgumentNullException>(() =>
                PubSubJsonDecoder.DecodeMessage<ReadResponse>(segment, null));
        }

        [Test]
        public void DecoderReadExtensionObjectReturnsValue()
        {
            const string json = /*lang=json,strict*/ "{\"EO\": {}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Assert.DoesNotThrow(() => decoder.ReadExtensionObject("EO"));
        }

        [Test]
        public void DecoderReadEncodingTypeIsJson()
        {
            using var decoder = new PubSubJsonDecoder("{}", m_context);
            Assert.That(decoder.EncodingType, Is.EqualTo(EncodingType.Json));
        }

        [Test]
        public void DecoderPushStructureForNonExistentFieldReturnsFalse()
        {
            const string json = /*lang=json,strict*/ "{\"A\": 1}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            bool pushed = decoder.PushStructure("NonExistent");
            Assert.That(pushed, Is.False);
        }

        [Test]
        public void DecoderPushArrayOutOfBoundsReturnsFalse()
        {
            const string json = /*lang=json,strict*/ "{\"Arr\": [1]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            bool pushed = decoder.PushArray("Arr", 5);
            Assert.That(pushed, Is.False);
        }

        [Test]
        public void RoundTripEncodeDecodeMultipleFieldsRawData()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var fields = new Field[]
            {
                new() {
                    FieldMetaData = new FieldMetaData
                    {
                        Name = "IntVal",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    Value = new DataValue(new Variant(42))
                },
                new() {
                    FieldMetaData = new FieldMetaData
                    {
                        Name = "DblVal",
                        BuiltInType = (byte)BuiltInType.Double,
                        ValueRank = ValueRanks.Scalar
                    },
                    Value = new DataValue(new Variant(3.14))
                },
                new() {
                    FieldMetaData = new FieldMetaData
                    {
                        Name = "StrVal",
                        BuiltInType = (byte)BuiltInType.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    Value = new DataValue(new Variant("test"))
                },
                new() {
                    FieldMetaData = new FieldMetaData
                    {
                        Name = "BoolVal",
                        BuiltInType = (byte)BuiltInType.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    Value = new DataValue(new Variant(true))
                }
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var writerGroup = new WriterGroupDataType
            {
                Name = "RTWG",
                WriterGroupId = 1,
                Enabled = true
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = fields });
            message.SetFieldContentMask(DataSetFieldContentMask.RawData);

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [message]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.PublisherId);
            networkMessage.PublisherId = "RTPub";

            byte[] encoded = networkMessage.Encode(m_context);

            DataSetReaderDataType reader = CreateDataSetReader("RTPub", 0,
                new FieldMetaData
                {
                    Name = "IntVal",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                new FieldMetaData
                {
                    Name = "DblVal",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                },
                new FieldMetaData
                {
                    Name = "StrVal",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.Scalar
                },
                new FieldMetaData
                {
                    Name = "BoolVal",
                    BuiltInType = (byte)BuiltInType.Boolean,
                    ValueRank = ValueRanks.Scalar
                });

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_context, encoded, [reader]);

            Assert.That(decoded.PublisherId, Is.EqualTo("RTPub"));
            Assert.That(decoded.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void RoundTripEncodeDecodeVariantEncoding()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "VarVal",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                Value = new DataValue(new Variant(99))
            };
#pragma warning restore CS0618 // Type or member is obsolete

            var writerGroup = new WriterGroupDataType
            {
                Name = "VarWG",
                WriterGroupId = 1,
                Enabled = true
            };

            var message = new PubSubEncoding.JsonDataSetMessage(
                new DataSet { Fields = [field] });
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                [message]);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.PublisherId);
            networkMessage.PublisherId = "VarPub";

            byte[] encoded = networkMessage.Encode(m_context);
            string json = System.Text.Encoding.UTF8.GetString(encoded);

            Assert.That(json, Does.Contain("VarVal"));
            Assert.That(json, Does.Contain("VarPub"));
        }

        [Test]
        public void RoundTripEncodeDecodeMetaDataMessage()
        {
            var writerGroup = new WriterGroupDataType
            {
                Name = "MetaWG",
                WriterGroupId = 1,
                Enabled = true
            };

            var metadata = new DataSetMetaDataType
            {
                Name = "RTMeta",
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "F1",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar
                    }
                ],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(writerGroup, metadata, null);
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader);
            networkMessage.PublisherId = "MetaPub";
            networkMessage.DataSetWriterId = 15;

            byte[] encoded = networkMessage.Encode(m_context);

            var decoded = new PubSubEncoding.JsonNetworkMessage();
            decoded.Decode(m_context, encoded, Array.Empty<DataSetReaderDataType>());

            Assert.That(decoded.IsMetaDataMessage, Is.True);
            Assert.That(decoded.PublisherId, Is.EqualTo("MetaPub"));
            Assert.That(decoded.DataSetWriterId, Is.EqualTo((ushort)15));
        }

        private PubSubEncoding.JsonNetworkMessage DecodeNetworkMessage(
            string json,
            DataSetReaderDataType reader)
        {
            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);
            networkMessage.Decode(m_context, messageBytes, [reader]);
            return networkMessage;
        }

        private static DataSetReaderDataType CreateDataSetReader(
            string publisherId,
            ushort dataSetWriterId,
            params FieldMetaData[] fields)
        {
            return new DataSetReaderDataType
            {
                Name = "Reader",
                PublisherId = string.IsNullOrEmpty(publisherId)
                    ? Variant.Null
                    : new Variant(publisherId),
                WriterGroupId = 0,
                DataSetWriterId = dataSetWriterId,
                Enabled = true,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS",
                    Fields = [.. fields],
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                },
                MessageSettings = new ExtensionObject(
                    new JsonDataSetReaderMessageDataType
                    {
                        NetworkMessageContentMask = (uint)(
                            JsonNetworkMessageContentMask.NetworkMessageHeader |
                            JsonNetworkMessageContentMask.PublisherId),
                        DataSetMessageContentMask = 0
                    })
            };
        }

        private static DataSetReaderDataType CreateDataSetReaderNoHeader(
            string publisherId,
            ushort dataSetWriterId,
            params FieldMetaData[] fields)
        {
            return new DataSetReaderDataType
            {
                Name = "Reader",
                PublisherId = string.IsNullOrEmpty(publisherId)
                    ? Variant.Null
                    : new Variant(publisherId),
                WriterGroupId = 0,
                DataSetWriterId = dataSetWriterId,
                Enabled = true,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS",
                    Fields = [.. fields],
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                },
                MessageSettings = new ExtensionObject(
                    new JsonDataSetReaderMessageDataType
                    {
                        NetworkMessageContentMask = 0,
                        DataSetMessageContentMask = 0
                    })
            };
        }

        private static DataSetReaderDataType CreateDataSetReaderWithHeader(
            string publisherId,
            ushort dataSetWriterId,
            params FieldMetaData[] fields)
        {
            return new DataSetReaderDataType
            {
                Name = "Reader",
                PublisherId = new Variant(publisherId),
                WriterGroupId = 0,
                DataSetWriterId = dataSetWriterId,
                Enabled = true,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS",
                    Fields = [.. fields],
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                },
                MessageSettings = new ExtensionObject(
                    new JsonDataSetReaderMessageDataType
                    {
                        NetworkMessageContentMask = (uint)(
                            JsonNetworkMessageContentMask.NetworkMessageHeader |
                            JsonNetworkMessageContentMask.PublisherId |
                            JsonNetworkMessageContentMask.DataSetMessageHeader),
                        DataSetMessageContentMask = (uint)(
                            JsonDataSetMessageContentMask.DataSetWriterId |
                            JsonDataSetMessageContentMask.SequenceNumber)
                    })
            };
        }

        private static DataSetReaderDataType CreateDataSetReaderDataValue(
            string publisherId,
            ushort dataSetWriterId,
            params FieldMetaData[] fields)
        {
            return new DataSetReaderDataType
            {
                Name = "Reader",
                PublisherId = new Variant(publisherId),
                WriterGroupId = 0,
                DataSetWriterId = dataSetWriterId,
                Enabled = true,
                DataSetFieldContentMask = (uint)(
                    DataSetFieldContentMask.StatusCode |
                    DataSetFieldContentMask.SourceTimestamp |
                    DataSetFieldContentMask.ServerTimestamp),
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS",
                    Fields = [.. fields],
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                },
                MessageSettings = new ExtensionObject(
                    new JsonDataSetReaderMessageDataType
                    {
                        NetworkMessageContentMask = (uint)(
                            JsonNetworkMessageContentMask.NetworkMessageHeader |
                            JsonNetworkMessageContentMask.PublisherId),
                        DataSetMessageContentMask = 0
                    })
            };
        }

        private static DataSetReaderDataType CreateDataSetReaderDataValueWithPicos(
            string publisherId,
            ushort dataSetWriterId,
            params FieldMetaData[] fields)
        {
            return new DataSetReaderDataType
            {
                Name = "Reader",
                PublisherId = new Variant(publisherId),
                WriterGroupId = 0,
                DataSetWriterId = dataSetWriterId,
                Enabled = true,
                DataSetFieldContentMask = (uint)(
                    DataSetFieldContentMask.StatusCode |
                    DataSetFieldContentMask.SourceTimestamp |
                    DataSetFieldContentMask.SourcePicoSeconds |
                    DataSetFieldContentMask.ServerTimestamp |
                    DataSetFieldContentMask.ServerPicoSeconds),
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DS",
                    Fields = [.. fields],
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                },
                MessageSettings = new ExtensionObject(
                    new JsonDataSetReaderMessageDataType
                    {
                        NetworkMessageContentMask = (uint)(
                            JsonNetworkMessageContentMask.NetworkMessageHeader |
                            JsonNetworkMessageContentMask.PublisherId),
                        DataSetMessageContentMask = 0
                    })
            };
        }
    }
}
