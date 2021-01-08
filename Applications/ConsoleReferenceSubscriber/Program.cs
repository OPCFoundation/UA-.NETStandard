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
using System.Threading;
using Opc.Ua;
using Opc.Ua.PubSub;
using Opc.Ua.PubSub.PublishedData;

namespace Quickstarts.ConsoleReferenceSubscriber
{
    public static class Program
    {
        public const ushort NamespaceIndexSimple = 2;
        public const ushort NamespaceIndexAllTypes = 3;

        private static object m_lock = new object();

        public static void Main(string[] args)
        {
            Console.WriteLine("OPC UA Console Reference Subscriber");

            try
            {
                // Define the configuration of UA Subscriber application
                PubSubConfigurationDataType pubSubConfiguration = CreateSubscriberConfiguration();

                // Create the UA Publisher application
                using (UaPubSubApplication uaPubSubApplication = UaPubSubApplication.Create(pubSubConfiguration))
                {
                    // Subscribte to DataReceived event
                    uaPubSubApplication.DataReceived += UaPubSubApplication_DataReceived;

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

        private static void UaPubSubApplication_DataReceived(object sender, SubscribedDataEventArgs e)
        {
            lock (m_lock)
            {
                Console.WriteLine("Data Received from Source={0}, SequenceNumber={1}, DataSet count={2}",
                    e.SourceEndPoint, e.NetworkMessageSequenceNumber, e.DataSets.Count);

                foreach (DataSet dataSet in e.DataSets)
                {
                    Console.WriteLine("\tDataSet.Name={0}, DataSetWriterId={1}", dataSet.Name, dataSet.DataSetWriterId);

                    for (int i = 0; i < dataSet.Fields.Length; i++)
                    {
                        Console.WriteLine("\t\tTargetNodeId:{0}, Attribute:{1}, Value:{2}",
                            dataSet.Fields[i].TargetNodeId, dataSet.Fields[i].TargetAttribute, dataSet.Fields[i].Value);
                    }
                }
                Console.WriteLine("------------------------------------------------");
            }
        }

        /// <summary>
        /// Creates a PubSubConfiguration object programmatically.
        /// </summary>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreateSubscriberConfiguration()
        {
            // Define a PubSub connection with PublisherId 100
            PubSubConnectionDataType pubSubConnection1 = new PubSubConnectionDataType();
            pubSubConnection1.Name = "UADPConnection1";
            pubSubConnection1.Enabled = true;
            pubSubConnection1.PublisherId = (UInt16)100;
            pubSubConnection1.TransportProfileUri = Profiles.UadpTransport;
            NetworkAddressUrlDataType address = new NetworkAddressUrlDataType();
            // Specify the local Network interface name to be used
            // e.g. address.NetworkInterface = "Ethernet";
            // Leave empty to subscribe on all available local interfaces.
            address.NetworkInterface = String.Empty;
            address.Url = "opc.udp://239.0.0.1:4840";
            pubSubConnection1.Address = new ExtensionObject(address);

            #region  Define  'Simple' MetaData
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
            simpleMetaData.ConfigurationVersion = new ConfigurationVersionDataType() {
                MinorVersion = 1,
                MajorVersion = 1
            };
            #endregion

            #region Define 'AllTypes' Metadata
            DataSetMetaDataType allTypesMetaData = new DataSetMetaDataType();
            allTypesMetaData.DataSetClassId = new Uuid(Guid.Empty);
            allTypesMetaData.Name = "AllTypes";
            allTypesMetaData.Fields = new FieldMetaDataCollection()
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
                        Name = "Byte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte) DataTypes.Byte,
                        DataType = DataTypeIds.Byte,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Int16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte) DataTypes.Int16,
                        DataType = DataTypeIds.Int16,
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
                        Name = "SByte",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte) DataTypes.SByte,
                        DataType = DataTypeIds.SByte,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt16",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte) DataTypes.UInt16,
                        DataType = DataTypeIds.UInt16,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "UInt32",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte) DataTypes.UInt32,
                        DataType = DataTypeIds.UInt32,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Float",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte) DataTypes.Float,
                        DataType = DataTypeIds.Float,
                        ValueRank = ValueRanks.Scalar
                    },
                    new FieldMetaData()
                    {
                        Name = "Double",
                        DataSetFieldId = new Uuid(Guid.NewGuid()),
                        BuiltInType = (byte) DataTypes.Double,
                        DataType = DataTypeIds.Double,
                        ValueRank = ValueRanks.Scalar
                    },

            };
            allTypesMetaData.ConfigurationVersion = new ConfigurationVersionDataType() {
                MinorVersion = 1,
                MajorVersion = 1
            };
            #endregion

            #region Define ReaderGroup1
            ReaderGroupDataType readerGroup1 = new ReaderGroupDataType();
            readerGroup1.Name = "ReaderGroup 1";
            readerGroup1.Enabled = true;
            readerGroup1.MaxNetworkMessageSize = 1500;
            readerGroup1.MessageSettings = new ExtensionObject(new ReaderGroupMessageDataType());
            readerGroup1.TransportSettings = new ExtensionObject(new ReaderGroupTransportDataType());

            #region Define DataSetReader 'Simple' for PublisherId = (UInt16)100, DataSetWriterId = 1
            DataSetReaderDataType dataSetReaderSimple = new DataSetReaderDataType();
            dataSetReaderSimple.Name = "Reader 1";
            dataSetReaderSimple.PublisherId = (UInt16)100;
            dataSetReaderSimple.WriterGroupId = 0;
            dataSetReaderSimple.DataSetWriterId = 0;
            dataSetReaderSimple.Enabled = true;
            dataSetReaderSimple.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;
            dataSetReaderSimple.KeyFrameCount = 1;
            dataSetReaderSimple.DataSetMetaData = simpleMetaData;

            UadpDataSetReaderMessageDataType uadpDataSetReaderMessage = new UadpDataSetReaderMessageDataType() {
                GroupVersion = 0,
                DataSetOffset = 15,
                NetworkMessageNumber = 0,
                NetworkMessageContentMask = (uint)(uint)(UadpNetworkMessageContentMask.PublisherId | UadpNetworkMessageContentMask.GroupHeader
                        | UadpNetworkMessageContentMask.WriterGroupId | UadpNetworkMessageContentMask.GroupVersion
                        | UadpNetworkMessageContentMask.NetworkMessageNumber | UadpNetworkMessageContentMask.SequenceNumber),
                DataSetMessageContentMask = (uint)(UadpDataSetMessageContentMask.Status | UadpDataSetMessageContentMask.SequenceNumber),
            };
            dataSetReaderSimple.MessageSettings = new ExtensionObject(uadpDataSetReaderMessage);
            TargetVariablesDataType subscribedDataSet = new TargetVariablesDataType();
            subscribedDataSet.TargetVariables = new FieldTargetDataTypeCollection();
            foreach (var fieldMetaData in simpleMetaData.Fields)
            {
                subscribedDataSet.TargetVariables.Add(new FieldTargetDataType() {
                    DataSetFieldId = fieldMetaData.DataSetFieldId,
                    TargetNodeId = new NodeId(fieldMetaData.Name, NamespaceIndexSimple),
                    AttributeId = Attributes.Value,
                    OverrideValueHandling = OverrideValueHandling.OverrideValue,
                    OverrideValue = new Variant(TypeInfo.GetDefaultValue(fieldMetaData.DataType, (int)ValueRanks.Scalar))
                });
            }

            dataSetReaderSimple.SubscribedDataSet = new ExtensionObject(subscribedDataSet);
            #endregion
            readerGroup1.DataSetReaders.Add(dataSetReaderSimple);

            #region Define DataSetReader 'AllTypes' for PublisherId = (UInt16)100, DataSetWriterId = 2
            DataSetReaderDataType dataSetReaderAllTypes = new DataSetReaderDataType();
            dataSetReaderAllTypes.Name = "Reader 2";
            dataSetReaderAllTypes.PublisherId = (UInt16)100;
            dataSetReaderAllTypes.WriterGroupId = 0;
            dataSetReaderAllTypes.DataSetWriterId = 0;
            dataSetReaderAllTypes.Enabled = true;
            dataSetReaderAllTypes.DataSetFieldContentMask = (uint)DataSetFieldContentMask.RawData;
            dataSetReaderAllTypes.KeyFrameCount = 1;
            dataSetReaderAllTypes.DataSetMetaData = allTypesMetaData;

            uadpDataSetReaderMessage = new UadpDataSetReaderMessageDataType() {
                GroupVersion = 0,
                DataSetOffset = 47,
                NetworkMessageNumber = 0,
                NetworkMessageContentMask = (uint)(uint)(UadpNetworkMessageContentMask.PublisherId | UadpNetworkMessageContentMask.GroupHeader
                        | UadpNetworkMessageContentMask.WriterGroupId | UadpNetworkMessageContentMask.GroupVersion
                        | UadpNetworkMessageContentMask.NetworkMessageNumber | UadpNetworkMessageContentMask.SequenceNumber),
                DataSetMessageContentMask = (uint)(UadpDataSetMessageContentMask.Status | UadpDataSetMessageContentMask.SequenceNumber),
            };
            dataSetReaderAllTypes.MessageSettings = new ExtensionObject(uadpDataSetReaderMessage);
            subscribedDataSet = new TargetVariablesDataType();
            subscribedDataSet.TargetVariables = new FieldTargetDataTypeCollection();
            foreach (var fieldMetaData in allTypesMetaData.Fields)
            {
                subscribedDataSet.TargetVariables.Add(new FieldTargetDataType() {
                    DataSetFieldId = fieldMetaData.DataSetFieldId,
                    TargetNodeId = new NodeId(fieldMetaData.Name, NamespaceIndexAllTypes),
                    AttributeId = Attributes.Value,
                    OverrideValueHandling = OverrideValueHandling.OverrideValue,
                    OverrideValue = new Variant(TypeInfo.GetDefaultValue(fieldMetaData.DataType, (int)ValueRanks.Scalar))
                });
            }

            dataSetReaderAllTypes.SubscribedDataSet = new ExtensionObject(subscribedDataSet);
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
    }
}
