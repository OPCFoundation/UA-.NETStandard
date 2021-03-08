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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Mqtt;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture(Description = "Tests for Encoding/Decoding of JsonNetworkMessage objects")]
    public class JsonNetworkMessageTests
    {
        private const UInt16 PublisherId = 30;
        private const UInt16 WriterGroupId = 1;

        public const ushort NamespaceIndexSimple = 2;
        public const ushort NamespaceIndexAllTypes = 3;

        //private const string PublisherConfigurationFileName = "PublisherConfiguration.xml";
        //private const string SubscriberConfigurationFileName = "SubscriberConfiguration.xml";

        private UaPubSubApplication m_publisherApplication;
        private PubSubConfigurationDataType m_publisherConfiguration;
        private PubSubConnectionDataType m_firstPublisherConnection;
        private WriterGroupDataType m_firstJsonWriterGroup;
        private IUaPubSubConnection m_firstJsonPublisherConnection;

        private PubSubConfigurationDataType m_subscriberConfiguration;
        private PubSubConnectionDataType m_firstSubscriberConnection;
        private UaPubSubApplication m_subscriberApplication;
        private ReaderGroupDataType m_firstJsonReaderGroup;
        private List<DataSetReaderDataType> m_firstJsonDataSetReadersType;

        private const uint NetworkMessageContentMask = 0x3ff;
        private DataSetFieldContentMask fieldContentMaskVariant = DataSetFieldContentMask.None;
        private DataSetFieldContentMask fieldContentMaskDataValue =
            DataSetFieldContentMask.StatusCode
            | DataSetFieldContentMask.SourceTimestamp
            | DataSetFieldContentMask.ServerTimestamp
            | DataSetFieldContentMask.SourcePicoSeconds
            | DataSetFieldContentMask.ServerPicoSeconds;
        private DataSetFieldContentMask fieldContentMaskRawData = DataSetFieldContentMask.RawData;

        [OneTimeSetUp()]
        public void MyTestInitialize()
        {
            /*
            // Create a publisher application
            string publisherConfigurationFile = Utils.GetAbsoluteFilePath(PublisherConfigurationFileName, true, true, false);
            m_publisherApplication = UaPubSubApplication.Create(publisherConfigurationFile);
            Assert.IsNotNull(m_publisherApplication, "m_publisherApplication shall not be null");

            // Get the publisher configuration
            m_publisherConfiguration = m_publisherApplication.UaPubSubConfigurator.PubSubConfiguration;
            Assert.IsNotNull(m_publisherConfiguration, "m_publisherConfiguration should not be null");

            //Get first connection
            Assert.IsNotNull(m_publisherConfiguration.Connections, "m_publisherConfiguration.Connections should not be null");
            Assert.IsNotEmpty(m_publisherConfiguration.Connections, "m_publisherConfiguration.Connections should not be empty");
            m_firstJsonPublisherConnection = m_publisherApplication.PubSubConnections[2];
            Assert.IsNotNull(m_firstJsonPublisherConnection, "m_firstJsonPublisherConnection should not be null");

            // Read the first writer group
            Assert.IsNotEmpty(m_publisherConfiguration.Connections[0].WriterGroups, "pubSubConfigConnection.WriterGroups should not be empty");
            m_firstJsonWriterGroup = m_publisherConfiguration.Connections[2].WriterGroups[0];
            Assert.IsNotNull(m_firstJsonWriterGroup, "m_firstJsonWriterGroup should not be null");

            // Create a subscriber application
            string subscriberConfigurationFile = Utils.GetAbsoluteFilePath(SubscriberConfigurationFileName, true, true, false);
            m_subscriberApplication = UaPubSubApplication.Create(subscriberConfigurationFile);
            Assert.IsNotNull(m_subscriberApplication, "m_subscriberApplication should not be null");

            // Get the subscriber configuration
            m_subscriberConfiguration = m_subscriberApplication.UaPubSubConfigurator.PubSubConfiguration;
            Assert.IsNotNull(m_subscriberConfiguration, "m_subscriberConfiguration should not be null");

            // Get first reader group
            m_firstJsonReaderGroup = m_subscriberConfiguration.Connections[2].ReaderGroups[0];
            Assert.IsNotNull(m_firstJsonWriterGroup, "m_firstJsonReaderGroup should not be null");
            */

            //m_publisherApplication = UaPubSubApplication.Create(publisherConfigurationFile);
            //Assert.IsNotNull(m_publisherApplication, "m_publisherApplication shall not be null");

            m_publisherConfiguration = MessagesHelper.CreateJsonPublisherConfigurationWithRawDataSingleDataSetMessage(PublisherId, WriterGroupId);
            Assert.IsNotNull(m_publisherConfiguration, "m_publisherConfiguration should not be null");

            m_firstPublisherConnection = MessagesHelper.GetFirstConnection(m_publisherConfiguration);
            Assert.IsNotNull(m_firstPublisherConnection, "m_firstJsonPublisherConnection should not be null");

            m_firstJsonWriterGroup = MessagesHelper.GetFirstWriterGroup(m_firstPublisherConnection);
            Assert.IsNotNull(m_firstJsonWriterGroup, "m_firstJsonWriterGroup should not be null");

            // Configure the mqtt specific configuration with the MQTTbroker
            ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V500);

            m_publisherApplication = UaPubSubApplication.Create(m_publisherConfiguration);
            Assert.IsNotNull(m_publisherApplication, "m_publisherApplication shall not be null");

            m_firstJsonPublisherConnection = m_publisherApplication.PubSubConnections[0];
            Assert.IsNotNull(m_firstJsonPublisherConnection, "m_firstJsonPublisherConnection should not be null");

            // Get the subscriber configuration
            m_subscriberConfiguration = MessagesHelper.CreateJsonSubscriberConfigurationWithRawDataSingleDataSetMessage(PublisherId, WriterGroupId);
            Assert.IsNotNull(m_subscriberConfiguration, "m_subscriberConfiguration should not be null");

            m_firstSubscriberConnection = MessagesHelper.GetFirstConnection(m_subscriberConfiguration);
            Assert.IsNotNull(m_firstPublisherConnection, "m_firstSubscriberConnection should not be null");

            // Get first reader group
            m_firstJsonReaderGroup = MessagesHelper.GetFirstReaderGroup(m_firstSubscriberConnection); 
            Assert.IsNotNull(m_firstJsonReaderGroup, "m_firstJsonReaderGroup should not be null");

            m_firstJsonDataSetReadersType = GetFirstDataSetReaders();
        }

        [Test(Description = "Validate PublisherId as string with RawData data type")]
        public void ValidatePublisherIdStringWithRawDataType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskDataValue);

            // Act  
            // Check PublisherId as byte type
            uaNetworkMessage.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = "30";
            //uaNetworkMessage.PublisherId = "Test$!#$%^&*87";
            //uaNetworkMessage.PublisherId = "Begrüßung";

            // Assert
            CompareEncodeDecode(uaNetworkMessage);
        }

        #region Private methods

        /// <summary>
        /// Load RawData data type into datasets
        /// </summary>
        private void LoadData()
        {
            Assert.IsNotNull(m_publisherApplication, "m_publisherApplication should not be null");

            #region DataSet Simple
            // DataSet 'Simple' fill with data
            DataValue booleanValue = new DataValue(new Variant(true));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggle", NamespaceIndexSimple), Attributes.Value, booleanValue);
            DataValue scalarInt32XValue = new DataValue(new Variant(100));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int32", NamespaceIndexSimple), Attributes.Value, scalarInt32XValue);
            DataValue scalarInt32YValue = new DataValue(new Variant(50));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int32Fast", NamespaceIndexSimple), Attributes.Value, scalarInt32YValue);
            DataValue dateTimeValue = new DataValue(new Variant(DateTime.UtcNow));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("DateTime", NamespaceIndexSimple), Attributes.Value, dateTimeValue);
            #endregion

            #region DataSet AllTypes
            // DataSet 'AllTypes' fill with data
            DataValue allTypesBooleanValue = new DataValue(new Variant(false));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggle", NamespaceIndexAllTypes), Attributes.Value, allTypesBooleanValue);
            DataValue byteValue = new DataValue(new Variant((byte)10));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Byte", NamespaceIndexAllTypes), Attributes.Value, byteValue);
            DataValue int16Value = new DataValue(new Variant((short)100));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int16", NamespaceIndexAllTypes), Attributes.Value, int16Value);
            DataValue int32Value = new DataValue(new Variant((int)1000));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int32", NamespaceIndexAllTypes), Attributes.Value, int32Value);
            DataValue sByteValue = new DataValue(new Variant((sbyte)11));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("SByte", NamespaceIndexAllTypes), Attributes.Value, sByteValue);
            DataValue uInt16Value = new DataValue(new Variant((ushort)110));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("UInt16", NamespaceIndexAllTypes), Attributes.Value, uInt16Value);
            DataValue uInt32Value = new DataValue(new Variant((uint)1100));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("UInt32", NamespaceIndexAllTypes), Attributes.Value, uInt32Value);
            DataValue floatValue = new DataValue(new Variant((float)1100.5));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Float", NamespaceIndexAllTypes), Attributes.Value, floatValue);
            DataValue doubleValue = new DataValue(new Variant((double)1100));
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Double", NamespaceIndexAllTypes), Attributes.Value, doubleValue);
            #endregion
        }

        /// <summary>
        /// Get first DataSetReaders from configuration
        /// </summary>
        /// <returns></returns>
        private List<DataSetReaderDataType> GetFirstDataSetReaders()
        {
            // Read the first configured ReaderGroup
            Assert.IsNotNull(m_firstJsonReaderGroup, "m_firstJsonReaderGroup should not be null");
            Assert.IsNotEmpty(m_firstJsonReaderGroup.DataSetReaders, "m_firstJsonReaderGroup.DataSetReaders should not be empty");

            return m_firstJsonReaderGroup.DataSetReaders;
        }

        /// <summary>
        /// Creates a network message (based on a configuration)
        /// </summary>
        /// <param name="dataSetFieldContentMask"></param>
        /// <returns></returns>
        private JsonNetworkMessage CreateNetworkMessage(DataSetFieldContentMask dataSetFieldContentMask)
        {
            LoadData();

            // set the configurable field content mask to allow only Variant data type
            foreach (DataSetWriterDataType dataSetWriter in m_firstJsonWriterGroup.DataSetWriters)
            {
                // 00 The DataSet fields are encoded as Variant data type
                // The Variant can contain a StatusCode instead of the expected DataType if the status of the field is Bad.
                // The Variant can contain a DataValue with the value and the statusCode if the status of the field is Uncertain.
                dataSetWriter.DataSetFieldContentMask = (uint)dataSetFieldContentMask;
            }

            JsonNetworkMessage uaNetworkMessage = (JsonNetworkMessage)m_firstJsonPublisherConnection.CreateNetworkMessage(m_firstJsonWriterGroup);
            Assert.IsNotNull(uaNetworkMessage, "networkMessageEncode should not be null");

            return uaNetworkMessage;
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="uadpNetworkMessageEncode"></param>
        /// <param name="uadpNetworkMessageDecoded"></param>
        private void CompareEncodeDecode(JsonNetworkMessage jsonNetworkMessage)
        {
            byte[] bytes = jsonNetworkMessage.Encode();

            JsonNetworkMessage uaNetworkMessageDecoded = new JsonNetworkMessage();
            uaNetworkMessageDecoded.Decode("Test", bytes, m_firstJsonDataSetReadersType);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            // TODO Fix: this might be broken after refactor
            Compare(jsonNetworkMessage, uaNetworkMessageDecoded, uaNetworkMessageDecoded.ReceivedDataSets);
        }

        /// <summary>
        /// Compare network messages options 
        /// </summary>
        /// <param name="jsonNetworkMessageEncode"></param>
        /// <param name="jsonNetworkMessageDecoded"></param>
        /// <returns></returns>
        private void Compare(JsonNetworkMessage jsonNetworkMessageEncode, JsonNetworkMessage jsonNetworkMessageDecoded, List<DataSet> subscribedDataSets)
        {
            JsonNetworkMessageContentMask networkMessageContentMask = jsonNetworkMessageEncode.NetworkMessageContentMask;

            if ((networkMessageContentMask | JsonNetworkMessageContentMask.None) == JsonNetworkMessageContentMask.None)
            {
                // nothing to check
                return;
            }

            // Verify flags
            Assert.AreEqual(jsonNetworkMessageEncode.NetworkMessageContentMask, jsonNetworkMessageDecoded.NetworkMessageContentMask, "NetworkMessageContentMask were not decoded correctly");

            #region Network Message Header
            if ((networkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
            {
                Assert.AreEqual(jsonNetworkMessageEncode.PublisherId, jsonNetworkMessageDecoded.PublisherId, "PublisherId was not decoded correctly");
            }

            if ((networkMessageContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0)
            {
                Assert.AreEqual(jsonNetworkMessageEncode.DataSetClassId, jsonNetworkMessageDecoded.DataSetClassId, "DataSetClassId was not decoded correctly");
            }
            #endregion

            #region Payload header + Payload data

            if ((networkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0)
            {
                // check the number of JsonDataSetMessage counts
                Assert.AreEqual(jsonNetworkMessageEncode.DataSetMessages.Count,
                    jsonNetworkMessageDecoded.DataSetMessages.Count, "JsonDataSetMessages.Count was not decoded correctly");

                Assert.IsNotNull(subscribedDataSets, "SubscribedDataSets is null");

                // check if the encoded match the decoded DataSetWriterId's
                foreach (JsonDataSetMessage jsonDataSetMessage in jsonNetworkMessageEncode.DataSetMessages)
                {
                    JsonDataSetMessage jsonDataSetMessageDecoded =
                        jsonNetworkMessageDecoded.DataSetMessages.FirstOrDefault(decoded =>
                            ((JsonDataSetMessage)decoded).DataSetWriterId == jsonDataSetMessage.DataSetWriterId) as JsonDataSetMessage;

                    Assert.IsNotNull(jsonDataSetMessageDecoded, "Decoded message did not found jsonDataSetMessage.DataSetWriterId = {0}", jsonDataSetMessage.DataSetWriterId);

                    // check payload data fields count 
                    // get related dataset from subscriber DataSets
                    DataSet decodedDataSet = subscribedDataSets.FirstOrDefault(dataSet => dataSet.Name == jsonDataSetMessage.DataSet.Name);
                    Assert.IsNotNull(decodedDataSet, "DataSet '{0}' is missing from subscriber datasets!", jsonDataSetMessage.DataSet.Name);

                    Assert.AreEqual(jsonDataSetMessage.DataSet.Fields.Length, decodedDataSet.Fields.Length,
                        "DataSet.Fields.Length was not decoded correctly, DataSetWriterId = {0}", jsonDataSetMessage.DataSetWriterId);

                    // check the fields data consistency
                    // at this time the DataSetField has just value!?
                    for (int index = 0; index < jsonDataSetMessage.DataSet.Fields.Length; index++)
                    {
                        Field fieldEncoded = jsonDataSetMessage.DataSet.Fields[index];
                        Field fieldDecoded = decodedDataSet.Fields[index];
                        Assert.IsNotNull(fieldEncoded, "jsonDataSetMessage.DataSet.Fields[{0}] is null,  DataSetWriterId = {1}",
                            index, jsonDataSetMessage.DataSetWriterId);
                        Assert.IsNotNull(fieldDecoded, "jsonDataSetMessageDecoded.DataSet.Fields[{0}] is null,  DataSetWriterId = {1}",
                            index, jsonDataSetMessage.DataSetWriterId);

                        DataValue dataValueEncoded = fieldEncoded.Value;
                        DataValue dataValueDecoded = fieldDecoded.Value;
                        Assert.IsNotNull(fieldEncoded.Value, "jsonDataSetMessage.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                           index, jsonDataSetMessage.DataSetWriterId);
                        Assert.IsNotNull(fieldDecoded.Value, "jsonDataSetMessageDecoded.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                          index, jsonDataSetMessage.DataSetWriterId);

                        // check dataValues values
                        Assert.IsNotNull(fieldEncoded.Value.Value, "jsonDataSetMessage.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                           index, jsonDataSetMessage.DataSetWriterId);
                        Assert.IsNotNull(fieldDecoded.Value.Value, "jsonDataSetMessageDecoded.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                          index, jsonDataSetMessage.DataSetWriterId);

                        Assert.AreEqual(dataValueEncoded.Value, dataValueDecoded.Value, "Wrong: Fields[{0}].DataValue.Value; DataSetWriterId = {1}", index, jsonDataSetMessage.DataSetWriterId);

                        // Checks just for DataValue type only 
                        if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.StatusCode) ==
                            DataSetFieldContentMask.StatusCode)
                        {
                            // check dataValues StatusCode
                            Assert.AreEqual(dataValueEncoded.StatusCode, dataValueDecoded.StatusCode,
                                "Wrong: Fields[{0}].DataValue.StatusCode; DataSetWriterId = {1}", index, jsonDataSetMessage.DataSetWriterId);
                        }

                        // check dataValues SourceTimestamp
                        if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.SourceTimestamp) ==
                            DataSetFieldContentMask.SourceTimestamp)
                        {
                            Assert.AreEqual(dataValueEncoded.SourceTimestamp, dataValueDecoded.SourceTimestamp,
                                "Wrong: Fields[{0}].DataValue.SourceTimestamp; DataSetWriterId = {1}", index, jsonDataSetMessage.DataSetWriterId);
                        }

                        // check dataValues ServerTimestamp
                        if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.ServerTimestamp) ==
                            DataSetFieldContentMask.ServerTimestamp)
                        {
                            // check dataValues ServerTimestamp
                            Assert.AreEqual(dataValueEncoded.ServerTimestamp, dataValueDecoded.ServerTimestamp,
                               "Wrong: Fields[{0}].DataValue.ServerTimestamp; DataSetWriterId = {1}", index, jsonDataSetMessage.DataSetWriterId);
                        }

                        // check dataValues SourcePicoseconds
                        if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.SourcePicoSeconds) ==
                            DataSetFieldContentMask.SourcePicoSeconds)
                        {
                            Assert.AreEqual(dataValueEncoded.SourcePicoseconds, dataValueDecoded.SourcePicoseconds,
                               "Wrong: Fields[{0}].DataValue.SourcePicoseconds; DataSetWriterId = {1}", index, jsonDataSetMessage.DataSetWriterId);
                        }

                        // check dataValues ServerPicoSeconds
                        if ((jsonDataSetMessage.FieldContentMask & DataSetFieldContentMask.ServerPicoSeconds) ==
                            DataSetFieldContentMask.ServerPicoSeconds)
                        {
                            // check dataValues ServerPicoseconds
                            Assert.AreEqual(dataValueEncoded.ServerPicoseconds, dataValueDecoded.ServerPicoseconds,
                               "Wrong: Fields[{0}].DataValue.ServerPicoseconds; DataSetWriterId = {1}", index, jsonDataSetMessage.DataSetWriterId);
                        }
                    }
                }
            }
            #endregion
        }
        #endregion
    }
}
