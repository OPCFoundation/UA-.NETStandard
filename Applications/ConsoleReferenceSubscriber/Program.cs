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
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.PubSub.Transport;

namespace Quickstarts.ConsoleReferenceSubscriber
{
    public static class Program
    {
        public const ushort NamespaceIndexSimple = 2;
        public const ushort NamespaceIndexAllTypes = 3;

        // constant DateTime that represents the initial time when the metadata for the configuration was created
        private static DateTime kTimeOfConfiguration = new DateTime(2021, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        private const string kDisplaySeparator = "------------------------------------------------";

        private static object m_lock = new object();

        public static void Main(string[] args)
        {
            Console.WriteLine("OPC UA Console Reference Subscriber");

            // command line options
            bool showHelp = false;
            bool useMqttJson = true;
            bool useMqttUadp = false;
            bool useUdpUadp = false;
            string subscriberUrl = null;

            Mono.Options.OptionSet options = new Mono.Options.OptionSet {
                    { "h|help", "Show usage information", v => showHelp = v != null },
                    { "m|mqtt_json", "Use MQTT with Json encoding Profile. This is the default option.", v => useMqttJson = v != null },
                    { "p|mqtt_uadp", "Use MQTT with UADP encoding Profile.", v => useMqttUadp = v != null },
                    { "u|udp_uadp", "Use UDP with UADP encoding Profile", v => useUdpUadp = v != null },
                    { "url|subscriber_url=", "Subscriber Url Address", v => subscriberUrl = v},
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
                Console.WriteLine("Usage: dotnet ConsoleReferenceSubscriber.dll [OPTIONS]");
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
                    // set default UDP Subscriber Url to local multicast if not sent in args.
                    if (string.IsNullOrEmpty(subscriberUrl))
                    {
                        subscriberUrl = "opc.udp://239.0.0.1:4840";
                    }

                    // Create configuration using UDP protocol and UADP Encoding
                    pubSubConfiguration = CreateSubscriberConfiguration_UdpUadp(subscriberUrl);
                    Console.WriteLine("The Pubsub Connection was initialized using UDP & UADP Profile.");
                }
                else
                {
                    // set default MQTT Broker Url to localhost if not sent in args.
                    if (string.IsNullOrEmpty(subscriberUrl))
                    {
                        subscriberUrl = "mqtt://localhost:1883";
                    }

                    if (useMqttUadp)
                    {
                        // Create configuration using MQTT protocol and UADP Encoding
                        pubSubConfiguration = CreateSubscriberConfiguration_MqttUadp(subscriberUrl);
                        Console.WriteLine("The PubSub Connection was initialized using MQTT & UADP Profile.");
                    }
                    else
                    {
                        // Create configuration using MQTT protocol and JSON Encoding
                        pubSubConfiguration = CreateSubscriberConfiguration_MqttJson(subscriberUrl);
                        Console.WriteLine("The PubSub Connection was initialized using MQTT & JSON Profile.");
                    }
                }

                // Create the UA Publisher application
                using (UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(pubSubConfiguration))
                {
                    // Subscribte to RawDataReceived event
                    uaPubSubApplication.RawDataReceived += UaPubSubApplication_RawDataReceived;

                    // Subscribte to DataReceived event
                    uaPubSubApplication.DataReceived += UaPubSubApplication_DataReceived;

                    // Subscribte to MetaDataReceived event
                    uaPubSubApplication.MetaDataReceived += UaPubSubApplication_MetaDataDataReceived;

                    uaPubSubApplication.ConfigurationUpdating += UaPubSubApplication_ConfigurationUpdating;

                    // Start the publisher
                    uaPubSubApplication.Start();

                    Console.WriteLine("Subscriber Started. Press Ctrl-C to exit...");

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
        /// Handler for <see cref="UaPubSubApplication.RawDataReceived" /> event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void UaPubSubApplication_RawDataReceived(object sender, RawDataReceivedEventArgs e)
        {
            lock (m_lock)
            {
                Console.WriteLine("RawDataReceived bytes:{0}, Source:{1}, TransportProtocol:{2}, MessageMapping:{3}",
                    e.Message.Length, e.Source, e.TransportProtocol, e.MessageMapping);

                Console.WriteLine(kDisplaySeparator);
            }
        }

        /// <summary>
        /// Handler for <see cref="UaPubSubApplication.DataReceived" /> event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void UaPubSubApplication_DataReceived(object sender, SubscribedDataEventArgs e)
        {
            lock (m_lock)
            {
                Console.WriteLine("DataReceived event:");

                if (e.NetworkMessage is UadpNetworkMessage)
                {
                    Console.WriteLine("UADP Network DataSetMessage ({0} DataSets): Source={1}, SequenceNumber={2}",
                            e.NetworkMessage.DataSetMessages.Count, e.Source, ((UadpNetworkMessage)e.NetworkMessage).SequenceNumber);
                }
                else if (e.NetworkMessage is JsonNetworkMessage)
                {
                    Console.WriteLine("JSON Network DataSetMessage ({0} DataSets): Source={1}, MessageId={2}",
                            e.NetworkMessage.DataSetMessages.Count, e.Source, ((JsonNetworkMessage)e.NetworkMessage).MessageId);
                }

                foreach (UaDataSetMessage dataSetMessage in e.NetworkMessage.DataSetMessages)
                {
                    DataSet dataSet = dataSetMessage.DataSet;
                    Console.WriteLine("\tDataSet.Name={0}, DataSetWriterId={1}", dataSet.Name, dataSet.DataSetWriterId);

                    for (int i = 0; i < dataSet.Fields.Length; i++)
                    {
                        Console.WriteLine("\t\tTargetNodeId:{0}, Attribute:{1}, Value:{2}",
                            dataSet.Fields[i].TargetNodeId, dataSet.Fields[i].TargetAttribute, dataSetMessage.DataSet.Fields[i].Value);
                    }
                }
                Console.WriteLine(kDisplaySeparator);
            }
        }

        /// <summary>
        /// Handler for <see cref="UaPubSubApplication.MetaDataDataReceived" /> event.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void UaPubSubApplication_MetaDataDataReceived(object sender, SubscribedDataEventArgs e)
        {
            lock (m_lock)
            {
                Console.WriteLine("MetaDataDataReceived event:");
                if (e.NetworkMessage is JsonNetworkMessage)
                {
                    Console.WriteLine("JSON Network MetaData Message: Source={0}, PublisherId={1}, DataSetWriterId={2} Fields count={3}\n",
                         e.Source,
                         ((JsonNetworkMessage)e.NetworkMessage).PublisherId,
                         ((JsonNetworkMessage)e.NetworkMessage).DataSetWriterId,
                         e.NetworkMessage.DataSetMetaData.Fields.Count);
                }

                Console.WriteLine("\tMetaData.Name={0}, MajorVersion={1} MinorVersion={2}",
                    e.NetworkMessage.DataSetMetaData.Name,
                    e.NetworkMessage.DataSetMetaData.ConfigurationVersion.MajorVersion,
                    e.NetworkMessage.DataSetMetaData.ConfigurationVersion.MinorVersion);

                foreach (FieldMetaData metaDataField in e.NetworkMessage.DataSetMetaData.Fields)
                {
                    Console.WriteLine("\t\t{0, -20} DataType:{1, 10}, ValueRank:{2, 5}", metaDataField.Name, metaDataField.DataType, metaDataField.ValueRank);
                }
                Console.WriteLine(kDisplaySeparator);
            }
        }

        /// <summary>
        /// Handler for <see cref="UaPubSubApplication.ConfigurationUpdating"/>
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void UaPubSubApplication_ConfigurationUpdating(object sender, ConfigurationUpdatingEventArgs e)
        {
            Console.WriteLine("The UaPubSubApplication.ConfigurationUpdating event was triggered for part: {0} for {1}, With new value: {2}",
                e.ChangedProperty, e.Parent.GetType().Name, e.NewValue.GetType().Name);
            Console.WriteLine(kDisplaySeparator);
        }

        /// <summary>
        /// Creates a Subscriber PubSubConfiguration object for UDP & UADP programmatically.
        /// </summary>
        /// <returns></returns>
        private static PubSubConfigurationDataType CreateSubscriberConfiguration_UdpUadp(string urlAddress)
        {
            // Define a PubSub connection with PublisherId 1
            PubSubConnectionDataType pubSubConnection1 = new PubSubConnectionDataType();
            pubSubConnection1.Name = "Subscriber Connection1 UDP UADP";
            pubSubConnection1.Enabled = true;
            pubSubConnection1.PublisherId = (UInt16)1;
            pubSubConnection1.TransportProfileUri = Profiles.PubSubUdpUadpTransport;
            NetworkAddressUrlDataType address = new NetworkAddressUrlDataType();
            // Specify the local Network interface name to be used
            // e.g. address.NetworkInterface = "Ethernet";
            // Leave empty to subscribe on all available local interfaces.
            address.NetworkInterface = String.Empty;
            address.Url = urlAddress;
            pubSubConnection1.Address = new ExtensionObject(address);

            // configure custoom DicoveryAddress for Dicovery messages
            pubSubConnection1.TransportSettings = new ExtensionObject() {
                Body = new DatagramConnectionTransportDataType() {
                    DiscoveryAddress = new ExtensionObject() {
                        Body = new NetworkAddressUrlDataType() {
                            Url = "opc.udp://224.0.2.15:4840"
                        }
                    }
                }
            };

            #region Define ReaderGroup1
            ReaderGroupDataType readerGroup1 = new ReaderGroupDataType();
            readerGroup1.Name = "ReaderGroup 1";
            readerGroup1.Enabled = true;
            readerGroup1.MaxNetworkMessageSize = 1500;
            readerGroup1.MessageSettings = new ExtensionObject(new ReaderGroupMessageDataType());
            readerGroup1.TransportSettings = new ExtensionObject(new ReaderGroupTransportDataType());

            #region Define DataSetReader 'Simple' for PublisherId = (UInt16)1, DataSetWriterId = 1
            DataSetReaderDataType dataSetReaderSimple = new DataSetReaderDataType();
            dataSetReaderSimple.Name = "Reader 1 UDP UADP";
            dataSetReaderSimple.PublisherId = (UInt16)1;
            dataSetReaderSimple.WriterGroupId = 0;
            dataSetReaderSimple.DataSetWriterId = 1;
            dataSetReaderSimple.Enabled = true;
            dataSetReaderSimple.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;
            dataSetReaderSimple.KeyFrameCount = 1;
            dataSetReaderSimple.TransportSettings = new ExtensionObject(new DataSetReaderTransportDataType());

            UadpDataSetReaderMessageDataType uadpDataSetReaderMessage = new UadpDataSetReaderMessageDataType() {
                GroupVersion = 0,
                NetworkMessageNumber = 0,
                NetworkMessageContentMask = (uint)(uint)(UadpNetworkMessageContentMask.PublisherId
                        | UadpNetworkMessageContentMask.GroupHeader
                        | UadpNetworkMessageContentMask.WriterGroupId
                        | UadpNetworkMessageContentMask.GroupVersion
                        | UadpNetworkMessageContentMask.NetworkMessageNumber
                        | UadpNetworkMessageContentMask.SequenceNumber),
                DataSetMessageContentMask = (uint)(UadpDataSetMessageContentMask.Status | UadpDataSetMessageContentMask.SequenceNumber),
            };
            dataSetReaderSimple.MessageSettings = new ExtensionObject(uadpDataSetReaderMessage);            
            #endregion
            readerGroup1.DataSetReaders.Add(dataSetReaderSimple);

            #region Define DataSetReader 'AllTypes' for PublisherId = (UInt16)1, DataSetWriterId = 2
            DataSetReaderDataType dataSetReaderAllTypes = new DataSetReaderDataType();
            dataSetReaderAllTypes.Name = "Reader 2 UDP UADP";
            dataSetReaderAllTypes.PublisherId = (UInt16)1;
            dataSetReaderAllTypes.WriterGroupId = 0;
            dataSetReaderAllTypes.DataSetWriterId = 2;
            dataSetReaderAllTypes.Enabled = true;
            dataSetReaderAllTypes.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;
            dataSetReaderAllTypes.KeyFrameCount = 1;
            dataSetReaderAllTypes.TransportSettings = new ExtensionObject(new DataSetReaderTransportDataType());

            uadpDataSetReaderMessage = new UadpDataSetReaderMessageDataType() {
                GroupVersion = 0,
                NetworkMessageNumber = 0,
                NetworkMessageContentMask = (uint)(uint)(UadpNetworkMessageContentMask.PublisherId
                        | UadpNetworkMessageContentMask.GroupHeader
                        | UadpNetworkMessageContentMask.WriterGroupId
                        | UadpNetworkMessageContentMask.GroupVersion
                        | UadpNetworkMessageContentMask.NetworkMessageNumber
                        | UadpNetworkMessageContentMask.SequenceNumber),
                DataSetMessageContentMask = (uint)(UadpDataSetMessageContentMask.Status | UadpDataSetMessageContentMask.SequenceNumber),
            };
            dataSetReaderAllTypes.MessageSettings = new ExtensionObject(uadpDataSetReaderMessage);
            #endregion
            readerGroup1.DataSetReaders.Add(dataSetReaderAllTypes);

            #endregion
            pubSubConnection1.ReaderGroups.Add(readerGroup1);

            //create  pub sub configuration root object
            PubSubConfigurationDataType pubSubConfiguration = new PubSubConfigurationDataType();
            pubSubConfiguration.Connections = new PubSubConnectionDataTypeCollection()
                {
                    pubSubConnection1
                };

            return pubSubConfiguration;
        }

        /// <summary>
        /// Creates a Subscriber PubSubConfiguration object for MQTT & Json programmatically.
        /// </summary>
        /// <returns></returns>
        private static PubSubConfigurationDataType CreateSubscriberConfiguration_MqttJson(string urlAddress)
        {
            // Define a PubSub connection with PublisherId 2
            PubSubConnectionDataType pubSubConnection1 = new PubSubConnectionDataType();
            pubSubConnection1.Name = "Subscriber Connection3 MQTT Json";
            pubSubConnection1.Enabled = true;
            pubSubConnection1.PublisherId = (UInt16)2;
            pubSubConnection1.TransportProfileUri = Profiles.PubSubMqttJsonTransport;
            NetworkAddressUrlDataType address = new NetworkAddressUrlDataType();
            // Specify the local Network interface name to be used
            // e.g. address.NetworkInterface = "Ethernet";
            // Leave empty to subscribe on all available local interfaces.
            address.NetworkInterface = String.Empty;
            address.Url = urlAddress;
            pubSubConnection1.Address = new ExtensionObject(address);

            // Configure the mqtt specific configuration with the MQTTbroker
            ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V500);
            pubSubConnection1.ConnectionProperties = mqttConfiguration.ConnectionProperties;

            string brokerQueueName = "Json_WriterGroup_1";
            string brokerMetaData = "$Metadata";

            #region Define ReaderGroup1
            ReaderGroupDataType readerGroup1 = new ReaderGroupDataType();
            readerGroup1.Name = "ReaderGroup 1";
            readerGroup1.Enabled = true;
            readerGroup1.MaxNetworkMessageSize = 1500;
            readerGroup1.MessageSettings = new ExtensionObject(new ReaderGroupMessageDataType());
            readerGroup1.TransportSettings = new ExtensionObject(new ReaderGroupTransportDataType());

            #region Define DataSetReader1 'Simple' for PublisherId = (UInt16)2, DataSetWriterId = 1

            DataSetReaderDataType dataSetReaderSimple = new DataSetReaderDataType();
            dataSetReaderSimple.Name = "Reader 1 MQTT JSON Variant Encoding";
            dataSetReaderSimple.PublisherId = (UInt16)2;
            dataSetReaderSimple.WriterGroupId = 1;
            dataSetReaderSimple.DataSetWriterId = 1;
            dataSetReaderSimple.Enabled = true;
            dataSetReaderSimple.DataSetFieldContentMask = 0;// Variant encoding;
            dataSetReaderSimple.KeyFrameCount = 1;

            BrokerDataSetReaderTransportDataType brokerTransportSettings = new BrokerDataSetReaderTransportDataType() {
                QueueName = brokerQueueName,
                MetaDataQueueName = $"{brokerQueueName}/{brokerMetaData}",
            };

            dataSetReaderSimple.TransportSettings = new ExtensionObject(brokerTransportSettings);

            JsonDataSetReaderMessageDataType jsonDataSetReaderMessage = new JsonDataSetReaderMessageDataType() {
                NetworkMessageContentMask = (uint)(uint)(JsonNetworkMessageContentMask.NetworkMessageHeader
                        | JsonNetworkMessageContentMask.DataSetMessageHeader
                        | JsonNetworkMessageContentMask.PublisherId
                        | JsonNetworkMessageContentMask.DataSetClassId
                        | JsonNetworkMessageContentMask.ReplyTo),
                DataSetMessageContentMask = (uint)(JsonDataSetMessageContentMask.DataSetWriterId
                        | JsonDataSetMessageContentMask.MetaDataVersion
                        | JsonDataSetMessageContentMask.SequenceNumber
                        | JsonDataSetMessageContentMask.Status
                        | JsonDataSetMessageContentMask.Timestamp),
            };

            dataSetReaderSimple.MessageSettings = new ExtensionObject(jsonDataSetReaderMessage);

            #endregion
            readerGroup1.DataSetReaders.Add(dataSetReaderSimple);

            #region Define DataSetReader2 'AllTypes' for PublisherId = (UInt16)2, DataSetWriterId = 2
            DataSetReaderDataType dataSetReaderAllTypes = new DataSetReaderDataType();
            dataSetReaderAllTypes.Name = "Reader 2 MQTT JSON RawData Encoding";
            dataSetReaderAllTypes.PublisherId = (UInt16)2;
            dataSetReaderAllTypes.WriterGroupId = 1;
            dataSetReaderAllTypes.DataSetWriterId = 2;
            dataSetReaderAllTypes.Enabled = true;
            dataSetReaderAllTypes.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;
            dataSetReaderAllTypes.KeyFrameCount = 1;
            dataSetReaderAllTypes.TransportSettings = new ExtensionObject(brokerTransportSettings);

            jsonDataSetReaderMessage = new JsonDataSetReaderMessageDataType() {
                NetworkMessageContentMask = (uint)(JsonNetworkMessageContentMask.NetworkMessageHeader
                        | JsonNetworkMessageContentMask.DataSetMessageHeader
                        | JsonNetworkMessageContentMask.PublisherId
                        | JsonNetworkMessageContentMask.DataSetClassId
                        | JsonNetworkMessageContentMask.ReplyTo),
                DataSetMessageContentMask = (uint)(JsonDataSetMessageContentMask.DataSetWriterId
                        | JsonDataSetMessageContentMask.MetaDataVersion
                        | JsonDataSetMessageContentMask.SequenceNumber
                        | JsonDataSetMessageContentMask.Status
                        | JsonDataSetMessageContentMask.Timestamp),
            };
            dataSetReaderAllTypes.MessageSettings = new ExtensionObject(jsonDataSetReaderMessage);

            #endregion
            readerGroup1.DataSetReaders.Add(dataSetReaderAllTypes);

            #endregion
            pubSubConnection1.ReaderGroups.Add(readerGroup1);

            //create  pub sub configuration root object
            PubSubConfigurationDataType pubSubConfiguration = new PubSubConfigurationDataType();
            pubSubConfiguration.Connections = new PubSubConnectionDataTypeCollection()
                {
                    pubSubConnection1
                };

            return pubSubConfiguration;
        }

        /// <summary>
        /// Creates a Subscriber PubSubConfiguration object for UDP & UADP programmatically.
        /// </summary>
        /// <returns></returns>
        private static PubSubConfigurationDataType CreateSubscriberConfiguration_MqttUadp(string urlAddress)
        {
            // Define a PubSub connection with PublisherId 1
            PubSubConnectionDataType pubSubConnection1 = new PubSubConnectionDataType();
            pubSubConnection1.Name = "Subscriber Connection1 MQTT UADP";
            pubSubConnection1.Enabled = true;
            pubSubConnection1.PublisherId = (UInt16)3;
            pubSubConnection1.TransportProfileUri = Profiles.PubSubMqttUadpTransport;
            NetworkAddressUrlDataType address = new NetworkAddressUrlDataType();
            // Specify the local Network interface name to be used
            // e.g. address.NetworkInterface = "Ethernet";
            // Leave empty to subscribe on all available local interfaces.
            address.NetworkInterface = String.Empty;
            address.Url = urlAddress;
            pubSubConnection1.Address = new ExtensionObject(address);

            // Configure the mqtt specific configuration with the MQTTbroker
            ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V500);
            pubSubConnection1.ConnectionProperties = mqttConfiguration.ConnectionProperties;

            string brokerQueueName = "Uadp_WriterGroup_1";
            string brokerMetaData = "$Metadata";

            #region Define ReaderGroup1
            ReaderGroupDataType readerGroup1 = new ReaderGroupDataType();
            readerGroup1.Name = "ReaderGroup 1";
            readerGroup1.Enabled = true;
            readerGroup1.MaxNetworkMessageSize = 1500;
            readerGroup1.MessageSettings = new ExtensionObject(new ReaderGroupMessageDataType());
            readerGroup1.TransportSettings = new ExtensionObject(new ReaderGroupTransportDataType());

            #region Define DataSetReader 'Simple' for PublisherId = (UInt16)1, DataSetWriterId = 1
            DataSetReaderDataType dataSetReaderSimple = new DataSetReaderDataType();
            dataSetReaderSimple.Name = "Reader 1 MQTT UADP";
            dataSetReaderSimple.PublisherId = (UInt16)3;
            dataSetReaderSimple.WriterGroupId = 0;
            dataSetReaderSimple.DataSetWriterId = 1;
            dataSetReaderSimple.Enabled = true;
            dataSetReaderSimple.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;
            dataSetReaderSimple.KeyFrameCount = 1;
            BrokerDataSetReaderTransportDataType brokerTransportSettings = new BrokerDataSetReaderTransportDataType() {
                QueueName = brokerQueueName,
                MetaDataQueueName = $"{brokerQueueName}/{brokerMetaData}",
            };

            dataSetReaderSimple.TransportSettings = new ExtensionObject(brokerTransportSettings);

            UadpDataSetReaderMessageDataType uadpDataSetReaderMessage = new UadpDataSetReaderMessageDataType() {
                GroupVersion = 0,
                NetworkMessageNumber = 0,
                NetworkMessageContentMask = (uint)(uint)(UadpNetworkMessageContentMask.PublisherId
                        | UadpNetworkMessageContentMask.GroupHeader
                        | UadpNetworkMessageContentMask.WriterGroupId
                        | UadpNetworkMessageContentMask.PayloadHeader
                        | UadpNetworkMessageContentMask.GroupVersion
                        | UadpNetworkMessageContentMask.NetworkMessageNumber
                        | UadpNetworkMessageContentMask.SequenceNumber),
                DataSetMessageContentMask = (uint)(UadpDataSetMessageContentMask.Status | UadpDataSetMessageContentMask.SequenceNumber),
            };
            dataSetReaderSimple.MessageSettings = new ExtensionObject(uadpDataSetReaderMessage);

            #endregion
            readerGroup1.DataSetReaders.Add(dataSetReaderSimple);

            #region Define DataSetReader 'AllTypes' for PublisherId = (UInt16)1, DataSetWriterId = 2
            DataSetReaderDataType dataSetReaderAllTypes = new DataSetReaderDataType();
            dataSetReaderAllTypes.Name = "Reader 2 MQTT UADP";
            dataSetReaderAllTypes.PublisherId = (UInt16)3;
            dataSetReaderAllTypes.WriterGroupId = 0;
            dataSetReaderAllTypes.DataSetWriterId = 2;
            dataSetReaderAllTypes.Enabled = true;
            dataSetReaderAllTypes.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;
            dataSetReaderAllTypes.KeyFrameCount = 1;

            dataSetReaderAllTypes.TransportSettings = new ExtensionObject(brokerTransportSettings);

            uadpDataSetReaderMessage = new UadpDataSetReaderMessageDataType() {
                GroupVersion = 0,
                NetworkMessageNumber = 0,
                NetworkMessageContentMask = (uint)(uint)(UadpNetworkMessageContentMask.PublisherId
                        | UadpNetworkMessageContentMask.GroupHeader
                        | UadpNetworkMessageContentMask.WriterGroupId
                        | UadpNetworkMessageContentMask.PayloadHeader
                        | UadpNetworkMessageContentMask.GroupVersion
                        | UadpNetworkMessageContentMask.NetworkMessageNumber
                        | UadpNetworkMessageContentMask.SequenceNumber),
                DataSetMessageContentMask = (uint)(UadpDataSetMessageContentMask.Status | UadpDataSetMessageContentMask.SequenceNumber),
            };
            dataSetReaderAllTypes.MessageSettings = new ExtensionObject(uadpDataSetReaderMessage);
            #endregion
            readerGroup1.DataSetReaders.Add(dataSetReaderAllTypes);

            #endregion
            pubSubConnection1.ReaderGroups.Add(readerGroup1);

            //create  pub sub configuration root object
            PubSubConfigurationDataType pubSubConfiguration = new PubSubConfigurationDataType();
            pubSubConfiguration.Connections = new PubSubConnectionDataTypeCollection()
                {
                    pubSubConnection1
                };

            return pubSubConfiguration;
        }

        /// <summary>
        /// Creates the "Simple" DataSetMetaData
        /// </summary>
        /// <returns></returns>
        private static DataSetMetaDataType CreateDataSetMetaDataSimple()
        {
            DataSetMetaDataType simpleMetaData = new DataSetMetaDataType();
            simpleMetaData.DataSetClassId = new Uuid(Guid.Empty);
            simpleMetaData.Name = "Simple";
            simpleMetaData.Fields = new FieldMetaDataCollection()
                {
                    new FieldMetaData()
                    {
                        Name = "BoolToggle",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte) DataTypes.Boolean,
                        DataType = DataTypeIds.Boolean,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte) DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int32Fast",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte) DataTypes.Int32,
                        DataType = DataTypeIds.Int32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "DateTime",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte) DataTypes.DateTime,
                        DataType = DataTypeIds.DateTime,
                        ValueRank = ValueRanks.Scalar
                    },
                };
            // set the ConfigurationVersion relative to kTimeOfConfiguration constant
            simpleMetaData.ConfigurationVersion = new ConfigurationVersionDataType() {
                MinorVersion = ConfigurationVersionUtils.CalculateVersionTime(kTimeOfConfiguration),
                MajorVersion = ConfigurationVersionUtils.CalculateVersionTime(kTimeOfConfiguration)
            };

            return simpleMetaData;
        }

        /// <summary>
        /// Creates the "AllTypes" DataSetMetaData
        /// </summary>
        /// <returns></returns>
        private static DataSetMetaDataType CreateDataSetMetaDataAllTypes()
        {
            DataSetMetaDataType allTypesMetaData = new DataSetMetaDataType();
            allTypesMetaData.DataSetClassId = new Uuid(Guid.Empty);
            allTypesMetaData.Name = "AllTypes";
            allTypesMetaData.Fields = new FieldMetaDataCollection()
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
            // set the ConfigurationVersion relative to kTimeOfConfiguration constant
            allTypesMetaData.ConfigurationVersion = new ConfigurationVersionDataType() {
                MinorVersion = ConfigurationVersionUtils.CalculateVersionTime(kTimeOfConfiguration),
                MajorVersion = ConfigurationVersionUtils.CalculateVersionTime(kTimeOfConfiguration)
            };

            return allTypesMetaData;
        }

        /// <summary>
        /// Initialize logging
        /// </summary>
        private static void InitializeLog()
        {
            // Initialize logger
            Utils.SetTraceLog("%CommonApplicationData%\\OPC Foundation\\Logs\\Quickstarts.ConsoleReferenceSubscriber.log.txt", true);
            Utils.SetTraceMask(Utils.TraceMasks.Error);
            Utils.SetTraceOutput(Utils.TraceOutput.DebugAndFile);
        }
        #endregion
    }
}
