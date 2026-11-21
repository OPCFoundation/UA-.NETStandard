// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Eth;
using Opc.Ua.PubSub.Kafka;

namespace Quickstarts.ConsoleReferencePubSubClient
{
    /// <summary>
    /// Builds minimal Part 14 <see cref="PubSubConfigurationDataType"/>
    /// payloads for the demo wire profiles using the fluent
    /// <see cref="PubSubConfigurationBuilder"/>. The payloads use the
    /// "Simple" DataSet exposed by <see cref="SampleDataSetSource"/>
    /// (BoolToggle, Int32 counter, DateTime).
    /// </summary>
    public static class PublisherConfigurationBuilder
    {
        public const string DataSetName = "Simple";
        public const string DefaultUdpEndpoint = "opc.udp://239.0.0.1:4840";
        public const string DefaultEthEndpoint = "opc.eth://01-00-5E-7F-00-01";
        public const string DefaultMqttEndpoint = "mqtt://localhost:1883";
        public const string DefaultKafkaEndpoint = "kafka://localhost:9092";
        private const string MqttQueueName = "Quickstarts/Reference/Simple";
        private const string KafkaTopicName = "Quickstarts.Reference.Simple";

        public static string DefaultEndpointFor(PublisherProfile profile)
        {
            return profile switch
            {
                PublisherProfile.UdpUadp => DefaultUdpEndpoint,
                PublisherProfile.EthUadp => DefaultEthEndpoint,
                PublisherProfile.KafkaUadp or PublisherProfile.KafkaJson => DefaultKafkaEndpoint,
                PublisherProfile.MqttUadp or PublisherProfile.MqttJson => DefaultMqttEndpoint,
                _ => throw new ArgumentOutOfRangeException(nameof(profile))
            };
        }

        public static PubSubConfigurationDataType Build(
            PublisherProfile profile,
            string endpoint,
            ushort publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId,
            int intervalMs)
        {
            // UDP and Ethernet are datagram transports (no broker queue);
            // broker profiles use broker transport settings instead.
            bool udp = profile is PublisherProfile.UdpUadp or PublisherProfile.EthUadp;

            // UADP message security (SignAndEncrypt) is wired for the UADP
            // profiles via the shared StaticSecurityKeyProvider. The JSON
            // profile has no UADP security wrapper, so it stays unsecured.
            bool secured = profile is not (PublisherProfile.MqttJson or PublisherProfile.KafkaJson);
            string brokerQueueName = profile is PublisherProfile.KafkaUadp or PublisherProfile.KafkaJson
                ? KafkaTopicName
                : MqttQueueName;

            string transportProfileUri = profile switch
            {
                PublisherProfile.UdpUadp => Profiles.PubSubUdpUadpTransport,
                PublisherProfile.EthUadp => EthProfiles.PubSubEthUadpTransport,
                PublisherProfile.MqttUadp => Profiles.PubSubMqttUadpTransport,
                PublisherProfile.MqttJson => Profiles.PubSubMqttJsonTransport,
                PublisherProfile.KafkaUadp => KafkaProfiles.PubSubKafkaUadpTransport,
                PublisherProfile.KafkaJson => KafkaProfiles.PubSubKafkaJsonTransport,
                _ => throw new ArgumentOutOfRangeException(nameof(profile))
            };

            return PubSubConfigurationBuilder.Create()
                .AddPublishedDataSet(DataSetName, ds => ds
                    .AddField("BoolToggle", (byte)DataTypes.Boolean, DataTypeIds.Boolean)
                    .AddField("Int32", (byte)DataTypes.Int32, DataTypeIds.Int32)
                    .AddField("DateTime", (byte)DataTypes.DateTime, DataTypeIds.DateTime))
                .AddConnection("Publisher Connection", connection => connection
                        .WithPublisherId(new Variant(publisherId))
                        .WithTransportProfile(transportProfileUri)
                        .WithAddress(endpoint)
                        .AddWriterGroup("WriterGroup 1", group =>
                        {
                            group
                                .WithWriterGroupId(writerGroupId)
                                .WithPublishingInterval(intervalMs)
                                .WithMessageSettings(WriterGroupMessageSettings(profile))
                                .WithTransportSettings(udp
                                    ? new DatagramWriterGroupTransportDataType()
                                    : new BrokerWriterGroupTransportDataType
                                    {
                                        QueueName = brokerQueueName
                                    });
                            if (secured)
                            {
                                group.WithSecurity(
                                    MessageSecurityMode.SignAndEncrypt,
                                    SampleSecurity.SecurityGroupId,
                                    SampleSecurity.SecurityKeyServiceUrl);
                            }
                            group.AddDataSetWriter("Writer 1", writer =>
                            {
                                writer
                                    .WithDataSetWriterId(dataSetWriterId)
                                    .WithDataSetName(DataSetName)
                                    .WithKeyFrameCount(1)
                                    .WithFieldContentMask(DataSetFieldContentMask.RawData)
                                    .WithMessageSettings(WriterMessageSettings(profile));
                                if (!udp)
                                {
                                    writer.WithTransportSettings(
                                        new BrokerDataSetWriterTransportDataType
                                        {
                                            QueueName = brokerQueueName,
                                            RequestedDeliveryGuarantee
                                                = BrokerTransportQualityOfService.BestEffort
                                        });
                                }
                            });
                        }))
                .Build();
        }

        private static IEncodeable WriterGroupMessageSettings(PublisherProfile profile)
        {
            if (profile is PublisherProfile.MqttJson or PublisherProfile.KafkaJson)
            {
                return new JsonWriterGroupMessageDataType
                {
                    NetworkMessageContentMask = (uint)(
                        JsonNetworkMessageContentMask.NetworkMessageHeader |
                        JsonNetworkMessageContentMask.DataSetMessageHeader |
                        JsonNetworkMessageContentMask.PublisherId)
                };
            }
            return new UadpWriterGroupMessageDataType
            {
                DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                NetworkMessageContentMask = (uint)(
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader |
                    UadpNetworkMessageContentMask.NetworkMessageNumber |
                    UadpNetworkMessageContentMask.SequenceNumber)
            };
        }

        private static IEncodeable WriterMessageSettings(PublisherProfile profile)
        {
            if (profile is PublisherProfile.MqttJson or PublisherProfile.KafkaJson)
            {
                return new JsonDataSetWriterMessageDataType
                {
                    DataSetMessageContentMask = (uint)(
                        JsonDataSetMessageContentMask.DataSetWriterId |
                        JsonDataSetMessageContentMask.SequenceNumber |
                        JsonDataSetMessageContentMask.Status |
                        JsonDataSetMessageContentMask.Timestamp)
                };
            }
            return new UadpDataSetWriterMessageDataType
            {
                DataSetMessageContentMask = (uint)(
                    UadpDataSetMessageContentMask.Status |
                    UadpDataSetMessageContentMask.SequenceNumber)
            };
        }
    }
}
