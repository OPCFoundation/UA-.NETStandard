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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Builds standard configuration records from the bounded MCP contract. Credentials stay outside these records.
    /// </summary>
    internal static class McpPubSubConfiguration
    {
        /// <summary>
        /// Builds a standard, initially configured publisher/subscriber application.
        /// </summary>
        internal static PubSubConfigurationDataType Build(PubSubApplicationInput input, DataSetMetaDataType metadata)
        {
            var connection = new PubSubConnectionDataType
            {
                Name = ConnectionName,
                Enabled = true,
                PublisherId = PubSubInputValidation.Publisher(input.PublisherId, input.Connection.Encoding).ToVariant(),
                TransportProfileUri = PubSubInputValidation.Profile(input.Connection),
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = input.Connection.Endpoint,
                    NetworkInterface = input.Connection.NetworkInterface ?? string.Empty
                })
            };
            var configuration = new PubSubConfigurationDataType
            {
                Enabled = true,
                Connections = [connection]
            };
            if (input.Mode == "Publisher")
            {
                configuration.PublishedDataSets =
                [
                    new PublishedDataSetDataType
                    {
                        Name = input.DataSetName,
                        DataSetMetaData = metadata,
                        DataSetSource = new ExtensionObject(new PublishedDataItemsDataType())
                    }
                ];
                connection.WriterGroups = [WriterGroup(input)];
            }
            else
            {
                connection.ReaderGroups = [ReaderGroup(input, metadata)];
            }
            return configuration;
        }

        /// <summary>
        /// Builds a standard writer group, including explicit broker data/metadata queues and native masks.
        /// </summary>
        internal static WriterGroupDataType WriterGroup(PubSubApplicationInput input)
        {
            bool json = input.Connection.Encoding == PubSubMessageEncoding.Json;
            bool broker = input.Connection.Transport is PubSubTransportKind.Mqtt or PubSubTransportKind.Kafka;
            return new WriterGroupDataType
            {
                Name = "McpWriterGroup",
                Enabled = true,
                WriterGroupId = input.WriterGroupId,
                PublishingInterval = input.PublishingIntervalMilliseconds,
                KeepAliveTime = input.PublishingIntervalMilliseconds * 5,
                MaxNetworkMessageSize = 65507,
                SecurityMode = input.Security?.Mode ?? MessageSecurityMode.None,
                SecurityGroupId = input.Security?.GroupId ?? string.Empty,
                MessageSettings = new ExtensionObject(json
                    ? new JsonWriterGroupMessageDataType
                    {
                        NetworkMessageContentMask = (uint)(JsonNetworkMessageContentMask.NetworkMessageHeader |
                            JsonNetworkMessageContentMask.DataSetMessageHeader |
                            JsonNetworkMessageContentMask.PublisherId |
                            JsonNetworkMessageContentMask.DataSetClassId)
                    }
                    : new UadpWriterGroupMessageDataType
                    {
                        NetworkMessageContentMask = UadpNetworkMask
                    }),
                TransportSettings = broker
                    ? new ExtensionObject(new BrokerWriterGroupTransportDataType { QueueName = input.Connection.Topic })
                    : new ExtensionObject(new DatagramWriterGroupTransportDataType()),
                DataSetWriters =
                [
                    new DataSetWriterDataType
                    {
                        Name = WriterName,
                        Enabled = true,
                        DataSetName = input.DataSetName,
                        DataSetWriterId = input.DataSetWriterId,
                        KeyFrameCount = 1,
                        DataSetFieldContentMask = (uint)(DataSetFieldContentMask.StatusCode |
                            DataSetFieldContentMask.SourceTimestamp),
                        MessageSettings = new ExtensionObject(json
                            ? new JsonDataSetWriterMessageDataType
                            {
                                DataSetMessageContentMask = (uint)(JsonDataSetMessageContentMask.DataSetWriterId |
                                    JsonDataSetMessageContentMask.MetaDataVersion |
                                    JsonDataSetMessageContentMask.SequenceNumber |
                                    JsonDataSetMessageContentMask.Status)
                            }
                            : new UadpDataSetWriterMessageDataType
                            {
                                DataSetMessageContentMask = UadpDataSetMask
                            }),
                        TransportSettings = broker
                            ? new ExtensionObject(new BrokerDataSetWriterTransportDataType
                            {
                                QueueName = input.Connection.Topic,
                                MetaDataQueueName = input.Connection.MetadataTopic,
                                RequestedDeliveryGuarantee = BrokerTransportQualityOfService.AtLeastOnce
                            }) : ExtensionObject.Null
                    }
                ]
            };
        }

        /// <summary>
        /// Builds a standard reader and preset metadata so no wildcard discovery subscription is necessary.
        /// </summary>
        internal static ReaderGroupDataType ReaderGroup(PubSubApplicationInput input, DataSetMetaDataType metadata)
        {
            bool json = input.Connection.Encoding == PubSubMessageEncoding.Json;
            bool broker = input.Connection.Transport is PubSubTransportKind.Mqtt or PubSubTransportKind.Kafka;
            return new ReaderGroupDataType
            {
                Name = "McpReaderGroup",
                Enabled = true,
                SecurityMode = input.Security?.Mode ?? MessageSecurityMode.None,
                SecurityGroupId = input.Security?.GroupId ?? string.Empty,
                MaxNetworkMessageSize = 65507,
                DataSetReaders =
                [
                    new DataSetReaderDataType
                    {
                        Name = ReaderName,
                        Enabled = true,
                        PublisherId = PubSubInputValidation.Publisher(input.PublisherId, input.Connection.Encoding)
                            .ToVariant(),
                        WriterGroupId = input.WriterGroupId,
                        DataSetWriterId = input.DataSetWriterId,
                        DataSetMetaData = metadata,
                        SecurityMode = input.Security?.Mode ?? MessageSecurityMode.None,
                        SecurityGroupId = input.Security?.GroupId ?? string.Empty,
                        DataSetFieldContentMask = (uint)(DataSetFieldContentMask.StatusCode |
                            DataSetFieldContentMask.SourceTimestamp),
                        MessageReceiveTimeout = 5000,
                        MessageSettings = new ExtensionObject(json
                            ? new JsonDataSetReaderMessageDataType()
                            : new UadpDataSetReaderMessageDataType
                            {
                                NetworkMessageContentMask = UadpNetworkMask,
                                DataSetMessageContentMask = UadpDataSetMask
                            }),
                        TransportSettings = broker
                            ? new ExtensionObject(new BrokerDataSetReaderTransportDataType
                            {
                                QueueName = input.Connection.Topic,
                                MetaDataQueueName = input.Connection.MetadataTopic,
                                RequestedDeliveryGuarantee = BrokerTransportQualityOfService.AtLeastOnce
                            }) : ExtensionObject.Null,
                        SubscribedDataSet = new ExtensionObject(new SubscribedDataSetMirrorDataType
                        {
                            ParentNodeName = ReaderName
                        })
                    }
                ]
            };
        }

        /// <summary>The stable internal connection name.</summary>
        internal const string ConnectionName = "McpConnection";

        /// <summary>The stable internal writer name.</summary>
        internal const string WriterName = "McpWriter";

        /// <summary>The stable internal reader name.</summary>
        internal const string ReaderName = "McpReader";

        /// <summary>The explicit UADP NetworkMessage mask.</summary>
        internal const uint UadpNetworkMask = (uint)(UadpNetworkMessageContentMask.PublisherId |
            UadpNetworkMessageContentMask.GroupHeader |
            UadpNetworkMessageContentMask.WriterGroupId |
            UadpNetworkMessageContentMask.PayloadHeader |
            UadpNetworkMessageContentMask.SequenceNumber);

        /// <summary>The explicit UADP DataSetMessage mask.</summary>
        internal const uint UadpDataSetMask = (uint)(UadpDataSetMessageContentMask.Status |
            UadpDataSetMessageContentMask.SequenceNumber |
            UadpDataSetMessageContentMask.MajorVersion |
            UadpDataSetMessageContentMask.MinorVersion);
    }
}
