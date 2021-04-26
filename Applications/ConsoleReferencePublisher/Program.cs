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
using System.Collections.Generic;
using System.Threading;
using Mono.Options;
using Opc.Ua;
using Opc.Ua.PubSub;
using Opc.Ua.PubSub.Transport;

namespace Quickstarts.ConsoleReferencePublisher
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("OPC UA Console Reference Publisher");

            // command line options
            bool showHelp = false;
            bool useMqttJson = true;
            bool useUdpUadp = false;
            string publisherUrl = null;

            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                    { "h|help", "Show usage information", v => showHelp = v != null },
                    { "m|mqtt_json", "Use MQTT with Json encoding Profile. This is the default option.", v => useMqttJson = v != null },
                    { "u|udp_uadp", "Use UDP with UADP encoding Profile.", v => useUdpUadp = v != null },
                    { "url=", "Publisher Url Address", v => publisherUrl = v},
                };

            IList<string> extraArgs = null;
            try
            {
                extraArgs = options.Parse(args);
                if (extraArgs.Count > 0)
                {
                    foreach (string extraArg in extraArgs)
                    {
                        Console.WriteLine("Error: Unknown option: {0}", extraArg);
                        showHelp = true;
                    }
                }
            }
            catch (OptionException e)
            {
                Console.WriteLine(e.Message);
                showHelp = true;
            }

            if (showHelp)
            {
                Console.WriteLine("Usage: dotnet ConsoleReferencePublisher.dll/exe [OPTIONS]");
                Console.WriteLine();

                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }
            try
            {
                InitializeLog();

                PubSubConfigurationDataType pubSubConfiguration = null;
                if (useUdpUadp)
                {
                    // set default UDP Publisher Url to local multicast if not sent in args.
                    if (string.IsNullOrEmpty(publisherUrl))
                    {
                        publisherUrl = "opc.udp://239.0.0.1:4840";
                    }

                    // Create configuration using UDP protocol and UADP Encoding
                    pubSubConfiguration = CreatePublisherConfiguration_UdpUadp(publisherUrl);
                    Console.WriteLine("The Pubsub Connection was initialized using UDP & UADP Profile.");
                }
                else
                {
                    // set default MQTT Broker Url to localhost if not sent in args.
                    if (string.IsNullOrEmpty(publisherUrl))
                    {
                        publisherUrl = "mqtt://localhost:1883";
                    }

                    // Create configuration using MQTT protocol and JSON Encoding
                    pubSubConfiguration = CreatePublisherConfiguration_MqttJson(publisherUrl);
                    Console.WriteLine("The Pubsub Connection was initialized using MQTT & JSON Profile.");
                }

                // Create the UA Publisher application using configuration file
                using (UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(pubSubConfiguration))
                {
                    // Start values simulator
                    PublishedValuesWrites valuesSimulator = new PublishedValuesWrites(uaPubSubApplication);
                    valuesSimulator.Start();

                    // Start the publisher
                    uaPubSubApplication.Start();

                    Console.WriteLine("Publisher Started. Press Ctrl-C to exit...");

                    ManualResetEvent quitEvent = new ManualResetEvent(false);
                    try
                    {
                        Console.CancelKeyPress += (sender, eArgs) => {
                            quitEvent.Set();
                            eArgs.Cancel = true;
                        };
                    }
                    catch
                    {
                    }

                    // wait for timeout or Ctrl-C
                    quitEvent.WaitOne();
                }

                Console.WriteLine("Program ended.");
                Console.WriteLine("Press any key to finish...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        #region Private Methods
        /// <summary>
        /// Creates a PubSubConfiguration object for UDP & UADP programmatically.
        /// </summary>
        /// <returns></returns>
        private static PubSubConfigurationDataType CreatePublisherConfiguration_UdpUadp(string urlAddress)
        {
            // Define a PubSub connection with PublisherId 1
            PubSubConnectionDataType pubSubConnection1 = new PubSubConnectionDataType();
            pubSubConnection1.Name = "Publisher Connection1 UDP UADP";
            pubSubConnection1.Enabled = true;
            pubSubConnection1.PublisherId = (UInt16)1;
            pubSubConnection1.TransportProfileUri = Profiles.PubSubUdpUadpTransport;
            NetworkAddressUrlDataType address = new NetworkAddressUrlDataType();
            // Specify the local Network interface name to be used
            // e.g. address.NetworkInterface = "Ethernet";
            // Leave empty to publish on all available local interfaces.
            address.NetworkInterface = String.Empty;
            address.Url = urlAddress;
            pubSubConnection1.Address = new ExtensionObject(address);

            #region Define WriterGroup1
            WriterGroupDataType writerGroup1 = new WriterGroupDataType();
            writerGroup1.Name = "WriterGroup 1";
            writerGroup1.Enabled = true;
            writerGroup1.WriterGroupId = 1;
            writerGroup1.PublishingInterval = 5000;
            writerGroup1.KeepAliveTime = 5000;
            writerGroup1.MaxNetworkMessageSize = 1500;
            writerGroup1.HeaderLayoutUri = "UADP-Cyclic-Fixed";
            UadpWriterGroupMessageDataType uadpMessageSettings = new UadpWriterGroupMessageDataType() {
                DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                GroupVersion = 0,
                NetworkMessageContentMask = (uint)(UadpNetworkMessageContentMask.PublisherId
                        | UadpNetworkMessageContentMask.GroupHeader
                        | UadpNetworkMessageContentMask.WriterGroupId
                        | UadpNetworkMessageContentMask.GroupVersion
                        | UadpNetworkMessageContentMask.NetworkMessageNumber
                        | UadpNetworkMessageContentMask.SequenceNumber)
            };

            writerGroup1.MessageSettings = new ExtensionObject(uadpMessageSettings);
            // initialize Datagram (UDP) Transport Settings
            writerGroup1.TransportSettings = new ExtensionObject(new DatagramWriterGroupTransportDataType());

            // Define DataSetWriter 'Simple'
            DataSetWriterDataType dataSetWriter1 = new DataSetWriterDataType();
            dataSetWriter1.Name = "Writer 1";
            dataSetWriter1.DataSetWriterId = 1;
            dataSetWriter1.Enabled = true;
            dataSetWriter1.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;
            dataSetWriter1.DataSetName = "Simple";
            dataSetWriter1.KeyFrameCount = 1;
            UadpDataSetWriterMessageDataType uadpDataSetWriterMessage = new UadpDataSetWriterMessageDataType() {
                ConfiguredSize = 32,
                DataSetOffset = 15,
                NetworkMessageNumber = 1,
                DataSetMessageContentMask = (uint)(UadpDataSetMessageContentMask.Status | UadpDataSetMessageContentMask.SequenceNumber),
            };

            dataSetWriter1.MessageSettings = new ExtensionObject(uadpDataSetWriterMessage);
            writerGroup1.DataSetWriters.Add(dataSetWriter1);

            // Define DataSetWriter 'AllTypes'
            DataSetWriterDataType dataSetWriter2 = new DataSetWriterDataType();
            dataSetWriter2.Name = "Writer 2";
            dataSetWriter2.DataSetWriterId = 2;
            dataSetWriter2.Enabled = true;
            dataSetWriter2.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;
            dataSetWriter2.DataSetName = "AllTypes";
            dataSetWriter2.KeyFrameCount = 1;
            uadpDataSetWriterMessage = new UadpDataSetWriterMessageDataType() {
                ConfiguredSize = 32,
                DataSetOffset = 47,
                NetworkMessageNumber = 1,
                DataSetMessageContentMask = (uint)(UadpDataSetMessageContentMask.Status | UadpDataSetMessageContentMask.SequenceNumber),
            };

            dataSetWriter2.MessageSettings = new ExtensionObject(uadpDataSetWriterMessage);
            writerGroup1.DataSetWriters.Add(dataSetWriter2);

            pubSubConnection1.WriterGroups.Add(writerGroup1);
            #endregion

            //  Define PublishedDataSet Simple
            PublishedDataSetDataType publishedDataSetSimple = CreatePublishedDataSetSimple();

            // Define PublishedDataSet AllTypes
            PublishedDataSetDataType publishedDataSetAllTypes = CreatePublishedDataSetAllTypes();

            //create  the PubSub configuration root object
            PubSubConfigurationDataType pubSubConfiguration = new PubSubConfigurationDataType();
            pubSubConfiguration.Connections = new PubSubConnectionDataTypeCollection()
                {
                    pubSubConnection1
                };
            pubSubConfiguration.PublishedDataSets = new PublishedDataSetDataTypeCollection()
                {
                    publishedDataSetSimple, publishedDataSetAllTypes
                };

            return pubSubConfiguration;
        }

        /// <summary>
        /// Creates a PubSubConfiguration object for MQTT & Json programmatically.
        /// </summary>
        /// <returns></returns>
        private static PubSubConfigurationDataType CreatePublisherConfiguration_MqttJson(string urlAddress)
        {
            // Define a PubSub connection with PublisherId 100
            PubSubConnectionDataType pubSubConnection1 = new PubSubConnectionDataType();
            pubSubConnection1.Name = "Publisher Connection3 MQTT Json";
            pubSubConnection1.Enabled = true;
            pubSubConnection1.PublisherId = (UInt16)2;
            pubSubConnection1.TransportProfileUri = Profiles.PubSubMqttJsonTransport;
            NetworkAddressUrlDataType address = new NetworkAddressUrlDataType();
            // Specify the local Network interface name to be used
            // e.g. address.NetworkInterface = "Ethernet";
            // Leave empty to publish on all available local interfaces.
            address.NetworkInterface = String.Empty;
            address.Url = urlAddress;
            pubSubConnection1.Address = new ExtensionObject(address);

            // Configure the mqtt specific configuration with the MQTTbroker
            ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V500);
            pubSubConnection1.ConnectionProperties = mqttConfiguration.ConnectionProperties;

            #region Define WriterGroup1 - Json
            WriterGroupDataType writerGroup1 = new WriterGroupDataType();
            writerGroup1.Name = "WriterGroup 1";
            writerGroup1.Enabled = true;
            writerGroup1.WriterGroupId = 1;
            writerGroup1.PublishingInterval = 5000;
            writerGroup1.KeepAliveTime = 5000;
            writerGroup1.MaxNetworkMessageSize = 1500;

            JsonWriterGroupMessageDataType jsonMessageSettings = new JsonWriterGroupMessageDataType() {
                NetworkMessageContentMask = (uint)(JsonNetworkMessageContentMask.NetworkMessageHeader
                       | JsonNetworkMessageContentMask.DataSetMessageHeader
                       | JsonNetworkMessageContentMask.PublisherId
                       | JsonNetworkMessageContentMask.DataSetClassId
                       | JsonNetworkMessageContentMask.ReplyTo)
            };

            writerGroup1.MessageSettings = new ExtensionObject(jsonMessageSettings);
            writerGroup1.TransportSettings = new ExtensionObject(new BrokerWriterGroupTransportDataType() {
                QueueName = "Json_WriterGroup_1",
            }
            );

            // Define DataSetWriter 'Simple' Variant encoding
            DataSetWriterDataType dataSetWriter1 = new DataSetWriterDataType();
            dataSetWriter1.Name = "Writer Variant Encoding";
            dataSetWriter1.DataSetWriterId = 1;
            dataSetWriter1.Enabled = true;
            dataSetWriter1.DataSetFieldContentMask = 0;// Variant encoding;
            dataSetWriter1.DataSetName = "Simple";
            dataSetWriter1.KeyFrameCount = 1;

            JsonDataSetWriterMessageDataType jsonDataSetWriterMessage = new JsonDataSetWriterMessageDataType() {
                DataSetMessageContentMask = (uint)(JsonDataSetMessageContentMask.DataSetWriterId
                | JsonDataSetMessageContentMask.MetaDataVersion
                | JsonDataSetMessageContentMask.SequenceNumber
                | JsonDataSetMessageContentMask.Status
                | JsonDataSetMessageContentMask.Timestamp),
            };

            dataSetWriter1.MessageSettings = new ExtensionObject(jsonDataSetWriterMessage);
            writerGroup1.DataSetWriters.Add(dataSetWriter1);

            // Define DataSetWriter 'Simple' - Variant encoding
            DataSetWriterDataType dataSetWriter2 = new DataSetWriterDataType();
            dataSetWriter2.Name = "Writer RawData Encoding";
            dataSetWriter2.DataSetWriterId = 2;
            dataSetWriter2.Enabled = true;
            dataSetWriter2.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;
            dataSetWriter2.DataSetName = "AllTypes";
            dataSetWriter2.KeyFrameCount = 1;

            jsonDataSetWriterMessage = new JsonDataSetWriterMessageDataType() {
                DataSetMessageContentMask = (uint)(JsonDataSetMessageContentMask.DataSetWriterId
                | JsonDataSetMessageContentMask.SequenceNumber
                | JsonDataSetMessageContentMask.Status
                | JsonDataSetMessageContentMask.Timestamp),
            };
            dataSetWriter2.MessageSettings = new ExtensionObject(jsonDataSetWriterMessage);
            writerGroup1.DataSetWriters.Add(dataSetWriter2);

            pubSubConnection1.WriterGroups.Add(writerGroup1);
            #endregion

            // Define PublishedDataSet Simple
            PublishedDataSetDataType publishedDataSetSimple = CreatePublishedDataSetSimple();

            // Define PublishedDataSet AllTypes
            PublishedDataSetDataType publishedDataSetAllTypes = CreatePublishedDataSetAllTypes();

            //create  the PubSub configuration root object
            PubSubConfigurationDataType pubSubConfiguration = new PubSubConfigurationDataType();
            pubSubConfiguration.Connections = new PubSubConnectionDataTypeCollection()
                {
                    pubSubConnection1
                };
            pubSubConfiguration.PublishedDataSets = new PublishedDataSetDataTypeCollection()
                {
                    publishedDataSetSimple, publishedDataSetAllTypes
                };

            return pubSubConfiguration;
        }

        /// <summary>
        /// Creates the "Simple" DataSet
        /// </summary>
        /// <returns></returns>
        private static PublishedDataSetDataType CreatePublishedDataSetSimple()
        {
            PublishedDataSetDataType publishedDataSetSimple = new PublishedDataSetDataType();
            publishedDataSetSimple.Name = "Simple"; //name shall be unique in a configuration
            // Define  publishedDataSetSimple.DataSetMetaData
            publishedDataSetSimple.DataSetMetaData = new DataSetMetaDataType();
            publishedDataSetSimple.DataSetMetaData.DataSetClassId = Uuid.Empty;
            publishedDataSetSimple.DataSetMetaData.Name = publishedDataSetSimple.Name;
            publishedDataSetSimple.DataSetMetaData.Fields = new FieldMetaDataCollection()
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
                    },
                };
            publishedDataSetSimple.DataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType() {
                MinorVersion = 1,
                MajorVersion = 1
            };

            PublishedDataItemsDataType publishedDataSetSimpleSource = new PublishedDataItemsDataType();
            publishedDataSetSimpleSource.PublishedData = new PublishedVariableDataTypeCollection();
            //create PublishedData based on metadata names
            foreach (var field in publishedDataSetSimple.DataSetMetaData.Fields)
            {
                publishedDataSetSimpleSource.PublishedData.Add(
                    new PublishedVariableDataType() {
                        PublishedVariable = new NodeId(field.Name, PublishedValuesWrites.NamespaceIndexSimple),
                        AttributeId = Attributes.Value,
                    });
            }

            publishedDataSetSimple.DataSetSource = new ExtensionObject(publishedDataSetSimpleSource);

            return publishedDataSetSimple;
        }

        /// <summary>
        /// Creates the "AllTypes" DataSet
        /// </summary>
        /// <returns></returns>
        private static PublishedDataSetDataType CreatePublishedDataSetAllTypes()
        {
            PublishedDataSetDataType publishedDataSetAllTypes = new PublishedDataSetDataType();
            publishedDataSetAllTypes.Name = "AllTypes"; //name shall be unique in a configuration
            // Define  publishedDataSetAllTypes.DataSetMetaData
            publishedDataSetAllTypes.DataSetMetaData = new DataSetMetaDataType();
            publishedDataSetAllTypes.DataSetMetaData.DataSetClassId = Uuid.Empty;
            publishedDataSetAllTypes.DataSetMetaData.Name = publishedDataSetAllTypes.Name;
            publishedDataSetAllTypes.DataSetMetaData.Fields = new FieldMetaDataCollection()
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
                        Name = "UInt64",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                         BuiltInType = (byte)DataTypes.UInt64,
                        DataType = DataTypeIds.UInt64,
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
                    },
                    new FieldMetaData()
                    {
                        Name = "String",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "ByteString",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.ByteString,
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Guid",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Guid,
                        DataType = DataTypeIds.Guid,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "DateTime",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt32Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.OneDimension
                    },
                };

            publishedDataSetAllTypes.DataSetMetaData.ConfigurationVersion = new ConfigurationVersionDataType() {
                MinorVersion = 1,
                MajorVersion = 1
            };
            PublishedDataItemsDataType publishedDataSetAllTypesSource = new PublishedDataItemsDataType();

            //create PublishedData based on metadata names
            foreach (var field in publishedDataSetAllTypes.DataSetMetaData.Fields)
            {
                publishedDataSetAllTypesSource.PublishedData.Add(
                    new PublishedVariableDataType() {
                        PublishedVariable = new NodeId(field.Name, PublishedValuesWrites.NamespaceIndexAllTypes),
                        AttributeId = Attributes.Value,
                    });
            }
            publishedDataSetAllTypes.DataSetSource = new ExtensionObject(publishedDataSetAllTypesSource);

            return publishedDataSetAllTypes;
        }

        /// <summary>
        /// Initialize logging
        /// </summary>
        private static void InitializeLog()
        {
            // Initialize logger
            Utils.SetTraceLog("%CommonApplicationData%\\OPC Foundation\\Logs\\Quickstarts.ConsoleReferencePublisher.log.txt", true);
            Utils.SetTraceMask(Utils.TraceMasks.Error);
            Utils.SetTraceOutput(Utils.TraceOutput.DebugAndFile);
        }
        #endregion
    }
}
