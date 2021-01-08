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

namespace Quickstarts.ConsoleReferencePublisher
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("OPC UA Console Reference Publisher");

            try
            {
                // Define the configuration of UA Publisher application
                PubSubConfigurationDataType pubSubConfiguration = CreatePublisherConfiguration();

                // Create the UA Publisher application
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
                        Console.CancelKeyPress += (sender, eArgs) =>
                        {
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

        /// <summary>
        /// Creates a PubSubConfiguration object programmatically.
        /// </summary>
        /// <returns></returns>
        public static PubSubConfigurationDataType CreatePublisherConfiguration()
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
            // Leave empty to publish on all available local interfaces.
            address.NetworkInterface = String.Empty;
            address.Url = "opc.udp://239.0.0.1:4840";
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
            UadpWriterGroupMessageDataType messageSettings = new UadpWriterGroupMessageDataType() {
                DataSetOrdering = DataSetOrderingType.AscendingWriterId,
                GroupVersion = 0,
                NetworkMessageContentMask = (uint)(UadpNetworkMessageContentMask.PublisherId | UadpNetworkMessageContentMask.GroupHeader
                        | UadpNetworkMessageContentMask.WriterGroupId | UadpNetworkMessageContentMask.GroupVersion
                        | UadpNetworkMessageContentMask.NetworkMessageNumber | UadpNetworkMessageContentMask.SequenceNumber)
            };

            writerGroup1.MessageSettings = new ExtensionObject(messageSettings);
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

            #region  Define PublishedDataSet Simple
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
            #endregion

            #region  Define PublishedDataSet AllTypes
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
            #endregion

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
    }
}
