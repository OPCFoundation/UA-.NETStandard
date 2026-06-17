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
using Opc.Ua;

namespace Quickstarts.ConsoleReferencePublisher
{
    /// <summary>
    /// Constructs minimal Part 14 <see cref="PubSubConfigurationDataType"/>
    /// payloads for the three demo wire profiles. The payloads use the
    /// "Simple" DataSet exposed by <see cref="SampleDataSetSource"/>
    /// (BoolToggle, Int32 counter, DateTime).
    /// </summary>
    public static class PublisherConfigurationBuilder
    {
        public const string DataSetName = "Simple";
        public const string DefaultUdpEndpoint = "opc.udp://239.0.0.1:4840";
        public const string DefaultMqttEndpoint = "mqtt://localhost:1883";
        private const string MqttQueueName = "Quickstarts/Reference/Simple";

        public static string DefaultEndpointFor(PublisherProfile profile)
        {
            return profile == PublisherProfile.UdpUadp
                ? DefaultUdpEndpoint
                : DefaultMqttEndpoint;
        }

        public static PubSubConfigurationDataType Build(
            PublisherProfile profile,
            string endpoint,
            ushort publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId,
            int intervalMs)
        {
            string transportProfileUri = profile switch
            {
                PublisherProfile.UdpUadp => Profiles.PubSubUdpUadpTransport,
                PublisherProfile.MqttUadp => Profiles.PubSubMqttUadpTransport,
                PublisherProfile.MqttJson => Profiles.PubSubMqttJsonTransport,
                _ => throw new ArgumentOutOfRangeException(nameof(profile))
            };

            var address = new NetworkAddressUrlDataType
            {
                NetworkInterface = string.Empty,
                Url = endpoint
            };

            ExtensionObject writerGroupTransport = profile == PublisherProfile.UdpUadp
                ? new ExtensionObject(new DatagramWriterGroupTransportDataType())
                : new ExtensionObject(
                    new BrokerWriterGroupTransportDataType { QueueName = MqttQueueName });

            ExtensionObject writerGroupMessage = profile == PublisherProfile.MqttJson
                ? new ExtensionObject(new JsonWriterGroupMessageDataType
                {
                    NetworkMessageContentMask = (uint)(
                        JsonNetworkMessageContentMask.NetworkMessageHeader
                        | JsonNetworkMessageContentMask.DataSetMessageHeader
                        | JsonNetworkMessageContentMask.PublisherId)
                })
                : new ExtensionObject(new UadpWriterGroupMessageDataType
                {
                    DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                    NetworkMessageContentMask = (uint)(
                        UadpNetworkMessageContentMask.PublisherId
                        | UadpNetworkMessageContentMask.GroupHeader
                        | UadpNetworkMessageContentMask.WriterGroupId
                        | UadpNetworkMessageContentMask.PayloadHeader
                        | UadpNetworkMessageContentMask.NetworkMessageNumber
                        | UadpNetworkMessageContentMask.SequenceNumber)
                });

            ExtensionObject writerMessage = profile == PublisherProfile.MqttJson
                ? new ExtensionObject(new JsonDataSetWriterMessageDataType
                {
                    DataSetMessageContentMask = (uint)(
                        JsonDataSetMessageContentMask.DataSetWriterId
                        | JsonDataSetMessageContentMask.SequenceNumber
                        | JsonDataSetMessageContentMask.Status
                        | JsonDataSetMessageContentMask.Timestamp)
                })
                : new ExtensionObject(new UadpDataSetWriterMessageDataType
                {
                    DataSetMessageContentMask = (uint)(
                        UadpDataSetMessageContentMask.Status
                        | UadpDataSetMessageContentMask.SequenceNumber)
                });

            var writer = new DataSetWriterDataType
            {
                Name = "Writer 1",
                DataSetWriterId = dataSetWriterId,
                Enabled = true,
                DataSetName = DataSetName,
                KeyFrameCount = 1,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData,
                MessageSettings = writerMessage
            };
            if (profile != PublisherProfile.UdpUadp)
            {
                writer.TransportSettings = new ExtensionObject(
                    new BrokerDataSetWriterTransportDataType
                    {
                        QueueName = MqttQueueName,
                        RequestedDeliveryGuarantee
                            = BrokerTransportQualityOfService.BestEffort
                    });
            }

            // UADP message security (SignAndEncrypt) is wired for the UADP
            // profiles via the shared StaticSecurityKeyProvider. The
            // JSON profile has no UADP security wrapper, so it stays
            // explicitly unsecured.
            bool secured = profile != PublisherProfile.MqttJson;

            var writerGroup = new WriterGroupDataType
            {
                Name = "WriterGroup 1",
                WriterGroupId = writerGroupId,
                Enabled = true,
                SecurityMode = secured
                    ? MessageSecurityMode.SignAndEncrypt
                    : MessageSecurityMode.None,
                SecurityGroupId = secured ? SampleSecurity.SecurityGroupId : string.Empty,
                SecurityKeyServices = secured
                    ? new ArrayOf<EndpointDescription>(new[]
                    {
                        new EndpointDescription
                        {
                            EndpointUrl = SampleSecurity.SecurityKeyServiceUrl
                        }
                    })
                    : default,
                PublishingInterval = intervalMs,
                KeepAliveTime = intervalMs * 5.0,
                MaxNetworkMessageSize = 1500,
                MessageSettings = writerGroupMessage,
                TransportSettings = writerGroupTransport,
                DataSetWriters = new ArrayOf<DataSetWriterDataType>(new[] { writer })
            };

            var connection = new PubSubConnectionDataType
            {
                Name = "Publisher Connection",
                Enabled = true,
                PublisherId = new Variant(publisherId),
                TransportProfileUri = transportProfileUri,
                Address = new ExtensionObject(address),
                WriterGroups = new ArrayOf<WriterGroupDataType>(new[] { writerGroup })
            };

            return new PubSubConfigurationDataType
            {
                Enabled = true,
                Connections =
                    new ArrayOf<PubSubConnectionDataType>(new[] { connection }),
                PublishedDataSets =
                    new ArrayOf<PublishedDataSetDataType>(
                        new[] { BuildPublishedDataSet() })
            };
        }

        private static PublishedDataSetDataType BuildPublishedDataSet()
        {
            var fields = new ArrayOf<FieldMetaData>(new[]
            {
                new FieldMetaData
                {
                    Name = "BoolToggle",
                    DataSetFieldId = Uuid.NewUuid(),
                    BuiltInType = (byte)DataTypes.Boolean,
                    DataType = DataTypeIds.Boolean,
                    ValueRank = ValueRanks.Scalar
                },
                new FieldMetaData
                {
                    Name = "Int32",
                    DataSetFieldId = Uuid.NewUuid(),
                    BuiltInType = (byte)DataTypes.Int32,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar
                },
                new FieldMetaData
                {
                    Name = "DateTime",
                    DataSetFieldId = Uuid.NewUuid(),
                    BuiltInType = (byte)DataTypes.DateTime,
                    DataType = DataTypeIds.DateTime,
                    ValueRank = ValueRanks.Scalar
                }
            });
            return new PublishedDataSetDataType
            {
                Name = DataSetName,
                DataSetMetaData = new DataSetMetaDataType
                {
                    Name = DataSetName,
                    DataSetClassId = Uuid.Empty,
                    Fields = fields,
                    ConfigurationVersion = new ConfigurationVersionDataType
                    {
                        MajorVersion = 1,
                        MinorVersion = 0
                    }
                }
            };
        }
    }
}
