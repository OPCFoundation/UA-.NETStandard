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

namespace Quickstarts.ConsoleReferenceSubscriber
{
    /// <summary>
    /// Constructs minimal Part 14 <see cref="PubSubConfigurationDataType"/>
    /// payloads for the three demo wire profiles. The payloads wire one
    /// PubSubConnection &gt; ReaderGroup &gt; DataSetReader filtered on
    /// PublisherId / WriterGroupId / DataSetWriterId.
    /// </summary>
    public static class SubscriberConfigurationBuilder
    {
        public const string ReaderName = "Reader 1";
        public const string DataSetName = "Simple";
        public const string DefaultUdpEndpoint = "opc.udp://239.0.0.1:4840";
        public const string DefaultMqttEndpoint = "mqtt://localhost:1883";
        private const string MqttQueueName = "Quickstarts/Reference/Simple";

        public static string DefaultEndpointFor(SubscriberProfile profile)
        {
            return profile == SubscriberProfile.UdpUadp
                ? DefaultUdpEndpoint
                : DefaultMqttEndpoint;
        }

        public static PubSubConfigurationDataType Build(
            SubscriberProfile profile,
            string endpoint,
            ushort publisherIdFilter,
            ushort writerGroupIdFilter,
            ushort dataSetWriterIdFilter)
        {
            string transportProfileUri = profile switch
            {
                SubscriberProfile.UdpUadp => Profiles.PubSubUdpUadpTransport,
                SubscriberProfile.MqttUadp => Profiles.PubSubMqttUadpTransport,
                SubscriberProfile.MqttJson => Profiles.PubSubMqttJsonTransport,
                _ => throw new ArgumentOutOfRangeException(nameof(profile))
            };

            var address = new NetworkAddressUrlDataType
            {
                NetworkInterface = string.Empty,
                Url = endpoint
            };

            ExtensionObject readerMessage = profile == SubscriberProfile.MqttJson
                ? new ExtensionObject(new JsonDataSetReaderMessageDataType
                {
                    NetworkMessageContentMask = (uint)(
                        JsonNetworkMessageContentMask.NetworkMessageHeader
                        | JsonNetworkMessageContentMask.DataSetMessageHeader
                        | JsonNetworkMessageContentMask.PublisherId),
                    DataSetMessageContentMask = (uint)(
                        JsonDataSetMessageContentMask.DataSetWriterId
                        | JsonDataSetMessageContentMask.SequenceNumber
                        | JsonDataSetMessageContentMask.Status
                        | JsonDataSetMessageContentMask.Timestamp)
                })
                : new ExtensionObject(new UadpDataSetReaderMessageDataType
                {
                    NetworkMessageContentMask = (uint)(
                        UadpNetworkMessageContentMask.PublisherId
                        | UadpNetworkMessageContentMask.GroupHeader
                        | UadpNetworkMessageContentMask.WriterGroupId
                        | UadpNetworkMessageContentMask.PayloadHeader
                        | UadpNetworkMessageContentMask.NetworkMessageNumber
                        | UadpNetworkMessageContentMask.SequenceNumber),
                    DataSetMessageContentMask = (uint)(
                        UadpDataSetMessageContentMask.Status
                        | UadpDataSetMessageContentMask.SequenceNumber)
                });

            var dataSetReader = new DataSetReaderDataType
            {
                Name = ReaderName,
                Enabled = true,
                PublisherId = new Variant(publisherIdFilter),
                WriterGroupId = writerGroupIdFilter,
                DataSetWriterId = dataSetWriterIdFilter,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData,
                MessageReceiveTimeout = 5000,
                MessageSettings = readerMessage,
                SubscribedDataSet = new ExtensionObject(
                    new SubscribedDataSetMirrorDataType
                    {
                        ParentNodeName = ReaderName
                    }),
                DataSetMetaData = BuildMetaData()
            };

            if (profile != SubscriberProfile.UdpUadp)
            {
                dataSetReader.TransportSettings = new ExtensionObject(
                    new BrokerDataSetReaderTransportDataType
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
            bool secured = profile != SubscriberProfile.MqttJson;

            var readerGroup = new ReaderGroupDataType
            {
                Name = "ReaderGroup 1",
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
                MaxNetworkMessageSize = 1500,
                MessageSettings = new ExtensionObject(
                    new ReaderGroupMessageDataType()),
                DataSetReaders = new ArrayOf<DataSetReaderDataType>(
                    new[] { dataSetReader })
            };

            var connection = new PubSubConnectionDataType
            {
                Name = "Subscriber Connection",
                Enabled = true,
                PublisherId = new Variant(publisherIdFilter),
                TransportProfileUri = transportProfileUri,
                Address = new ExtensionObject(address),
                ReaderGroups = new ArrayOf<ReaderGroupDataType>(
                    new[] { readerGroup })
            };

            return new PubSubConfigurationDataType
            {
                Enabled = true,
                Connections = new ArrayOf<PubSubConnectionDataType>(
                    new[] { connection }),
                PublishedDataSets = []
            };
        }

        private static DataSetMetaDataType BuildMetaData()
        {
            return new DataSetMetaDataType
            {
                Name = DataSetName,
                DataSetClassId = Uuid.Empty,
                Fields = new ArrayOf<FieldMetaData>(new[]
                {
                    new FieldMetaData
                    {
                        Name = "BoolToggle",
                        BuiltInType = (byte)DataTypes.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32",
                        BuiltInType = (byte)DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "DateTime",
                        BuiltInType = (byte)DataTypes.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    }
                }),
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = 1,
                    MinorVersion = 0
                }
            };
        }
    }
}
