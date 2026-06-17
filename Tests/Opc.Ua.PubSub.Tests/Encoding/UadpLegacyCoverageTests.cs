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

namespace Opc.Ua.PubSub.Tests.Encoding
{
    /// <summary>
    /// Legacy UADP message flag coverage for the public compatibility
    /// wrappers retained in <see cref="Opc.Ua.PubSub.Encoding"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.2")]
    [TestSpec("7.2.4")]
    public sealed class UadpLegacyCoverageTests
    {
        [Test]
        public void NetworkMessagePublisherIdSetterMapsSupportedTypes()
        {
            var message = new UadpNetworkMessage(
                new WriterGroupDataType(),
                new List<UadpDataSetMessage>());

            message.PublisherId = new Variant((byte)1);
            Assert.That(message.PublisherId.TryGetValue(out byte byteValue), Is.True);
            Assert.That(byteValue, Is.EqualTo((byte)1));

            message.PublisherId = new Variant((sbyte)2);
            Assert.That(message.PublisherId.TryGetValue(out byte sbyteValue), Is.True);
            Assert.That(sbyteValue, Is.EqualTo((byte)2));

            message.PublisherId = new Variant((ushort)3);
            Assert.That(message.PublisherId.TryGetValue(out ushort ushortValue), Is.True);
            Assert.That(ushortValue, Is.EqualTo((ushort)3));

            message.PublisherId = new Variant((short)4);
            Assert.That(message.PublisherId.TryGetValue(out ushort shortValue), Is.True);
            Assert.That(shortValue, Is.EqualTo((ushort)4));

            message.PublisherId = new Variant(5u);
            Assert.That(message.PublisherId.TryGetValue(out uint uintValue), Is.True);
            Assert.That(uintValue, Is.EqualTo(5u));

            message.PublisherId = new Variant(6);
            Assert.That(message.PublisherId.TryGetValue(out uint intValue), Is.True);
            Assert.That(intValue, Is.EqualTo(6u));

            message.PublisherId = new Variant(7UL);
            Assert.That(message.PublisherId.TryGetValue(out ulong ulongValue), Is.True);
            Assert.That(ulongValue, Is.EqualTo(7UL));

            message.PublisherId = new Variant(8L);
            Assert.That(message.PublisherId.TryGetValue(out ulong longValue), Is.True);
            Assert.That(longValue, Is.EqualTo(8UL));

            message.PublisherId = new Variant("publisher");
            Assert.That(message.PublisherId.TryGetValue(out string? stringValue), Is.True);
            Assert.That(stringValue, Is.EqualTo("publisher"));
        }

        [Test]
        public void NetworkMessageContentMaskSetsAllHeaderFlags()
        {
            var message = new UadpNetworkMessage(
                new WriterGroupDataType(),
                new List<UadpDataSetMessage>());

            message.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.DataSetClassId |
                UadpNetworkMessageContentMask.GroupHeader |
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.GroupVersion |
                UadpNetworkMessageContentMask.NetworkMessageNumber |
                UadpNetworkMessageContentMask.SequenceNumber |
                UadpNetworkMessageContentMask.Timestamp |
                UadpNetworkMessageContentMask.PicoSeconds |
                UadpNetworkMessageContentMask.PromotedFields |
                UadpNetworkMessageContentMask.PayloadHeader);

            Assert.Multiple(() =>
            {
                Assert.That(message.UADPFlags.HasFlag(UADPFlagsEncodingMask.PublisherId), Is.True);
                Assert.That(message.UADPFlags.HasFlag(UADPFlagsEncodingMask.GroupHeader), Is.True);
                Assert.That(message.UADPFlags.HasFlag(UADPFlagsEncodingMask.PayloadHeader), Is.True);
                Assert.That(message.ExtendedFlags1.HasFlag(ExtendedFlags1EncodingMask.DataSetClassId), Is.True);
                Assert.That(message.ExtendedFlags1.HasFlag(ExtendedFlags1EncodingMask.Timestamp), Is.True);
                Assert.That(message.ExtendedFlags1.HasFlag(ExtendedFlags1EncodingMask.PicoSeconds), Is.True);
                Assert.That(message.ExtendedFlags2.HasFlag(ExtendedFlags2EncodingMask.PromotedFields), Is.True);
                Assert.That(message.GroupFlags.HasFlag(GroupFlagsEncodingMask.WriterGroupId), Is.True);
                Assert.That(message.GroupFlags.HasFlag(GroupFlagsEncodingMask.GroupVersion), Is.True);
                Assert.That(message.GroupFlags.HasFlag(GroupFlagsEncodingMask.NetworkMessageNumber), Is.True);
                Assert.That(message.GroupFlags.HasFlag(GroupFlagsEncodingMask.SequenceNumber), Is.True);
            });
        }

        [Test]
        public void DiscoveryConstructorsInitializeMessageTypesAndFlags()
        {
            var metadata = new DataSetMetaDataType
            {
                Name = "DataSet",
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 2
                }
            };
            var metadataResponse = new UadpNetworkMessage(new WriterGroupDataType(), metadata);
            var discoveryRequest = new UadpNetworkMessage(
                UADPNetworkMessageDiscoveryType.DataSetMetaData);
            var endpointResponse = new UadpNetworkMessage(
                new[]
                {
                    new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" }
                },
                StatusCodes.Good);
            var writerConfigResponse = new UadpNetworkMessage(
                new ushort[] { 1, 2 },
                new WriterGroupDataType { WriterGroupId = 10 },
                new StatusCode[] { StatusCodes.Good, StatusCodes.Bad });

            Assert.Multiple(() =>
            {
                Assert.That(metadataResponse.UADPNetworkMessageType, Is.EqualTo(UADPNetworkMessageType.DiscoveryResponse));
                Assert.That(metadataResponse.UADPDiscoveryType, Is.EqualTo(UADPNetworkMessageDiscoveryType.DataSetMetaData));
                Assert.That(discoveryRequest.UADPNetworkMessageType, Is.EqualTo(UADPNetworkMessageType.DiscoveryRequest));
                Assert.That(discoveryRequest.UADPDiscoveryType, Is.EqualTo(UADPNetworkMessageDiscoveryType.DataSetMetaData));
                Assert.That(endpointResponse.UADPDiscoveryType, Is.EqualTo(UADPNetworkMessageDiscoveryType.PublisherEndpoint));
                Assert.That(writerConfigResponse.UADPDiscoveryType,
                    Is.EqualTo(UADPNetworkMessageDiscoveryType.DataSetWriterConfiguration));
                Assert.That(writerConfigResponse.DataSetWriterIds, Is.EqualTo(new ushort[] { 1, 2 }));
                Assert.That(writerConfigResponse.MessageStatusCodes, Is.EqualTo(new StatusCode[] { StatusCodes.Good, StatusCodes.Bad }));
            });
        }

        [Test]
        public void DataSetMessageMasksSetFieldAndHeaderBits()
        {
            var message = new UadpDataSetMessage();

            message.SetFieldContentMask(DataSetFieldContentMask.None);
            DataSetFlags1EncodingMask variantFlags = message.DataSetFlags1;

            message.SetFieldContentMask(DataSetFieldContentMask.RawData);
            DataSetFlags1EncodingMask rawFlags = message.DataSetFlags1;

            message.SetFieldContentMask(
                DataSetFieldContentMask.StatusCode |
                DataSetFieldContentMask.SourceTimestamp |
                DataSetFieldContentMask.ServerTimestamp |
                DataSetFieldContentMask.SourcePicoSeconds |
                DataSetFieldContentMask.ServerPicoSeconds);
            DataSetFlags1EncodingMask dataValueFlags = message.DataSetFlags1;

            message.SetMessageContentMask(
                UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.Status |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion |
                UadpDataSetMessageContentMask.Timestamp |
                UadpDataSetMessageContentMask.PicoSeconds);

            Assert.Multiple(() =>
            {
                Assert.That(variantFlags.HasFlag(DataSetFlags1EncodingMask.MessageIsValid), Is.True);
                Assert.That(rawFlags, Is.Not.EqualTo(variantFlags));
                Assert.That(dataValueFlags, Is.Not.EqualTo(rawFlags));
                Assert.That(message.DataSetFlags1.HasFlag(DataSetFlags1EncodingMask.SequenceNumber), Is.True);
                Assert.That(message.DataSetFlags1.HasFlag(DataSetFlags1EncodingMask.Status), Is.True);
                Assert.That(message.DataSetFlags1.HasFlag(
                    DataSetFlags1EncodingMask.ConfigurationVersionMajorVersion), Is.True);
                Assert.That(message.DataSetFlags1.HasFlag(
                    DataSetFlags1EncodingMask.ConfigurationVersionMinorVersion), Is.True);
                Assert.That(message.DataSetFlags2.HasFlag(DataSetFlags2EncodingMask.Timestamp), Is.True);
                Assert.That(message.DataSetFlags2.HasFlag(DataSetFlags2EncodingMask.PicoSeconds), Is.True);
            });
        }

        [Test]
        public void DataSetNetworkMessageEncodesVariantDataValueAndRawDataPayloads()
        {
            byte[] variantBytes = EncodeDataSetNetworkMessage(DataSetFieldContentMask.None, isDeltaFrame: false);
            byte[] dataValueBytes = EncodeDataSetNetworkMessage(
                DataSetFieldContentMask.StatusCode | DataSetFieldContentMask.SourceTimestamp,
                isDeltaFrame: true);
            byte[] rawDataBytes = EncodeDataSetNetworkMessage(DataSetFieldContentMask.RawData, isDeltaFrame: false);

            Assert.Multiple(() =>
            {
                Assert.That(variantBytes, Has.Length.GreaterThan(0));
                Assert.That(dataValueBytes, Has.Length.GreaterThan(0));
                Assert.That(rawDataBytes, Has.Length.GreaterThan(0));
            });
        }

        [Test]
        public void DiscoveryMessagesEncodeNonEmptyPayloads()
        {
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);
            var metadata = new DataSetMetaDataType
            {
                Name = "DataSet",
                Fields = new ArrayOf<FieldMetaData>(
                    new[]
                    {
                        new FieldMetaData
                        {
                            Name = "Value",
                            BuiltInType = (byte)BuiltInType.Int32,
                            ValueRank = ValueRanks.Scalar
                        }
                    }.AsMemory()),
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };

            var metadataResponse = new UadpNetworkMessage(new WriterGroupDataType(), metadata)
            {
                PublisherId = new Variant((ushort)1),
                DataSetWriterId = 1
            };
            var request = new UadpNetworkMessage(UADPNetworkMessageDiscoveryType.DataSetMetaData)
            {
                PublisherId = new Variant((ushort)1),
                DataSetWriterId = 1
            };
            var endpoints = new UadpNetworkMessage(
                new[]
                {
                    new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840" }
                },
                StatusCodes.Good)
            {
                PublisherId = new Variant((ushort)1)
            };

            Assert.Multiple(() =>
            {
                Assert.That(metadataResponse.Encode(context), Has.Length.GreaterThan(0));
                Assert.That(request.Encode(context), Has.Length.GreaterThan(0));
                Assert.That(endpoints.Encode(context), Has.Length.GreaterThan(0));
            });
        }

        private static byte[] EncodeDataSetNetworkMessage(
            DataSetFieldContentMask fieldMask,
            bool isDeltaFrame)
        {
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(null!);
            var fieldMetaData = new FieldMetaData
            {
                Name = "Value",
                BuiltInType = (byte)BuiltInType.Int32,
                ValueRank = ValueRanks.Scalar
            };
            var dataSet = new DataSet("DataSet")
            {
                DataSetWriterId = 1,
                IsDeltaFrame = isDeltaFrame,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = "DataSet",
                    Fields = new ArrayOf<FieldMetaData>(new[] { fieldMetaData }.AsMemory()),
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                },
                Fields =
                [
                    new Field
                    {
                        Value = new DataValue(new Variant(42)),
                        TargetNodeId = new NodeId(1u, 0),
                        TargetAttribute = Attributes.Value,
                        FieldMetaData = fieldMetaData
                    }
                ]
            };
            var dataSetMessage = new UadpDataSetMessage(dataSet)
            {
                DataSetWriterId = 1,
                MetaDataVersion = dataSet.DataSetMetaData.ConfigurationVersion
            };
            dataSetMessage.SetFieldContentMask(fieldMask);
            dataSetMessage.SetMessageContentMask(
                UadpDataSetMessageContentMask.SequenceNumber |
                UadpDataSetMessageContentMask.Status |
                UadpDataSetMessageContentMask.MajorVersion |
                UadpDataSetMessageContentMask.MinorVersion);

            var networkMessage = new UadpNetworkMessage(
                new WriterGroupDataType { WriterGroupId = 1 },
                new List<UadpDataSetMessage> { dataSetMessage })
            {
                PublisherId = new Variant((ushort)1),
                WriterGroupId = 1,
                DataSetClassId = Uuid.Empty,
                GroupVersion = 1,
                NetworkMessageNumber = 1,
                SequenceNumber = 1
            };
            networkMessage.SetNetworkMessageContentMask(
                UadpNetworkMessageContentMask.PublisherId |
                UadpNetworkMessageContentMask.GroupHeader |
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.GroupVersion |
                UadpNetworkMessageContentMask.NetworkMessageNumber |
                UadpNetworkMessageContentMask.SequenceNumber |
                UadpNetworkMessageContentMask.PayloadHeader);

            return networkMessage.Encode(context);
        }
    }
}
