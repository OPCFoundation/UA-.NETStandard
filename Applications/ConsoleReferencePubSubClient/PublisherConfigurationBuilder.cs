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
using Opc.Ua.PubSub.Configuration;

namespace Quickstarts.ConsoleReferencePubSubClient
{
    /// <summary>
    /// Builds minimal Part 14 <see cref="PubSubConfigurationDataType"/>
    /// payloads for the three demo wire profiles using the fluent
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
        private const string MqttQueueName = "Quickstarts/Reference/Simple";

        public static string DefaultEndpointFor(PublisherProfile profile)
        {
            return profile switch
            {
                PublisherProfile.UdpUadp => DefaultUdpEndpoint,
                PublisherProfile.EthUadp => DefaultEthEndpoint,
                _ => DefaultMqttEndpoint
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
            // the MQTT profiles use broker transport settings instead.
            bool udp = profile is PublisherProfile.UdpUadp or PublisherProfile.EthUadp;

            // UADP message security (SignAndEncrypt) is wired for the UADP
            // profiles via the shared StaticSecurityKeyProvider. The JSON
            // profile has no UADP security wrapper, so it stays unsecured.
            bool secured = profile != PublisherProfile.MqttJson;

            string transportProfileUri = profile switch
            {
                PublisherProfile.UdpUadp => Profiles.PubSubUdpUadpTransport,
                PublisherProfile.EthUadp => Profiles.PubSubEthUadpTransport,
                PublisherProfile.MqttUadp => Profiles.PubSubMqttUadpTransport,
                PublisherProfile.MqttJson => Profiles.PubSubMqttJsonTransport,
                _ => throw new ArgumentOutOfRangeException(nameof(profile))
            };

            return PubSubConfigurationBuilder.Create()
                .AddPublishedDataSet(DataSetName, ds => ds
                    .AddField("BoolToggle", (byte)DataTypes.Boolean, DataTypeIds.Boolean)
                    .AddField("Int32", (byte)DataTypes.Int32, DataTypeIds.Int32)
                    .AddField("DateTime", (byte)DataTypes.DateTime, DataTypeIds.DateTime))
                .AddConnection("Publisher Connection", connection =>
                {
                    connection
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
                                        QueueName = MqttQueueName
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
                                            QueueName = MqttQueueName,
                                            RequestedDeliveryGuarantee
                                                = BrokerTransportQualityOfService.BestEffort
                                        });
                                }
                            });
                        });
                })
                .Build();
        }

        private static IEncodeable WriterGroupMessageSettings(PublisherProfile profile)
        {
            if (profile == PublisherProfile.MqttJson)
            {
                return new JsonWriterGroupMessageDataType
                {
                    NetworkMessageContentMask = (uint)(
                        JsonNetworkMessageContentMask.NetworkMessageHeader
                        | JsonNetworkMessageContentMask.DataSetMessageHeader
                        | JsonNetworkMessageContentMask.PublisherId)
                };
            }
            return new UadpWriterGroupMessageDataType
            {
                DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                NetworkMessageContentMask = (uint)(
                    UadpNetworkMessageContentMask.PublisherId
                    | UadpNetworkMessageContentMask.GroupHeader
                    | UadpNetworkMessageContentMask.WriterGroupId
                    | UadpNetworkMessageContentMask.PayloadHeader
                    | UadpNetworkMessageContentMask.NetworkMessageNumber
                    | UadpNetworkMessageContentMask.SequenceNumber)
            };
        }

        private static IEncodeable WriterMessageSettings(PublisherProfile profile)
        {
            if (profile == PublisherProfile.MqttJson)
            {
                return new JsonDataSetWriterMessageDataType
                {
                    DataSetMessageContentMask = (uint)(
                        JsonDataSetMessageContentMask.DataSetWriterId
                        | JsonDataSetMessageContentMask.SequenceNumber
                        | JsonDataSetMessageContentMask.Status
                        | JsonDataSetMessageContentMask.Timestamp)
                };
            }
            return new UadpDataSetWriterMessageDataType
            {
                DataSetMessageContentMask = (uint)(
                    UadpDataSetMessageContentMask.Status
                    | UadpDataSetMessageContentMask.SequenceNumber)
            };
        }
    }
}
