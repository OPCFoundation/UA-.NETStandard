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
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;
using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture(Description = "Extended coverage tests for PubSubJsonDecoder and JsonNetworkMessage decoding")]
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
            string json = @"{
                ""MessageId"": ""msg-1"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""BoolVal"": true
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "BoolVal",
                    BuiltInType = (byte)BuiltInType.Boolean,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarSByte()
        {
            string json = @"{
                ""MessageId"": ""msg-sb"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""SByteVal"": -50
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "SByteVal",
                    BuiltInType = (byte)BuiltInType.SByte,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarByte()
        {
            string json = @"{
                ""MessageId"": ""msg-b"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""ByteVal"": 200
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "ByteVal",
                    BuiltInType = (byte)BuiltInType.Byte,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarInt16()
        {
            string json = @"{
                ""MessageId"": ""msg-i16"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Int16Val"": -1000
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "Int16Val",
                    BuiltInType = (byte)BuiltInType.Int16,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarUInt16()
        {
            string json = @"{
                ""MessageId"": ""msg-u16"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""UInt16Val"": 60000
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "UInt16Val",
                    BuiltInType = (byte)BuiltInType.UInt16,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarInt32()
        {
            string json = @"{
                ""MessageId"": ""msg-i32"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Int32Val"": -100000
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "Int32Val",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarUInt32()
        {
            string json = @"{
                ""MessageId"": ""msg-u32"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""UInt32Val"": 4000000000
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "UInt32Val",
                    BuiltInType = (byte)BuiltInType.UInt32,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarInt64()
        {
            string json = @"{
                ""MessageId"": ""msg-i64"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Int64Val"": -999999999999
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "Int64Val",
                    BuiltInType = (byte)BuiltInType.Int64,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarUInt64()
        {
            string json = @"{
                ""MessageId"": ""msg-u64"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""UInt64Val"": 999999999999
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "UInt64Val",
                    BuiltInType = (byte)BuiltInType.UInt64,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarFloat()
        {
            string json = @"{
                ""MessageId"": ""msg-f"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""FloatVal"": 3.14
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "FloatVal",
                    BuiltInType = (byte)BuiltInType.Float,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarDouble()
        {
            string json = @"{
                ""MessageId"": ""msg-d"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""DoubleVal"": 2.718281828
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "DoubleVal",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarString()
        {
            string json = @"{
                ""MessageId"": ""msg-s"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""StrVal"": ""hello""
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "StrVal",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarDateTime()
        {
            string json = @"{
                ""MessageId"": ""msg-dt"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""DateVal"": ""2024-01-15T10:30:00Z""
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "DateVal",
                    BuiltInType = (byte)BuiltInType.DateTime,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
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

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "GuidVal",
                    BuiltInType = (byte)BuiltInType.Guid,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarByteString()
        {
            string base64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
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

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "ByteStrVal",
                    BuiltInType = (byte)BuiltInType.ByteString,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarNodeId()
        {
            string json = @"{
                ""MessageId"": ""msg-nid"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""NodeIdVal"": ""i=1234""
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "NodeIdVal",
                    BuiltInType = (byte)BuiltInType.NodeId,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarExpandedNodeId()
        {
            string json = @"{
                ""MessageId"": ""msg-enid"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""EnidVal"": ""i=5678""
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "EnidVal",
                    BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarStatusCode()
        {
            string json = @"{
                ""MessageId"": ""msg-sc"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""StatusVal"": 0
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "StatusVal",
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarQualifiedName()
        {
            string json = @"{
                ""MessageId"": ""msg-qn"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""QnVal"": ""TestQN""
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "QnVal",
                    BuiltInType = (byte)BuiltInType.QualifiedName,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarLocalizedText()
        {
            string json = @"{
                ""MessageId"": ""msg-lt"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""LtVal"": ""Hello World""
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "LtVal",
                    BuiltInType = (byte)BuiltInType.LocalizedText,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataScalarEnumeration()
        {
            string json = @"{
                ""MessageId"": ""msg-enum"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""EnumVal"": 3
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "EnumVal",
                    BuiltInType = (byte)BuiltInType.Enumeration,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeDataSetMessageWithDataSetHeader()
        {
            string json = @"{
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

            var reader = CreateDataSetReaderWithHeader("P1", 5,
                new FieldMetaData
                {
                    Name = "Temp",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeDataSetMessageFiltersByDataSetWriterId()
        {
            string json = @"{
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

            var reader = CreateDataSetReaderWithHeader("P1", 99,
                new FieldMetaData
                {
                    Name = "Val",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.EqualTo(0));
        }

        [Test]
        public void DecodeDataSetWithMissingFieldReturnsNullVariant()
        {
            string json = @"{
                ""MessageId"": ""msg-miss"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Field1"": 42
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
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

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeMissingStatusCodeFieldReturnsGood()
        {
            string json = @"{
                ""MessageId"": ""msg-goodsc"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {}
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "StatusField",
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodePayloadWithExtraFieldsFilteredOut()
        {
            string json = @"{
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

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "Known",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.EqualTo(0));
        }

        [Test]
        public void DecodeDataValueEncodingWithStatusCodeAndTimestamps()
        {
            string json = @"{
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

            var reader = CreateDataSetReaderDataValue("P1", 0,
                new FieldMetaData
                {
                    Name = "Temp",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeDataValueEncodingWithPicoseconds()
        {
            string json = @"{
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

            var reader = CreateDataSetReaderDataValueWithPicos("P1", 0,
                new FieldMetaData
                {
                    Name = "Val",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeDataValueEncodingWithMissingValueForStatusCode()
        {
            string json = @"{
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

            var reader = CreateDataSetReaderDataValue("P1", 0,
                new FieldMetaData
                {
                    Name = "StatusField",
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeNetworkMessageWithSingleDataSetNoHeader()
        {
            string json = @"{
                ""Temperature"": 25.5
            }";

            var reader = CreateDataSetReaderNoHeader("", 0,
                new FieldMetaData
                {
                    Name = "Temperature",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);
            networkMessage.Decode(m_context, messageBytes, new[] { reader });

            Assert.That(networkMessage, Is.Not.EqualTo(default(PubSubEncoding.JsonNetworkMessage)));
        }

        [Test]
        public void DecodeNetworkMessageWithMultipleMessages()
        {
            string json = @"{
                ""MessageId"": ""msg-multi"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [
                    { ""Payload"": { ""F1"": 1 } },
                    { ""Payload"": { ""F1"": 2 } }
                ]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "F1",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void DecodeNetworkMessageWithMultipleReadersMatchesByPublisherId()
        {
            string json = @"{
                ""MessageId"": ""msg-mr"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""PubA"",
                ""Messages"": [{
                    ""Payload"": {
                        ""Val"": 42
                    }
                }]
            }";

            var readerA = CreateDataSetReader("PubA", 0,
                new FieldMetaData
                {
                    Name = "Val",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            var readerB = CreateDataSetReader("PubB", 0,
                new FieldMetaData
                {
                    Name = "Val",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.Scalar
                });

            var networkMessage = new PubSubEncoding.JsonNetworkMessage();
            byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(json);
            networkMessage.Decode(m_context, messageBytes, new[] { readerA, readerB });

            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void DecodeRawDataArrayInt32()
        {
            string json = @"{
                ""MessageId"": ""msg-arr"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""IntArr"": [1, 2, 3]
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "IntArr",
                    BuiltInType = (byte)BuiltInType.Int32,
                    ValueRank = ValueRanks.OneDimension
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataArrayString()
        {
            string json = @"{
                ""MessageId"": ""msg-sarr"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""StrArr"": [""a"", ""b"", ""c""]
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "StrArr",
                    BuiltInType = (byte)BuiltInType.String,
                    ValueRank = ValueRanks.OneDimension
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataArrayDouble()
        {
            string json = @"{
                ""MessageId"": ""msg-darr"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""DblArr"": [1.1, 2.2, 3.3]
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "DblArr",
                    BuiltInType = (byte)BuiltInType.Double,
                    ValueRank = ValueRanks.OneDimension
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeRawDataArrayBoolean()
        {
            string json = @"{
                ""MessageId"": ""msg-barr"",
                ""MessageType"": ""ua-data"",
                ""PublisherId"": ""P1"",
                ""Messages"": [{
                    ""Payload"": {
                        ""BoolArr"": [true, false, true]
                    }
                }]
            }";

            var reader = CreateDataSetReader("P1", 0,
                new FieldMetaData
                {
                    Name = "BoolArr",
                    BuiltInType = (byte)BuiltInType.Boolean,
                    ValueRank = ValueRanks.OneDimension
                });

            var networkMessage = DecodeNetworkMessage(json, reader);
            Assert.That(networkMessage.DataSetMessages.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DecodeMetaDataMessageProducesMetaData()
        {
            string json = @"{
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
            networkMessage.Decode(m_context, messageBytes, new DataSetReaderDataType[0]);

            Assert.That(networkMessage.IsMetaDataMessage, Is.True);
            Assert.That(networkMessage.DataSetWriterId, Is.EqualTo((ushort)10));
        }

        [Test]
        public void DecoderReadSByteReturnsCorrectValue()
        {
            string json = "{\"Val\": -50}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            sbyte result = decoder.ReadSByte("Val");
            Assert.That(result, Is.EqualTo((sbyte)-50));
        }

        [Test]
        public void DecoderReadInt16ReturnsCorrectValue()
        {
            string json = "{\"Val\": -30000}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            short result = decoder.ReadInt16("Val");
            Assert.That(result, Is.EqualTo((short)-30000));
        }

        [Test]
        public void DecoderReadUInt32ReturnsCorrectValue()
        {
            string json = "{\"Val\": 4000000000}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            uint result = decoder.ReadUInt32("Val");
            Assert.That(result, Is.EqualTo((uint)4000000000));
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
            string json = "{\"Val\": \"2024-06-15T12:30:00Z\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadDateTime("Val");
            Assert.That(((DateTime)result).Year, Is.EqualTo(2024));
        }

        [Test]
        public void DecoderReadNodeIdReturnsValue()
        {
            string json = "{\"Val\": \"i=1234\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            NodeId result = decoder.ReadNodeId("Val");
            Assert.That(result, Is.Not.EqualTo(NodeId.Null));
        }

        [Test]
        public void DecoderReadExpandedNodeIdReturnsValue()
        {
            string json = "{\"Val\": \"i=5678\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ExpandedNodeId result = decoder.ReadExpandedNodeId("Val");
            Assert.That(result, Is.Not.EqualTo(ExpandedNodeId.Null));
        }

        [Test]
        public void DecoderReadQualifiedNameReturnsValue()
        {
            string json = "{\"Val\": \"TestQN\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            QualifiedName result = decoder.ReadQualifiedName("Val");
            Assert.That(result, Is.Not.EqualTo(QualifiedName.Null));
        }

        [Test]
        public void DecoderReadLocalizedTextReturnsValue()
        {
            string json = "{\"Val\": \"Hello World\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            LocalizedText result = decoder.ReadLocalizedText("Val");
            Assert.That(result, Is.Not.EqualTo(LocalizedText.Null));
        }

        [Test]
        public void DecoderReadDiagnosticInfoReturnsValue()
        {
            string json = "{\"Val\": {}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Assert.DoesNotThrow(() => decoder.ReadDiagnosticInfo("Val"));
        }

        [Test]
        public void DecoderReadInt32ArrayReturnsCorrectValues()
        {
            string json = "{\"Arr\": [10, 20, 30]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadInt32Array("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo(10));
        }

        [Test]
        public void DecoderReadStringArrayReturnsCorrectValues()
        {
            string json = "{\"Arr\": [\"a\", \"b\", \"c\"]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadStringArray("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadDoubleArrayReturnsCorrectValues()
        {
            string json = "{\"Arr\": [1.1, 2.2, 3.3]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadDoubleArray("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadBooleanArrayReturnsCorrectValues()
        {
            string json = "{\"Arr\": [true, false, true]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadBooleanArray("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadFloatArrayReturnsCorrectValues()
        {
            string json = "{\"Arr\": [1.1, 2.2]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadFloatArray("Arr");
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecoderReadUInt16ArrayReturnsCorrectValues()
        {
            string json = "{\"Arr\": [100, 200, 300]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadUInt16Array("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadInt64ArrayReturnsCorrectValues()
        {
            string json = "{\"Arr\": [-1, 0, 1]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadInt64Array("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadUInt64ArrayReturnsCorrectValues()
        {
            string json = "{\"Arr\": [0, 999]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadUInt64Array("Arr");
            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void DecoderReadByteArrayReturnsCorrectValues()
        {
            string json = "{\"Arr\": [1, 2, 3]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadByteArray("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadSByteArrayReturnsCorrectValues()
        {
            string json = "{\"Arr\": [-1, 0, 1]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadSByteArray("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadInt16ArrayReturnsCorrectValues()
        {
            string json = "{\"Arr\": [-100, 0, 100]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadInt16Array("Arr");
            Assert.That(result.Count, Is.EqualTo(3));
        }

        [Test]
        public void DecoderReadUInt32ArrayReturnsCorrectValues()
        {
            string json = "{\"Arr\": [0, 1000, 2000]}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var result = decoder.ReadUInt32Array("Arr");
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
            string json = "{}";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(buffer);

            Assert.DoesNotThrow(() =>
                PubSubJsonDecoder.DecodeMessage<ReadResponse>(segment, m_context));
        }

        [Test]
        public void DecoderDecodeMessageFromArraySegmentNullContextThrows()
        {
            string json = "{}";
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(buffer);

            Assert.Throws<ArgumentNullException>(() =>
                PubSubJsonDecoder.DecodeMessage<ReadResponse>(segment, null));
        }

        [Test]
        public void DecoderReadExtensionObjectReturnsValue()
        {
            string json = "{\"EO\": {}}";
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
            string json = "{\"A\": 1}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            bool pushed = decoder.PushStructure("NonExistent");
            Assert.That(pushed, Is.False);
        }

        [Test]
        public void DecoderPushArrayOutOfBoundsReturnsFalse()
        {
            string json = "{\"Arr\": [1]}";
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
                new Field
                {
                    FieldMetaData = new FieldMetaData
                    {
                        Name = "IntVal",
                        BuiltInType = (byte)BuiltInType.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    Value = new DataValue(new Variant(42))
                },
                new Field
                {
                    FieldMetaData = new FieldMetaData
                    {
                        Name = "DblVal",
                        BuiltInType = (byte)BuiltInType.Double,
                        ValueRank = ValueRanks.Scalar
                    },
                    Value = new DataValue(new Variant(3.14))
                },
                new Field
                {
                    FieldMetaData = new FieldMetaData
                    {
                        Name = "StrVal",
                        BuiltInType = (byte)BuiltInType.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    Value = new DataValue(new Variant("test"))
                },
                new Field
                {
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
                new List<PubSubEncoding.JsonDataSetMessage> { message });
            networkMessage.SetNetworkMessageContentMask(
                JsonNetworkMessageContentMask.NetworkMessageHeader |
                JsonNetworkMessageContentMask.SingleDataSetMessage |
                JsonNetworkMessageContentMask.PublisherId);
            networkMessage.PublisherId = "RTPub";

            byte[] encoded = networkMessage.Encode(m_context);

            var reader = CreateDataSetReader("RTPub", 0,
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
            decoded.Decode(m_context, encoded, new[] { reader });

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
                new DataSet { Fields = new Field[] { field } });
            message.SetFieldContentMask(DataSetFieldContentMask.None);

            var networkMessage = new PubSubEncoding.JsonNetworkMessage(
                writerGroup,
                new List<PubSubEncoding.JsonDataSetMessage> { message });
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
            decoded.Decode(m_context, encoded, new DataSetReaderDataType[0]);

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
            networkMessage.Decode(m_context, messageBytes, new[] { reader });
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
