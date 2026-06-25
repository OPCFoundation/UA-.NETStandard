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
    /// <see cref="PubSubConfigurationBuilder"/>. Each payload wires one
    /// PubSubConnection &gt; ReaderGroup &gt; DataSetReader filtered on
    /// PublisherId / WriterGroupId / DataSetWriterId.
    /// </summary>
    public static class SubscriberConfigurationBuilder
    {
        public const string ReaderName = "Reader 1";
        public const string DataSetName = "Simple";
        public const string DefaultUdpEndpoint = "opc.udp://239.0.0.1:4840";
        public const string DefaultEthEndpoint = "opc.eth://01-00-5E-7F-00-01";
        public const string DefaultMqttEndpoint = "mqtt://localhost:1883";
        private const string MqttQueueName = "Quickstarts/Reference/Simple";

        public static string DefaultEndpointFor(SubscriberProfile profile)
        {
            return profile switch
            {
                SubscriberProfile.UdpUadp => DefaultUdpEndpoint,
                SubscriberProfile.EthUadp => DefaultEthEndpoint,
                _ => DefaultMqttEndpoint
            };
        }

        public static PubSubConfigurationDataType Build(
            SubscriberProfile profile,
            string endpoint,
            ushort publisherIdFilter,
            ushort writerGroupIdFilter,
            ushort dataSetWriterIdFilter)
        {
            // UDP and Ethernet are datagram transports (no broker queue);
            // the MQTT profiles use broker transport settings instead.
            bool udp = profile is SubscriberProfile.UdpUadp or SubscriberProfile.EthUadp;

            // UADP message security (SignAndEncrypt) is wired for the UADP
            // profiles via the shared StaticSecurityKeyProvider. The JSON
            // profile has no UADP security wrapper, so it stays unsecured.
            bool secured = profile != SubscriberProfile.MqttJson;

            string transportProfileUri = profile switch
            {
                SubscriberProfile.UdpUadp => Profiles.PubSubUdpUadpTransport,
                SubscriberProfile.EthUadp => Profiles.PubSubEthUadpTransport,
                SubscriberProfile.MqttUadp => Profiles.PubSubMqttUadpTransport,
                SubscriberProfile.MqttJson => Profiles.PubSubMqttJsonTransport,
                _ => throw new ArgumentOutOfRangeException(nameof(profile))
            };

            return PubSubConfigurationBuilder.Create()
                .AddConnection("Subscriber Connection", connection =>
                {
                    connection
                        .WithPublisherId(new Variant(publisherIdFilter))
                        .WithTransportProfile(transportProfileUri)
                        .WithAddress(endpoint)
                        .AddReaderGroup("ReaderGroup 1", group =>
                        {
                            group.WithMaxNetworkMessageSize(1500);
                            if (secured)
                            {
                                group.WithSecurity(
                                    MessageSecurityMode.SignAndEncrypt,
                                    SampleSecurity.SecurityGroupId,
                                    SampleSecurity.SecurityKeyServiceUrl);
                            }
                            group.AddDataSetReader(ReaderName, reader =>
                            {
                                reader
                                    .WithFilter(
                                        new Variant(publisherIdFilter),
                                        writerGroupIdFilter,
                                        dataSetWriterIdFilter)
                                    .WithFieldContentMask(DataSetFieldContentMask.RawData)
                                    .WithMessageReceiveTimeout(5000)
                                    .WithMessageSettings(ReaderMessageSettings(profile))
                                    .WithMirrorSubscribedDataSet(ReaderName)
                                    .WithDataSetMetaData(DataSetName, metaData => metaData
                                        .WithoutFieldIds()
                                        .AddField("BoolToggle", (byte)DataTypes.Boolean, DataTypeIds.Boolean)
                                        .AddField("Int32", (byte)DataTypes.Int32, DataTypeIds.Int32)
                                        .AddField("DateTime", (byte)DataTypes.DateTime, DataTypeIds.DateTime));
                                if (!udp)
                                {
                                    reader.WithTransportSettings(
                                        new BrokerDataSetReaderTransportDataType
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

        private static IEncodeable ReaderMessageSettings(SubscriberProfile profile)
        {
            if (profile == SubscriberProfile.MqttJson)
            {
                return new JsonDataSetReaderMessageDataType
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
                };
            }
            return new UadpDataSetReaderMessageDataType
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
            };
        }
    }
}
