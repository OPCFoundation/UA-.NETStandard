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

using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.Tests;

using DataSet = Opc.Ua.PubSub.PublishedData.DataSet;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture]
    [Category("Encoders")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class UadpNetworkMessageAdditionalTests
    {
        private const UadpNetworkMessageContentMask AllContentMask =
            UadpNetworkMessageContentMask.PublisherId |
            UadpNetworkMessageContentMask.GroupHeader |
            UadpNetworkMessageContentMask.WriterGroupId |
            UadpNetworkMessageContentMask.GroupVersion |
            UadpNetworkMessageContentMask.NetworkMessageNumber |
            UadpNetworkMessageContentMask.SequenceNumber |
            UadpNetworkMessageContentMask.PayloadHeader |
            UadpNetworkMessageContentMask.Timestamp |
            UadpNetworkMessageContentMask.PicoSeconds |
            UadpNetworkMessageContentMask.DataSetClassId |
            UadpNetworkMessageContentMask.PromotedFields;

        private static readonly ushort[] SampleWriterIds = [1, 2];

        private static readonly StatusCode[] SampleStatusCodes =
            [StatusCodes.Good, StatusCodes.Good];

        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [Test]
        public void ConstructorDataSetMessageSetsDefaults()
        {
            WriterGroupDataType writerGroup = CreateWriterGroup(UadpNetworkMessageContentMask.PublisherId);
            var messages = new List<UadpDataSetMessage>();

            var message = new UadpNetworkMessage(writerGroup, messages);

            Assert.That(message.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DataSetMessage));
            Assert.That(message.UADPVersion, Is.EqualTo(1));
        }

        [Test]
        public void ConstructorDiscoveryRequestSetsType()
        {
            var message = new UadpNetworkMessage(
                UADPNetworkMessageDiscoveryType.DataSetMetaData);

            Assert.That(message.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DiscoveryRequest));
            Assert.That(message.UADPDiscoveryType,
                Is.EqualTo(UADPNetworkMessageDiscoveryType.DataSetMetaData));
        }

        [Test]
        public void ConstructorDiscoveryResponseMetaDataSetsType()
        {
            WriterGroupDataType writerGroup = CreateWriterGroup(UadpNetworkMessageContentMask.PublisherId);
            var metadata = new DataSetMetaDataType { Name = "TestMeta" };

            var message = new UadpNetworkMessage(writerGroup, metadata);

            Assert.That(message.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DiscoveryResponse));
            Assert.That(message.UADPDiscoveryType,
                Is.EqualTo(UADPNetworkMessageDiscoveryType.DataSetMetaData));
        }

        [Test]
        public void ConstructorDiscoveryResponsePublisherEndpointsSetsType()
        {
            EndpointDescription[] endpoints = [new EndpointDescription()];

            var message = new UadpNetworkMessage(endpoints, StatusCodes.Good);

            Assert.That(message.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DiscoveryResponse));
            Assert.That(message.UADPDiscoveryType,
                Is.EqualTo(UADPNetworkMessageDiscoveryType.PublisherEndpoint));
        }

        [Test]
        public void ConstructorDiscoveryResponseWriterConfigSetsType()
        {
            WriterGroupDataType writerGroup = CreateWriterGroup(UadpNetworkMessageContentMask.PublisherId);

            var message = new UadpNetworkMessage(
                SampleWriterIds, writerGroup, SampleStatusCodes);

            Assert.That(message.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DiscoveryResponse));
            Assert.That(message.UADPDiscoveryType,
                Is.EqualTo(UADPNetworkMessageDiscoveryType.DataSetWriterConfiguration));
        }

        [Test]
        public void PublisherIdByte()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((byte)1);

            Assert.That(message.PublisherId.GetByte(), Is.EqualTo(1));
        }

        [Test]
        public void PublisherIdUInt16()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((ushort)100);

            Assert.That(message.PublisherId.GetUInt16(), Is.EqualTo(100));
        }

        [Test]
        public void PublisherIdUInt32()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((uint)1000);

            Assert.That(message.PublisherId.GetUInt32(), Is.EqualTo(1000));
        }

        [Test]
        public void PublisherIdUInt64()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((ulong)10000);

            Assert.That(message.PublisherId.GetUInt64(), Is.EqualTo(10000));
        }

        [Test]
        public void PublisherIdString()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From("publisher1");

            Assert.That(message.PublisherId.GetString(), Is.EqualTo("publisher1"));
        }

        [Test]
        public void PublisherIdSignedByteCast()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((sbyte)5);

            Assert.That(message.PublisherId.TryGet(out byte result), Is.True);
            Assert.That(result, Is.EqualTo(5));
        }

        [Test]
        public void PublisherIdSignedInt16Cast()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((short)100);

            Assert.That(message.PublisherId.TryGet(out ushort result), Is.True);
            Assert.That(result, Is.EqualTo(100));
        }

        [Test]
        public void PublisherIdSignedInt32Cast()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From(1000);

            Assert.That(message.PublisherId.TryGet(out uint result), Is.True);
            Assert.That(result, Is.EqualTo(1000));
        }

        [Test]
        public void PublisherIdSignedInt64Cast()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);
            message.PublisherId = Variant.From((long)10000);

            Assert.That(message.PublisherId.TryGet(out ulong result), Is.True);
            Assert.That(result, Is.EqualTo(10000));
        }

        [Test]
        public void SetNetworkMessageContentMaskPublisherId()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PublisherId);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.PublisherId), Is.True);
            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.ExtendedFlags1), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskGroupHeader()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.GroupHeader);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.GroupHeader), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskWriterGroupId()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.WriterGroupId);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.GroupHeader), Is.True);
            Assert.That(message.GroupFlags.HasFlag(
                GroupFlagsEncodingMask.WriterGroupId), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskTimestamp()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.Timestamp);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.ExtendedFlags1), Is.True);
            Assert.That(message.ExtendedFlags1.HasFlag(
                ExtendedFlags1EncodingMask.Timestamp), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskPicoSeconds()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PicoSeconds);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.ExtendedFlags1), Is.True);
            Assert.That(message.ExtendedFlags1.HasFlag(
                ExtendedFlags1EncodingMask.PicoSeconds), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskPromotedFields()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PromotedFields);

            Assert.That(message.ExtendedFlags1.HasFlag(
                ExtendedFlags1EncodingMask.ExtendedFlags2), Is.True);
            Assert.That(message.ExtendedFlags2.HasFlag(
                ExtendedFlags2EncodingMask.PromotedFields), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskPayloadHeader()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(
                UadpNetworkMessageContentMask.PayloadHeader);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.PayloadHeader), Is.True);
        }

        [Test]
        public void SetNetworkMessageContentMaskAll()
        {
            UadpNetworkMessage message = CreateDataSetNetworkMessage(AllContentMask);

            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.PublisherId), Is.True);
            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.GroupHeader), Is.True);
            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.PayloadHeader), Is.True);
            Assert.That(message.UADPFlags.HasFlag(
                UADPFlagsEncodingMask.ExtendedFlags1), Is.True);
            Assert.That(message.GroupFlags.HasFlag(
                GroupFlagsEncodingMask.WriterGroupId), Is.True);
            Assert.That(message.GroupFlags.HasFlag(
                GroupFlagsEncodingMask.GroupVersion), Is.True);
            Assert.That(message.GroupFlags.HasFlag(
                GroupFlagsEncodingMask.NetworkMessageNumber), Is.True);
            Assert.That(message.GroupFlags.HasFlag(
                GroupFlagsEncodingMask.SequenceNumber), Is.True);
            Assert.That(message.ExtendedFlags1.HasFlag(
                ExtendedFlags1EncodingMask.Timestamp), Is.True);
            Assert.That(message.ExtendedFlags1.HasFlag(
                ExtendedFlags1EncodingMask.PicoSeconds), Is.True);
            Assert.That(message.ExtendedFlags2.HasFlag(
                ExtendedFlags2EncodingMask.PromotedFields), Is.True);
        }

        [Test]
        public void EncodeDecodeDataSetMessageRoundTrip()
        {
            const UadpNetworkMessageContentMask contentMask =
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.GroupHeader |
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.GroupVersion |
                UadpNetworkMessageContentMask.NetworkMessageNumber |
                UadpNetworkMessageContentMask.SequenceNumber |
                UadpNetworkMessageContentMask.PayloadHeader |
                UadpNetworkMessageContentMask.Timestamp |
                UadpNetworkMessageContentMask.PicoSeconds;

            WriterGroupDataType writerGroup = CreateWriterGroup(contentMask);
            UadpDataSetMessage dataSetMessage = CreateSimpleDataSetMessage();
            var messages = new List<UadpDataSetMessage> { dataSetMessage };

            var networkMessage = new UadpNetworkMessage(writerGroup, messages);
            networkMessage.SetNetworkMessageContentMask(contentMask);
            networkMessage.PublisherId = Variant.From((ushort)100);
            networkMessage.WriterGroupId = 1;
            networkMessage.GroupVersion = 1;
            networkMessage.NetworkMessageNumber = 1;
            networkMessage.SequenceNumber = 1;

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] encoded = networkMessage.Encode(context);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded, Is.Not.Empty);

            var decodedMessage = new UadpNetworkMessage(writerGroup, []);
            decodedMessage.SetNetworkMessageContentMask(contentMask);

            List<DataSetReaderDataType> readers = CreateMatchingReaders(dataSetMessage);
            decodedMessage.Decode(context, encoded, readers);

            Assert.That(decodedMessage.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DataSetMessage));
            Assert.That(decodedMessage.PublisherId.GetUInt16(), Is.EqualTo(100));
        }

        [Test]
        public void EncodeDecodeDiscoveryRequestRoundTrip()
        {
            var message = new UadpNetworkMessage(
                UADPNetworkMessageDiscoveryType.DataSetMetaData)
            {
                PublisherId = Variant.From((ushort)50)
            };

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] encoded = message.Encode(context);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded, Is.Not.Empty);

            var decoded = new UadpNetworkMessage(
                UADPNetworkMessageDiscoveryType.DataSetMetaData);
            decoded.Decode(context, encoded, null);

            Assert.That(decoded.UADPNetworkMessageType,
                Is.EqualTo(UADPNetworkMessageType.DiscoveryRequest));
        }

        [Test]
        public void EncodeDecodeDiscoveryResponseMetaDataRoundTrip()
        {
            WriterGroupDataType writerGroup = CreateWriterGroup(UadpNetworkMessageContentMask.PublisherId);
            var metadata = new DataSetMetaDataType
            {
                Name = "TestMeta",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };

            var message = new UadpNetworkMessage(writerGroup, metadata)
            {
                PublisherId = Variant.From((ushort)10),
                DataSetWriterId = 1
            };

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] encoded = message.Encode(context);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded, Is.Not.Empty);
        }

        [Test]
        public void EncodeDecodeDiscoveryResponsePublisherEndpointsRoundTrip()
        {
            EndpointDescription[] endpoints = [new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" }];

            var message = new UadpNetworkMessage(endpoints, StatusCodes.Good)
            {
                PublisherId = Variant.From((ushort)20)
            };

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] encoded = message.Encode(context);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded, Is.Not.Empty);
        }

        [Test]
        public void EncodeDecodeDiscoveryResponseWriterConfigRoundTrip()
        {
            WriterGroupDataType writerGroup = CreateWriterGroup(UadpNetworkMessageContentMask.PublisherId);

            var message = new UadpNetworkMessage(
                SampleWriterIds, writerGroup, SampleStatusCodes)
            {
                PublisherId = Variant.From((ushort)30)
            };

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] encoded = message.Encode(context);
            Assert.That(encoded, Is.Not.Null);
            Assert.That(encoded, Is.Not.Empty);
        }

        [Test]
        public void EncodeToByteArrayMatchesStreamEncode()
        {
            const UadpNetworkMessageContentMask contentMask =
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader;

            WriterGroupDataType writerGroup = CreateWriterGroup(contentMask);
            UadpDataSetMessage dataSetMessage = CreateSimpleDataSetMessage();
            var messages = new List<UadpDataSetMessage> { dataSetMessage };

            var networkMessage = new UadpNetworkMessage(writerGroup, messages);
            networkMessage.SetNetworkMessageContentMask(contentMask);
            networkMessage.PublisherId = Variant.From((byte)1);
            networkMessage.WriterGroupId = 1;

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] fromByteArray = networkMessage.Encode(context);

            using var stream = new MemoryStream();
            networkMessage.Encode(context, stream);
            byte[] fromStream = stream.ToArray();

            Assert.That(fromByteArray, Is.EqualTo(fromStream));
        }

        [Test]
        public void DecodeWithNullReadersReturnsEarly()
        {
            const UadpNetworkMessageContentMask contentMask =
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.PayloadHeader;

            WriterGroupDataType writerGroup = CreateWriterGroup(contentMask);
            UadpDataSetMessage dataSetMessage = CreateSimpleDataSetMessage();
            var messages = new List<UadpDataSetMessage> { dataSetMessage };

            var networkMessage = new UadpNetworkMessage(writerGroup, messages);
            networkMessage.SetNetworkMessageContentMask(contentMask);
            networkMessage.PublisherId = Variant.From((byte)1);

            IServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            byte[] encoded = networkMessage.Encode(context);

            var decoded = new UadpNetworkMessage(writerGroup, []);
            decoded.SetNetworkMessageContentMask(contentMask);
            decoded.Decode(context, encoded, null);

            Assert.That(decoded.DataSetMessages, Has.Count.EqualTo(0));
        }

        private static WriterGroupDataType CreateWriterGroup(
            UadpNetworkMessageContentMask contentMask)
        {
            return new WriterGroupDataType
            {
                WriterGroupId = 1,
                MessageSettings = new ExtensionObject(
                    new UadpWriterGroupMessageDataType
                    {
                        NetworkMessageContentMask = (uint)contentMask
                    })
            };
        }

        private static UadpNetworkMessage CreateDataSetNetworkMessage(
            UadpNetworkMessageContentMask contentMask)
        {
            WriterGroupDataType writerGroup = CreateWriterGroup(contentMask);
            var message = new UadpNetworkMessage(writerGroup, []);
            message.SetNetworkMessageContentMask(contentMask);
            return message;
        }

        private static UadpDataSetMessage CreateSimpleDataSetMessage()
        {
            var field = new Field
            {
                FieldMetaData = new FieldMetaData
                {
                    Name = "Int32Field",
                    BuiltInType = (byte)BuiltInType.Int32
                },
                Value = new DataValue(Variant.From(42))
            };

            var dataSet = new DataSet("TestDataSet")
            {
                Fields = [field]
            };

            var dataSetMessage = new UadpDataSetMessage(dataSet);
            dataSetMessage.SetFieldContentMask(DataSetFieldContentMask.None);
            dataSetMessage.SetMessageContentMask(
                UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion);
            dataSetMessage.MetaDataVersion = new ConfigurationVersionDataType
            {
                MajorVersion = 1,
                MinorVersion = 1
            };
            dataSetMessage.DataSetWriterId = 1;

            return dataSetMessage;
        }

        private static List<DataSetReaderDataType> CreateMatchingReaders(
            UadpDataSetMessage dataSetMessage)
        {
            var metaData = new DataSetMetaDataType
            {
                ConfigurationVersion = dataSetMessage.MetaDataVersion,
                Fields = [dataSetMessage.DataSet.Fields[0].FieldMetaData]
            };

            var reader = new DataSetReaderDataType
            {
                DataSetWriterId = dataSetMessage.DataSetWriterId,
                WriterGroupId = 1,
                DataSetMetaData = metaData,
                MessageSettings = new ExtensionObject(
                    new UadpDataSetReaderMessageDataType
                    {
                        DataSetMessageContentMask = (uint)(
                            UadpDataSetMessageContentMask.SequenceNumber |
                            UadpDataSetMessageContentMask.MajorVersion |
                            UadpDataSetMessageContentMask.MinorVersion),
                        NetworkMessageContentMask = (uint)(
                            UadpNetworkMessageContentMask.PublisherId |
                            UadpNetworkMessageContentMask.GroupHeader |
                            UadpNetworkMessageContentMask.WriterGroupId |
                            UadpNetworkMessageContentMask.GroupVersion |
                            UadpNetworkMessageContentMask.NetworkMessageNumber |
                            UadpNetworkMessageContentMask.SequenceNumber |
                            UadpNetworkMessageContentMask.PayloadHeader |
                            UadpNetworkMessageContentMask.Timestamp |
                            UadpNetworkMessageContentMask.PicoSeconds)
                    })
            };

            return [reader];
        }
    }
}
