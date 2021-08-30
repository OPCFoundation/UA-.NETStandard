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
using System.Linq;
using System.Collections.Generic;
using System.Xml;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    public class MessagesHelper
    {
        /// <summary>
        /// Ua data message type
        /// </summary>
        private const string UaDataMessageType = "ua-data";
        /// <summary>
        ///  Ua metadata message type
        /// </summary>
        internal const string UaMetaDataMessageType = "ua-metadata";

        /// <summary>
        /// Create PubSub connection
        /// </summary>
        /// <param name="uriScheme"></param>
        /// <param name="publisherId"></param>
        /// <returns></returns>
        public static PubSubConnectionDataType CreatePubSubConnection(string uriScheme, UInt16 publisherId)
        {
            // Define a PubSub connection with PublisherId 30
            PubSubConnectionDataType pubSubConnection = new PubSubConnectionDataType();
            pubSubConnection.Name = string.Format("Connection {0} {1}", uriScheme, publisherId);
            pubSubConnection.Enabled = true;
            pubSubConnection.PublisherId = publisherId;
            pubSubConnection.TransportProfileUri = Profiles.PubSubMqttJsonTransport;
            NetworkAddressUrlDataType address = new NetworkAddressUrlDataType();
            // Specify the local Network interface name to be used
            // e.g. address.NetworkInterface = "Ethernet";
            // Leave empty to publish on all available local interfaces.
            address.NetworkInterface = String.Empty;
            address.Url = uriScheme;
            switch (uriScheme)
            {
                case Utils.UriSchemeOpcUdp:
                default:
                    address.Url += "://239.0.0.1:4840";
                    break;
                case Utils.UriSchemeMqtt:
                    address.Url += "://localhost:1883";
                    break;
                case Utils.UriSchemeMqtts:
                    address.Url += "://localhost:8883";
                    break;
            }
            pubSubConnection.Address = new ExtensionObject(address);

            // Configure the mqtt specific configuration with the MQTTbroker
            ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V500);
            pubSubConnection.TransportSettings = new ExtensionObject(mqttConfiguration);

            return pubSubConnection;
        }

        #region Publisher Methods
        /// <summary>
        /// Create writer group
        /// </summary>
        /// <param name="writerGroupId"></param>
        /// <param name="writerGroupMessageDataType"></param>
        /// <param name="writerGroupTransportDataType"></param>
        /// <returns></returns>
        public static WriterGroupDataType CreateWriterGroup(ushort writerGroupId,
            WriterGroupMessageDataType messageSettings,
            WriterGroupTransportDataType transportSettings)
        {
            WriterGroupDataType writerGroup = new WriterGroupDataType();
            writerGroup.Name = $"WriterGroup { writerGroupId}";
            writerGroup.Enabled = true;
            writerGroup.WriterGroupId = writerGroupId;
            writerGroup.PublishingInterval = 5000;
            writerGroup.KeepAliveTime = 5000;
            writerGroup.MaxNetworkMessageSize = 1500;

            writerGroup.MessageSettings = new ExtensionObject(messageSettings);
            writerGroup.TransportSettings = new ExtensionObject(transportSettings);

            return writerGroup;
        }

        /// <summary>
        /// Create dataset writer
        /// </summary>
        /// <param name="dataSetWriterId"></param>
        /// <param name="dataSetName"></param>
        /// <param name="messageSettings"></param>
        /// <returns></returns>
        public static DataSetWriterDataType CreateDataSetWriter(ushort dataSetWriterId,
            string dataSetName,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetWriterMessageDataType messageSettings)
        {
            // Define DataSetWriter 'dataSetName'
            DataSetWriterDataType dataSetWriter = new DataSetWriterDataType();
            dataSetWriter.Name = $"Writer {dataSetWriterId}";
            dataSetWriter.DataSetWriterId = dataSetWriterId;
            dataSetWriter.Enabled = true;
            dataSetWriter.DataSetFieldContentMask = (uint)dataSetFieldContentMask;
            dataSetWriter.DataSetName = dataSetName;
            dataSetWriter.KeyFrameCount = 1;

            dataSetWriter.MessageSettings = new ExtensionObject(messageSettings);

            return dataSetWriter;
        }

        /// <summary>
        /// Create Published dataset
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <param name="namespaceIndex"></param>
        /// <returns></returns>
        public static PublishedDataSetDataType CreatePublishedDataSet(string dataSetName,
            ushort namespaceIndex,
            FieldMetaDataCollection fieldMetaDatas)
        {
            PublishedDataSetDataType publishedDataSet = new PublishedDataSetDataType();
            publishedDataSet.Name = dataSetName; //name shall be unique in a configuration
            // Define  publishedDataSetSimple.DataSetMetaData
            publishedDataSet.DataSetMetaData = new DataSetMetaDataType();
            publishedDataSet.DataSetMetaData.DataSetClassId = new Uuid(Guid.NewGuid()); 
            publishedDataSet.DataSetMetaData.Name = publishedDataSet.Name;
            publishedDataSet.DataSetMetaData.Fields = fieldMetaDatas;
            publishedDataSet.DataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType()
            {
                MinorVersion = 1,
                MajorVersion = 1
            };

            PublishedDataItemsDataType publishedDataSetSimpleSource = new PublishedDataItemsDataType();
            publishedDataSetSimpleSource.PublishedData = new PublishedVariableDataTypeCollection();
            //create PublishedData based on metadata names
            foreach (var field in publishedDataSet.DataSetMetaData.Fields)
            {
                publishedDataSetSimpleSource.PublishedData.Add(
                    new PublishedVariableDataType()
                    {
                        PublishedVariable = new NodeId(field.Name, namespaceIndex),
                        AttributeId = Attributes.Value,
                    });
            }

            publishedDataSet.DataSetSource = new ExtensionObject(publishedDataSetSimpleSource);

            return publishedDataSet;
        }

        #endregion

        #region Subscriber Methods
        /// <summary>
        /// Create reader group
        /// </summary>
        /// <param name="readerGroupId"></param>
        /// <param name="messageSettings"></param>
        /// <param name="transportSettings"></param>
        /// <returns></returns>
        public static ReaderGroupDataType CreateReaderGroup(ushort readerGroupId,
            ReaderGroupMessageDataType messageSettings,
            ReaderGroupTransportDataType transportSettings)
        {
            ReaderGroupDataType readerGroup = new ReaderGroupDataType();
            readerGroup.Name = $"ReaderGroup { readerGroupId}";
            readerGroup.Enabled = true;
            readerGroup.MaxNetworkMessageSize = 1500;
            readerGroup.MessageSettings = new ExtensionObject(messageSettings);
            readerGroup.TransportSettings = new ExtensionObject(transportSettings);

            return readerGroup;
        }

        /// <summary>
        /// Create dataset reader
        /// </summary>
        /// <param name="dataSetWriterId"></param>
        /// <param name="dataSetName"></param>
        /// <param name="dataSetFieldContentMask"></param>
        /// <param name="messageSettings"></param>
        /// <returns></returns>
        public static DataSetReaderDataType CreateDataSetReader(
            UInt16 publisherId,
            UInt16 writerGroupId,
            UInt16 dataSetWriterId,
            DataSetMetaDataType dataSetMetaData,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetReaderMessageDataType messageSettings,
            DataSetReaderTransportDataType transportSettings)
        {
            // Define DataSetReader 'dataSetName'
            DataSetReaderDataType dataSetReader = new DataSetReaderDataType();
            dataSetReader.Name = $"Reader {writerGroupId}{dataSetWriterId}";
            dataSetReader.PublisherId = publisherId;
            dataSetReader.WriterGroupId = writerGroupId;
            //dataSetReader.DataSetWriterId = 0;
            dataSetReader.DataSetWriterId = dataSetWriterId;
            dataSetReader.Enabled = true;
            dataSetReader.DataSetFieldContentMask = (uint)dataSetFieldContentMask;
            //dataSetReader.DataSetName = dataSetName;
            dataSetReader.KeyFrameCount = 1;
            dataSetReader.DataSetMetaData = dataSetMetaData;

            dataSetReader.MessageSettings = new ExtensionObject(messageSettings);
            dataSetReader.TransportSettings = new ExtensionObject(transportSettings);

            return dataSetReader;
        }

        /// <summary>
        /// Create DataSetMetaData type
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <param name="namespaceIndex"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateDataSetMetaData(string dataSetName,
            ushort namespaceIndex,
            FieldMetaDataCollection fieldMetaDatas,
            uint majorVersion = 1, uint minorVersion = 1)
        {
            DataSetMetaDataType metaData = new DataSetMetaDataType();
            metaData.DataSetClassId = Uuid.Empty;
            metaData.Name = dataSetName;
            metaData.Fields = fieldMetaDatas;
            metaData.ConfigurationVersion = new ConfigurationVersionDataType()
            {
                MajorVersion = majorVersion,
                MinorVersion = minorVersion,
            };

            metaData.Description = LocalizedText.Null;
            return metaData;
        }
        
        #endregion

        /// <summary>
        /// Get first connection
        /// </summary>
        /// <param name="pubSubConfiguration"></param>
        /// <returns></returns>
        public static PubSubConnectionDataType GetConnection(PubSubConfigurationDataType pubSubConfiguration, object publisherId)
        {
            if (pubSubConfiguration != null)
            {
                return pubSubConfiguration.Connections.Find(x => x.PublisherId.Value.Equals(publisherId));
            }
            return null;
        }

        /// <summary>
        /// Get first writer group
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        public static WriterGroupDataType GetWriterGroup(PubSubConnectionDataType connection, UInt16 writerGroupId)
        {
            if (connection != null)
            {
                return connection.WriterGroups.Find(x => x.WriterGroupId.Equals(writerGroupId));
            }
            return null;
        }

        /// <summary>
        /// Get first reader group
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        public static ReaderGroupDataType GetReaderGroup(PubSubConnectionDataType connection, UInt16 writerGroupId)
        {
            if (connection != null)
            {
                return connection.ReaderGroups.Find(x => x.Name == $"ReaderGroup { writerGroupId}");
            }
            return null;
        }

        /// <summary>
        /// Get Json ua-data entry
        /// </summary>
        /// <param name="networkMessages"></param>
        /// <returns></returns>
        public static List<JsonNetworkMessage> GetJsonUaDataNetworkMessages(IList<JsonNetworkMessage> networkMessages)
        {
            if (networkMessages != null)
            {
                return networkMessages.Where(x => x.MessageType == UaDataMessageType).ToList();
            }
            return null;
        }

        /// <summary>
        /// Get Json ua-metadata entries
        /// </summary>
        /// <param name="networkMessages"></param>
        /// <returns></returns>
        public static List<JsonNetworkMessage> GetJsonUaMetaDataNetworkMessages(IList<JsonNetworkMessage> networkMessages)
        {
            if (networkMessages != null)
            {
                return networkMessages.Where(x => x.MessageType == UaMetaDataMessageType).ToList();
            }
            return null;
        }

        /// <summary>
        /// Get Uadp ua-metadata entries
        /// </summary>
        /// <param name="networkMessages"></param>
        /// <returns></returns>
        public static List<UadpNetworkMessage> GetUadpUaMetaDataNetworkMessages(IList<UadpNetworkMessage> networkMessages)
        {
            if (networkMessages != null)
            {
                return networkMessages.Where(x =>x.UADPNetworkMessageType == UADPNetworkMessageType.DiscoveryResponse && x.UADPDiscoveryType == UADPNetworkMessageDiscoveryType.DataSetMetaData).ToList();
            }
            return null;
        }

        /// <summary>
        /// Create a Publisher with the specified parameters
        /// </summary>
        /// <param name="transportProfileUri"></param>
        /// <param name="addressUrl"></param>
        /// <param name="publisherId"></param>
        /// <param name="writerGroupId"></param>
        /// <param name="networkMessageContentMask"></param>
        /// <param name="dataSetMessageContentMask"></param>
        /// <param name="dataSetFieldContentMask"></param>
        /// <param name="dataSetMetaDataArray"></param>
        /// <param name="nameSpaceIndexForData"></param>
        /// <returns></returns>
        private static PubSubConfigurationDataType CreatePublisherConfiguration(
                        string transportProfileUri, string addressUrl,
                        object publisherId, ushort writerGroupId,
                        UInt32 networkMessageContentMask,
                        UInt32 dataSetMessageContentMask,
                        DataSetFieldContentMask dataSetFieldContentMask,
                        DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData,
                        double metaDataUpdateTime=0)
        {

            // Define a PubSub connection with PublisherId 100
            PubSubConnectionDataType pubSubConnection1 = new PubSubConnectionDataType();
            pubSubConnection1.Name = "Connection Publisher PubId:" + publisherId;
            pubSubConnection1.Enabled = true;
            pubSubConnection1.PublisherId = new Variant(publisherId);
            pubSubConnection1.TransportProfileUri = transportProfileUri;

            NetworkAddressUrlDataType address = new NetworkAddressUrlDataType();
            // Specify the local Network interface name to be used
            // e.g. address.NetworkInterface = "Ethernet";
            // Leave empty to publish on all available local interfaces.
            address.NetworkInterface = String.Empty;
            address.Url = addressUrl;
            pubSubConnection1.Address = new ExtensionObject(address);

            string brokerMetaData = "$Metadata";

            #region Define WriterGroup1            
            WriterGroupDataType writerGroup1 = new WriterGroupDataType();
            writerGroup1.Name = "WriterGroup id:" + writerGroupId;
            writerGroup1.Enabled = true;
            writerGroup1.WriterGroupId = writerGroupId;
            writerGroup1.PublishingInterval = 5000;
            writerGroup1.KeepAliveTime = 5000;
            writerGroup1.MaxNetworkMessageSize = 1500;

            WriterGroupMessageDataType messageSettings = null;
            WriterGroupTransportDataType transportSettings = null;
            switch (transportProfileUri)
            {
                case Profiles.PubSubUdpUadpTransport:
                    messageSettings = new UadpWriterGroupMessageDataType()
                    {
                        DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                        GroupVersion = 0,
                        NetworkMessageContentMask = (uint)networkMessageContentMask
                    };
                    transportSettings = new DatagramWriterGroupTransportDataType();
                    break;
                case Profiles.PubSubMqttUadpTransport:
                    messageSettings = new UadpWriterGroupMessageDataType()
                    {
                        DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                        GroupVersion = 0,
                        NetworkMessageContentMask = (uint)networkMessageContentMask
                    };
                    transportSettings = new BrokerWriterGroupTransportDataType()
                    {
                        QueueName = writerGroup1.Name,
                    };
                    break;
                case Profiles.PubSubMqttJsonTransport:
                    messageSettings = new JsonWriterGroupMessageDataType()
                    {
                        NetworkMessageContentMask = (uint)networkMessageContentMask
                    };
                    transportSettings = new BrokerWriterGroupTransportDataType()
                    {
                        QueueName = writerGroup1.Name,
                    };
                    break;
            }

            writerGroup1.MessageSettings = new ExtensionObject(messageSettings);
            writerGroup1.TransportSettings = new ExtensionObject(transportSettings);

            // create all dataset writers
            for (ushort dataSetWriterId = 1; dataSetWriterId <= dataSetMetaDataArray.Length; dataSetWriterId++)
            {
                DataSetMetaDataType dataSetMetaData = dataSetMetaDataArray[dataSetWriterId - 1];
                // Define DataSetWriter
                DataSetWriterDataType dataSetWriter = new DataSetWriterDataType();
                dataSetWriter.Name = "Writer id:" + dataSetWriterId;
                dataSetWriter.DataSetWriterId = dataSetWriterId;
                dataSetWriter.Enabled = true;
                dataSetWriter.DataSetFieldContentMask = (uint)dataSetFieldContentMask;
                dataSetWriter.DataSetName = dataSetMetaData.Name;
                dataSetWriter.KeyFrameCount = 1;

                DataSetWriterMessageDataType dataSetWriterMessage = null;
                switch (transportProfileUri)
                {
                    case Profiles.PubSubUdpUadpTransport:
                        dataSetWriterMessage = new UadpDataSetWriterMessageDataType()
                        {
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        break;
                    case Profiles.PubSubMqttUadpTransport:
                        dataSetWriterMessage = new UadpDataSetWriterMessageDataType()
                        {
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        BrokerDataSetWriterTransportDataType jsonDataSetWriterTransport2 = new BrokerDataSetWriterTransportDataType() {
                            QueueName = writerGroup1.Name,
                            MetaDataQueueName = $"{writerGroup1.Name}/{brokerMetaData}",
                            MetaDataUpdateTime = metaDataUpdateTime
                        };
                        dataSetWriter.TransportSettings = new ExtensionObject(jsonDataSetWriterTransport2);
                        break;
                    case Profiles.PubSubMqttJsonTransport:
                        dataSetWriterMessage = new JsonDataSetWriterMessageDataType()
                        {
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        BrokerDataSetWriterTransportDataType jsonDataSetWriterTransport = new BrokerDataSetWriterTransportDataType()
                        {
                            QueueName = writerGroup1.Name,
                            MetaDataQueueName = $"{writerGroup1.Name}/{brokerMetaData}",
                            MetaDataUpdateTime = metaDataUpdateTime
                        };
                        dataSetWriter.TransportSettings = new ExtensionObject(jsonDataSetWriterTransport);
                        break;

                }

                dataSetWriter.MessageSettings = new ExtensionObject(dataSetWriterMessage);
                writerGroup1.DataSetWriters.Add(dataSetWriter);
            }
            #endregion

            pubSubConnection1.WriterGroups.Add(writerGroup1);

            //create  the PubSub configuration root object
            PubSubConfigurationDataType pubSubConfiguration = new PubSubConfigurationDataType();
            pubSubConfiguration.Connections = new PubSubConnectionDataTypeCollection()
                {
                    pubSubConnection1
                };
            pubSubConfiguration.PublishedDataSets = new PublishedDataSetDataTypeCollection();

            // creates the published data sets
            for (ushort i = 0; i < dataSetMetaDataArray.Length; i++)
            {
                DataSetMetaDataType dataSetMetaData = dataSetMetaDataArray[i];
                PublishedDataSetDataType publishedDataSetDataType = new PublishedDataSetDataType();
                publishedDataSetDataType.Name = dataSetMetaDataArray[i].Name; //name shall be unique in a configuration
                                                                              // set  publishedDataSetSimple.DataSetMetaData
                publishedDataSetDataType.DataSetMetaData = dataSetMetaData;

                PublishedDataItemsDataType publishedDataSetSource = new PublishedDataItemsDataType();
                publishedDataSetSource.PublishedData = new PublishedVariableDataTypeCollection();
                //create PublishedData based on metadata names
                foreach (var field in dataSetMetaData.Fields)
                {
                    publishedDataSetSource.PublishedData.Add(
                        new PublishedVariableDataType()
                        {
                            PublishedVariable = new NodeId(field.Name, nameSpaceIndexForData),
                            AttributeId = Attributes.Value,
                        });
                }

                publishedDataSetDataType.DataSetSource = new ExtensionObject(publishedDataSetSource);

                pubSubConfiguration.PublishedDataSets.Add(publishedDataSetDataType);
            }

            return pubSubConfiguration;
        }

        /// <summary>
        /// Create a Publisher with the specified parameters for json
        /// </summary>
        /// <param name="transportProfileUri"></param>
        /// <param name="addressUrl"></param>
        /// <param name="publisherId"></param>
        /// <param name="writerGroupId"></param>
        /// <param name="jsonNetworkMessageContentMask"></param>
        /// <param name="jsonDataSetMessageContentMask"></param>
        /// <param name="dataSetFieldContentMask"></param>
        /// <param name="dataSetMetaDataArray"></param>
        /// <param name="nameSpaceIndexForData"></param>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreatePublisherConfiguration(
            string transportProfileUri, string addressUrl,
            object publisherId, ushort writerGroupId,
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask,
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData,
            double metaDataUpdateTime = 0)
        {
            return CreatePublisherConfiguration(
                transportProfileUri, addressUrl,
                publisherId, writerGroupId,
                (UInt32)jsonNetworkMessageContentMask,
                (UInt32)jsonDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray, nameSpaceIndexForData, metaDataUpdateTime);
        }

        /// <summary>
        /// Create a Publisher with the specified parameters for mqtt + udp together
        /// </summary>
        /// <param name="udpTransportProfileUri"></param>
        /// <param name="udpAddressUrl"></param>
        /// <param name="udpPublisherId"></param>
        /// <param name="udpWriterGroupId"></param>
        /// <param name="mqttTransportProfileUri"></param>
        /// <param name="mqttAddressUrl"></param>
        /// <param name="mqttPublisherId"></param>
        /// <param name="mqttWriterGroupId"></param>
        /// <param name="uadpNetworkMessageContentMask"></param>
        /// <param name="uadpDataSetMessageContentMask"></param>
        /// <param name="dataSetFieldContentMask"></param>
        /// <param name="dataSetMetaDataArray"></param>
        /// <param name="nameSpaceIndexForData"></param>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreateUdpPlusMqttPublisherConfiguration(
            string udpTransportProfileUri, string udpAddressUrl, object udpPublisherId, ushort udpWriterGroupId,
            string mqttTransportProfileUri, string mqttAddressUrl, object mqttPublisherId, ushort mqttWriterGroupId,
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask,
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData)
        {
            PubSubConfigurationDataType udpPublisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                udpTransportProfileUri,
                udpAddressUrl, publisherId: udpPublisherId, writerGroupId: udpWriterGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: nameSpaceIndexForData);

            PubSubConfigurationDataType mqttPublisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                mqttTransportProfileUri,
                mqttAddressUrl, publisherId: mqttPublisherId, writerGroupId: mqttWriterGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: nameSpaceIndexForData);

            // add the udp connection too
            if (udpPublisherConfiguration.Connections != null &&
                udpPublisherConfiguration.Connections.Count > 0)
            {
                mqttPublisherConfiguration.Connections.Add(udpPublisherConfiguration.Connections[0]);
            }

            return mqttPublisherConfiguration;
        }

        /// <summary>
        /// Create an Azure Publisher with the specified parameters for json
        /// </summary>
        /// <param name="transportProfileUri"></param>
        /// <param name="addressUrl"></param>
        /// <param name="publisherId"></param>
        /// <param name="writerGroupId"></param>
        /// <param name="jsonNetworkMessageContentMask"></param>
        /// <param name="jsonDataSetMessageContentMask"></param>
        /// <param name="dataSetFieldContentMask"></param>
        /// <param name="dataSetMetaDataArray"></param>
        /// <param name="nameSpaceIndexForData"></param>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreateAzurePublisherConfiguration(
            string transportProfileUri, string addressUrl,
            object publisherId, ushort writerGroupId,
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask,
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData, string topic)
        {
            PubSubConfigurationDataType pubSubConfiguration = CreatePublisherConfiguration(
                transportProfileUri, addressUrl,
                publisherId, writerGroupId,
                (UInt32)jsonNetworkMessageContentMask,
                (UInt32)jsonDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray, nameSpaceIndexForData);

            foreach (var pubSubConnection in pubSubConfiguration.Connections)
            {
                foreach (var writerGroup in pubSubConnection.WriterGroups)
                {
                    BrokerWriterGroupTransportDataType brokerTransportSettings = ExtensionObject.ToEncodeable(writerGroup.TransportSettings)
                        as BrokerWriterGroupTransportDataType;
                    if (brokerTransportSettings != null)
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
        /// <param name="transportProfileUri"></param>
        /// <param name="addressUrl"></param>
        /// <param name="publisherId"></param>
        /// <param name="writerGroupId"></param>
        /// <param name="uadpNetworkMessageContentMask"></param>
        /// <param name="uadpDataSetMessageContentMask"></param>
        /// <param name="dataSetFieldContentMask"></param>
        /// <param name="dataSetMetaDataArray"></param>
        /// <param name="nameSpaceIndexForData"></param>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreatePublisherConfiguration(
            string transportProfileUri, string addressUrl,
            object publisherId, ushort writerGroupId,
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask,
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData,
            double metaDataUpdateTime = 0)
        {
            return CreatePublisherConfiguration(
                transportProfileUri, addressUrl,
                publisherId, writerGroupId,
                (UInt32)uadpNetworkMessageContentMask,
                (UInt32)uadpDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray, nameSpaceIndexForData, metaDataUpdateTime);
        }

        /// <summary>
        /// Create a Subscriber with the specified parameters for json
        /// </summary>
        /// <param name="transportProfileUri"></param>
        /// <param name="addressUrl"></param>
        /// <param name="publisherId"></param>
        /// <param name="writerGroupId"></param>
        /// <param name="setDataSetWriterId"></param>
        /// <param name="jsonNetworkMessageContentMask"></param>
        /// <param name="jsonDataSetMessageContentMask"></param>
        /// <param name="dataSetFieldContentMask"></param>
        /// <param name="dataSetMetaDataArray"></param>
        /// <param name="nameSpaceIndexForData"></param>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreateSubscriberConfiguration(
            string transportProfileUri, string addressUrl,
            object publisherId, ushort writerGroupId, bool setDataSetWriterId,
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask,
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData)
        {
            return CreateSubscriberConfiguration(
                transportProfileUri, addressUrl,
                publisherId, writerGroupId, setDataSetWriterId,
                (UInt32)jsonNetworkMessageContentMask,
                (UInt32)jsonDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray, nameSpaceIndexForData);
        }


        /// <summary>
        /// Create a Subscriber with the specified parameters
        /// </summary>
        /// <param name="transportProfileUri"></param>
        /// <param name="addressUrl"></param>
        /// <param name="publisherId"></param>
        /// <param name="writerGroupId"></param>
        /// <param name="setDataSetWriterId"></param>
        /// <param name="networkMessageContentMask"></param>
        /// <param name="dataSetMessageContentMask"></param>
        /// <param name="dataSetFieldContentMask"></param>
        /// <param name="dataSetMetaDataArray"></param>
        /// <param name="nameSpaceIndexForData"></param>
        /// <returns></returns>
        private static PubSubConfigurationDataType CreateSubscriberConfiguration(
            string transportProfileUri, string addressUrl,
            object publisherId, ushort writerGroupId, bool setDataSetWriterId,
            UInt32 networkMessageContentMask,
            UInt32 dataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData)
        {

            // Define a PubSub connection with PublisherId 100
            PubSubConnectionDataType pubSubConnection1 = new PubSubConnectionDataType();
            pubSubConnection1.Name = "Connection Subscriber PubId:" + publisherId;
            pubSubConnection1.Enabled = true;
            if (publisherId != null)
            {
                pubSubConnection1.PublisherId = new Variant(publisherId);
            }
            pubSubConnection1.TransportProfileUri = transportProfileUri;

            NetworkAddressUrlDataType address = new NetworkAddressUrlDataType();
            // Specify the local Network interface name to be used
            // e.g. address.NetworkInterface = "Ethernet";
            // Leave empty to publish on all available local interfaces.
            address.NetworkInterface = String.Empty;
            address.Url = addressUrl;
            pubSubConnection1.Address = new ExtensionObject(address);

            string brokerQueueName = $"WriterGroup id:{writerGroupId}";
            string brokerMetaData = "$Metadata";

            #region Define ReaderGroup1
            ReaderGroupDataType readerGroup1 = new ReaderGroupDataType();
            readerGroup1.Name = "ReaderGroup 1";
            readerGroup1.Enabled = true;
            readerGroup1.MaxNetworkMessageSize = 1500;
            readerGroup1.MessageSettings = new ExtensionObject(new ReaderGroupMessageDataType());
            readerGroup1.TransportSettings = new ExtensionObject(new ReaderGroupTransportDataType());
            #endregion

            for (ushort dataSetWriterId = 1; dataSetWriterId <= dataSetMetaDataArray.Length; dataSetWriterId++)
            {
                DataSetMetaDataType dataSetMetaData = dataSetMetaDataArray[dataSetWriterId - 1];
                #region Define DataSetReader
                DataSetReaderDataType dataSetReader = new DataSetReaderDataType();
                dataSetReader.Name = "dataSetReader:" + dataSetWriterId;
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
                dataSetReader.KeyFrameCount = 1;
                dataSetReader.DataSetMetaData = dataSetMetaData;

                DataSetReaderMessageDataType dataSetReaderMessageSettings = null;
                DataSetReaderTransportDataType dataSetReaderTransportSettings = null;
                switch (transportProfileUri)
                {
                    case Profiles.PubSubUdpUadpTransport:
                        dataSetReaderMessageSettings = new UadpDataSetReaderMessageDataType()
                        {
                            NetworkMessageContentMask = (uint)networkMessageContentMask,
                            DataSetMessageContentMask = (uint)dataSetMessageContentMask,
                        };
                        dataSetReaderTransportSettings = new DataSetReaderTransportDataType();
                        break;
                    case Profiles.PubSubMqttUadpTransport:
                        dataSetReaderMessageSettings = new UadpDataSetReaderMessageDataType()
                        {
                            NetworkMessageContentMask = (uint)networkMessageContentMask,
                            DataSetMessageContentMask = (uint)dataSetMessageContentMask,
                        };
                        dataSetReaderTransportSettings = new BrokerDataSetReaderTransportDataType()
                        {
                            QueueName = brokerQueueName,
                        };
                        break;
                    case Profiles.PubSubMqttJsonTransport:
                        dataSetReaderMessageSettings = new JsonDataSetReaderMessageDataType()
                        {
                            NetworkMessageContentMask = (uint)networkMessageContentMask,
                            DataSetMessageContentMask = (uint)dataSetMessageContentMask,
                        };
                        dataSetReaderTransportSettings = new BrokerDataSetReaderTransportDataType()
                        {
                            QueueName = brokerQueueName,
                            MetaDataQueueName = $"{brokerQueueName}/{brokerMetaData}",
                        };
                        break;
                }

                dataSetReader.MessageSettings = new ExtensionObject(dataSetReaderMessageSettings);
                dataSetReader.TransportSettings = new ExtensionObject(dataSetReaderTransportSettings);

                TargetVariablesDataType subscribedDataSet = new TargetVariablesDataType();
                subscribedDataSet.TargetVariables = new FieldTargetDataTypeCollection();
                foreach (var fieldMetaData in dataSetMetaData.Fields)
                {
                    subscribedDataSet.TargetVariables.Add(new FieldTargetDataType()
                    {
                        DataSetFieldId = fieldMetaData.DataSetFieldId,
                        TargetNodeId = new NodeId(fieldMetaData.Name, nameSpaceIndexForData),
                        AttributeId = Attributes.Value,
                        OverrideValueHandling = OverrideValueHandling.OverrideValue,
                        OverrideValue = new Variant(TypeInfo.GetDefaultValue(fieldMetaData.DataType, (int)ValueRanks.Scalar))
                    });
                }

                dataSetReader.SubscribedDataSet = new ExtensionObject(subscribedDataSet);

                readerGroup1.DataSetReaders.Add(dataSetReader);
                #endregion
            }

            pubSubConnection1.ReaderGroups.Add(readerGroup1);

            //create  the PubSub configuration root object
            PubSubConfigurationDataType pubSubConfiguration = new PubSubConfigurationDataType();
            pubSubConfiguration.Connections = new PubSubConnectionDataTypeCollection()
                {
                    pubSubConnection1
                };

            return pubSubConfiguration;
        }

        /// <summary>
        /// Create a Subscriber with the specified parameters for uadp
        /// </summary>
        /// <param name="transportProfileUri"></param>
        /// <param name="addressUrl"></param>
        /// <param name="publisherId"></param>
        /// <param name="writerGroupId"></param>
        /// <param name="setDataSetWriterId"></param>
        /// <param name="uadpNetworkMessageContentMask"></param>
        /// <param name="uadpDataSetMessageContentMask"></param>
        /// <param name="dataSetFieldContentMask"></param>
        /// <param name="dataSetMetaDataArray"></param>
        /// <param name="nameSpaceIndexForData"></param>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreateSubscriberConfiguration(
            string transportProfileUri, string addressUrl,
            object publisherId, ushort writerGroupId, bool setDataSetWriterId,
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask,
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData)
        {
            return CreateSubscriberConfiguration(
                transportProfileUri, addressUrl,
                publisherId, writerGroupId, setDataSetWriterId,
                (UInt32)uadpNetworkMessageContentMask,
                (UInt32)uadpDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray, nameSpaceIndexForData);
        }

        /// <summary>
        /// Create a subscriber configuration for mqtt + udp together
        /// </summary>
        /// <param name="udpTransportProfileUri"></param>
        /// <param name="udpAddressUrl"></param>
        /// <param name="udpPublisherId"></param>
        /// <param name="udpWriterGroupId"></param>
        /// <param name="mqttTransportProfileUri"></param>
        /// <param name="mqttAddressUrl"></param>
        /// <param name="mqttPublisherId"></param>
        /// <param name="mqttWriterGroupId"></param>
        /// <param name="setDataSetWriterId"></param>
        /// <param name="uadpNetworkMessageContentMask"></param>
        /// <param name="uadpDataSetMessageContentMask"></param>
        /// <param name="dataSetFieldContentMask"></param>
        /// <param name="dataSetMetaDataArray"></param>
        /// <param name="nameSpaceIndexForData"></param>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreateUdpPlusMqttSubscriberConfiguration(
            string udpTransportProfileUri, string udpAddressUrl, object udpPublisherId, ushort udpWriterGroupId,
            string mqttTransportProfileUri, string mqttAddressUrl, object mqttPublisherId, ushort mqttWriterGroupId,
            bool setDataSetWriterId,
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask,
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData)
        {
            PubSubConfigurationDataType udpSubscriberConfiguration = CreateSubscriberConfiguration(
                udpTransportProfileUri, udpAddressUrl,
                udpPublisherId, udpWriterGroupId, setDataSetWriterId,
                uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray, nameSpaceIndexForData);

            PubSubConfigurationDataType mqttSubscriberConfiguration = CreateSubscriberConfiguration(
                mqttTransportProfileUri, mqttAddressUrl,
                mqttPublisherId, mqttWriterGroupId, setDataSetWriterId,
                uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray, nameSpaceIndexForData);

            // add the udp connection too
            if (udpSubscriberConfiguration.Connections != null &&
                udpSubscriberConfiguration.Connections.Count > 0)
            {
                mqttSubscriberConfiguration.Connections.Add(udpSubscriberConfiguration.Connections[0]);
            }

            return mqttSubscriberConfiguration;
        }

        /// <summary>
        /// Create Azure subscriber configuration
        /// </summary>
        /// <param name="transportProfileUri"></param>
        /// <param name="addressUrl"></param>
        /// <param name="publisherId"></param>
        /// <param name="writerGroupId"></param>
        /// <param name="setDataSetWriterId"></param>
        /// <param name="jsonNetworkMessageContentMask"></param>
        /// <param name="jsonDataSetMessageContentMask"></param>
        /// <param name="dataSetFieldContentMask"></param>
        /// <param name="dataSetMetaDataArray"></param>
        /// <param name="nameSpaceIndexForData"></param>
        /// <param name="topic"></param>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreateAzureSubscriberConfiguration(
            string transportProfileUri, string addressUrl,
            object publisherId, ushort writerGroupId, bool setDataSetWriterId,
            JsonNetworkMessageContentMask jsonNetworkMessageContentMask,
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask,
            DataSetFieldContentMask dataSetFieldContentMask,
            DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData, string topic)
        {
            PubSubConfigurationDataType pubSubConfiguration = CreateSubscriberConfiguration(
                transportProfileUri, addressUrl,
                publisherId, writerGroupId, setDataSetWriterId,
                (UInt32)jsonNetworkMessageContentMask,
                (UInt32)jsonDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray, nameSpaceIndexForData);

            foreach (var pubSubConnection in pubSubConfiguration.Connections)
            {
                foreach (var readerGroup in pubSubConnection.ReaderGroups)
                {
                    foreach (var dataSetReader in readerGroup.DataSetReaders)
                    {
                        BrokerDataSetReaderTransportDataType brokerTransportSettings = ExtensionObject.ToEncodeable(dataSetReader.TransportSettings)
                        as BrokerDataSetReaderTransportDataType;
                        if (brokerTransportSettings != null)
                        {
                            brokerTransportSettings.QueueName = topic;
                        }
                    }
                }
            }

            return pubSubConfiguration;
        }

        /// <summary>
        /// Create version of DataSetMetaData matrices
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateDataSetMetaDataMatrices(string dataSetName)
        {
            // Define  DataSetMetaData
            DataSetMetaDataType dataSetMetaData = new DataSetMetaDataType();
            dataSetMetaData.DataSetClassId = new Uuid(Guid.NewGuid());
            dataSetMetaData.Name = dataSetName;
            dataSetMetaData.Fields = new FieldMetaDataCollection()
                {
                   new FieldMetaData()
                    {
                        Name = "BoolToggleMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "SByteMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "ByteMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "Int16Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt16Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt32Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "Int64Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt64Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "FloatMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "DoubleMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "StringMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "DateTimeMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "GuidMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Guid,
                        DataType = DataTypeIds.Guid,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "ByteStringMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ByteString,
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "XmlElementMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.XmlElement,
                        DataType = DataTypeIds.XmlElement,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "NodeIdMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "ExpandedNodeIdMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                        DataType = DataTypeIds.ExpandedNodeId,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "StatusCodeMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.StatusCode,
                        DataType = DataTypeIds.StatusCode,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "QualifiedNameMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.QualifiedName,
                        DataType = DataTypeIds.QualifiedName,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "LocalizedTextMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.LocalizedText,
                        DataType = DataTypeIds.LocalizedText,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "DiagnosticInfoMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                        DataType = DataTypeIds.DiagnosticInfo,
                        ValueRank = ValueRanks.TwoDimensions, Description = LocalizedText.Null 
                    },
                };
            dataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType()
            {
                MinorVersion = 1,
                MajorVersion = 1
            };
            dataSetMetaData.Description = LocalizedText.Null;
            return dataSetMetaData;
        }

        /// <summary>
        /// Create version of DataSetMetaData arrays
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateDataSetMetaDataArrays(string dataSetName)
        {
            // Define  DataSetMetaData
            DataSetMetaDataType dataSetMetaData = new DataSetMetaDataType();
            dataSetMetaData.DataSetClassId = new Uuid(Guid.NewGuid());
            dataSetMetaData.Name = dataSetName;
            dataSetMetaData.Fields = new FieldMetaDataCollection()
                {
                   new FieldMetaData()
                    {
                        Name = "BoolToggleArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "SByteArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "ByteArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "Int16Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt16Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt32Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "Int64Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt64Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "FloatArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "DoubleArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "StringArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "DateTimeArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "GuidArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Guid,
                        DataType = DataTypeIds.Guid,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "ByteStringArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ByteString,
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "XmlElementArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.XmlElement,
                        DataType = DataTypeIds.XmlElement,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "NodeIdArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "ExpandedNodeIdArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                        DataType = DataTypeIds.ExpandedNodeId,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "StatusCodeArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.StatusCode,
                        DataType = DataTypeIds.StatusCode,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "QualifiedNameArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.QualifiedName,
                        DataType = DataTypeIds.QualifiedName,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "LocalizedTextArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.LocalizedText,
                        DataType = DataTypeIds.LocalizedText,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                    new FieldMetaData()
                    {
                        Name = "DiagnosticInfoArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                        DataType = DataTypeIds.DiagnosticInfo,
                        ValueRank = ValueRanks.OneDimension, Description = LocalizedText.Null 
                    },
                };
            dataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType()
            {
                MinorVersion = 1,
                MajorVersion = 1
            };
            dataSetMetaData.Description = LocalizedText.Null;
            return dataSetMetaData;
        }

        /// <summary>
        /// Create version 1 of DataSetMetaData
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateDataSetMetaData1(string dataSetName)
        {
            // Define  DataSetMetaData
            DataSetMetaDataType dataSetMetaData = new DataSetMetaDataType();
            dataSetMetaData.DataSetClassId = new Uuid(Guid.NewGuid()); 
            dataSetMetaData.Name = dataSetName;
            dataSetMetaData.Fields = new FieldMetaDataCollection()
                {
                    new FieldMetaData()
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData()
                    {
                        Name = "Byte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData()
                    {
                        Name = "SByte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                };
            dataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType()
            {
                MinorVersion = 1,
                MajorVersion = 1
            };
            dataSetMetaData.Description = LocalizedText.Null;
            return dataSetMetaData;
        }

        /// <summary>
        /// Create version 2 of DataSetMetaData
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateDataSetMetaData2(string dataSetName)
        {
            // Define  DataSetMetaData
            DataSetMetaDataType dataSetMetaData = new DataSetMetaDataType();
            dataSetMetaData.DataSetClassId = new Uuid(Guid.NewGuid());
            dataSetMetaData.Name = dataSetName;
            dataSetMetaData.Fields = new FieldMetaDataCollection()
                {
                    new FieldMetaData()
                    {
                        Name = "UInt16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt64",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    }
                };
            dataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType()
            {
                MinorVersion = 1,
                MajorVersion = 1
            };
            dataSetMetaData.Description = LocalizedText.Null;
            return dataSetMetaData;
        }

        /// <summary>
        /// Create version 3 of DataSetMetaData
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateDataSetMetaData3(string dataSetName)
        {
            // Define  DataSetMetaData
            DataSetMetaDataType dataSetMetaData = new DataSetMetaDataType();
            dataSetMetaData.DataSetClassId = new Uuid(Guid.NewGuid());
            dataSetMetaData.Name = dataSetName;
            dataSetMetaData.Fields = new FieldMetaDataCollection()
                {
                    new FieldMetaData()
                    {
                        Name = "Int16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    },
                    new FieldMetaData()
                    {
                        Name = "Int64",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.Int64,
                        ValueRank = ValueRanks.Scalar,
                        Description = LocalizedText.Null
                    }
                };
            dataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType()
            {
                MinorVersion = 1,
                MajorVersion = 1
            };
            dataSetMetaData.Description = LocalizedText.Null;
            return dataSetMetaData;
        }

        /// <summary>
        /// Create DataSetMetaData for all types
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateDataSetMetaDataAllTypes(string dataSetName)
        {
            // Define  DataSetMetaData
            DataSetMetaDataType dataSetMetaData = new DataSetMetaDataType();
            dataSetMetaData.DataSetClassId = new Uuid(Guid.NewGuid());
            dataSetMetaData.Name = dataSetName;
            dataSetMetaData.Fields = new FieldMetaDataCollection()
                {
                new FieldMetaData()
                {
                    Name = "BoolToggle",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Boolean,
                    DataType = DataTypeIds.Boolean,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "SByte",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.SByte,
                    DataType = DataTypeIds.SByte,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Byte",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Byte,
                    DataType = DataTypeIds.Byte,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Int16",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Int16,
                    DataType = DataTypeIds.Int16,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "UInt16",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.UInt16,
                    DataType = DataTypeIds.UInt16,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Int32",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Int32,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "UInt32",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                     BuiltInType = (byte)BuiltInType.UInt32,
                    DataType = DataTypeIds.UInt32,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Int64",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Int64,
                    DataType = DataTypeIds.UInt64,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "UInt64",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                     BuiltInType = (byte)BuiltInType.UInt64,
                    DataType = DataTypeIds.UInt64,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Float",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Float,
                    DataType = DataTypeIds.Float,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Double",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Double,
                    DataType = DataTypeIds.Double,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "String",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.String,
                    DataType = DataTypeIds.String,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "DateTime",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.DateTime,
                    DataType = DataTypeIds.DateTime,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Guid",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Guid,
                    DataType = DataTypeIds.Guid,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "ByteString",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.ByteString,
                    DataType = DataTypeIds.ByteString,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "XmlElement",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.XmlElement,
                    DataType = DataTypeIds.XmlElement,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "NodeIdNumeric",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.NodeId,
                    DataType = DataTypeIds.NodeId,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "NodeIdGuid",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.NodeId,
                    DataType = DataTypeIds.NodeId,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "NodeIdString",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.NodeId,
                    DataType = DataTypeIds.NodeId,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "NodeIdOpaque",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.NodeId,
                    DataType = DataTypeIds.NodeId,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "ExpandedNodeIdNumeric",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                    DataType = DataTypeIds.ExpandedNodeId,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "ExpandedNodeIdGuid",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                    DataType = DataTypeIds.ExpandedNodeId,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "ExpandedNodeIdString",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                    DataType = DataTypeIds.ExpandedNodeId,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "ExpandedNodeIdOpaque",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                    DataType = DataTypeIds.ExpandedNodeId,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "StatusCodeGood",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    DataType = DataTypeIds.StatusCode,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "StatusCodeBad",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    DataType = DataTypeIds.StatusCode,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "QualifiedName",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.QualifiedName,
                    DataType = DataTypeIds.QualifiedName,
                    ValueRank = ValueRanks.Scalar,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
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
                new FieldMetaData()
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
                new FieldMetaData()
                {
                    Name = "BoolToggleArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Boolean,
                    DataType = DataTypeIds.Boolean,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "SByteArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.SByte,
                    DataType = DataTypeIds.SByte,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "ByteArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Byte,
                    DataType = DataTypeIds.Byte,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Int16Array",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Int16,
                    DataType = DataTypeIds.Int16,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "UInt16Array",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.UInt16,
                    DataType = DataTypeIds.UInt16,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Int32Array",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Int32,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "UInt32Array",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                     BuiltInType = (byte)BuiltInType.UInt32,
                    DataType = DataTypeIds.UInt32,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Int64Array",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Int64,
                    DataType = DataTypeIds.UInt64,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "UInt64Array",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                     BuiltInType = (byte)BuiltInType.UInt64,
                    DataType = DataTypeIds.UInt64,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "FloatArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Float,
                    DataType = DataTypeIds.Float,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "DoubleArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Double,
                    DataType = DataTypeIds.Double,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "StringArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.String,
                    DataType = DataTypeIds.String,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "DateTimeArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.DateTime,
                    DataType = DataTypeIds.DateTime,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "GuidArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Guid,
                    DataType = DataTypeIds.Guid,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "ByteStringArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.ByteString,
                    DataType = DataTypeIds.ByteString,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "XmlElementArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.XmlElement,
                    DataType = DataTypeIds.XmlElement,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "NodeIdArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.NodeId,
                    DataType = DataTypeIds.NodeId,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "ExpandedNodeIdArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                    DataType = DataTypeIds.ExpandedNodeId,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "StatusCodeArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    DataType = DataTypeIds.StatusCode,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "QualifiedNameArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.QualifiedName,
                    DataType = DataTypeIds.QualifiedName,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
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
                new FieldMetaData()
                {
                    Name = "DiagnosticInfoArray",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                    DataType = DataTypeIds.DiagnosticInfo,
                    ValueRank = ValueRanks.OneDimension,
                    Description = LocalizedText.Null
                },
                // Matrix type
                new FieldMetaData()
                {
                    Name = "BoolToggleMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Boolean,
                    DataType = DataTypeIds.Boolean,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "SByteMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.SByte,
                    DataType = DataTypeIds.SByte,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "ByteMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Byte,
                    DataType = DataTypeIds.Byte,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Int16Matrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Int16,
                    DataType = DataTypeIds.Int16,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "UInt16Matrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.UInt16,
                    DataType = DataTypeIds.UInt16,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Int32Matrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Int32,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "UInt32Matrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                     BuiltInType = (byte)BuiltInType.UInt32,
                    DataType = DataTypeIds.UInt32,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "Int64Matrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Int64,
                    DataType = DataTypeIds.UInt64,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "UInt64Matrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                     BuiltInType = (byte)BuiltInType.UInt64,
                    DataType = DataTypeIds.UInt64,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "FloatMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Float,
                    DataType = DataTypeIds.Float,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "DoubleMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Double,
                    DataType = DataTypeIds.Double,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "StringMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.String,
                    DataType = DataTypeIds.String,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "DateTimeMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.DateTime,
                    DataType = DataTypeIds.DateTime,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "GuidMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.Guid,
                    DataType = DataTypeIds.Guid,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "ByteStringMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.ByteString,
                    DataType = DataTypeIds.ByteString,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "XmlElementMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.XmlElement,
                    DataType = DataTypeIds.XmlElement,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
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
                new FieldMetaData()
                {
                    Name = "StatusCodeMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.StatusCode,
                    DataType = DataTypeIds.StatusCode,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
                {
                    Name = "QualifiedNameMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.QualifiedName,
                    DataType = DataTypeIds.QualifiedName,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },
                new FieldMetaData()
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
                new FieldMetaData()
                {
                    Name = "DiagnosticInfoMatrix",
                    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                    DataType = DataTypeIds.DiagnosticInfo,
                    ValueRank = ValueRanks.TwoDimensions,
                    Description = LocalizedText.Null
                },

            };
            dataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType()
            {
                MinorVersion = 1,
                MajorVersion = 1
            };
            dataSetMetaData.Description = LocalizedText.Null;
            return dataSetMetaData;
        }

        /// <summary>
        /// Load publishing data
        /// </summary>
        /// <param name="pubSubApplication"></param>
        public static void LoadData(UaPubSubApplication pubSubApplication, UInt16 namespaceIndexAllTypes)
        {
            #region DataSet AllTypes
            // DataSet 'AllTypes' fill with primitive data
            DataValue boolToggle = new DataValue(new Variant(false));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggle", namespaceIndexAllTypes), Attributes.Value, boolToggle);
            DataValue byteValue = new DataValue(new Variant((byte)10));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Byte", namespaceIndexAllTypes), Attributes.Value, byteValue);
            DataValue int16Value = new DataValue(new Variant((short)100));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int16", namespaceIndexAllTypes), Attributes.Value, int16Value);
            DataValue int32Value = new DataValue(new Variant((int)1000));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int32", namespaceIndexAllTypes), Attributes.Value, int32Value);
            DataValue int64Value = new DataValue(new Variant((Int64)10000));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int64", namespaceIndexAllTypes), Attributes.Value, int64Value);
            DataValue sByteValue = new DataValue(new Variant((sbyte)11));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("SByte", namespaceIndexAllTypes), Attributes.Value, sByteValue);
            DataValue uInt16Value = new DataValue(new Variant((ushort)110));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt16", namespaceIndexAllTypes), Attributes.Value, uInt16Value);
            DataValue uInt32Value = new DataValue(new Variant((uint)1100));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt32", namespaceIndexAllTypes), Attributes.Value, uInt32Value);
            DataValue uInt64Value = new DataValue(new Variant((UInt64)11100));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt64", namespaceIndexAllTypes), Attributes.Value, uInt64Value);
            DataValue floatValue = new DataValue(new Variant((float)1100.5));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Float", namespaceIndexAllTypes), Attributes.Value, floatValue);
            DataValue doubleValue = new DataValue(new Variant((double)1100));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Double", namespaceIndexAllTypes), Attributes.Value, doubleValue);
            DataValue stringValue = new DataValue(new Variant("String info"));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("String", namespaceIndexAllTypes), Attributes.Value, stringValue);
            DataValue dateTimeVal = new DataValue(new Variant(DateTime.UtcNow));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DateTime", namespaceIndexAllTypes), Attributes.Value, dateTimeVal);
            DataValue guidValue = new DataValue(new Variant(new Guid()));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Guid", namespaceIndexAllTypes), Attributes.Value, guidValue);
            DataValue byteStringValue = new DataValue(new Variant(new byte[] { 1, 2, 3 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ByteString", namespaceIndexAllTypes), Attributes.Value, byteStringValue);
            XmlDocument document = new XmlDocument();
            XmlElement xmlElement = document.CreateElement("test");
            xmlElement.InnerText = "Text";
            DataValue xmlElementValue = new DataValue(new Variant(xmlElement));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("XmlElement", namespaceIndexAllTypes), Attributes.Value, xmlElementValue);
            DataValue nodeIdValue = new DataValue(new Variant(new NodeId(30, 1)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("NodeId", namespaceIndexAllTypes), Attributes.Value, nodeIdValue);
            nodeIdValue = new DataValue(new Variant(new NodeId(30, 1)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("NodeIdNumeric", namespaceIndexAllTypes), Attributes.Value, nodeIdValue);
            nodeIdValue = new DataValue(new Variant(new NodeId(Guid.NewGuid(), 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("NodeIdGuid", namespaceIndexAllTypes), Attributes.Value, nodeIdValue);
            nodeIdValue = new DataValue(new Variant(new NodeId("NodeIdentifier", 3)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("NodeIdString", namespaceIndexAllTypes), Attributes.Value, nodeIdValue);
            nodeIdValue = new DataValue(new Variant(new NodeId(new byte[] { 1, 2, 3 }, 0)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("NodeIdOpaque", namespaceIndexAllTypes), Attributes.Value, nodeIdValue);
            DataValue expandedNodeId = new DataValue(new Variant(new ExpandedNodeId(30, 1)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ExpandedNodeId", namespaceIndexAllTypes), Attributes.Value, expandedNodeId);
            expandedNodeId = new DataValue(new Variant(new ExpandedNodeId(30, 1)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ExpandedNodeIdNumeric", namespaceIndexAllTypes), Attributes.Value, expandedNodeId);
            expandedNodeId = new DataValue(new Variant(new ExpandedNodeId(Guid.NewGuid(), 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ExpandedNodeIdGuid", namespaceIndexAllTypes), Attributes.Value, expandedNodeId);
            expandedNodeId = new DataValue(new Variant(new ExpandedNodeId("NodeIdGuid", 3)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ExpandedNodeIdString", namespaceIndexAllTypes), Attributes.Value, expandedNodeId);
            expandedNodeId = new DataValue(new Variant(new ExpandedNodeId(new byte[] { 1, 2, 3 }, 0)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ExpandedNodeIdOpaque", namespaceIndexAllTypes), Attributes.Value, expandedNodeId);
            DataValue statusCode = new DataValue(new Variant(new StatusCode(StatusCodes.BadAggregateInvalidInputs)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("StatusCode", namespaceIndexAllTypes), Attributes.Value, statusCode);
            statusCode = new DataValue(new Variant(new StatusCode(StatusCodes.Good)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("StatusCodeGood", namespaceIndexAllTypes), Attributes.Value, statusCode);
            statusCode = new DataValue(new Variant(new StatusCode(StatusCodes.BadAttributeIdInvalid)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("StatusCodeBad", namespaceIndexAllTypes), Attributes.Value, statusCode);

            // the extension object cannot be encoded as RawData
            NetworkAddressUrlDataType publisherAddress = new NetworkAddressUrlDataType();
            publisherAddress.Url = "opc.udp://localhost:4840";
            DataValue extensionObject = new DataValue(new Variant(new ExtensionObject(DataTypeIds.NetworkAddressUrlDataType, publisherAddress)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ExtensionObject", namespaceIndexAllTypes), Attributes.Value, extensionObject);

            DataValue qualifiedValue = new DataValue(new Variant(new QualifiedName("wererwerw", 3)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("QualifiedName", namespaceIndexAllTypes), Attributes.Value, qualifiedValue);
            DataValue localizedTextValue = new DataValue(new Variant(new LocalizedText("Localized_abcd")));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("LocalizedText", namespaceIndexAllTypes), Attributes.Value, localizedTextValue);
            DataValue dataValue = new DataValue(new Variant(new DataValue(new Variant("DataValue_info"), StatusCodes.BadBoundNotFound)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DataValue", namespaceIndexAllTypes), Attributes.Value, dataValue);
            DataValue diagnosticInfoValue = new DataValue(new Variant(new DiagnosticInfo(1, 1, 1, 1, "Diagnostic_info")));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DiagnosticInfo", namespaceIndexAllTypes), Attributes.Value, diagnosticInfoValue);

            // DataSet 'AllTypes' fill with data array
            DataValue boolToggleArray = new DataValue(new Variant(new BooleanCollection() { true, false, true }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggleArray", namespaceIndexAllTypes), Attributes.Value, boolToggleArray);
            DataValue byteValueArray = new DataValue(new Variant(new byte[] { 127, 101, 1 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ByteArray", namespaceIndexAllTypes), Attributes.Value, byteValueArray);
            DataValue int16ValueArray = new DataValue(new Variant(new Int16Collection() { -100, -200, 300 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int16Array", namespaceIndexAllTypes), Attributes.Value, int16ValueArray);
            DataValue int32ValueArray = new DataValue(new Variant(new Int32Collection() { -1000, -2000, 3000 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int32Array", namespaceIndexAllTypes), Attributes.Value, int32ValueArray);
            DataValue int64ValueArray = new DataValue(new Variant(new Int64Collection() { -10000, -20000, 30000 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int64Array", namespaceIndexAllTypes), Attributes.Value, int64ValueArray);
            DataValue sByteValueArray = new DataValue(new Variant(new SByteCollection() { 1, -2, -3 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("SByteArray", namespaceIndexAllTypes), Attributes.Value, sByteValueArray);
            DataValue uInt16ValueArray = new DataValue(new Variant(new UInt16Collection() { 110, 120, 130 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt16Array", namespaceIndexAllTypes), Attributes.Value, uInt16ValueArray);
            DataValue uInt32ValueArray = new DataValue(new Variant(new UInt32Collection() { 1100, 1200, 1300 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt32Array", namespaceIndexAllTypes), Attributes.Value, uInt32ValueArray);
            DataValue uInt64ValueArray = new DataValue(new Variant(new UInt64Collection() { 11100, 11200, 11300 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt64Array", namespaceIndexAllTypes), Attributes.Value, uInt64ValueArray);
            DataValue floatValueArray = new DataValue(new Variant(new FloatCollection() { 1100, 5, 1200, 5, 1300, 5 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("FloatArray", namespaceIndexAllTypes), Attributes.Value, floatValueArray);
            DataValue doubleValueArray = new DataValue(new Variant(new DoubleCollection() { 11000.5, 12000.6, 13000.7 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DoubleArray", namespaceIndexAllTypes), Attributes.Value, doubleValueArray);
            DataValue stringValueArray = new DataValue(new Variant(new StringCollection() { "1a", "2b", "3c" }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("StringArray", namespaceIndexAllTypes), Attributes.Value, stringValueArray);
            DataValue dateTimeValArray = new DataValue(new Variant(new DateTimeCollection() { new DateTime(2020, 3, 11).ToUniversalTime(), new DateTime(2021, 2, 17).ToUniversalTime() }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DateTimeArray", namespaceIndexAllTypes), Attributes.Value, dateTimeValArray);
            DataValue guidValueArray = new DataValue(new Variant(new UuidCollection() { new Uuid(new Guid()), new Uuid(new Guid()) }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("GuidArray", namespaceIndexAllTypes), Attributes.Value, guidValueArray);
            DataValue byteStringValueArray = new DataValue(new Variant(new ByteStringCollection() { new byte[] { 1, 2, 3 }, new byte[] { 5, 6, 7 } }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ByteStringArray", namespaceIndexAllTypes), Attributes.Value, byteStringValueArray);

            XmlElement xmlElement1 = document.CreateElement("test1");
            xmlElement1.InnerText = "Text_2";

            XmlElement xmlElement2 = document.CreateElement("test2");
            xmlElement2.InnerText = "Text_2";
            DataValue xmlElementValueArray = new DataValue(new Variant(new XmlElementCollection() { xmlElement1, xmlElement2 }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("XmlElementArray", namespaceIndexAllTypes), Attributes.Value, xmlElementValueArray);
            DataValue nodeIdValueArray = new DataValue(new Variant(new NodeIdCollection() { new NodeId(30, 1), new NodeId(20, 3) }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("NodeIdArray", namespaceIndexAllTypes), Attributes.Value, nodeIdValueArray);
            DataValue expandedNodeIdArray = new DataValue(new Variant(new ExpandedNodeIdCollection() { new ExpandedNodeId(50, 1), new ExpandedNodeId(70, 9) }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ExpandedNodeIdArray", namespaceIndexAllTypes), Attributes.Value, expandedNodeIdArray);
            DataValue statusCodeArray = new DataValue(new Variant(new StatusCodeCollection() { StatusCodes.Good, StatusCodes.Bad, StatusCodes.Uncertain }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("StatusCodeArray", namespaceIndexAllTypes), Attributes.Value, statusCodeArray);
            DataValue qualifiedValueArray = new DataValue(new Variant(new QualifiedNameCollection() { new QualifiedName("123"), new QualifiedName("abc") }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("QualifiedNameArray", namespaceIndexAllTypes), Attributes.Value, qualifiedValueArray);
            DataValue localizedTextValueArray = new DataValue(new Variant(new LocalizedTextCollection() { new LocalizedText("1234"), new LocalizedText("abcd") }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("LocalizedTextArray", namespaceIndexAllTypes), Attributes.Value, localizedTextValueArray);
            DataValue dataValueArray = new DataValue(new Variant(new DataValueCollection() { new DataValue(new Variant("DataValue_info1"), StatusCodes.BadBoundNotFound), new DataValue(new Variant("DataValue_info2"), StatusCodes.BadNoData) }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DataValueArray", namespaceIndexAllTypes), Attributes.Value, dataValueArray);
            DataValue diagnosticInfoValueArray = new DataValue(new Variant(new DiagnosticInfoCollection() { new DiagnosticInfo(1, 1, 1, 1, "Diagnostic_info1"), new DiagnosticInfo(2, 2, 2, 2, "Diagnostic_info2") }));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DiagnosticInfoArray", namespaceIndexAllTypes), Attributes.Value, diagnosticInfoValueArray);

            // DataSet 'AllTypes' fill with matrix data
            DataValue boolToggleMatrix = new DataValue(new Variant(new Matrix(new bool[] { true, false, true, false, true, false, true, false,
                                                                                           true, false, true, false, true, false, true, false,
                                                                                           true, false, true, false, true, false, true, false},
                                                                                           BuiltInType.Boolean, 2, 3, 4)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggleMatrix", namespaceIndexAllTypes), Attributes.Value, boolToggleMatrix);
            DataValue byteValueMatrix = new DataValue(new Variant(new Matrix(new byte[] { 127, 128, 101, 102 }, BuiltInType.Byte, 2, 2, 1)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ByteMatrix", namespaceIndexAllTypes), Attributes.Value, byteValueMatrix);
            DataValue int16ValueMatrix = new DataValue(new Variant(new Matrix(new Int16[] { -100, -101, -200, -201, -100, -101, -200, -201 }, BuiltInType.Int16, 2, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int16Matrix", namespaceIndexAllTypes), Attributes.Value, int16ValueMatrix);
            DataValue int32ValueMatrix = new DataValue(new Variant(new Matrix(new Int32[] { -1000, -1001, -2000, -2001 }, BuiltInType.Int32, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int32Matrix", namespaceIndexAllTypes), Attributes.Value, int32ValueMatrix);
            DataValue int64ValueMatrix = new DataValue(new Variant(new Matrix(new Int64[] { -10000, -10001, -20000, -20001 }, BuiltInType.Int64, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("Int64Matrix", namespaceIndexAllTypes), Attributes.Value, int64ValueMatrix);
            DataValue sByteValueMatrix = new DataValue(new Variant(new Matrix(new SByte[] { 1, 2, -2, -3 }, BuiltInType.SByte, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("SByteMatrix", namespaceIndexAllTypes), Attributes.Value, sByteValueMatrix);
            DataValue uInt16ValueMatrix = new DataValue(new Variant(new Matrix(new UInt16[] { 110, 120, 130, 140 }, BuiltInType.UInt16, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt16Matrix", namespaceIndexAllTypes), Attributes.Value, uInt16ValueMatrix);
            DataValue uInt32ValueMatrix = new DataValue(new Variant(new Matrix(new UInt32[] { 1100, 1200, 1300, 1400 }, BuiltInType.UInt32, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt32Matrix", namespaceIndexAllTypes), Attributes.Value, uInt32ValueMatrix);
            DataValue uInt64ValueMatrix = new DataValue(new Variant(new Matrix(new UInt64[] { 11100, 11200, 11300, 11400 }, BuiltInType.UInt64, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("UInt64Matrix", namespaceIndexAllTypes), Attributes.Value, uInt64ValueMatrix);
            DataValue floatValueMatrix = new DataValue(new Variant(new Matrix(new float[] { 1100, 5, 1200, 7 }, BuiltInType.Float, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("FloatMatrix", namespaceIndexAllTypes), Attributes.Value, floatValueMatrix);
            DataValue doubleValueMatrix = new DataValue(new Variant(new Matrix(new Double[] { 11000.5, 12000.6, 13000.7, 14000.8 }, BuiltInType.Double, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DoubleMatrix", namespaceIndexAllTypes), Attributes.Value, doubleValueMatrix);
            DataValue stringValueMatrix = new DataValue(new Variant(new Matrix(new String[] { "1a", "2b", "3c", "4d" }, BuiltInType.String, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("StringMatrix", namespaceIndexAllTypes), Attributes.Value, stringValueMatrix);
            DataValue dateTimeValMatrix = new DataValue(new Variant(new Matrix(new DateTime[]
            { new DateTime(2020, 3, 11).ToUniversalTime(), new DateTime(2021, 2, 17).ToUniversalTime(),
              new DateTime(2021, 5, 21).ToUniversalTime(), new DateTime(2020, 7, 23).ToUniversalTime() }, BuiltInType.DateTime, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DateTimeMatrix", namespaceIndexAllTypes), Attributes.Value, dateTimeValMatrix);
            DataValue guidValueMatrix = new DataValue(new Variant(new Matrix(new Uuid[]
                { new Uuid(new Guid()), new Uuid(new Guid()) , new Uuid(new Guid()), new Uuid(new Guid()) }, BuiltInType.Guid, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("GuidMatrix", namespaceIndexAllTypes), Attributes.Value, guidValueMatrix);
            DataValue byteStringValueMatrix = new DataValue(new Variant(new Matrix(new byte[][] { new byte[] { 1, 2 }, new byte[] { 11, 12 }, new byte[] { 21, 22 }, new byte[] { 31, 32 } }, BuiltInType.ByteString, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ByteStringMatrix", namespaceIndexAllTypes), Attributes.Value, byteStringValueMatrix);

            XmlElement xmlElement1m = document.CreateElement("test1m");
            xmlElement1m.InnerText = "Text_1m";

            XmlElement xmlElement2m = document.CreateElement("test2m");
            xmlElement2m.InnerText = "Text_2m";

            XmlElement xmlElement3m = document.CreateElement("test3m");
            xmlElement3m.InnerText = "Text_3m";

            XmlElement xmlElement4m = document.CreateElement("test4m");
            xmlElement4m.InnerText = "Text_4m";

            DataValue xmlElementValueMatrix = new DataValue(new Variant(new Matrix(new XmlElement[] { xmlElement1m, xmlElement2m, xmlElement3m, xmlElement4m }, BuiltInType.XmlElement, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("XmlElementMatrix", namespaceIndexAllTypes), Attributes.Value, xmlElementValueMatrix);
            DataValue nodeIdValueMatrix = new DataValue(new Variant(new Matrix(new NodeId[] { new NodeId(30, 1), new NodeId(20, 3), new NodeId(10, 3), new NodeId(50, 7) }, BuiltInType.NodeId, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("NodeIdMatrix", namespaceIndexAllTypes), Attributes.Value, nodeIdValueMatrix);
            DataValue expandedNodeIdMatrix = new DataValue(new Variant(new Matrix(new ExpandedNodeId[]
            { new ExpandedNodeId(50, 1), new ExpandedNodeId(70, 9), new ExpandedNodeId(30, 2), new ExpandedNodeId(80, 3) }, BuiltInType.ExpandedNodeId, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("ExpandedNodeIdMatrix", namespaceIndexAllTypes), Attributes.Value, expandedNodeIdMatrix);
            DataValue statusCodeMatrix = new DataValue(new Variant(new Matrix(new StatusCode[]
            { StatusCodes.Good, StatusCodes.Uncertain , StatusCodes.BadCertificateInvalid, StatusCodes.Uncertain }, BuiltInType.StatusCode, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("StatusCodeMatrix", namespaceIndexAllTypes), Attributes.Value, statusCodeMatrix);
            DataValue qualifiedValueMatrix = new DataValue(new Variant(new Matrix(new QualifiedName[]
              { new QualifiedName("123"), new QualifiedName("abc"), new QualifiedName("456"), new QualifiedName("xyz") }, BuiltInType.QualifiedName, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("QualifiedNameMatrix", namespaceIndexAllTypes), Attributes.Value, qualifiedValueMatrix);
            DataValue localizedTextValueMatrix = new DataValue(new Variant(new Matrix(new LocalizedText[]
            {new LocalizedText("1234"), new LocalizedText("abcd") ,new LocalizedText("5678"), new LocalizedText("efgh") }, BuiltInType.LocalizedText, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("LocalizedTextMatrix", namespaceIndexAllTypes), Attributes.Value, localizedTextValueMatrix);
            DataValue dataValueMatrix = new DataValue(new Variant(new Matrix(new DataValue[]
            { new DataValue(new Variant("DataValue_info1"), StatusCodes.BadBoundNotFound), new DataValue(new Variant("DataValue_info2"), StatusCodes.BadNoData),
              new DataValue(new Variant("DataValue_info3"), StatusCodes.BadCertificateInvalid), new DataValue(new Variant("DataValue_info4"), StatusCodes.GoodCallAgain) }, BuiltInType.DataValue, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DataValueMatrix", namespaceIndexAllTypes), Attributes.Value, dataValueMatrix);
            DataValue diagnosticInfoValueMatrix = new DataValue(new Variant(new Matrix(new DiagnosticInfo[]
            { new DiagnosticInfo(1, 1, 1, 1, "Diagnostic_info1"), new DiagnosticInfo(2, 2, 2, 2, "Diagnostic_info2"),
              new DiagnosticInfo(3, 3, 3, 3, "Diagnostic_info3"), new DiagnosticInfo(4, 4, 4, 4, "Diagnostic_info4") }, BuiltInType.DiagnosticInfo, 2, 2)));
            pubSubApplication.DataStore.WritePublishedDataItem(new NodeId("DiagnosticInfoMatrix", namespaceIndexAllTypes), Attributes.Value, diagnosticInfoValueMatrix);
            #endregion
        }
    }
}
