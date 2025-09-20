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
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Transport;

namespace Quickstarts.ConsoleReferencePublisher
{
    public static class Program
    {
        /// <summary>
        /// constant DateTime that represents the initial time when the metadata
        /// for the configuration was created
        /// </summary>
        private static readonly DateTime s_timeOfConfiguration = new(
            2021,
            6,
            1,
            0,
            0,
            0,
            DateTimeKind.Utc);

        public static void Main(string[] args)
        {
            Console.WriteLine("OPC UA Console Reference Publisher");

            // command line options
            bool showHelp = false;
            bool useMqttJson = true;
            bool useMqttUadp = false;
            bool useUdpUadp = false;
            string publisherUrl = null;

            var options = new Mono.Options.OptionSet
            {
                { "h|help", "Show usage information", v => showHelp = v != null },
                {
                    "m|mqtt_json",
                    "Use MQTT with Json encoding Profile. This is the default option.",
                    v => useMqttJson = v != null
                },
                {
                    "p|mqtt_uadp",
                    "Use MQTT with UADP encoding Profile.",
                    v => useMqttUadp = v != null },
                { "u|udp_uadp", "Use UDP with UADP encoding Profile", v => useUdpUadp = v != null },
                { "url|publisher_url=", "Publisher Url Address", v => publisherUrl = v }
            };

            try
            {
                List<string> extraArgs = options.Parse(args);
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
                var telemetry = new ConsoleTelemetry();
                // telemetry.AddFileOutput("%CommonApplicationData%\\OPC Foundation\\Logs\\Quickstarts.ConsoleReferencePublisher.log.txt");
                // Utils.SetTraceMask(Utils.TraceMasks.Error);
                // Utils.SetTraceOutput(Utils.TraceOutput.DebugAndFile);

                PubSubConfigurationDataType pubSubConfiguration = null;
                if (useUdpUadp)
                {
                    // set default UDP Publisher Url to local multi-cast if not sent in args.
                    if (string.IsNullOrEmpty(publisherUrl))
                    {
                        publisherUrl = "opc.udp://239.0.0.1:4840";
                    }

                    // Create configuration using UDP protocol and UADP Encoding
                    pubSubConfiguration = CreatePublisherConfiguration_UdpUadp(publisherUrl);
                    Console.WriteLine(
                        "The PubSub Connection was initialized using UDP & UADP Profile.");
                }
                else
                {
                    // set default MQTT Broker Url to localhost if not sent in args.
                    if (string.IsNullOrEmpty(publisherUrl))
                    {
                        publisherUrl = "mqtt://localhost:1883";
                    }

                    if (useMqttUadp)
                    {
                        // Create configuration using MQTT protocol and UADP Encoding
                        pubSubConfiguration = CreatePublisherConfiguration_MqttUadp(publisherUrl);
                        Console.WriteLine(
                            "The PubSub Connection was initialized using MQTT & UADP Profile.");
                    }
                    else
                    {
                        // Create configuration using MQTT protocol and JSON Encoding
                        pubSubConfiguration = CreatePublisherConfiguration_MqttJson(publisherUrl);
                        Console.WriteLine(
                            "The PubSub Connection was initialized using MQTT & JSON Profile.");
                    }
                }

                // Create the UA Publisher application using configuration file
                using (var uaPubSubApplication = UaPubSubApplication.Create(pubSubConfiguration, telemetry))
                {
                    // Start values simulator
                    var valuesSimulator = new PublishedValuesWrites(uaPubSubApplication, telemetry);
                    valuesSimulator.Start();

                    // Start the publisher
                    uaPubSubApplication.Start();

                    Console.WriteLine("Publisher Started. Press Ctrl-C to exit...");

                    var quitEvent = new ManualResetEvent(false);

                    Console.CancelKeyPress += (sender, eArgs) =>
                    {
                        quitEvent.Set();
                        eArgs.Cancel = true;
                    };

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

        /// <summary>
        /// Creates a PubSubConfiguration object for UDP & UADP programmatically.
        /// </summary>
        /// <returns></returns>
        private static PubSubConfigurationDataType CreatePublisherConfiguration_UdpUadp(
            string urlAddress)
        {
            // Define a PubSub connection with PublisherId 1
            var pubSubConnection1 = new PubSubConnectionDataType
            {
                Name = "Publisher Connection UDP UADP",
                Enabled = true,
                PublisherId = (ushort)1,
                TransportProfileUri = Profiles.PubSubUdpUadpTransport
            };
            var address = new NetworkAddressUrlDataType
            {
                // Specify the local Network interface name to be used
                // e.g. address.NetworkInterface = "Ethernet";
                // Leave empty to publish on all available local interfaces.
                NetworkInterface = string.Empty,
                Url = urlAddress
            };
            pubSubConnection1.Address = new ExtensionObject(address);

            // configure custom DiscoveryAddress for Discovery messages
            pubSubConnection1.TransportSettings = new ExtensionObject
            {
                Body = new DatagramConnectionTransportDataType
                {
                    DiscoveryAddress = new ExtensionObject
                    {
                        Body = new NetworkAddressUrlDataType { Url = "opc.udp://224.0.2.15:4840" }
                    }
                }
            };

            var writerGroup1 = new WriterGroupDataType
            {
                Name = "WriterGroup 1",
                Enabled = true,
                WriterGroupId = 1,
                PublishingInterval = 5000,
                KeepAliveTime = 5000,
                MaxNetworkMessageSize = 1500,
                HeaderLayoutUri = "UADP-Cyclic-Fixed"
            };
            var uadpMessageSettings = new UadpWriterGroupMessageDataType
            {
                DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                GroupVersion = 0,
                NetworkMessageContentMask = (uint)(
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.PayloadHeader // needed to be able to decode the DataSetWriterId
                    | UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.GroupVersion |
                    UadpNetworkMessageContentMask.NetworkMessageNumber |
                    UadpNetworkMessageContentMask.SequenceNumber
                )
            };

            writerGroup1.MessageSettings = new ExtensionObject(uadpMessageSettings);
            // initialize Datagram (UDP) Transport Settings
            writerGroup1.TransportSettings = new ExtensionObject(
                new DatagramWriterGroupTransportDataType());

            // Define DataSetWriter 'Simple'
            var dataSetWriter1 = new DataSetWriterDataType
            {
                Name = "Writer 1",
                DataSetWriterId = 1,
                Enabled = true,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData,
                DataSetName = "Simple",
                KeyFrameCount = 1
            };
            var uadpDataSetWriterMessage = new UadpDataSetWriterMessageDataType
            {
                NetworkMessageNumber = 1,
                DataSetMessageContentMask = (uint)(
                    UadpDataSetMessageContentMask.Status |
                    UadpDataSetMessageContentMask.SequenceNumber
                )
            };

            dataSetWriter1.MessageSettings = new ExtensionObject(uadpDataSetWriterMessage);
            writerGroup1.DataSetWriters.Add(dataSetWriter1);

            // Define DataSetWriter 'AllTypes'
            var dataSetWriter2 = new DataSetWriterDataType
            {
                Name = "Writer 2",
                DataSetWriterId = 2,
                Enabled = true,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData,
                DataSetName = "AllTypes",
                KeyFrameCount = 1
            };
            uadpDataSetWriterMessage = new UadpDataSetWriterMessageDataType
            {
                NetworkMessageNumber = 1,
                DataSetMessageContentMask = (uint)(
                    UadpDataSetMessageContentMask.Status |
                    UadpDataSetMessageContentMask.SequenceNumber
                )
            };

            dataSetWriter2.MessageSettings = new ExtensionObject(uadpDataSetWriterMessage);
            writerGroup1.DataSetWriters.Add(dataSetWriter2);

            pubSubConnection1.WriterGroups.Add(writerGroup1);

            //  Define PublishedDataSet Simple
            PublishedDataSetDataType publishedDataSetSimple = CreatePublishedDataSetSimple();

            // Define PublishedDataSet AllTypes
            PublishedDataSetDataType publishedDataSetAllTypes = CreatePublishedDataSetAllTypes();

            //create  the PubSub configuration root object
            return new PubSubConfigurationDataType
            {
                Connections = [pubSubConnection1],
                PublishedDataSets = [publishedDataSetSimple, publishedDataSetAllTypes]
            };
        }

        /// <summary>
        /// Creates a PubSubConfiguration object for MQTT & Json programmatically.
        /// </summary>
        /// <returns></returns>
        private static PubSubConfigurationDataType CreatePublisherConfiguration_MqttJson(
            string urlAddress)
        {
            // Define a PubSub connection with PublisherId 2
            var pubSubConnection1 = new PubSubConnectionDataType
            {
                Name = "Publisher Connection MQTT Json",
                Enabled = true,
                PublisherId = (ushort)2,
                TransportProfileUri = Profiles.PubSubMqttJsonTransport
            };
            var address = new NetworkAddressUrlDataType
            {
                // Specify the local Network interface name to be used
                // e.g. address.NetworkInterface = "Ethernet";
                // Leave empty to publish on all available local interfaces.
                NetworkInterface = string.Empty,
                Url = urlAddress
            };
            pubSubConnection1.Address = new ExtensionObject(address);

            // Configure the mqtt specific configuration with the MQTT broker
            var mqttConfiguration = new MqttClientProtocolConfiguration(
                version: EnumMqttProtocolVersion.V500);
            pubSubConnection1.ConnectionProperties = mqttConfiguration.ConnectionProperties;

            const string brokerQueueName = "Json_WriterGroup_1";
            const string brokerMetaData = "$Metadata";

            var writerGroup1 = new WriterGroupDataType
            {
                Name = "WriterGroup 1",
                Enabled = true,
                WriterGroupId = 1,
                PublishingInterval = 5000,
                KeepAliveTime = 5000,
                MaxNetworkMessageSize = 1500
            };

            var jsonMessageSettings = new JsonWriterGroupMessageDataType
            {
                NetworkMessageContentMask = (uint)(
                    JsonNetworkMessageContentMask.NetworkMessageHeader |
                    JsonNetworkMessageContentMask.DataSetMessageHeader |
                    JsonNetworkMessageContentMask.PublisherId |
                    JsonNetworkMessageContentMask.DataSetClassId |
                    JsonNetworkMessageContentMask.ReplyTo
                )
            };

            writerGroup1.MessageSettings = new ExtensionObject(jsonMessageSettings);
            writerGroup1.TransportSettings = new ExtensionObject(
                new BrokerWriterGroupTransportDataType { QueueName = brokerQueueName }
            );

            // Define DataSetWriter 'Simple' Variant encoding
            var dataSetWriter1 = new DataSetWriterDataType
            {
                Name = "Writer Variant Encoding",
                DataSetWriterId = 1,
                Enabled = true,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.None, // Variant encoding;
                DataSetName = "Simple",
                KeyFrameCount = 3
            };

            var jsonDataSetWriterMessage = new JsonDataSetWriterMessageDataType
            {
                DataSetMessageContentMask = (uint)(
                    JsonDataSetMessageContentMask.DataSetWriterId |
                    JsonDataSetMessageContentMask.MetaDataVersion |
                    JsonDataSetMessageContentMask.SequenceNumber |
                    JsonDataSetMessageContentMask.Status |
                    JsonDataSetMessageContentMask.Timestamp
                )
            };
            dataSetWriter1.MessageSettings = new ExtensionObject(jsonDataSetWriterMessage);

            var jsonDataSetWriterTransport = new BrokerDataSetWriterTransportDataType
            {
                QueueName = brokerQueueName,
                RequestedDeliveryGuarantee = BrokerTransportQualityOfService.BestEffort,
                MetaDataQueueName = $"{brokerQueueName}/{brokerMetaData}",
                MetaDataUpdateTime = 0
            };
            dataSetWriter1.TransportSettings = new ExtensionObject(jsonDataSetWriterTransport);

            writerGroup1.DataSetWriters.Add(dataSetWriter1);

            // Define DataSetWriter 'Simple' - Variant encoding
            var dataSetWriter2 = new DataSetWriterDataType
            {
                Name = "Writer RawData Encoding",
                DataSetWriterId = 2,
                Enabled = true,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData,
                DataSetName = "AllTypes",
                KeyFrameCount = 1
            };

            jsonDataSetWriterMessage = new JsonDataSetWriterMessageDataType
            {
                DataSetMessageContentMask = (uint)(
                    JsonDataSetMessageContentMask.DataSetWriterId |
                    JsonDataSetMessageContentMask.MetaDataVersion |
                    JsonDataSetMessageContentMask.SequenceNumber |
                    JsonDataSetMessageContentMask.Status |
                    JsonDataSetMessageContentMask.Timestamp
                )
            };
            dataSetWriter2.MessageSettings = new ExtensionObject(jsonDataSetWriterMessage);

            jsonDataSetWriterTransport = new BrokerDataSetWriterTransportDataType
            {
                QueueName = brokerQueueName,
                RequestedDeliveryGuarantee = BrokerTransportQualityOfService.BestEffort,
                MetaDataQueueName = $"{brokerQueueName}/{brokerMetaData}",
                MetaDataUpdateTime = 0
            };
            dataSetWriter2.TransportSettings = new ExtensionObject(jsonDataSetWriterTransport);

            writerGroup1.DataSetWriters.Add(dataSetWriter2);

            pubSubConnection1.WriterGroups.Add(writerGroup1);

            // Define PublishedDataSet Simple
            PublishedDataSetDataType publishedDataSetSimple = CreatePublishedDataSetSimple();

            // Define PublishedDataSet AllTypes
            PublishedDataSetDataType publishedDataSetAllTypes = CreatePublishedDataSetAllTypes();

            //create  the PubSub configuration root object
            return new PubSubConfigurationDataType
            {
                Connections = [pubSubConnection1],
                PublishedDataSets = [publishedDataSetSimple, publishedDataSetAllTypes]
            };
        }

        /// <summary>
        /// Creates a PubSubConfiguration object for MQTT & UADP programmatically.
        /// </summary>
        /// <returns></returns>
        private static PubSubConfigurationDataType CreatePublisherConfiguration_MqttUadp(
            string urlAddress)
        {
            // Define a PubSub connection with PublisherId 3
            var pubSubConnection1 = new PubSubConnectionDataType
            {
                Name = "Publisher Connection MQTT UADP",
                Enabled = true,
                PublisherId = (ushort)3,
                TransportProfileUri = Profiles.PubSubMqttUadpTransport
            };
            var address = new NetworkAddressUrlDataType
            {
                // Specify the local Network interface name to be used
                // e.g. address.NetworkInterface = "Ethernet";
                // Leave empty to publish on all available local interfaces.
                NetworkInterface = string.Empty,
                Url = urlAddress
            };
            pubSubConnection1.Address = new ExtensionObject(address);

            // Configure the mqtt specific configuration with the MQTTbroker
            var mqttConfiguration = new MqttClientProtocolConfiguration(
                version: EnumMqttProtocolVersion.V500);
            pubSubConnection1.ConnectionProperties = mqttConfiguration.ConnectionProperties;

            const string brokerQueueName = "Uadp_WriterGroup_1";
            const string brokerMetaData = "$Metadata";

            var writerGroup1 = new WriterGroupDataType
            {
                Name = "WriterGroup 1",
                Enabled = true,
                WriterGroupId = 1,
                PublishingInterval = 5000,
                KeepAliveTime = 5000,
                MaxNetworkMessageSize = 1500,
                HeaderLayoutUri = "UADP-Cyclic-Fixed"
            };
            var uadpMessageSettings = new UadpWriterGroupMessageDataType
            {
                DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                GroupVersion = 0,
                NetworkMessageContentMask = (uint)(
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader |
                    UadpNetworkMessageContentMask.GroupVersion |
                    UadpNetworkMessageContentMask.NetworkMessageNumber |
                    UadpNetworkMessageContentMask.SequenceNumber
                )
            };

            writerGroup1.MessageSettings = new ExtensionObject(uadpMessageSettings);
            // initialize Broker transport settings
            writerGroup1.TransportSettings = new ExtensionObject(
                new BrokerWriterGroupTransportDataType { QueueName = brokerQueueName }
            );

            // Define DataSetWriter 'Simple'
            var dataSetWriter1 = new DataSetWriterDataType
            {
                Name = "Writer 1",
                DataSetWriterId = 1,
                Enabled = true,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData,
                DataSetName = "Simple",
                KeyFrameCount = 1
            };
            var uadpDataSetWriterMessage = new UadpDataSetWriterMessageDataType
            {
                ConfiguredSize = 32,
                DataSetOffset = 15,
                NetworkMessageNumber = 1,
                DataSetMessageContentMask = (uint)(
                    UadpDataSetMessageContentMask.Status |
                    UadpDataSetMessageContentMask.SequenceNumber
                )
            };

            dataSetWriter1.MessageSettings = new ExtensionObject(uadpDataSetWriterMessage);
            var uadpDataSetWriterTransport = new BrokerDataSetWriterTransportDataType
            {
                QueueName = brokerQueueName,
                MetaDataQueueName = $"{brokerQueueName}/{brokerMetaData}",
                MetaDataUpdateTime = 60000
            };
            dataSetWriter1.TransportSettings = new ExtensionObject(uadpDataSetWriterTransport);

            writerGroup1.DataSetWriters.Add(dataSetWriter1);

            // Define DataSetWriter 'AllTypes'
            var dataSetWriter2 = new DataSetWriterDataType
            {
                Name = "Writer 2",
                DataSetWriterId = 2,
                Enabled = true,
                DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData,
                DataSetName = "AllTypes",
                KeyFrameCount = 1
            };
            uadpDataSetWriterMessage = new UadpDataSetWriterMessageDataType
            {
                ConfiguredSize = 32,
                DataSetOffset = 47,
                NetworkMessageNumber = 1,
                DataSetMessageContentMask = (uint)(
                    UadpDataSetMessageContentMask.Status |
                    UadpDataSetMessageContentMask.SequenceNumber
                )
            };

            dataSetWriter2.MessageSettings = new ExtensionObject(uadpDataSetWriterMessage);

            dataSetWriter2.TransportSettings = new ExtensionObject(uadpDataSetWriterTransport);
            writerGroup1.DataSetWriters.Add(dataSetWriter2);

            pubSubConnection1.WriterGroups.Add(writerGroup1);

            //  Define PublishedDataSet Simple
            PublishedDataSetDataType publishedDataSetSimple = CreatePublishedDataSetSimple();

            // Define PublishedDataSet AllTypes
            PublishedDataSetDataType publishedDataSetAllTypes = CreatePublishedDataSetAllTypes();

            //create  the PubSub configuration root object
            return new PubSubConfigurationDataType
            {
                Connections = [pubSubConnection1],
                PublishedDataSets = [publishedDataSetSimple, publishedDataSetAllTypes]
            };
        }

        /// <summary>
        /// Creates the "Simple" DataSet
        /// </summary>
        /// <returns></returns>
        private static PublishedDataSetDataType CreatePublishedDataSetSimple()
        {
            var publishedDataSetSimple = new PublishedDataSetDataType
            {
                Name = "Simple" //name shall be unique in a configuration
            };
            // Define  publishedDataSetSimple.DataSetMetaData
            publishedDataSetSimple.DataSetMetaData = new DataSetMetaDataType
            {
                DataSetClassId = Uuid.Empty,
                Name = publishedDataSetSimple.Name,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32Fast",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "DateTime",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    }
                ],

                // set the ConfigurationVersion relative to kTimeOfConfiguration constant
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MinorVersion = ConfigurationVersionUtils.CalculateVersionTime(
                        s_timeOfConfiguration),
                    MajorVersion = ConfigurationVersionUtils.CalculateVersionTime(
                        s_timeOfConfiguration)
                }
            };

            var publishedDataSetSimpleSource = new PublishedDataItemsDataType
            {
                PublishedData = []
            };
            //create PublishedData based on metadata names
            foreach (FieldMetaData field in publishedDataSetSimple.DataSetMetaData.Fields)
            {
                publishedDataSetSimpleSource.PublishedData.Add(
                    new PublishedVariableDataType
                    {
                        PublishedVariable = new NodeId(
                            field.Name,
                            PublishedValuesWrites.NamespaceIndexSimple),
                        AttributeId = Attributes.Value
                    }
                );
            }

            publishedDataSetSimple.DataSetSource
                = new ExtensionObject(publishedDataSetSimpleSource);

            return publishedDataSetSimple;
        }

        /// <summary>
        /// Creates the "AllTypes" DataSet
        /// </summary>
        /// <returns></returns>
        private static PublishedDataSetDataType CreatePublishedDataSetAllTypes()
        {
            var publishedDataSetAllTypes = new PublishedDataSetDataType
            {
                Name = "AllTypes" //name shall be unique in a configuration
            };
            // Define  publishedDataSetAllTypes.DataSetMetaData
            publishedDataSetAllTypes.DataSetMetaData = new DataSetMetaDataType
            {
                DataSetClassId = Uuid.Empty,
                Name = publishedDataSetAllTypes.Name,
                Fields =
                [
                    new FieldMetaData
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Byte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Int16,
                        DataType = DataTypeIds.Int16,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "SByte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "UInt16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "UInt32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "UInt64",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.UInt64,
                        DataType = DataTypeIds.UInt64,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Float",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Double",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "String",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.String,
                        DataType = DataTypeIds.String,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "ByteString",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.ByteString,
                        DataType = DataTypeIds.ByteString,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "Guid",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.Guid,
                        DataType = DataTypeIds.Guid,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "DateTime",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData
                    {
                        Name = "UInt32Array",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte)DataTypes.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.OneDimension
                    }
                ],

                // set the ConfigurationVersion relative to kTimeOfConfiguration constant
                ConfigurationVersion = new ConfigurationVersionDataType
                {
                    MinorVersion = ConfigurationVersionUtils.CalculateVersionTime(
                        s_timeOfConfiguration),
                    MajorVersion = ConfigurationVersionUtils.CalculateVersionTime(
                        s_timeOfConfiguration)
                }
            };
            var publishedDataSetAllTypesSource = new PublishedDataItemsDataType();

            //create PublishedData based on metadata names
            foreach (FieldMetaData field in publishedDataSetAllTypes.DataSetMetaData.Fields)
            {
                publishedDataSetAllTypesSource.PublishedData.Add(
                    new PublishedVariableDataType
                    {
                        PublishedVariable = new NodeId(
                            field.Name,
                            PublishedValuesWrites.NamespaceIndexAllTypes),
                        AttributeId = Attributes.Value
                    }
                );
            }
            publishedDataSetAllTypes.DataSetSource
                = new ExtensionObject(publishedDataSetAllTypesSource);

            return publishedDataSetAllTypes;
        }
    }
}
