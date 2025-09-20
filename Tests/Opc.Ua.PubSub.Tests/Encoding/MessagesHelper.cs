/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.PublishedData;
using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    public static class MessagesHelper
    {
        /// <summary>
        /// Ua data message type
        /// </summary>
        internal const string UaDataMessageType = "ua-data";

        /// <summary>
        ///  Ua metadata message type
        /// </summary>
        internal const string UaMetaDataMessageType = "ua-metadata";

        private static readonly bool[] s_elements =
        [
            true,
            false,
            true,
            false,
            true,
            false,
            true,
            false,
            true,
            false,
            true,
            false,
            true,
            false,
            true,
            false,
            true,
            false,
            true,
            false,
            true,
            false,
            true,
            false
        ];

        private static readonly double[] s_elementsArray = [11000.5, 12000.6, 13000.7, 14000.8];
        private static readonly string[] s_elementsArray0 = ["1a", "2b", "3c", "4d"];

        /// <summary>
        /// PubSub options
        /// </summary>
        internal enum PubSubType
        {
            Publisher,
            Subscriber
        }

        /// <summary>
        /// Create PubSub connection
        /// </summary>
        internal static PubSubConnectionDataType CreatePubSubConnection(
            string transportProfileUri,
            string addressUrl,
            object publisherId,
            PubSubType pubSubType = PubSubType.Publisher)
        {
            // Define a PubSub connection with PublisherId
            var pubSubConnection = new PubSubConnectionDataType
            {
                Name = $"Connection {pubSubType} PubId:" + publisherId,
                Enabled = true
            };
            if (publisherId != null)
            {
                pubSubConnection.PublisherId = new Variant(publisherId);
            }
            pubSubConnection.PublisherId = new Variant(publisherId);
            pubSubConnection.TransportProfileUri = transportProfileUri;

            var address = new NetworkAddressUrlDataType
            {
                // Specify the local Network interface name to be used
                // e.g. address.NetworkInterface = "Ethernet";
                // Leave empty to publish on all available local interfaces.
                NetworkInterface = string.Empty,
                Url = addressUrl
            };
            pubSubConnection.Address = new ExtensionObject(address);

            return pubSubConnection;
        }

        /// <summary>
        /// Get first connection
        /// </summary>
        public static PubSubConnectionDataType GetConnection(
            PubSubConfigurationDataType pubSubConfiguration,
            object publisherId)
        {
            if (pubSubConfiguration != null)
            {
                return pubSubConfiguration.Connections
                    .Find(x => x.PublisherId.Value.Equals(publisherId));
            }
            return null;
        }

        /// <summary>
        /// Create writer group with default message and transport settings
        /// </summary>
        public static WriterGroupDataType CreateWriterGroup(
            ushort writerGroupId,
            string writerGroupName = null)
        {
            return new WriterGroupDataType
            {
                Name = !string.IsNullOrEmpty(writerGroupName)
                    ? writerGroupName
                    : $"WriterGroup {writerGroupId}",
                Enabled = true,
                WriterGroupId = writerGroupId,
                PublishingInterval = 5000,
                KeepAliveTime = 5000,
                MaxNetworkMessageSize = 1500
            };
        }

        /// <summary>
        /// Create writer group with specified message and transport settings
        /// </summary>
        private static WriterGroupDataType CreateWriterGroup(
            ushort writerGroupId,
            WriterGroupMessageDataType messageSettings,
            WriterGroupTransportDataType transportSettings,
            string writerGroupName = null)
        {
            WriterGroupDataType writerGroup = CreateWriterGroup(writerGroupId, writerGroupName);

            writerGroup.MessageSettings = new ExtensionObject(messageSettings);
            writerGroup.TransportSettings = new ExtensionObject(transportSettings);

            return writerGroup;
        }

        /// <summary>
        /// Get first writer group
        /// </summary>
        public static WriterGroupDataType GetWriterGroup(
            PubSubConnectionDataType connection,
            ushort writerGroupId)
        {
            if (connection != null)
            {
                return connection.WriterGroups.Find(x => x.WriterGroupId.Equals(writerGroupId));
            }
            return null;
        }

        /// <summary>
        /// Create a Publisher with the specified parameters
        /// </summary>
        private static PubSubConfigurationDataType CreatePublisherConfiguration(
            string transportProfileUri,
            string addressUrl,
            object publisherId,
            ushort writerGroupId,
            uint networkMessageContentMask,
            uint dataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData,
            double metaDataUpdateTime = 0,
            uint keyFrameCount = 1)
        {
            // Define a PubSub connection with PublisherId
            PubSubConnectionDataType pubSubConnection1 = CreatePubSubConnection(
                transportProfileUri,
                addressUrl,
                publisherId,
                PubSubType.Publisher);

            const string brokerMetaData = "$Metadata";

            var writerGroup1 = new WriterGroupDataType
            {
                Name = "WriterGroup id:" + writerGroupId,
                Enabled = true,
                WriterGroupId = writerGroupId,
                PublishingInterval = 5000,
                KeepAliveTime = 5000,
                MaxNetworkMessageSize = 1500
            };

            WriterGroupMessageDataType messageSettings = null;
            WriterGroupTransportDataType transportSettings = null;
            switch (transportProfileUri)
            {
                case Profiles.PubSubUdpUadpTransport:
                    messageSettings = new UadpWriterGroupMessageDataType
                    {
                        DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                        GroupVersion = 0,
                        NetworkMessageContentMask = networkMessageContentMask
                    };
                    transportSettings = new DatagramWriterGroupTransportDataType();
                    break;
                case Profiles.PubSubMqttUadpTransport:
                    messageSettings = new UadpWriterGroupMessageDataType
                    {
                        DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                        GroupVersion = 0,
                        NetworkMessageContentMask = networkMessageContentMask
                    };
                    transportSettings = new BrokerWriterGroupTransportDataType
                    {
                        QueueName = writerGroup1.Name
                    };
                    break;
                case Profiles.PubSubMqttJsonTransport:
                    messageSettings = new JsonWriterGroupMessageDataType
                    {
                        NetworkMessageContentMask = networkMessageContentMask
                    };
                    transportSettings = new BrokerWriterGroupTransportDataType
                    {
                        QueueName = writerGroup1.Name
                    };
                    break;
            }

            writerGroup1.MessageSettings = new ExtensionObject(messageSettings);
            writerGroup1.TransportSettings = new ExtensionObject(transportSettings);

            // create all dataset writers
            for (ushort dataSetWriterId = 1;
                dataSetWriterId <= dataSetMetaDataArray.Length;
                dataSetWriterId++)
            {
                DataSetMetaDataType dataSetMetaData = dataSetMetaDataArray[dataSetWriterId - 1];
                // Define DataSetWriter
                var dataSetWriter = new DataSetWriterDataType
                {
                    Name = "Writer id:" + dataSetWriterId,
                    DataSetWriterId = dataSetWriterId,
                    Enabled = true,
                    DataSetFieldContentMask = (uint)dataSetFieldContentMask,
                    DataSetName = dataSetMetaData.Name,
                    KeyFrameCount = keyFrameCount
                };

                DataSetWriterMessageDataType dataSetWriterMessage = null;
                switch (transportProfileUri)
                {
                    case Profiles.PubSubUdpUadpTransport:
                        dataSetWriterMessage = new UadpDataSetWriterMessageDataType
                        {
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        break;
                    case Profiles.PubSubMqttUadpTransport:
                        dataSetWriterMessage = new UadpDataSetWriterMessageDataType
                        {
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        var jsonDataSetWriterTransport2 = new BrokerDataSetWriterTransportDataType
                        {
                            QueueName = writerGroup1.Name,
                            MetaDataQueueName = $"{writerGroup1.Name}/{brokerMetaData}",
                            MetaDataUpdateTime = metaDataUpdateTime
                        };
                        dataSetWriter.TransportSettings
                            = new ExtensionObject(jsonDataSetWriterTransport2);
                        break;
                    case Profiles.PubSubMqttJsonTransport:
                        dataSetWriterMessage = new JsonDataSetWriterMessageDataType
                        {
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        var jsonDataSetWriterTransport = new BrokerDataSetWriterTransportDataType
                        {
                            QueueName = writerGroup1.Name,
                            MetaDataQueueName = $"{writerGroup1.Name}/{brokerMetaData}",
                            MetaDataUpdateTime = metaDataUpdateTime
                        };
                        dataSetWriter.TransportSettings
                            = new ExtensionObject(jsonDataSetWriterTransport);
                        break;
                }

                dataSetWriter.MessageSettings = new ExtensionObject(dataSetWriterMessage);
                writerGroup1.DataSetWriters.Add(dataSetWriter);
            }

            pubSubConnection1.WriterGroups.Add(writerGroup1);

            //create  the PubSub configuration root object
            var pubSubConfiguration = new PubSubConfigurationDataType
            {
                Connections = [pubSubConnection1],
                PublishedDataSets = []
            };

            // creates the published data sets
            for (ushort i = 0; i < dataSetMetaDataArray.Length; i++)
            {
                DataSetMetaDataType dataSetMetaData = dataSetMetaDataArray[i];
                var publishedDataSetDataType = new PublishedDataSetDataType
                {
                    Name = dataSetMetaDataArray[i].Name, //name shall be unique in a configuration
                    // set  publishedDataSetSimple.DataSetMetaData
                    DataSetMetaData = dataSetMetaData
                };

                var publishedDataSetSource = new PublishedDataItemsDataType { PublishedData = [] };
                //create PublishedData based on metadata names
                foreach (FieldMetaData field in dataSetMetaData.Fields)
                {
                    publishedDataSetSource.PublishedData.Add(
                        new PublishedVariableDataType
                        {
                            PublishedVariable = new NodeId(field.Name, nameSpaceIndexForData),
                            AttributeId = Attributes.Value
                        });
                }

                publishedDataSetDataType.DataSetSource
                    = new ExtensionObject(publishedDataSetSource);

                pubSubConfiguration.PublishedDataSets.Add(publishedDataSetDataType);
            }

            return pubSubConfiguration;
        }

        /// <summary>
        /// Create a Publisher with the specified parameters for json
        /// </summary>
        public static PubSubConfigurationDataType CreatePublisherConfiguration(
            string transportProfileUri,
            string addressUrl,
            object publisherId,
            ushort writerGroupId,
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask,
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData,
            double metaDataUpdateTime = 0,
            uint keyFrameCount = 1)
        {
            return CreatePublisherConfiguration(
                transportProfileUri,
                addressUrl,
                publisherId,
                writerGroupId,
                (uint)jsonNetworkMessageContentMask,
                (uint)jsonDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray,
                nameSpaceIndexForData,
                metaDataUpdateTime,
                keyFrameCount);
        }

        /// <summary>
        /// Create a Publisher with the specified parameters for mqtt + udp together
        /// </summary>
        public static PubSubConfigurationDataType CreateUdpPlusMqttPublisherConfiguration(
            string udpTransportProfileUri,
            string udpAddressUrl,
            object udpPublisherId,
            ushort udpWriterGroupId,
            string mqttTransportProfileUri,
            string mqttAddressUrl,
            object mqttPublisherId,
            ushort mqttWriterGroupId,
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask,
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData,
            uint keyFrameCount = 1)
        {
            PubSubConfigurationDataType udpPublisherConfiguration = CreatePublisherConfiguration(
                udpTransportProfileUri,
                udpAddressUrl,
                publisherId: udpPublisherId,
                writerGroupId: udpWriterGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray,
                nameSpaceIndexForData: nameSpaceIndexForData,
                keyFrameCount: keyFrameCount);

            PubSubConfigurationDataType mqttPublisherConfiguration = CreatePublisherConfiguration(
                mqttTransportProfileUri,
                mqttAddressUrl,
                publisherId: mqttPublisherId,
                writerGroupId: mqttWriterGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray,
                nameSpaceIndexForData: nameSpaceIndexForData,
                keyFrameCount: keyFrameCount);

            // add the udp connection too
            if (udpPublisherConfiguration.Connections != null &&
                udpPublisherConfiguration.Connections.Count > 0)
            {
                mqttPublisherConfiguration.Connections
                    .Add(udpPublisherConfiguration.Connections[0]);
            }

            return mqttPublisherConfiguration;
        }

        /// <summary>
        /// Create an Azure Publisher with the specified parameters for json
        /// </summary>
        public static PubSubConfigurationDataType CreateAzurePublisherConfiguration(
            string transportProfileUri,
            string addressUrl,
            object publisherId,
            ushort writerGroupId,
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask,
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData,
            string topic)
        {
            PubSubConfigurationDataType pubSubConfiguration = CreatePublisherConfiguration(
                transportProfileUri,
                addressUrl,
                publisherId,
                writerGroupId,
                (uint)jsonNetworkMessageContentMask,
                (uint)jsonDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray,
                nameSpaceIndexForData);

            foreach (PubSubConnectionDataType pubSubConnection in pubSubConfiguration.Connections)
            {
                foreach (WriterGroupDataType writerGroup in pubSubConnection.WriterGroups)
                {
                    if (ExtensionObject.ToEncodeable(writerGroup.TransportSettings)
                        is BrokerWriterGroupTransportDataType brokerTransportSettings)
                    {
                        brokerTransportSettings.QueueName = topic;
                    }
                }
            }

            return pubSubConfiguration;
        }

        /// <summary>
        /// Create a Publisher with the specified parameters for uadp
        /// </summary>
        public static PubSubConfigurationDataType CreatePublisherConfiguration(
            string transportProfileUri,
            string addressUrl,
            object publisherId,
            ushort writerGroupId,
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask,
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData,
            double metaDataUpdateTime = 0,
            uint keyFrameCount = 1)
        {
            return CreatePublisherConfiguration(
                transportProfileUri,
                addressUrl,
                publisherId,
                writerGroupId,
                (uint)uadpNetworkMessageContentMask,
                (uint)uadpDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray,
                nameSpaceIndexForData,
                metaDataUpdateTime,
                keyFrameCount);
        }

        /// <summary>
        /// Create PubSubConfiguration with configurated DataSetMessages
        /// </summary>
        public static PubSubConfigurationDataType ConfigureDataSetMessages(
            string transportProfileUri,
            string addressUrl,
            ushort writerGroupId,
            uint networkMessageContentMask,
            uint dataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData,
            double metaDataUpdateTime = 0,
            uint keyFrameCount = 1)
        {
            string writerGroupName = $"WriterGroup {writerGroupId}";
            const string brokerMetaData = "$Metadata";

            WriterGroupMessageDataType messageSettings = null;
            WriterGroupTransportDataType transportSettings = null;

            switch (transportProfileUri)
            {
                case Profiles.PubSubUdpUadpTransport:
                    messageSettings = new UadpWriterGroupMessageDataType
                    {
                        DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                        GroupVersion = 0,
                        NetworkMessageContentMask = networkMessageContentMask
                    };
                    transportSettings = new DatagramWriterGroupTransportDataType();
                    break;
                case Profiles.PubSubMqttUadpTransport:
                    messageSettings = new UadpWriterGroupMessageDataType
                    {
                        DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                        GroupVersion = 0,
                        NetworkMessageContentMask = networkMessageContentMask
                    };
                    transportSettings = new BrokerWriterGroupTransportDataType
                    {
                        QueueName = writerGroupName
                    };
                    break;
                case Profiles.PubSubMqttJsonTransport:
                    messageSettings = new JsonWriterGroupMessageDataType
                    {
                        NetworkMessageContentMask = networkMessageContentMask
                    };
                    transportSettings = new BrokerWriterGroupTransportDataType
                    {
                        QueueName = writerGroupName
                    };
                    break;
            }

            WriterGroupDataType writerGroup = CreateWriterGroup(
                writerGroupId,
                messageSettings,
                transportSettings,
                writerGroupName);

            // create all dataset writers
            for (ushort dataSetWriterId = 1;
                dataSetWriterId <= dataSetMetaDataArray.Length;
                dataSetWriterId++)
            {
                DataSetMetaDataType dataSetMetaData = dataSetMetaDataArray[dataSetWriterId - 1];

                // Define DataSetWriter
                var dataSetWriter = new DataSetWriterDataType
                {
                    Name = "Writer id:" + dataSetWriterId,
                    DataSetWriterId = dataSetWriterId,
                    Enabled = true,
                    DataSetFieldContentMask = (uint)dataSetFieldContentMask,
                    DataSetName = dataSetMetaData.Name,
                    KeyFrameCount = keyFrameCount
                };

                DataSetWriterMessageDataType dataSetWriterMessage = null;
                switch (transportProfileUri)
                {
                    case Profiles.PubSubUdpUadpTransport:
                        dataSetWriterMessage = new UadpDataSetWriterMessageDataType
                        {
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        break;
                    case Profiles.PubSubMqttUadpTransport:
                        dataSetWriterMessage = new UadpDataSetWriterMessageDataType
                        {
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        var jsonDataSetWriterTransport2 = new BrokerDataSetWriterTransportDataType
                        {
                            QueueName = writerGroup.Name,
                            MetaDataQueueName = $"{writerGroupName}/{brokerMetaData}",
                            MetaDataUpdateTime = metaDataUpdateTime
                        };
                        dataSetWriter.TransportSettings
                            = new ExtensionObject(jsonDataSetWriterTransport2);
                        break;
                    case Profiles.PubSubMqttJsonTransport:
                        dataSetWriterMessage = new JsonDataSetWriterMessageDataType
                        {
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        var jsonDataSetWriterTransport = new BrokerDataSetWriterTransportDataType
                        {
                            QueueName = writerGroup.Name,
                            MetaDataQueueName = $"{writerGroupName}/{brokerMetaData}",
                            MetaDataUpdateTime = metaDataUpdateTime
                        };
                        dataSetWriter.TransportSettings
                            = new ExtensionObject(jsonDataSetWriterTransport);
                        break;
                }

                dataSetWriter.MessageSettings = new ExtensionObject(dataSetWriterMessage);
                writerGroup.DataSetWriters.Add(dataSetWriter);
            }

            PubSubConnectionDataType pubSubConnection = CreatePubSubConnection(
                transportProfileUri,
                addressUrl,
                publisherId: 1);
            pubSubConnection.WriterGroups.Add(writerGroup);

            //create  the PubSub configuration root object
            var pubSubConfiguration = new PubSubConfigurationDataType
            {
                Connections = [pubSubConnection]
            };

            // creates the published data sets
            for (ushort i = 0; i < dataSetMetaDataArray.Length; i++)
            {
                DataSetMetaDataType dataSetMetaData = dataSetMetaDataArray[i];
                var publishedDataSetDataType = new PublishedDataSetDataType
                {
                    Name = dataSetMetaDataArray[i].Name, //name shall be unique in a configuration
                    // set  publishedDataSetSimple.DataSetMetaData
                    DataSetMetaData = dataSetMetaData
                };

                var publishedDataSetSource = new PublishedDataItemsDataType { PublishedData = [] };
                //create PublishedData based on metadata names
                foreach (FieldMetaData field in dataSetMetaData.Fields)
                {
                    publishedDataSetSource.PublishedData.Add(
                        new PublishedVariableDataType
                        {
                            PublishedVariable = new NodeId(field.Name, nameSpaceIndexForData),
                            AttributeId = Attributes.Value
                        });
                }

                publishedDataSetDataType.DataSetSource
                    = new ExtensionObject(publishedDataSetSource);

                pubSubConfiguration.PublishedDataSets.Add(publishedDataSetDataType);
            }

            return pubSubConfiguration;
        }

        /// <summary>
        /// Create PubSubConfiguration with DataSetMessages for Json
        /// </summary>
        public static PubSubConfigurationDataType ConfigureDataSetMessages(
            string transportProfileUri,
            string addressUrl,
            ushort writerGroupId,
            JsonNetworkMessageContentMask networkMessageContentMask,
            JsonDataSetMessageContentMask dataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData)
        {
            return ConfigureDataSetMessages(
                transportProfileUri,
                addressUrl,
                writerGroupId,
                (uint)networkMessageContentMask,
                (uint)dataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray,
                nameSpaceIndexForData);
        }

        /// <summary>
        /// Create PubSubConfiguration with DataSetMessages for Uadp
        /// </summary>
        public static PubSubConfigurationDataType ConfigureDataSetMessages(
            string transportProfileUri,
            ushort writerGroupId,
            string addressUrl,
            UadpNetworkMessageContentMask networkMessageContentMask,
            UadpDataSetMessageContentMask dataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData)
        {
            return ConfigureDataSetMessages(
                transportProfileUri,
                addressUrl,
                writerGroupId,
                (uint)networkMessageContentMask,
                (uint)dataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray,
                nameSpaceIndexForData);
        }

        /// <summary>
        /// Create dataset writer
        /// </summary>
        public static DataSetWriterDataType CreateDataSetWriter(
            ushort dataSetWriterId,
            string dataSetName,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetWriterMessageDataType messageSettings,
            uint keyFrameCount = 1)
        {
            // Define DataSetWriter 'dataSetName'
            return new DataSetWriterDataType
            {
                Name = $"Writer {dataSetWriterId}",
                DataSetWriterId = dataSetWriterId,
                Enabled = true,
                DataSetFieldContentMask = (uint)dataSetFieldContentMask,
                DataSetName = dataSetName,
                KeyFrameCount = keyFrameCount,

                MessageSettings = new ExtensionObject(messageSettings)
            };
        }

        /// <summary>
        /// Create Published dataset
        /// </summary>
        public static PublishedDataSetDataType CreatePublishedDataSet(
            string dataSetName,
            ushort namespaceIndex,
            FieldMetaDataCollection fieldMetaDatas)
        {
            var publishedDataSet = new PublishedDataSetDataType
            {
                Name = dataSetName, //name shall be unique in a configuration

                // Define publishedDataSet.DataSetMetaData
                DataSetMetaData = CreateDataSetMetaData(dataSetName, namespaceIndex, fieldMetaDatas)
            };
            //publishedDataSet.DataSetMetaData.DataSetClassId = new Uuid(Guid.NewGuid());

            var publishedDataSetSimpleSource = new PublishedDataItemsDataType
            {
                PublishedData = []
            };
            //create PublishedData based on metadata names
            foreach (FieldMetaData field in publishedDataSet.DataSetMetaData.Fields)
            {
                publishedDataSetSimpleSource.PublishedData.Add(
                    new PublishedVariableDataType
                    {
                        PublishedVariable = new NodeId(field.Name, namespaceIndex),
                        AttributeId = Attributes.Value
                    });
            }

            publishedDataSet.DataSetSource = new ExtensionObject(publishedDataSetSimpleSource);

            return publishedDataSet;
        }

        /// <summary>
        /// Create reader group
        /// </summary>
        public static ReaderGroupDataType CreateReaderGroup(
            ushort readerGroupId,
            ReaderGroupMessageDataType messageSettings,
            ReaderGroupTransportDataType transportSettings)
        {
            return new ReaderGroupDataType
            {
                Name = $"ReaderGroup {readerGroupId}",
                Enabled = true,
                MaxNetworkMessageSize = 1500,
                MessageSettings = new ExtensionObject(messageSettings),
                TransportSettings = new ExtensionObject(transportSettings)
            };
        }

        /// <summary>
        /// Get first reader group
        /// </summary>
        public static ReaderGroupDataType GetReaderGroup(
            PubSubConnectionDataType connection,
            ushort writerGroupId)
        {
            if (connection != null)
            {
                return connection.ReaderGroups.Find(x => x.Name == $"ReaderGroup {writerGroupId}");
            }
            return null;
        }

        /// <summary>
        /// Create dataset reader
        /// </summary>
        public static DataSetReaderDataType CreateDataSetReader(
            ushort publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId,
            DataSetMetaDataType dataSetMetaData,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetReaderMessageDataType messageSettings,
            DataSetReaderTransportDataType transportSettings,
            uint keyFrameCount = 1)
        {
            // Define DataSetReader 'dataSetName'
            return new DataSetReaderDataType
            {
                Name = $"Reader {writerGroupId}{dataSetWriterId}",
                PublisherId = publisherId,
                WriterGroupId = writerGroupId,
                DataSetWriterId = dataSetWriterId,
                Enabled = true,
                DataSetFieldContentMask = (uint)dataSetFieldContentMask,
                //dataSetReader.DataSetName = dataSetName;
                KeyFrameCount = keyFrameCount,
                DataSetMetaData = dataSetMetaData,

                MessageSettings = new ExtensionObject(messageSettings),
                TransportSettings = new ExtensionObject(transportSettings)
            };
        }

        /// <summary>
        /// Create a Subscriber with the specified parameters for json
        /// </summary>
        public static PubSubConfigurationDataType CreateSubscriberConfiguration(
            string transportProfileUri,
            string addressUrl,
            object publisherId,
            ushort writerGroupId,
            bool setDataSetWriterId,
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask,
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData,
            uint keyFrameCount = 1)
        {
            return CreateSubscriberConfiguration(
                transportProfileUri,
                addressUrl,
                publisherId,
                writerGroupId,
                setDataSetWriterId,
                (uint)jsonNetworkMessageContentMask,
                (uint)jsonDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray,
                nameSpaceIndexForData,
                keyFrameCount);
        }

        /// <summary>
        /// Create a Subscriber with the specified parameters
        /// </summary>
        private static PubSubConfigurationDataType CreateSubscriberConfiguration(
            string transportProfileUri,
            string addressUrl,
            object publisherId,
            ushort writerGroupId,
            bool setDataSetWriterId,
            uint networkMessageContentMask,
            uint dataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData,
            uint keyFrameCount = 1)
        {
            // Define a PubSub connection with PublisherId
            PubSubConnectionDataType pubSubConnection1 = CreatePubSubConnection(
                transportProfileUri,
                addressUrl,
                publisherId,
                PubSubType.Subscriber);

            string brokerQueueName = $"WriterGroup id:{writerGroupId}";
            const string brokerMetaData = "$Metadata";

            var readerGroup1 = new ReaderGroupDataType
            {
                Name = "ReaderGroup 1",
                Enabled = true,
                MaxNetworkMessageSize = 1500
            };

            for (ushort dataSetWriterId = 1;
                dataSetWriterId <= dataSetMetaDataArray.Length;
                dataSetWriterId++)
            {
                DataSetMetaDataType dataSetMetaData = dataSetMetaDataArray[dataSetWriterId - 1];

                var dataSetReader = new DataSetReaderDataType
                {
                    Name = "dataSetReader:" + dataSetWriterId
                };
                if (publisherId != null)
                {
                    dataSetReader.PublisherId = new Variant(publisherId);
                }
                dataSetReader.WriterGroupId = writerGroupId;
                if (setDataSetWriterId)
                {
                    dataSetReader.DataSetWriterId = dataSetWriterId;
                }
                dataSetReader.Enabled = true;
                dataSetReader.DataSetFieldContentMask = (uint)dataSetFieldContentMask;
                dataSetReader.KeyFrameCount = keyFrameCount;
                dataSetReader.DataSetMetaData = dataSetMetaData;

                DataSetReaderMessageDataType dataSetReaderMessageSettings = null;
                DataSetReaderTransportDataType dataSetReaderTransportSettings = null;
                switch (transportProfileUri)
                {
                    case Profiles.PubSubUdpUadpTransport:
                        dataSetReaderMessageSettings = new UadpDataSetReaderMessageDataType
                        {
                            NetworkMessageContentMask = networkMessageContentMask,
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        break;
                    case Profiles.PubSubMqttUadpTransport:
                        dataSetReaderMessageSettings = new UadpDataSetReaderMessageDataType
                        {
                            NetworkMessageContentMask = networkMessageContentMask,
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        dataSetReaderTransportSettings = new BrokerDataSetReaderTransportDataType
                        {
                            QueueName = brokerQueueName,
                            MetaDataQueueName = $"{brokerQueueName}/{brokerMetaData}"
                        };
                        break;
                    case Profiles.PubSubMqttJsonTransport:
                        dataSetReaderMessageSettings = new JsonDataSetReaderMessageDataType
                        {
                            NetworkMessageContentMask = networkMessageContentMask,
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        dataSetReaderTransportSettings = new BrokerDataSetReaderTransportDataType
                        {
                            QueueName = brokerQueueName,
                            MetaDataQueueName = $"{brokerQueueName}/{brokerMetaData}"
                        };
                        break;
                }

                dataSetReader.MessageSettings = new ExtensionObject(dataSetReaderMessageSettings);
                dataSetReader.TransportSettings
                    = new ExtensionObject(dataSetReaderTransportSettings);

                var subscribedDataSet = new TargetVariablesDataType { TargetVariables = [] };
                foreach (FieldMetaData fieldMetaData in dataSetMetaData.Fields)
                {
                    subscribedDataSet.TargetVariables.Add(
                        new FieldTargetDataType
                        {
                            DataSetFieldId = fieldMetaData.DataSetFieldId,
                            TargetNodeId = new NodeId(fieldMetaData.Name, nameSpaceIndexForData),
                            AttributeId = Attributes.Value,
                            OverrideValueHandling = OverrideValueHandling.OverrideValue,
                            OverrideValue = new Variant(
                                TypeInfo.GetDefaultValue(fieldMetaData.DataType, ValueRanks.Scalar))
                        });
                }

                dataSetReader.SubscribedDataSet = new ExtensionObject(subscribedDataSet);

                readerGroup1.DataSetReaders.Add(dataSetReader);
            }

            pubSubConnection1.ReaderGroups.Add(readerGroup1);

            //create  the PubSub configuration root object
            return new PubSubConfigurationDataType { Connections = [pubSubConnection1] };
        }

        /// <summary>
        /// Create a Subscriber with the specified parameters for uadp
        /// </summary>
        public static PubSubConfigurationDataType CreateSubscriberConfiguration(
            string transportProfileUri,
            string addressUrl,
            object publisherId,
            ushort writerGroupId,
            bool setDataSetWriterId,
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask,
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData,
            uint keyFrameCount = 1)
        {
            return CreateSubscriberConfiguration(
                transportProfileUri,
                addressUrl,
                publisherId,
                writerGroupId,
                setDataSetWriterId,
                (uint)uadpNetworkMessageContentMask,
                (uint)uadpDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray,
                nameSpaceIndexForData,
                keyFrameCount);
        }

        /// <summary>
        /// Create a subscriber configuration for mqtt + udp together
        /// </summary>
        public static PubSubConfigurationDataType CreateUdpPlusMqttSubscriberConfiguration(
            string udpTransportProfileUri,
            string udpAddressUrl,
            object udpPublisherId,
            ushort udpWriterGroupId,
            string mqttTransportProfileUri,
            string mqttAddressUrl,
            object mqttPublisherId,
            ushort mqttWriterGroupId,
            bool setDataSetWriterId,
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask,
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData)
        {
            PubSubConfigurationDataType udpSubscriberConfiguration = CreateSubscriberConfiguration(
                udpTransportProfileUri,
                udpAddressUrl,
                udpPublisherId,
                udpWriterGroupId,
                setDataSetWriterId,
                uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray,
                nameSpaceIndexForData);

            PubSubConfigurationDataType mqttSubscriberConfiguration = CreateSubscriberConfiguration(
                mqttTransportProfileUri,
                mqttAddressUrl,
                mqttPublisherId,
                mqttWriterGroupId,
                setDataSetWriterId,
                uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray,
                nameSpaceIndexForData);

            // add the udp connection too
            if (udpSubscriberConfiguration.Connections != null &&
                udpSubscriberConfiguration.Connections.Count > 0)
            {
                mqttSubscriberConfiguration.Connections
                    .Add(udpSubscriberConfiguration.Connections[0]);
            }

            return mqttSubscriberConfiguration;
        }

        /// <summary>
        /// Create Azure subscriber configuration
        /// </summary>
        public static PubSubConfigurationDataType CreateAzureSubscriberConfiguration(
            string transportProfileUri,
            string addressUrl,
            object publisherId,
            ushort writerGroupId,
            bool setDataSetWriterId,
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask,
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray,
            ushort nameSpaceIndexForData,
            string topic)
        {
            PubSubConfigurationDataType pubSubConfiguration = CreateSubscriberConfiguration(
                transportProfileUri,
                addressUrl,
                publisherId,
                writerGroupId,
                setDataSetWriterId,
                (uint)jsonNetworkMessageContentMask,
                (uint)jsonDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray,
                nameSpaceIndexForData);

            foreach (PubSubConnectionDataType pubSubConnection in pubSubConfiguration.Connections)
            {
                foreach (ReaderGroupDataType readerGroup in pubSubConnection.ReaderGroups)
                {
                    foreach (DataSetReaderDataType dataSetReader in readerGroup.DataSetReaders)
                    {
                        if (ExtensionObject.ToEncodeable(dataSetReader.TransportSettings)
                            is BrokerDataSetReaderTransportDataType brokerTransportSettings)
                        {
                            brokerTransportSettings.QueueName = topic;
                        }
                    }
                }
            }

            return pubSubConfiguration;
        }

        /// <summary>
        /// Create DataSetMetaData type
        /// </summary>
        public static DataSetMetaDataType CreateDataSetMetaData(
            string dataSetName,
            ushort namespaceIndex,
            FieldMetaDataCollection fieldMetaDatas,
            uint majorVersion = 1,
            uint minorVersion = 1)
        {
            return new DataSetMetaDataType
            {
                DataSetClassId = Uuid.Empty,
                Name = dataSetName,
                Fields = fieldMetaDatas,
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = majorVersion,
                    MinorVersion = minorVersion
                },

                Description = LocalizedText.Null
            };
        }

        /// <summary>
        /// Get Uadp | Json type entry
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static List<T> GetUaDataNetworkMessages<T>(IList<T> networkMessages)
            where T : UaNetworkMessage
        {
            if (typeof(T) == typeof(PubSubEncoding.UadpNetworkMessage))
            {
                return GetUadpUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.UadpNetworkMessage>()]) as
                    List<T>;
            }
            if (typeof(T) == typeof(PubSubEncoding.JsonNetworkMessage))
            {
                return GetJsonUaDataNetworkMessages(
                    [.. networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>()]) as
                    List<T>;
            }
            return null;
        }

        /// <summary>
        /// Get Json ua-data entry
        /// </summary>
        public static List<PubSubEncoding.JsonNetworkMessage> GetJsonUaDataNetworkMessages(
            IList<PubSubEncoding.JsonNetworkMessage> networkMessages)
        {
            if (networkMessages != null)
            {
                return [.. networkMessages.Where(x => x.MessageType == UaDataMessageType)];
            }
            return null;
        }

        /// <summary>
        /// Get Uadp DatasetMessage type entry
        /// </summary>
        public static List<PubSubEncoding.UadpNetworkMessage> GetUadpUaDataNetworkMessages(
            IList<PubSubEncoding.UadpNetworkMessage> networkMessages)
        {
            if (networkMessages != null)
            {
                return
                [
                    .. networkMessages.Where(
                        x => x.UADPNetworkMessageType == UADPNetworkMessageType.DataSetMessage)
                ];
            }
            return null;
        }

        /// <summary>
        /// Get Json ua-metadata entries
        /// </summary>
        public static List<PubSubEncoding.JsonNetworkMessage> GetJsonUaMetaDataNetworkMessages(
            IList<PubSubEncoding.JsonNetworkMessage> networkMessages)
        {
            if (networkMessages != null)
            {
                return [.. networkMessages.Where(x => x.MessageType == UaMetaDataMessageType)];
            }
            return null;
        }

        /// <summary>
        /// Get Uadp ua-metadata entries
        /// </summary>
        public static List<PubSubEncoding.UadpNetworkMessage> GetUadpUaMetaDataNetworkMessages(
            IList<PubSubEncoding.UadpNetworkMessage> networkMessages)
        {
            if (networkMessages != null)
            {
                return
                [
                    .. networkMessages.Where(x =>
                        x.UADPNetworkMessageType == UADPNetworkMessageType.DiscoveryResponse &&
                        x.UADPDiscoveryType == UADPNetworkMessageDiscoveryType.DataSetMetaData)
                ];
            }
            return null;
        }

        /// <summary>
        /// Create version of DataSetMetaData matrices
        /// </summary>
        public static DataSetMetaDataType CreateDataSetMetaDataMatrices(string dataSetName)
        {
            // Define  DataSetMetaData
            return new DataSetMetaDataType
            {
                DataSetClassId = new Uuid(Guid.NewGuid()),
                Name = dataSetName,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "BoolToggleMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "SByteMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ByteMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int16Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt16Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int32Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt32Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int64Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt64Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "FloatMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "DoubleMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "StringMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "DateTimeMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "GuidMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Guid,
                        DataType = DataTypeIds.Guid,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ByteStringMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ByteString,
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "XmlElementMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.XmlElement,
                        DataType = DataTypeIds.XmlElement,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "NodeIdMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ExpandedNodeIdMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                        DataType = DataTypeIds.ExpandedNodeId,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "StatusCodeMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.StatusCode,
                        DataType = DataTypeIds.StatusCode,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "QualifiedNameMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.QualifiedName,
                        DataType = DataTypeIds.QualifiedName,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "LocalizedTextMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.LocalizedText,
                        DataType = DataTypeIds.LocalizedText,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "DiagnosticInfoMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                        DataType = DataTypeIds.DiagnosticInfo,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    }
                ],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MinorVersion = 1,
                    MajorVersion = 1
                },
                Description = LocalizedText.Null
            };
        }

        /// <summary>
        /// Create version of DataSetMetaData arrays
        /// </summary>
        public static DataSetMetaDataType CreateDataSetMetaDataArrays(string dataSetName)
        {
            // Define  DataSetMetaData
            return new DataSetMetaDataType
            {
                DataSetClassId = new Uuid(Guid.NewGuid()),
                Name = dataSetName,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "BoolToggleArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "SByteArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ByteArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int16Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt16Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int32Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt32Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int64Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt64Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "FloatArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "DoubleArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "StringArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "DateTimeArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "GuidArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Guid,
                        DataType = DataTypeIds.Guid,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ByteStringArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ByteString,
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "XmlElementArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.XmlElement,
                        DataType = DataTypeIds.XmlElement,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "NodeIdArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ExpandedNodeIdArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                        DataType = DataTypeIds.ExpandedNodeId,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "StatusCodeArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.StatusCode,
                        DataType = DataTypeIds.StatusCode,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "QualifiedNameArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.QualifiedName,
                        DataType = DataTypeIds.QualifiedName,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "LocalizedTextArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.LocalizedText,
                        DataType = DataTypeIds.LocalizedText,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "DiagnosticInfoArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                        DataType = DataTypeIds.DiagnosticInfo,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    }
                ],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MinorVersion = 1,
                    MajorVersion = 1
                },
                Description = LocalizedText.Null
            };
        }

        /// <summary>
        /// Create version 1 of DataSetMetaData
        /// </summary>
        public static DataSetMetaDataType CreateDataSetMetaData1(string dataSetName)
        {
            // Define  DataSetMetaData
            return new DataSetMetaDataType
            {
                DataSetClassId = new Uuid(Guid.NewGuid()),
                Name = dataSetName,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Byte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "SByte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    }
                ],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MinorVersion = 1,
                    MajorVersion = 1
                },
                Description = LocalizedText.Null
            };
        }

        /// <summary>
        /// Create version 2 of DataSetMetaData
        /// </summary>
        public static DataSetMetaDataType CreateDataSetMetaData2(string dataSetName)
        {
            // Define  DataSetMetaData
            return new DataSetMetaDataType
            {
                DataSetClassId = new Uuid(Guid.NewGuid()),
                Name = dataSetName,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "UInt16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt64",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    }
                ],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MinorVersion = 1,
                    MajorVersion = 1
                },
                Description = LocalizedText.Null
            };
        }

        /// <summary>
        /// Create version 3 of DataSetMetaData
        /// </summary>
        public static DataSetMetaDataType CreateDataSetMetaData3(string dataSetName)
        {
            // Define  DataSetMetaData
            return new DataSetMetaDataType
            {
                DataSetClassId = new Uuid(Guid.NewGuid()),
                Name = dataSetName,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "Int16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int64",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.Int64,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    }
                ],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MinorVersion = 1,
                    MajorVersion = 1
                },
                Description = LocalizedText.Null
            };
        }

        /// <summary>
        /// Create DataSetMetaData for all types
        /// </summary>
        public static DataSetMetaDataType CreateDataSetMetaDataAllTypes(string dataSetName)
        {
            // Define  DataSetMetaData
            return new DataSetMetaDataType
            {
                DataSetClassId = new Uuid(Guid.NewGuid()),
                Name = dataSetName,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "SByte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Byte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int64",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.Int64,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt64",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Float",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Double",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "String",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "DateTime",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Guid",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Guid,
                        DataType = DataTypeIds.Guid,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ByteString",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ByteString,
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "XmlElement",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.XmlElement,
                        DataType = DataTypeIds.XmlElement,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "NodeIdNumeric",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "NodeIdGuid",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "NodeIdString",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "NodeIdOpaque",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ExpandedNodeIdNumeric",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                        DataType = DataTypeIds.ExpandedNodeId,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ExpandedNodeIdGuid",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                        DataType = DataTypeIds.ExpandedNodeId,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ExpandedNodeIdString",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                        DataType = DataTypeIds.ExpandedNodeId,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ExpandedNodeIdOpaque",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                        DataType = DataTypeIds.ExpandedNodeId,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "StatusCode",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.StatusCode,
                        DataType = DataTypeIds.StatusCode,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    //new FieldMetaData()
                    //{
                    //    Name = "StatusCodeGood",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.StatusCode,
                    //    DataType = DataTypeIds.StatusCode,
                    //    ValueRank = ValueRanks.Scalar,
                    //    Description = LocalizedText.Null
                    //},
                    new FieldMetaData
                    {
                        Name = "StatusCodeBad",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.StatusCode,
                        DataType = DataTypeIds.StatusCode,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "QualifiedName",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.QualifiedName,
                        DataType = DataTypeIds.QualifiedName,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "LocalizedText",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.LocalizedText,
                        DataType = DataTypeIds.LocalizedText,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    //new FieldMetaData()
                    //{
                    //    Name = "Structure",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.ExtensionObject, // this BuiltinType is not [possible to be decoded yet
                    //    DataType = DataTypeIds.Structure,
                    //    ValueRank = ValueRanks.Scalar,
                    //    Description = LocalizedText.Null
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "DataValue",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.DataValue,
                    //    DataType = DataTypeIds.DataValue,
                    //    ValueRank = ValueRanks.Scalar,
                    //    Description = LocalizedText.Null
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "Variant",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.Variant,
                    //    DataType = DataTypeIds.DataValue,
                    //    ValueRank = ValueRanks.Scalar,
                    //     //    Description = LocalizedText.Null
                    //},
                    new FieldMetaData
                    {
                        Name = "DiagnosticInfo",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                        DataType = DataTypeIds.DiagnosticInfo,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    // Number,Integer,UInteger, Enumeration internal use
                    // Array type
                    new FieldMetaData
                    {
                        Name = "BoolToggleArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "SByteArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ByteArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int16Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt16Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int32Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt32Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int64Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt64Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "FloatArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "DoubleArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "StringArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "DateTimeArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "GuidArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Guid,
                        DataType = DataTypeIds.Guid,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ByteStringArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ByteString,
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "XmlElementArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.XmlElement,
                        DataType = DataTypeIds.XmlElement,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "NodeIdArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ExpandedNodeIdArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                        DataType = DataTypeIds.ExpandedNodeId,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "StatusCodeArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.StatusCode,
                        DataType = DataTypeIds.StatusCode,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "QualifiedNameArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.QualifiedName,
                        DataType = DataTypeIds.QualifiedName,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "LocalizedTextArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.LocalizedText,
                        DataType = DataTypeIds.LocalizedText,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    //new FieldMetaData()
                    //{
                    //    Name = "StructureArray",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.ExtensionObject,
                    //    DataType = DataTypeIds.Structure,
                    //    ValueRank = ValueRanks.OneDimension,
                    //    Description = LocalizedText.Null
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "DataValueArray",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.DataValue,
                    //    DataType = DataTypeIds.DataValue,
                    //    ValueRank = ValueRanks.OneDimension,
                    //    Description = LocalizedText.Null
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "VariantArray",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.Variant,
                    //    DataType = DataTypeIds.DataValue,
                    //    ValueRank = ValueRanks.OneDimension,
                    //    Description = LocalizedText.Null
                    //},
                    new FieldMetaData
                    {
                        Name = "DiagnosticInfoArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                        DataType = DataTypeIds.DiagnosticInfo,
                        ValueRank = ValueRanks.OneDimension,
                        Description = LocalizedText.Null
                    },
                    // Matrix type
                    new FieldMetaData
                    {
                        Name = "BoolToggleMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "SByteMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ByteMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int16Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt16Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int32Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt32Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "Int64Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "UInt64Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "FloatMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "DoubleMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "StringMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "DateTimeMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "GuidMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Guid,
                        DataType = DataTypeIds.Guid,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "ByteStringMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ByteString,
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "XmlElementMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.XmlElement,
                        DataType = DataTypeIds.XmlElement,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "NodeIdMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    //new FieldMetaData()
                    //{
                    //    Name = "ExpandedNodeIdMatrix",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                    //    DataType = DataTypeIds.ExpandedNodeId,
                    //    ValueRank = ValueRanks.TwoDimensions
                    //    Description = LocalizedText.Null
                    //},
                    new FieldMetaData
                    {
                        Name = "StatusCodeMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.StatusCode,
                        DataType = DataTypeIds.StatusCode,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "QualifiedNameMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.QualifiedName,
                        DataType = DataTypeIds.QualifiedName,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData
                    {
                        Name = "LocalizedTextMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.LocalizedText,
                        DataType = DataTypeIds.LocalizedText,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    },
                    //new FieldMetaData()
                    //{
                    //    Name = "StructureMatrix",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.ExtensionObject,
                    //    DataType = DataTypeIds.Structure,
                    //    ValueRank = ValueRanks.TwoDimensions,
                    //    Description = LocalizedText.Null
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "DataValueMatrix",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.DataValue,
                    //    DataType = DataTypeIds.DataValue,
                    //    ValueRank = ValueRanks.TwoDimensions,
                    //    Description = LocalizedText.Null
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "VariantMatrix",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.Variant,
                    //    DataType = DataTypeIds.DataValue,
                    //    ValueRank = ValueRanks.TwoDimensions,
                    //    Description = LocalizedText.Null
                    //},
                    new FieldMetaData
                    {
                        Name = "DiagnosticInfoMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                        DataType = DataTypeIds.DiagnosticInfo,
                        ValueRank = ValueRanks.TwoDimensions,
                        Description = LocalizedText.Null
                    }
                ],
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MinorVersion = 1,
                    MajorVersion = 1
                },
                Description = LocalizedText.Null
            };
        }

        /// <summary>
        /// Load initial publishing data
        /// </summary>
        public static void LoadData(
            UaPubSubApplication pubSubApplication,
            ushort namespaceIndexAllTypes)
        {
            // DataSet fill with primitive data
            var boolToggle = new DataValue(new Variant(false));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("BoolToggle", namespaceIndexAllTypes),
                Attributes.Value,
                boolToggle);
            var byteValue = new DataValue(new Variant((byte)10));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Byte", namespaceIndexAllTypes),
                Attributes.Value,
                byteValue);
            var int16Value = new DataValue(new Variant((short)100));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int16", namespaceIndexAllTypes),
                Attributes.Value,
                int16Value);
            var int32Value = new DataValue(new Variant(1000));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int32", namespaceIndexAllTypes),
                Attributes.Value,
                int32Value);
            var int64Value = new DataValue(new Variant((long)10000));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int64", namespaceIndexAllTypes),
                Attributes.Value,
                int64Value);
            var sByteValue = new DataValue(new Variant((sbyte)11));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("SByte", namespaceIndexAllTypes),
                Attributes.Value,
                sByteValue);
            var uInt16Value = new DataValue(new Variant((ushort)110));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("UInt16", namespaceIndexAllTypes),
                Attributes.Value,
                uInt16Value);
            var uInt32Value = new DataValue(new Variant((uint)1100));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("UInt32", namespaceIndexAllTypes),
                Attributes.Value,
                uInt32Value);
            var uInt64Value = new DataValue(new Variant((ulong)11100));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("UInt64", namespaceIndexAllTypes),
                Attributes.Value,
                uInt64Value);
            var floatValue = new DataValue(new Variant((float)1100.5));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Float", namespaceIndexAllTypes),
                Attributes.Value,
                floatValue);
            var doubleValue = new DataValue(new Variant((double)1100));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Double", namespaceIndexAllTypes),
                Attributes.Value,
                doubleValue);
            var stringValue = new DataValue(new Variant("String info"));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("String", namespaceIndexAllTypes),
                Attributes.Value,
                stringValue);
            var dateTimeVal = new DataValue(new Variant(DateTime.UtcNow));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("DateTime", namespaceIndexAllTypes),
                Attributes.Value,
                dateTimeVal);
            var guidValue = new DataValue(new Variant(new Guid()));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Guid", namespaceIndexAllTypes),
                Attributes.Value,
                guidValue);
            var byteStringValue = new DataValue(new Variant(new byte[] { 1, 2, 3 }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ByteString", namespaceIndexAllTypes),
                Attributes.Value,
                byteStringValue);
            var document = new XmlDocument();
            XmlElement xmlElement = document.CreateElement("test");
            xmlElement.InnerText = "Text";
            var xmlElementValue = new DataValue(new Variant(xmlElement));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("XmlElement", namespaceIndexAllTypes),
                Attributes.Value,
                xmlElementValue);
            var nodeIdValue = new DataValue(new Variant(new NodeId(30, 1)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("NodeId", namespaceIndexAllTypes),
                Attributes.Value,
                nodeIdValue);
            nodeIdValue = new DataValue(new Variant(new NodeId(30, 1)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("NodeIdNumeric", namespaceIndexAllTypes),
                Attributes.Value,
                nodeIdValue);
            nodeIdValue = new DataValue(new Variant(new NodeId(Guid.NewGuid(), 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("NodeIdGuid", namespaceIndexAllTypes),
                Attributes.Value,
                nodeIdValue);
            nodeIdValue = new DataValue(new Variant(new NodeId("NodeIdentifier", 3)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("NodeIdString", namespaceIndexAllTypes),
                Attributes.Value,
                nodeIdValue);
            nodeIdValue = new DataValue(new Variant(new NodeId([1, 2, 3], 0)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("NodeIdOpaque", namespaceIndexAllTypes),
                Attributes.Value,
                nodeIdValue);
            var expandedNodeId = new DataValue(new Variant(new ExpandedNodeId(30, 1)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ExpandedNodeId", namespaceIndexAllTypes),
                Attributes.Value,
                expandedNodeId);
            expandedNodeId = new DataValue(new Variant(new ExpandedNodeId(30, 1)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ExpandedNodeIdNumeric", namespaceIndexAllTypes),
                Attributes.Value,
                expandedNodeId);
            expandedNodeId = new DataValue(new Variant(new ExpandedNodeId(Guid.NewGuid(), 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ExpandedNodeIdGuid", namespaceIndexAllTypes),
                Attributes.Value,
                expandedNodeId);
            expandedNodeId = new DataValue(new Variant(new ExpandedNodeId("NodeIdGuid", 3)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ExpandedNodeIdString", namespaceIndexAllTypes),
                Attributes.Value,
                expandedNodeId);
            expandedNodeId = new DataValue(new Variant(new ExpandedNodeId([1, 2, 3], 0)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ExpandedNodeIdOpaque", namespaceIndexAllTypes),
                Attributes.Value,
                expandedNodeId);
            var statusCode = new DataValue(
                new Variant(new StatusCode(StatusCodes.BadAggregateInvalidInputs)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("StatusCode", namespaceIndexAllTypes),
                Attributes.Value,
                statusCode);
            statusCode = new DataValue(new Variant(new StatusCode(StatusCodes.Good)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("StatusCodeGood", namespaceIndexAllTypes),
                Attributes.Value,
                statusCode);
            statusCode = new DataValue(
                new Variant(new StatusCode(StatusCodes.BadAttributeIdInvalid)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("StatusCodeBad", namespaceIndexAllTypes),
                Attributes.Value,
                statusCode);

            // the extension object cannot be encoded as RawData
            var publisherAddress = new NetworkAddressUrlDataType
            {
                Url = "opc.udp://localhost:4840"
            };
            var extensionObject = new DataValue(
                new Variant(
                    new ExtensionObject(DataTypeIds.NetworkAddressUrlDataType, publisherAddress)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ExtensionObject", namespaceIndexAllTypes),
                Attributes.Value,
                extensionObject);

            var qualifiedValue = new DataValue(new Variant(new QualifiedName("wererwerw", 3)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("QualifiedName", namespaceIndexAllTypes),
                Attributes.Value,
                qualifiedValue);
            var localizedTextValue = new DataValue(
                new Variant(new LocalizedText("Localized_abcd")));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("LocalizedText", namespaceIndexAllTypes),
                Attributes.Value,
                localizedTextValue);
            var dataValue = new DataValue(
                new Variant(
                    new DataValue(new Variant("DataValue_info"), StatusCodes.BadBoundNotFound)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("DataValue", namespaceIndexAllTypes),
                Attributes.Value,
                dataValue);
            var diagnosticInfoValue = new DataValue(
                new Variant(new DiagnosticInfo(1, 1, 1, 1, "Diagnostic_info")));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("DiagnosticInfo", namespaceIndexAllTypes),
                Attributes.Value,
                diagnosticInfoValue);

            // DataSet 'AllTypes' fill with data array
            var boolToggleArray = new DataValue(
                new Variant(new BooleanCollection { true, false, true }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("BoolToggleArray", namespaceIndexAllTypes),
                Attributes.Value,
                boolToggleArray);
            var byteValueArray = new DataValue(new Variant(new byte[] { 127, 101, 1 }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ByteArray", namespaceIndexAllTypes),
                Attributes.Value,
                byteValueArray);
            var int16ValueArray = new DataValue(
                new Variant(new Int16Collection { -100, -200, 300 }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int16Array", namespaceIndexAllTypes),
                Attributes.Value,
                int16ValueArray);
            var int32ValueArray = new DataValue(
                new Variant(new Int32Collection { -1000, -2000, 3000 }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int32Array", namespaceIndexAllTypes),
                Attributes.Value,
                int32ValueArray);
            var int64ValueArray = new DataValue(
                new Variant(new Int64Collection { -10000, -20000, 30000 }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int64Array", namespaceIndexAllTypes),
                Attributes.Value,
                int64ValueArray);
            var sByteValueArray = new DataValue(new Variant(new SByteCollection { 1, -2, -3 }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("SByteArray", namespaceIndexAllTypes),
                Attributes.Value,
                sByteValueArray);
            var uInt16ValueArray = new DataValue(
                new Variant(new UInt16Collection { 110, 120, 130 }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("UInt16Array", namespaceIndexAllTypes),
                Attributes.Value,
                uInt16ValueArray);
            var uInt32ValueArray = new DataValue(
                new Variant(new UInt32Collection { 1100, 1200, 1300 }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("UInt32Array", namespaceIndexAllTypes),
                Attributes.Value,
                uInt32ValueArray);
            var uInt64ValueArray = new DataValue(
                new Variant(new UInt64Collection { 11100, 11200, 11300 }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("UInt64Array", namespaceIndexAllTypes),
                Attributes.Value,
                uInt64ValueArray);
            var floatValueArray = new DataValue(
                new Variant(new FloatCollection { 1100, 5, 1200, 5, 1300, 5 }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("FloatArray", namespaceIndexAllTypes),
                Attributes.Value,
                floatValueArray);
            var doubleValueArray = new DataValue(
                new Variant(new DoubleCollection { 11000.5, 12000.6, 13000.7 }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("DoubleArray", namespaceIndexAllTypes),
                Attributes.Value,
                doubleValueArray);
            var stringValueArray = new DataValue(
                new Variant(new StringCollection { "1a", "2b", "3c" }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("StringArray", namespaceIndexAllTypes),
                Attributes.Value,
                stringValueArray);
            var dateTimeValArray = new DataValue(
                new Variant(
                    new DateTimeCollection
                    {
                        new DateTime(2020, 3, 11).ToUniversalTime(),
                        new DateTime(2021, 2, 17).ToUniversalTime()
                    }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("DateTimeArray", namespaceIndexAllTypes),
                Attributes.Value,
                dateTimeValArray);
            var guidValueArray = new DataValue(
                new Variant(new UuidCollection { new Uuid(new Guid()), new Uuid(new Guid()) }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("GuidArray", namespaceIndexAllTypes),
                Attributes.Value,
                guidValueArray);
            var byteStringValueArray = new DataValue(
                new Variant(
                    new ByteStringCollection { new byte[] { 1, 2, 3 }, new byte[] { 5, 6, 7 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ByteStringArray", namespaceIndexAllTypes),
                Attributes.Value,
                byteStringValueArray);

            XmlElement xmlElement1 = document.CreateElement("test1");
            xmlElement1.InnerText = "Text_2";

            XmlElement xmlElement2 = document.CreateElement("test2");
            xmlElement2.InnerText = "Text_2";
            var xmlElementValueArray = new DataValue(
                new Variant(new XmlElementCollection { xmlElement1, xmlElement2 }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("XmlElementArray", namespaceIndexAllTypes),
                Attributes.Value,
                xmlElementValueArray);
            var nodeIdValueArray = new DataValue(
                new Variant(new NodeIdCollection { new NodeId(30, 1), new NodeId(20, 3) }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("NodeIdArray", namespaceIndexAllTypes),
                Attributes.Value,
                nodeIdValueArray);
            var expandedNodeIdArray = new DataValue(
                new Variant(new ExpandedNodeIdCollection {
                    new ExpandedNodeId(50, 1),
                    new ExpandedNodeId(70, 9) }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ExpandedNodeIdArray", namespaceIndexAllTypes),
                Attributes.Value,
                expandedNodeIdArray);
            var statusCodeArray = new DataValue(
                new Variant(new StatusCodeCollection {
                    StatusCodes.Good,
                    StatusCodes.Bad,
                    StatusCodes.Uncertain }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("StatusCodeArray", namespaceIndexAllTypes),
                Attributes.Value,
                statusCodeArray);
            var qualifiedValueArray = new DataValue(
                new Variant(new QualifiedNameCollection {
                    new QualifiedName("123"),
                    new QualifiedName("abc") }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("QualifiedNameArray", namespaceIndexAllTypes),
                Attributes.Value,
                qualifiedValueArray);
            var localizedTextValueArray = new DataValue(
                new Variant(new LocalizedTextCollection {
                    new LocalizedText("1234"),
                    new LocalizedText("abcd") }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("LocalizedTextArray", namespaceIndexAllTypes),
                Attributes.Value,
                localizedTextValueArray);
            var dataValueArray = new DataValue(
                new Variant(
                    new DataValueCollection
                    {
                        new DataValue(new Variant("DataValue_info1"), StatusCodes.BadBoundNotFound),
                        new DataValue(new Variant("DataValue_info2"), StatusCodes.BadNoData)
                    }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("DataValueArray", namespaceIndexAllTypes),
                Attributes.Value,
                dataValueArray);
            var diagnosticInfoValueArray = new DataValue(
                new Variant(
                    new DiagnosticInfoCollection
                    {
                        new DiagnosticInfo(1, 1, 1, 1, "Diagnostic_info1"),
                        new DiagnosticInfo(2, 2, 2, 2, "Diagnostic_info2")
                    }));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("DiagnosticInfoArray", namespaceIndexAllTypes),
                Attributes.Value,
                diagnosticInfoValueArray);

            // DataSet 'AllTypes' fill with matrix data
            var boolToggleMatrix = new DataValue(
                new Variant(new Matrix(s_elements, BuiltInType.Boolean, 2, 3, 4)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("BoolToggleMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                boolToggleMatrix);
            var byteValueMatrix = new DataValue(
                new Variant(
                    new Matrix(new byte[] { 127, 128, 101, 102 }, BuiltInType.Byte, 2, 2, 1)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ByteMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                byteValueMatrix);
            var int16ValueMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new short[] { -100, -101, -200, -201, -100, -101, -200, -201 },
                        BuiltInType.Int16,
                        2,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int16Matrix", namespaceIndexAllTypes),
                Attributes.Value,
                int16ValueMatrix);
            var int32ValueMatrix = new DataValue(
                new Variant(
                    new Matrix(new int[] { -1000, -1001, -2000, -2001 }, BuiltInType.Int32, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int32Matrix", namespaceIndexAllTypes),
                Attributes.Value,
                int32ValueMatrix);
            var int64ValueMatrix = new DataValue(
                new Variant(new Matrix(
                    new long[] { -10000, -10001, -20000, -20001 },
                    BuiltInType.Int64,
                    2,
                    2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("Int64Matrix", namespaceIndexAllTypes),
                Attributes.Value,
                int64ValueMatrix);
            var sByteValueMatrix = new DataValue(
                new Variant(new Matrix(new sbyte[] { 1, 2, -2, -3 }, BuiltInType.SByte, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("SByteMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                sByteValueMatrix);
            var uInt16ValueMatrix = new DataValue(
                new Variant(
                    new Matrix(new ushort[] { 110, 120, 130, 140 }, BuiltInType.UInt16, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("UInt16Matrix", namespaceIndexAllTypes),
                Attributes.Value,
                uInt16ValueMatrix);
            var uInt32ValueMatrix = new DataValue(
                new Variant(
                    new Matrix(new uint[] { 1100, 1200, 1300, 1400 }, BuiltInType.UInt32, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("UInt32Matrix", namespaceIndexAllTypes),
                Attributes.Value,
                uInt32ValueMatrix);
            var uInt64ValueMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new ulong[] { 11100, 11200, 11300, 11400 },
                        BuiltInType.UInt64,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("UInt64Matrix", namespaceIndexAllTypes),
                Attributes.Value,
                uInt64ValueMatrix);
            var floatValueMatrix = new DataValue(
                new Variant(new Matrix(new float[] { 1100, 5, 1200, 7 }, BuiltInType.Float, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("FloatMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                floatValueMatrix);
            var doubleValueMatrix = new DataValue(
                new Variant(new Matrix(s_elementsArray, BuiltInType.Double, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("DoubleMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                doubleValueMatrix);
            var stringValueMatrix = new DataValue(
                new Variant(new Matrix(s_elementsArray0, BuiltInType.String, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("StringMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                stringValueMatrix);
            var dateTimeValMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new DateTime[]
                        {
                            new DateTime(2020, 3, 11).ToUniversalTime(),
                            new DateTime(2021, 2, 17).ToUniversalTime(),
                            new DateTime(2021, 5, 21).ToUniversalTime(),
                            new DateTime(2020, 7, 23).ToUniversalTime()
                        },
                        BuiltInType.DateTime,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("DateTimeMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                dateTimeValMatrix);
            var guidValueMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new Uuid[] {
                            new(new Guid()),
                            new(new Guid()),
                            new(new Guid()),
                            new(new Guid()) },
                        BuiltInType.Guid,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("GuidMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                guidValueMatrix);
            var byteStringValueMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new byte[][] { [1, 2], [11, 12], [21, 22], [31, 32] },
                        BuiltInType.ByteString,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ByteStringMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                byteStringValueMatrix);

            XmlElement xmlElement1m = document.CreateElement("test1m");
            xmlElement1m.InnerText = "Text_1m";

            XmlElement xmlElement2m = document.CreateElement("test2m");
            xmlElement2m.InnerText = "Text_2m";

            XmlElement xmlElement3m = document.CreateElement("test3m");
            xmlElement3m.InnerText = "Text_3m";

            XmlElement xmlElement4m = document.CreateElement("test4m");
            xmlElement4m.InnerText = "Text_4m";

            var xmlElementValueMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new XmlElement[] { xmlElement1m, xmlElement2m, xmlElement3m, xmlElement4m },
                        BuiltInType.XmlElement,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("XmlElementMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                xmlElementValueMatrix);
            var nodeIdValueMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new NodeId[] { new(30, 1), new(20, 3), new(10, 3), new(50, 7) },
                        BuiltInType.NodeId,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("NodeIdMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                nodeIdValueMatrix);
            var expandedNodeIdMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new ExpandedNodeId[] { new(50, 1), new(70, 9), new(30, 2), new(80, 3) },
                        BuiltInType.ExpandedNodeId,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("ExpandedNodeIdMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                expandedNodeIdMatrix);
            var statusCodeMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new StatusCode[]
                        {
                            StatusCodes.Good,
                            StatusCodes.Uncertain,
                            StatusCodes.BadCertificateInvalid,
                            StatusCodes.Uncertain
                        },
                        BuiltInType.StatusCode,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("StatusCodeMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                statusCodeMatrix);
            var qualifiedValueMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new QualifiedName[] { new("123"), new("abc"), new("456"), new("xyz") },
                        BuiltInType.QualifiedName,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("QualifiedNameMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                qualifiedValueMatrix);
            var localizedTextValueMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new LocalizedText[] { new("1234"), new("abcd"), new("5678"), new("efgh") },
                        BuiltInType.LocalizedText,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("LocalizedTextMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                localizedTextValueMatrix);
            var dataValueMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new DataValue[]
                        {
                            new(new Variant("DataValue_info1"), StatusCodes.BadBoundNotFound),
                            new(new Variant("DataValue_info2"), StatusCodes.BadNoData),
                            new(new Variant("DataValue_info3"), StatusCodes.BadCertificateInvalid),
                            new(new Variant("DataValue_info4"), StatusCodes.GoodCallAgain)
                        },
                        BuiltInType.DataValue,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("DataValueMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                dataValueMatrix);
            var diagnosticInfoValueMatrix = new DataValue(
                new Variant(
                    new Matrix(
                        new DiagnosticInfo[]
                        {
                            new(1, 1, 1, 1, "Diagnostic_info1"),
                            new(2, 2, 2, 2, "Diagnostic_info2"),
                            new(3, 3, 3, 3, "Diagnostic_info3"),
                            new(4, 4, 4, 4, "Diagnostic_info4")
                        },
                        BuiltInType.DiagnosticInfo,
                        2,
                        2)));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("DiagnosticInfoMatrix", namespaceIndexAllTypes),
                Attributes.Value,
                diagnosticInfoValueMatrix);
        }

        /// <summary>
        /// Get datastore data for specified datasets
        /// </summary>
        public static Dictionary<NodeId, DataValue> GetDataStoreData(
            UaPubSubApplication pubSubApplication,
            UaNetworkMessage uaDataNetworkMessage,
            ushort namespaceIndexAllTypes)
        {
            var dataSetsData = new Dictionary<NodeId, DataValue>();

            foreach (UaDataSetMessage datasetMessage in uaDataNetworkMessage.DataSetMessages)
            {
                foreach (Field field in datasetMessage.DataSet.Fields)
                {
                    var fieldNodeId = new NodeId(field.FieldMetaData.Name, namespaceIndexAllTypes);
                    DataValue fieldDataValue = pubSubApplication.DataStore.ReadPublishedDataItem(
                        fieldNodeId,
                        Attributes.Value);
                    if (fieldDataValue != null && !dataSetsData.ContainsKey(fieldNodeId))
                    {
                        dataSetsData.Add(fieldNodeId, fieldDataValue);
                    }
                }
            }

            return dataSetsData;
        }

        /// <summary>
        /// Get snapshot data
        /// </summary>
        public static Dictionary<NodeId, DataValue> GetSnapshotData(
            UaPubSubApplication pubSubApplication,
            ushort namespaceIndexAllTypes)
        {
            var snapshotData = new Dictionary<NodeId, DataValue>();

            var boolNodeId = new NodeId("BoolToggle", namespaceIndexAllTypes);
            DataValue boolToggle = pubSubApplication.DataStore
                .ReadPublishedDataItem(boolNodeId, Attributes.Value);
            snapshotData.Add(boolNodeId, (DataValue)boolToggle.MemberwiseClone());
            var byteNodeId = new NodeId("Byte", namespaceIndexAllTypes);
            DataValue byteValue = pubSubApplication.DataStore
                .ReadPublishedDataItem(byteNodeId, Attributes.Value);
            snapshotData.Add(byteNodeId, (DataValue)byteValue.MemberwiseClone());
            var int16NodeId = new NodeId("Int16", namespaceIndexAllTypes);
            DataValue int16Value = pubSubApplication.DataStore
                .ReadPublishedDataItem(int16NodeId, Attributes.Value);
            snapshotData.Add(int16NodeId, (DataValue)int16Value.MemberwiseClone());
            var int32NodeId = new NodeId("Int32", namespaceIndexAllTypes);
            DataValue int32Value = pubSubApplication.DataStore
                .ReadPublishedDataItem(int32NodeId, Attributes.Value);
            snapshotData.Add(int32NodeId, (DataValue)int32Value.MemberwiseClone());
            var uint16NodeId = new NodeId("UInt16", namespaceIndexAllTypes);
            DataValue uInt16Value = pubSubApplication.DataStore
                .ReadPublishedDataItem(uint16NodeId, Attributes.Value);
            snapshotData.Add(uint16NodeId, (DataValue)uInt16Value.MemberwiseClone());
            var uint32NodeId = new NodeId("UInt32", namespaceIndexAllTypes);
            DataValue uInt32Value = pubSubApplication.DataStore
                .ReadPublishedDataItem(uint32NodeId, Attributes.Value);
            snapshotData.Add(uint32NodeId, (DataValue)uInt32Value.MemberwiseClone());
            var doubleNodeId = new NodeId("Double", namespaceIndexAllTypes);
            DataValue doubleValue = pubSubApplication.DataStore
                .ReadPublishedDataItem(doubleNodeId, Attributes.Value);
            snapshotData.Add(doubleNodeId, (DataValue)doubleValue.MemberwiseClone());
            var dateTimeNodeId = new NodeId("DateTime", namespaceIndexAllTypes);
            DataValue dateTimeValue = pubSubApplication.DataStore.ReadPublishedDataItem(
                dateTimeNodeId,
                Attributes.Value);
            snapshotData.Add(dateTimeNodeId, (DataValue)dateTimeValue.MemberwiseClone());

            return snapshotData;
        }

        /// <summary>
        /// Update snapshot publishing data
        /// </summary>
        public static void UpdateSnapshotData(
            UaPubSubApplication pubSubApplication,
            ushort namespaceIndexAllTypes)
        {
            // DataSet update with primitive data
            DataValue boolToggle = pubSubApplication.DataStore.ReadPublishedDataItem(
                new NodeId("BoolToggle", namespaceIndexAllTypes),
                Attributes.Value);
            if (boolToggle.Value is bool)
            {
                bool boolVal = Convert.ToBoolean(boolToggle.Value, CultureInfo.InvariantCulture);
                boolToggle.Value = !boolVal;
                pubSubApplication.DataStore.WritePublishedDataItem(
                    new NodeId("BoolToggle", namespaceIndexAllTypes),
                    Attributes.Value,
                    boolToggle);
            }
            DataValue byteValue = pubSubApplication.DataStore.ReadPublishedDataItem(
                new NodeId("Byte", namespaceIndexAllTypes),
                Attributes.Value);
            if (byteValue.Value is byte)
            {
                byte byteVal = Convert.ToByte(byteValue.Value, CultureInfo.InvariantCulture);
                byteValue.Value = ++byteVal;
                pubSubApplication.DataStore.WritePublishedDataItem(
                    new NodeId("Byte", namespaceIndexAllTypes),
                    Attributes.Value,
                    byteValue);
            }
            DataValue int16Value = pubSubApplication.DataStore.ReadPublishedDataItem(
                new NodeId("Int16", namespaceIndexAllTypes),
                Attributes.Value);
            if (int16Value.Value is short)
            {
                int intIdentifier = Convert.ToInt16(int16Value.Value, CultureInfo.InvariantCulture);
                Interlocked.CompareExchange(ref intIdentifier, 0, short.MaxValue);
                int16Value.Value = (short)Interlocked.Increment(ref intIdentifier);
                pubSubApplication.DataStore.WritePublishedDataItem(
                    new NodeId("Int16", namespaceIndexAllTypes),
                    Attributes.Value,
                    int16Value);
            }
            DataValue int32Value = pubSubApplication.DataStore.ReadPublishedDataItem(
                new NodeId("Int32", namespaceIndexAllTypes),
                Attributes.Value);
            if (int32Value.Value is int)
            {
                int intIdentifier = Convert.ToInt32(int16Value.Value, CultureInfo.InvariantCulture);
                Interlocked.CompareExchange(ref intIdentifier, 0, int.MaxValue);
                int32Value.Value = Interlocked.Increment(ref intIdentifier);
                pubSubApplication.DataStore.WritePublishedDataItem(
                    new NodeId("Int32", namespaceIndexAllTypes),
                    Attributes.Value,
                    int32Value);
            }
            DataValue uInt16Value = pubSubApplication.DataStore.ReadPublishedDataItem(
                new NodeId("UInt16", namespaceIndexAllTypes),
                Attributes.Value);
            if (uInt16Value.Value is ushort)
            {
                int intIdentifier = Convert.ToUInt16(
                    uInt16Value.Value,
                    CultureInfo.InvariantCulture);
                Interlocked.CompareExchange(ref intIdentifier, 0, ushort.MaxValue);
                uInt16Value.Value = (ushort)Interlocked.Increment(ref intIdentifier);
                pubSubApplication.DataStore.WritePublishedDataItem(
                    new NodeId("UInt16", namespaceIndexAllTypes),
                    Attributes.Value,
                    uInt16Value);
            }
            DataValue uInt32Value = pubSubApplication.DataStore.ReadPublishedDataItem(
                new NodeId("UInt32", namespaceIndexAllTypes),
                Attributes.Value);
            if (uInt32Value.Value is uint)
            {
                long longIdentifier = Convert.ToUInt32(
                    uInt32Value.Value,
                    CultureInfo.InvariantCulture);
                Interlocked.CompareExchange(ref longIdentifier, 0, uint.MaxValue);
                uInt32Value.Value = (uint)Interlocked.Increment(ref longIdentifier);
                pubSubApplication.DataStore.WritePublishedDataItem(
                    new NodeId("UInt32", namespaceIndexAllTypes),
                    Attributes.Value,
                    uInt32Value);
            }
            DataValue doubleValue = pubSubApplication.DataStore.ReadPublishedDataItem(
                new NodeId("Double", namespaceIndexAllTypes),
                Attributes.Value);
            if (doubleValue.Value is double)
            {
                double doubleVal = Convert.ToDouble(
                    doubleValue.Value,
                    CultureInfo.InvariantCulture);
                Interlocked.CompareExchange(ref doubleVal, 0, double.MaxValue);
                doubleValue.Value = ++doubleVal;
                pubSubApplication.DataStore.WritePublishedDataItem(
                    new NodeId("Double", namespaceIndexAllTypes),
                    Attributes.Value,
                    doubleValue);
            }
            var dateTimeValue = new DataValue(new Variant(DateTime.UtcNow));
            pubSubApplication.DataStore.WritePublishedDataItem(
                new NodeId("DateTime", namespaceIndexAllTypes),
                Attributes.Value,
                dateTimeValue);
        }

        /// <summary>
        /// Convert a value type to nullable object
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static T? ConvertToNullable<T>(object value, ILogger logger)
            where T : struct
        {
            string valueString = value?.ToString();
            var nullableObject = new T?();
            try
            {
                if (!string.IsNullOrEmpty(valueString) && valueString.Trim().Length > 0)
                {
                    TypeConverter conv = TypeDescriptor.GetConverter(typeof(T));
                    nullableObject = (T)conv.ConvertFrom(valueString);
                }
            }
            catch (Exception ex)
            {
                logger.LogInformation(ex, "ConvertToNullable exception");
            }

            return nullableObject;
        }
    }
}
