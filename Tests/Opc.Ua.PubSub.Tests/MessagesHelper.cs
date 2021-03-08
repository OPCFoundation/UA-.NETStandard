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

            return pubSubConnection;
        }

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

        /// <summary>
        /// Create published dataset simple
        /// </summary>Create Published dataset 'Simple' 
        /// <returns></returns>
        public static PublishedDataSetDataType CreatePublishedDataSetSimple(string dataSetName)
        {
            return CreatePublishedDataSet(dataSetName,
                2, // namespaceIndex
                new FieldMetaDataCollection()
                {
                    new FieldMetaData()
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32Fast",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "DateTime",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    }
                });
        }

        /// <summary>
        /// Create Published dataset 'AllTypes' 
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static PublishedDataSetDataType CreatePublishedDataSetAllTypes(string dataSetName)
        {
            return CreatePublishedDataSet(dataSetName,
               3, // namespaceIndex
               new FieldMetaDataCollection()
                {
                    new FieldMetaData()
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Byte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "SByte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)DataTypes.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Float",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Double",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar
                    }
               });
        }

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
        /// Create Published dataset
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

            //PublishedDataItemsDataType publishedDataSetSimpleSource = new PublishedDataItemsDataType();
            //publishedDataSetSimpleSource.PublishedData = new PublishedVariableDataTypeCollection();
            //create PublishedData based on metadata names
            //foreach (var field in publishedDataSet.DataSetMetaData.Fields)
            //{
            //    publishedDataSetSimpleSource.PublishedData.Add(
            //        new PublishedVariableDataType() {
            //            PublishedVariable = new NodeId(field.Name, namespaceIndex),
            //            AttributeId = Attributes.Value,
            //        });
            //}

            //publishedDataSet.DataSetSource = new ExtensionObject(publishedDataSetSimpleSource);

            //PublishedDataSetDataType publishedDataSet = new PublishedDataSetDataType();
            //publishedDataSet.Name = dataSetName; //name shall be unique in a configuration
            //// Define  publishedDataSetSimple.DataSetMetaData
            //publishedDataSet.DataSetMetaData = new DataSetMetaDataType();
            //publishedDataSet.DataSetMetaData.DataSetClassId = Uuid.Empty;
            //publishedDataSet.DataSetMetaData.Name = publishedDataSet.Name;
            //publishedDataSet.DataSetMetaData.Fields = fieldMetaDatas;
            //publishedDataSet.DataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType() {
            //    MinorVersion = 1,
            //    MajorVersion = 1
            //};

            //PublishedDataItemsDataType publishedDataSetSimpleSource = new PublishedDataItemsDataType();
            //publishedDataSetSimpleSource.PublishedData = new PublishedVariableDataTypeCollection();
            ////create PublishedData based on metadata names
            //foreach (var field in publishedDataSet.DataSetMetaData.Fields)
            //{
            //    publishedDataSetSimpleSource.PublishedData.Add(
            //        new PublishedVariableDataType() {
            //            PublishedVariable = new NodeId(field.Name, namespaceIndex),
            //            AttributeId = Attributes.Value,
            //        });
            //}

            //publishedDataSet.DataSetSource = new ExtensionObject(publishedDataSetSimpleSource);

            return metaData;
        }

        /// <summary>
        /// Create subscribe dataset 'Simple' 
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateSubscribeDataSetSimple(string dataSetName)
        {
            return CreateDataSetMetaData(dataSetName,
                2, // namespaceIndex
                new FieldMetaDataCollection()
                {
                    new FieldMetaData()
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32Fast",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "DateTime",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    }
                });
        }

        /// <summary>
        /// Create subscribe dataset 'AllTypes' 
        /// </summary>
        /// <param name="dataSetName"></param>
        /// <returns></returns>
        public static DataSetMetaDataType CreateSubscribeDataSetAllTypes(string dataSetName)
        {
            return CreateDataSetMetaData(dataSetName,
               3, // namespaceIndex
               new FieldMetaDataCollection()
                {
                    new FieldMetaData()
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Byte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "SByte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)DataTypes.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Float",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Double",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar
                    }
               });
        }
        
        /// <summary>
        /// Create a configuration with single a dataset message 
        /// </summary>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreatePublisherConfigurationWithSingleDataSetMessage(
            string uriScheme,
            UInt16 publisherId,
            UInt16 writerGroupId,
            string dataSetName,
            DataSetFieldContentMask dataSetFieldContentMask,
            WriterGroupMessageDataType writterGroupMessageSettings,
            WriterGroupTransportDataType writerGroupTransportSettings,
            DataSetWriterMessageDataType dataSetWriterMessageSettings)
        {
            PubSubConnectionDataType pubSubConnection = CreatePubSubConnection(uriScheme, publisherId);

            WriterGroupDataType writerGroup =
                CreateWriterGroup(
                    writerGroupId,
                    writterGroupMessageSettings,
                    writerGroupTransportSettings
                );

            DataSetWriterDataType dataSetWriter =
                CreateDataSetWriter(
                    writerGroupId,
                    dataSetName,
                    dataSetFieldContentMask, //DataSetFieldContentMask.RawData,
                    dataSetWriterMessageSettings
                    );

            writerGroup.DataSetWriters.Add(dataSetWriter);
            pubSubConnection.WriterGroups.Add(writerGroup);

            PublishedDataSetDataType publishedDataSetSimple = CreatePublishedDataSetSimple(dataSetName);

            //create PubSub configuration 
            PubSubConfigurationDataType pubSubConfiguration = new PubSubConfigurationDataType();
            pubSubConfiguration.Connections = new PubSubConnectionDataTypeCollection()
                {
                    pubSubConnection
                };
            pubSubConfiguration.PublishedDataSets = new PublishedDataSetDataTypeCollection()
                {
                    publishedDataSetSimple
                };

            return pubSubConfiguration;
        }

        /// <summary>
        /// Create a JSON publisher configuration with a single dataset message
        /// </summary>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreateJsonPublisherConfigurationWithRawDataSingleDataSetMessage(
            UInt16 publisherId,
            ushort writerGroupId)
        {
            //DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.RawData;

            JsonWriterGroupMessageDataType jsonWriterGroupMessageSettings = new JsonWriterGroupMessageDataType() {
                NetworkMessageContentMask = (uint)(JsonNetworkMessageContentMask.NetworkMessageHeader
                            | JsonNetworkMessageContentMask.DataSetMessageHeader
                            | JsonNetworkMessageContentMask.SingleDataSetMessage
                            | JsonNetworkMessageContentMask.PublisherId
                            | JsonNetworkMessageContentMask.DataSetClassId
                            | JsonNetworkMessageContentMask.ReplyTo)
            };

            BrokerWriterGroupTransportDataType brokerWriterGroupTransportSettings = new BrokerWriterGroupTransportDataType() {
                QueueName = $"Json_Publisher_{publisherId}_Group_{writerGroupId}",
                RequestedDeliveryGuarantee = BrokerTransportQualityOfService.AtLeastOnce
            };

            JsonDataSetWriterMessageDataType jsonDataSetWriterMessageSettings = new JsonDataSetWriterMessageDataType() {
                DataSetMessageContentMask = (uint)(JsonDataSetMessageContentMask.DataSetWriterId
                        | JsonDataSetMessageContentMask.MetaDataVersion
                        | JsonDataSetMessageContentMask.SequenceNumber
                        | JsonDataSetMessageContentMask.Status
                        | JsonDataSetMessageContentMask.Timestamp)
            };

            return CreatePublisherConfigurationWithSingleDataSetMessage(Utils.UriSchemeMqtt,
                publisherId, 
                writerGroupId,  
                "Simple", // DataSetName
                DataSetFieldContentMask.RawData,
                jsonWriterGroupMessageSettings,
                brokerWriterGroupTransportSettings,
                jsonDataSetWriterMessageSettings);
        }

        public static PubSubConfigurationDataType CreateSubscriberConfigurationWithSingleDataSetMessage(
           string uriScheme,
           UInt16 publisherId,
           UInt16 writerGroupId,
           string dataSetName,
           DataSetFieldContentMask dataSetFieldContentMask,
           ReaderGroupMessageDataType readerGroupMessageSettings,
           ReaderGroupTransportDataType readerGroupTransportSettings,
           DataSetReaderMessageDataType dataSetReaderMessageSettings,
           DataSetReaderTransportDataType dataSetReaderTransportSettings)
        {
            PubSubConnectionDataType pubSubConnection = CreatePubSubConnection(uriScheme, publisherId);

            ReaderGroupDataType readerGroup =
                CreateReaderGroup(
                    writerGroupId,
                    readerGroupMessageSettings,
                    readerGroupTransportSettings
                );

            DataSetMetaDataType dataSetMetaData = CreateSubscribeDataSetSimple(dataSetName);

            DataSetReaderDataType dataSetReader =
                CreateDataSetReader(
                    publisherId,
                    writerGroupId,
                    publisherId,
                    dataSetMetaData,
                    dataSetFieldContentMask, //DataSetFieldContentMask.RawData,
                    dataSetReaderMessageSettings,
                    dataSetReaderTransportSettings
                    );

            readerGroup.DataSetReaders.Add(dataSetReader);
            pubSubConnection.ReaderGroups.Add(readerGroup);

            //create PubSub configuration 
            PubSubConfigurationDataType pubSubConfiguration = new PubSubConfigurationDataType();
            pubSubConfiguration.Connections = new PubSubConnectionDataTypeCollection()
                {
                    pubSubConnection
                };

            return pubSubConfiguration;
        }

        /// <summary>
        /// Create a JSON subscriber configuration with a single dataset message
        /// </summary>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreateJsonSubscriberConfigurationWithRawDataSingleDataSetMessage(
            UInt16 publisherId,
            ushort writerGroupId)
        {
            //DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.RawData;

            JsonDataSetReaderMessageDataType jsonDataSetReaderMessageSettings = new JsonDataSetReaderMessageDataType() {
                NetworkMessageContentMask = (uint)(JsonNetworkMessageContentMask.NetworkMessageHeader
                    | JsonNetworkMessageContentMask.DataSetMessageHeader
                    | JsonNetworkMessageContentMask.SingleDataSetMessage
                    | JsonNetworkMessageContentMask.PublisherId
                    | JsonNetworkMessageContentMask.DataSetClassId
                    | JsonNetworkMessageContentMask.ReplyTo),
                DataSetMessageContentMask = (uint)(JsonDataSetMessageContentMask.DataSetWriterId
                    | JsonDataSetMessageContentMask.MetaDataVersion
                    | JsonDataSetMessageContentMask.SequenceNumber
                    | JsonDataSetMessageContentMask.Status
                    | JsonDataSetMessageContentMask.Timestamp),
            };

            BrokerDataSetReaderTransportDataType brokerDataSetReaderTransportSettings = new BrokerDataSetReaderTransportDataType() {
                QueueName = $"Json_Publisher_{publisherId}_Group_{writerGroupId}",
                RequestedDeliveryGuarantee = BrokerTransportQualityOfService.AtLeastOnce
            };

            return CreateSubscriberConfigurationWithSingleDataSetMessage(Utils.UriSchemeMqtt,
                publisherId, // PublisherId
                writerGroupId,  // ReaderGroupId
                "Simple", // DataSetName
                DataSetFieldContentMask.RawData,
                new ReaderGroupMessageDataType(),
                new ReaderGroupTransportDataType(),
                jsonDataSetReaderMessageSettings,
                brokerDataSetReaderTransportSettings);
        }

        /// <summary>
        /// Get first connection
        /// </summary>
        /// <param name="pubSubConfiguration"></param>
        /// <returns></returns>
        public static PubSubConnectionDataType GetFirstConnection(PubSubConfigurationDataType pubSubConfiguration)
        {
            if (pubSubConfiguration != null)
            {
                return pubSubConfiguration.Connections[0] as PubSubConnectionDataType;
            }
            return null;
        }

        /// <summary>
        /// Get first writer group
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        public static WriterGroupDataType GetFirstWriterGroup(PubSubConnectionDataType connection)
        {
            if (connection != null)
            {
                return connection.WriterGroups[0] as WriterGroupDataType;
            }
            return null;
        }

        /// <summary>
        /// Get first reader group
        /// </summary>
        /// <param name="connection"></param>
        /// <returns></returns>
        public static ReaderGroupDataType GetFirstReaderGroup(PubSubConnectionDataType connection)
        {
            if (connection != null)
            {
                return connection.ReaderGroups[0] as ReaderGroupDataType;
            }
            return null;
        }

    }
}
