/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.PubSub.Mqtt;

namespace Opc.Ua.PubSub.Tests
{
    public class MessagesHelper
    {
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
            publishedDataSet.DataSetMetaData.DataSetClassId = Uuid.Empty;
            publishedDataSet.DataSetMetaData.Name = publishedDataSet.Name;
            publishedDataSet.DataSetMetaData.Fields = fieldMetaDatas;
            publishedDataSet.DataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType() {
                MinorVersion = 1,
                MajorVersion = 1
            };

            PublishedDataItemsDataType publishedDataSetSimpleSource = new PublishedDataItemsDataType();
            publishedDataSetSimpleSource.PublishedData = new PublishedVariableDataTypeCollection();
            //create PublishedData based on metadata names
            foreach (var field in publishedDataSet.DataSetMetaData.Fields)
            {
                publishedDataSetSimpleSource.PublishedData.Add(
                    new PublishedVariableDataType() {
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
            FieldMetaDataCollection fieldMetaDatas)
        {
            DataSetMetaDataType metaData = new DataSetMetaDataType();
            metaData.DataSetClassId = new Uuid(Guid.Empty);
            metaData.Name = dataSetName;
            metaData.Fields = fieldMetaDatas;
            metaData.ConfigurationVersion = new ConfigurationVersionDataType() {
                MinorVersion = 1,
                MajorVersion = 1
            };

            return metaData;
        }

      
        #endregion

        /// <summary>
        /// Get first connection
        /// </summary>
        /// <param name="pubSubConfiguration"></param>
        /// <returns></returns>
        public static PubSubConnectionDataType GetConnection(PubSubConfigurationDataType pubSubConfiguration, UInt16 publisherId)
        {
            if (pubSubConfiguration != null)
            {
                return pubSubConfiguration.Connections.Find(x => x.PublisherId == publisherId);
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
                return connection.WriterGroups.Find(x=>x.WriterGroupId == writerGroupId);
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
                return connection.ReaderGroups.Find(x=>x.Name == $"ReaderGroup { writerGroupId}");
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
            DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData)
        {

            // Define a PubSub connection with PublisherId 100
            PubSubConnectionDataType pubSubConnection1 = new PubSubConnectionDataType();
            pubSubConnection1.Name = "Connection1 Publisher PubId:" + publisherId;
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

            #region Define WriterGroup1            
            WriterGroupDataType writerGroup1 = new WriterGroupDataType();
            writerGroup1.Name = "WriterGroup id:" + writerGroupId;
            writerGroup1.Enabled = true;
            writerGroup1.WriterGroupId = writerGroupId;
            writerGroup1.PublishingInterval = 5000;
            writerGroup1.KeepAliveTime = 5000;
            writerGroup1.MaxNetworkMessageSize = 1500;

            WriterGroupMessageDataType messageSettings = null;
            switch (transportProfileUri)
            {
                case Profiles.PubSubMqttJsonTransport:
                    messageSettings = new JsonWriterGroupMessageDataType() {
                        NetworkMessageContentMask = (uint)networkMessageContentMask
                    };
                    break;
                case Profiles.PubSubMqttUadpTransport:
                    messageSettings = new UadpWriterGroupMessageDataType() {
                        DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                        GroupVersion = 0,
                        NetworkMessageContentMask = (uint)networkMessageContentMask
                    };
                    break;
            }
            

            writerGroup1.MessageSettings = new ExtensionObject(messageSettings);
            writerGroup1.TransportSettings = new ExtensionObject(
                new BrokerWriterGroupTransportDataType() {
                    QueueName = writerGroup1.Name,
                }
            );
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
                    case Profiles.PubSubMqttJsonTransport:
                        dataSetWriterMessage = new JsonDataSetWriterMessageDataType() {
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
                        break;
                    case Profiles.PubSubMqttUadpTransport:
                        dataSetWriterMessage = new UadpDataSetWriterMessageDataType() {
                            DataSetMessageContentMask = dataSetMessageContentMask
                        };
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
                        new PublishedVariableDataType() {
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
            DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData)
        {
            return CreatePublisherConfiguration(
                transportProfileUri, addressUrl,
                publisherId, writerGroupId,
                (UInt32)jsonNetworkMessageContentMask,
                (UInt32)jsonDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray, nameSpaceIndexForData);
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
            DataSetMetaDataType[] dataSetMetaDataArray, ushort nameSpaceIndexForData)
        {
            return CreatePublisherConfiguration(
                transportProfileUri, addressUrl,
                publisherId, writerGroupId,
                (UInt32)uadpNetworkMessageContentMask,
                (UInt32)uadpDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray, nameSpaceIndexForData);
        }

        /// <summary>
        /// Create a Publisher with the specified parameters
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

                BrokerDataSetReaderTransportDataType brokerTransportSettings = new BrokerDataSetReaderTransportDataType() {
                    QueueName = "WriterGroup id:" + writerGroupId,
                };

                dataSetReader.TransportSettings = new ExtensionObject(brokerTransportSettings);

                DataSetReaderMessageDataType dataSetReaderMessage = null;
                switch (transportProfileUri)
                {
                    case Profiles.PubSubMqttJsonTransport:
                        dataSetReaderMessage = new JsonDataSetReaderMessageDataType() {
                            NetworkMessageContentMask = (uint)networkMessageContentMask,
                            DataSetMessageContentMask = (uint)dataSetMessageContentMask,
                        };
                        break;
                    case Profiles.PubSubMqttUadpTransport:
                        dataSetReaderMessage = new UadpDataSetReaderMessageDataType() {
                            NetworkMessageContentMask = (uint)networkMessageContentMask,
                            DataSetMessageContentMask = (uint)dataSetMessageContentMask,
                        };
                        break;
                }

                dataSetReader.MessageSettings = new ExtensionObject(dataSetReaderMessage);

                TargetVariablesDataType subscribedDataSet = new TargetVariablesDataType();
                subscribedDataSet.TargetVariables = new FieldTargetDataTypeCollection();
                foreach (var fieldMetaData in dataSetMetaData.Fields)
                {
                    subscribedDataSet.TargetVariables.Add(new FieldTargetDataType() {
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
        /// Create a Publisher with the specified parameters for json
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
                transportProfileUri,addressUrl,
                publisherId, writerGroupId, setDataSetWriterId,
                (UInt32)jsonNetworkMessageContentMask,
                (UInt32)jsonDataSetMessageContentMask,
                dataSetFieldContentMask,
                dataSetMetaDataArray, nameSpaceIndexForData);
        }

        /// <summary>
        /// Create a Publisher with the specified parameters for uadp
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
        /// Create versuon 1 of datasetmetadata
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateDataSetMetaData1(string dataSetName)
        {
            // Define  DataSetMetaData
            DataSetMetaDataType dataSetMetaData = new DataSetMetaDataType();
            dataSetMetaData.DataSetClassId = Uuid.Empty;
            dataSetMetaData.Name = dataSetName;
            dataSetMetaData.Fields = new FieldMetaDataCollection()
                {
                    //new FieldMetaData()
                    //{
                    //    Name = "BoolToggle",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.Boolean,
                    //    DataType = DataTypeIds.Boolean,
                    //    ValueRank = ValueRanks.Scalar
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "Byte",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.Byte,
                    //    DataType = DataTypeIds.Byte,
                    //    ValueRank = ValueRanks.Scalar
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "SByte",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.SByte,
                    //    DataType = DataTypeIds.SByte,
                    //    ValueRank = ValueRanks.Scalar
                    //},
                    new FieldMetaData()
                    {
                        Name = "ByteMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "BoolToggleMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                };
            dataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType() {
                MinorVersion = 1,
                MajorVersion = 1
            };

            return dataSetMetaData;
        }

        /// <summary>
        /// Create version 2 of dataset metadata
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateDataSetMetaData2(string dataSetName)
        {
            // Define  DataSetMetaData
            DataSetMetaDataType dataSetMetaData = new DataSetMetaDataType();
            dataSetMetaData.DataSetClassId = Uuid.Empty;
            dataSetMetaData.Name = dataSetName;
            dataSetMetaData.Fields = new FieldMetaDataCollection()
                {
                    new FieldMetaData()
                    {
                        Name = "UInt16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt64",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.Scalar
                    }
                };
            dataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType() {
                MinorVersion = 1,
                MajorVersion = 1
            };

            return dataSetMetaData;
        }

        /// <summary>
        /// Create version 3 of datasetMetadata
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateDataSetMetaData3(string dataSetName)
        {
            // Define  DataSetMetaData
            DataSetMetaDataType dataSetMetaData = new DataSetMetaDataType();
            dataSetMetaData.DataSetClassId = Uuid.Empty;
            dataSetMetaData.Name = dataSetName;
            dataSetMetaData.Fields = new FieldMetaDataCollection()
                {
                    new FieldMetaData()
                    {
                        Name = "Int16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int64",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.Int64,
                        ValueRank = ValueRanks.Scalar
                    }
                };
            dataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType() {
                MinorVersion = 1,
                MajorVersion = 1
            };

            return dataSetMetaData;
        }
        /// <summary>
        /// Create Metadata for all types
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateDataSetMetaDataAllTypes(string dataSetName)
        {
            // Define  DataSetMetaData
            DataSetMetaDataType dataSetMetaData = new DataSetMetaDataType();
            dataSetMetaData.DataSetClassId = Uuid.Empty;
            dataSetMetaData.Name = dataSetName;
            dataSetMetaData.Fields = new FieldMetaDataCollection()
                {
                    new FieldMetaData()
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "SByte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Byte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int64",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt64",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Float",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Double",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "String",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "DateTime",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Guid",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Guid,
                        DataType = DataTypeIds.Guid,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "ByteString",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ByteString,
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "XmlElement",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.XmlElement,
                        DataType = DataTypeIds.XmlElement,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "NodeId",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "ExpandedNodeId",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                        DataType = DataTypeIds.ExpandedNodeId,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "StatusCode",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.StatusCode,
                        DataType = DataTypeIds.StatusCode,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "QualifiedName",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.QualifiedName,
                        DataType = DataTypeIds.QualifiedName,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "LocalizedText",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.LocalizedText,
                        DataType = DataTypeIds.LocalizedText,
                        ValueRank = ValueRanks.Scalar
                    },
                    //new FieldMetaData()
                    //{
                    //    Name = "Structure",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.ExtensionObject,
                    //    DataType = DataTypeIds.Structure,
                    //    ValueRank = ValueRanks.Scalar
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "DataValue",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.DataValue,
                    //    DataType = DataTypeIds.DataValue,
                    //    ValueRank = ValueRanks.Scalar
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "Variant",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.Variant,
                    //    DataType = DataTypeIds.DataValue,
                    //    ValueRank = ValueRanks.Scalar
                    //},
                    new FieldMetaData()
                    {
                        Name = "DiagnosticInfo",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                        DataType = DataTypeIds.DiagnosticInfo,
                        ValueRank = ValueRanks.Scalar
                    },
                    // Number,Integer,UInteger, Enumeration internal use
                    // Array type
                    new FieldMetaData()
                    {
                        Name = "BoolToggleArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "SByteArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "ByteArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "Int16Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt16Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt32Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "Int64Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt64Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "FloatArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "DoubleArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "StringArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "DateTimeArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "GuidArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Guid,
                        DataType = DataTypeIds.Guid,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "ByteStringArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ByteString,
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "XmlElementArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.XmlElement,
                        DataType = DataTypeIds.XmlElement,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "NodeIdArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "ExpandedNodeIdArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                        DataType = DataTypeIds.ExpandedNodeId,
                        ValueRank = ValueRanks.OneDimension
                    },
                    //new FieldMetaData()
                    //{
                    //    Name = "StatusCodeArray",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.StatusCode,
                    //    DataType = DataTypeIds.StatusCode,
                    //    ValueRank = ValueRanks.OneDimension
                    //},
                    new FieldMetaData()
                    {
                        Name = "QualifiedNameArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.QualifiedName,
                        DataType = DataTypeIds.QualifiedName,
                        ValueRank = ValueRanks.OneDimension
                    },
                    new FieldMetaData()
                    {
                        Name = "LocalizedTextArray",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.LocalizedText,
                        DataType = DataTypeIds.LocalizedText,
                        ValueRank = ValueRanks.OneDimension
                    },
                    //new FieldMetaData()
                    //{
                    //    Name = "StructureArray",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.ExtensionObject,
                    //    DataType = DataTypeIds.Structure,
                    //    ValueRank = ValueRanks.OneDimension
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "DataValueArray",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.DataValue,
                    //    DataType = DataTypeIds.DataValue,
                    //    ValueRank = ValueRanks.OneDimension
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "VariantArray",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.Variant,
                    //    DataType = DataTypeIds.DataValue,
                    //    ValueRank = ValueRanks.OneDimension
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "DiagnosticInfoArray",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                    //    DataType = DataTypeIds.DiagnosticInfo,
                    //    ValueRank = ValueRanks.OneDimension
                    //},
                    // Matrix type
                    new FieldMetaData()
                    {
                        Name = "BoolToggleMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "SByteMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "ByteMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "Int16Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt16Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt32Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)BuiltInType.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "Int64Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Int64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt64Matrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)BuiltInType.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "FloatMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "DoubleMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "StringMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "DateTimeMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "GuidMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.Guid,
                        DataType = DataTypeIds.Guid,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "ByteStringMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ByteString,
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "XmlElementMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.XmlElement,
                        DataType = DataTypeIds.XmlElement,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "NodeIdMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.NodeId,
                        DataType = DataTypeIds.NodeId,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "ExpandedNodeIdMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.ExpandedNodeId,
                        DataType = DataTypeIds.ExpandedNodeId,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    //new FieldMetaData()
                    //{
                    //    Name = "StatusCodeMatrix",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.StatusCode,
                    //    DataType = DataTypeIds.StatusCode,
                    //    ValueRank = ValueRanks.TwoDimensions
                    //},
                    new FieldMetaData()
                    {
                        Name = "QualifiedNameMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.QualifiedName,
                        DataType = DataTypeIds.QualifiedName,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    new FieldMetaData()
                    {
                        Name = "LocalizedTextMatrix",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)BuiltInType.LocalizedText,
                        DataType = DataTypeIds.LocalizedText,
                        ValueRank = ValueRanks.TwoDimensions
                    },
                    //new FieldMetaData()
                    //{
                    //    Name = "StructureMatrix",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.ExtensionObject,
                    //    DataType = DataTypeIds.Structure,
                    //    ValueRank = ValueRanks.TwoDimensions
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "DataValueMatrix",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.DataValue,
                    //    DataType = DataTypeIds.DataValue,
                    //    ValueRank = ValueRanks.TwoDimensions
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "VariantMatrix",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.Variant,
                    //    DataType = DataTypeIds.DataValue,
                    //    ValueRank = ValueRanks.TwoDimensions
                    //},
                    //new FieldMetaData()
                    //{
                    //    Name = "DiagnosticInfoMatrix",
                    //    DataSetFieldId = new Uuid(Guid.NewGuid()),
                    //    BuiltInType = (byte)BuiltInType.DiagnosticInfo,
                    //    DataType = DataTypeIds.DiagnosticInfo,
                    //    ValueRank = ValueRanks.TwoDimensions
                    //},
 
            };
            dataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType() {
                MinorVersion = 1,
                MajorVersion = 1
            };

            return dataSetMetaData;
        }
    }
}
