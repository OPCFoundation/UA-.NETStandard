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
        public const UInt16 NamespaceIndexSimple = 2;
        public const UInt16 NamespaceIndexAllTypes = 3;

        public const UInt16 PublisherId1 = 30;
        public const UInt16 WriterGroupId1 = 1;
        public const UInt16 PublisherId2 = 40;
        public const UInt16 WriterGroupId2 = 2;

        private UaPubSubApplication m_singleDataSetPublisherApplication;
        private PubSubConfigurationDataType m_publisherSingleDataSetConfiguration;
        private PubSubConnectionDataType m_publisherSingleDataSetConnection;
        private WriterGroupDataType m_singleDataSetWriterGroup;
        private IUaPubSubConnection m_singleDataSetPublisherConnection;
        
        //private UaPubSubApplication m_subscriberApplication;
        private PubSubConfigurationDataType m_subscriberSingleDataSetConfiguration;
        private PubSubConnectionDataType m_subscriberSingleDataSetConnection;
        private ReaderGroupDataType m_singleDataSetReaderGroup;
        private List<DataSetReaderDataType> m_singleDataSetReaders;
        
        private UaPubSubApplication m_multipleDataSetsPublisherApplication;
        private PubSubConfigurationDataType m_publisherMultipleDataSetsConfiguration;
        private PubSubConnectionDataType m_publisherMultipleDataSetsConnection;
        private WriterGroupDataType m_multipleDataSetsWriterGroup;
        private IUaPubSubConnection m_multipleDataSetsPublisherConnection;
        
        private PubSubConfigurationDataType m_subscriberMultipleDataSetsConfiguration;
        private PubSubConnectionDataType m_subscriberMultipleDataSetsConnection;
        private ReaderGroupDataType m_multipleDataSetsReaderGroup;
        private List<DataSetReaderDataType> m_multipleDataSetsReaders;
        
        private const uint NetworkMessageContentMask = 0x3f;

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
            #region Create single dataset configuration
            // Get the publisher configuration with single dataset
            m_publisherSingleDataSetConfiguration = MessagesHelper.CreateJsonPublisherConfigurationWithSingleDataSetMessage(PublisherId1, WriterGroupId1);
            Assert.IsNotNull(m_publisherSingleDataSetConfiguration, "m_publisherSingleDataSetConfiguration should not be null");

            // Get the subscriber configuration with single dataset
            m_subscriberSingleDataSetConfiguration = MessagesHelper.CreateJsonSubscriberConfigurationWithSingleDataSetMessage(PublisherId1, WriterGroupId1);
            Assert.IsNotNull(m_subscriberSingleDataSetConfiguration, "m_subscriberSingleDataSetConfiguration should not be null");

            // Get the publisher connection for single dataset
            m_publisherSingleDataSetConnection = MessagesHelper.GetConnection(m_publisherSingleDataSetConfiguration, PublisherId1);
            Assert.IsNotNull(m_publisherSingleDataSetConnection, "m_publisherSingleDataSetConnection should not be null");

            // Get the subscriber connnection for single dataset
            m_subscriberSingleDataSetConnection = MessagesHelper.GetConnection(m_subscriberSingleDataSetConfiguration, PublisherId1);
            Assert.IsNotNull(m_subscriberSingleDataSetConnection, "m_subscriberSingleDataSetConnection should not be null");

            // Get writer group
            m_singleDataSetWriterGroup = MessagesHelper.GetWriterGroup(m_publisherSingleDataSetConnection, WriterGroupId1);
            Assert.IsNotNull(m_singleDataSetWriterGroup, "m_singleDataSetWriterGroup should not be null");

            // Get reader group
            m_singleDataSetReaderGroup = MessagesHelper.GetReaderGroup(m_subscriberSingleDataSetConnection, WriterGroupId1);
            Assert.IsNotNull(m_singleDataSetReaderGroup, "m_singleDataSetReaderGroup should not be null");

            // Create publisher application for single dataset
            m_singleDataSetPublisherApplication = UaPubSubApplication.Create(m_publisherSingleDataSetConfiguration);
            Assert.IsNotNull(m_singleDataSetPublisherApplication, "m_singleDataSePublisherApplication shall not be null");

            // Create publisher connection for single dataset
            m_singleDataSetPublisherConnection = m_singleDataSetPublisherApplication.PubSubConnections.First();
            Assert.IsNotNull(m_singleDataSetPublisherConnection, "m_singleDataSetPublisherConnection should not be null");

            m_singleDataSetReaders = GetDataSetReaders(m_singleDataSetReaderGroup);
            #endregion

            #region Create multiple datasets configuration
            // Get the publisher configuration with multiple datasets
            m_publisherMultipleDataSetsConfiguration = MessagesHelper.CreateJsonPublisherConfigurationWithMultipleDataSetMessages(PublisherId2, WriterGroupId2);
            Assert.IsNotNull(m_publisherMultipleDataSetsConfiguration, "m_publisherMultipleDataSetsConfiguration should not be null");

            // Get the subscriber configuration with single dataset
            m_subscriberMultipleDataSetsConfiguration = MessagesHelper.CreateJsonSubscriberConfigurationWithMultipleDataSetMessages(PublisherId2, WriterGroupId2);
            Assert.IsNotNull(m_subscriberMultipleDataSetsConfiguration, "m_subscriberMultipleDataSetsConfiguration should not be null");

            // Get the publisher connnection for multiple datasets
            m_publisherMultipleDataSetsConnection = MessagesHelper.GetConnection(m_publisherMultipleDataSetsConfiguration, PublisherId2);
            Assert.IsNotNull(m_publisherMultipleDataSetsConnection, "m_publisherMultipleDataSetsConnection should not be null");

            // Get the subscriber connnection for multiple datasets
            m_subscriberMultipleDataSetsConnection = MessagesHelper.GetConnection(m_subscriberMultipleDataSetsConfiguration, PublisherId2);
            Assert.IsNotNull(m_subscriberMultipleDataSetsConnection, "m_subscriberMultipleDataSetsConnection should not be null");

            // Get writer group for multiple datasets
            m_multipleDataSetsWriterGroup = MessagesHelper.GetWriterGroup(m_publisherMultipleDataSetsConnection, WriterGroupId2);
            Assert.IsNotNull(m_multipleDataSetsWriterGroup, "m_multipleDataSetsWriterGroup should not be null");

            // Get reader group  for multiple datasets
            m_multipleDataSetsReaderGroup = MessagesHelper.GetReaderGroup(m_subscriberMultipleDataSetsConnection, WriterGroupId2);
            Assert.IsNotNull(m_multipleDataSetsReaderGroup, "m_multipleDataSetsReaderGroup should not be null");

            // Create publisher application for multiple datasets
            m_multipleDataSetsPublisherApplication = UaPubSubApplication.Create(m_publisherSingleDataSetConfiguration);
            Assert.IsNotNull(m_multipleDataSetsPublisherApplication, "m_multipleDataSetsPublisherApplication shall not be null");

            // Create publisher connection  for multiple datasets
            m_multipleDataSetsPublisherConnection = m_singleDataSetPublisherApplication.PubSubConnections.First();
            Assert.IsNotNull(m_multipleDataSetsPublisherConnection, "m_multipleDataSetsPublisherConnection should not be null");

            m_multipleDataSetsReaders = GetDataSetReaders(m_multipleDataSetsReaderGroup);
            #endregion
        }

        [Test(Description = "Validate network message mask ;" +
                            "Change the Json network message mask into the [0,63] range that covers all options(properties)")]
        public void ValidateNetworkMessageMaskWithVariantType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskVariant);

            // Act  
            // change network message mask
            for (uint networkMessageContentMask = 0; networkMessageContentMask < NetworkMessageContentMask; networkMessageContentMask++)
            {
                uaNetworkMessage.SetNetworkMessageContentMask((JsonNetworkMessageContentMask)networkMessageContentMask);

                // Assert
                CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
            }
        }

        [Test(Description = "Validate network message mask ;" +
                            "Change the Json network message mask into the [0,63] range that covers all options(properties)")]
        public void ValidateNetworkMessageMaskWithDataValueType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskDataValue);

            // Act  
            // change network message mask
            for (uint networkMessageContentMask = 0; networkMessageContentMask < NetworkMessageContentMask; networkMessageContentMask++)
            {
                uaNetworkMessage.SetNetworkMessageContentMask((JsonNetworkMessageContentMask)networkMessageContentMask);

                // Assert
                CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
            }
        }

        [Test(Description = "Validate network message mask ;" +
                            "Change the Json network message mask into the [0,63] range that covers all options(properties)")]
        public void ValidateNetworkMessageMaskWithRawDataType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskRawData);

            // Act  
            // change network message mask
            for (uint networkMessageContentMask = 0; networkMessageContentMask < NetworkMessageContentMask; networkMessageContentMask++)
            {
                uaNetworkMessage.SetNetworkMessageContentMask((JsonNetworkMessageContentMask)networkMessageContentMask);

                // Assert
                CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
            }
        }

        [Test(Description = "Validate NetworkMessageHeader & PublisherId with Variant")]
        public void ValidateMessageHeaderAndPublisherIdWithVariantType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskVariant);

            // Act  
            // Check NetworkMessageHeader & PublisherId 
            uaNetworkMessage.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = "50";
            //uaNetworkMessage.PublisherId = "Test$!#$%^&*87";
            //uaNetworkMessage.PublisherId = "Begrüßung";

            // Assert
            CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
        }

        [Test(Description = "Validate NetworkMessageHeader & PublisherId with DataValue")]
        public void ValidateMessageHeaderAndPublisherIdWithDataValueType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskDataValue);

            // Act  
            // Check NetworkMessageHeader & PublisherId 
            uaNetworkMessage.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.NetworkMessageHeader
                |JsonNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = "70";
            //uaNetworkMessage.PublisherId = "Test$!#$%^&*87";
            //uaNetworkMessage.PublisherId = "Begrüßung";

            // Assert
            CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
        }

        [Test(Description = "Validate NetworkMessageHeader & PublisherId with RawData")]
        public void ValidateMessageHeaderAndPublisherIdWithRawDataType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskDataValue);

            // Act  
            // Check NetworkMessageHeader & PublisherId 
            uaNetworkMessage.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId);
            uaNetworkMessage.PublisherId = "60";
            //uaNetworkMessage.PublisherId = "Test$!#$%^&*87";
            //uaNetworkMessage.PublisherId = "Begrüßung";

            // Assert
            CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
        }

        [Test(Description = "Validate NetworkMessageHeader & DataSetClassId")]
        public void ValidateMessageHeaderAndDataSetClassIdWithVariantType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskVariant);

            // Act  
            // Check NetworkMessageHeader & DataSetClassId 
            uaNetworkMessage.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetClassId);
            uaNetworkMessage.DataSetClassId = Guid.NewGuid().ToString();

            // Assert
            CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
        }

        [Test(Description = "Validate NetworkMessageHeader & DataSetClassId")]
        public void ValidateMessageHeaderAndDataSetClassIdWithDataValueType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskDataValue);

            // Act  
            // Check NetworkMessageHeader & DataSetClassId 
            uaNetworkMessage.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetClassId);
            uaNetworkMessage.DataSetClassId = Guid.NewGuid().ToString();

            // Assert
            CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
        }

        [Test(Description = "Validate NetworkMessageHeader & DataSetClassId")]
        public void ValidateMessageHeaderAndDataSetClassIdWithRawDataType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskRawData);

            // Act           
            JsonNetworkMessageContentMask jsonNetworkMessageContent = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetClassId;
            uaNetworkMessage.SetNetworkMessageContentMask(jsonNetworkMessageContent);
            uaNetworkMessage.DataSetClassId = Guid.NewGuid().ToString();

            // since the networkMessageHeader does not have PublisherId set filter to null
            m_singleDataSetReaders[0].PublisherId = Variant.Null;
            
            // set the same DataSetFieldContentMask as used for encoding
            m_singleDataSetReaders[0].DataSetFieldContentMask = (uint)fieldContentMaskRawData;

            // Assert
            CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
        }

        [Test(Description = "Validate NetworkMessageHeader & SingleDataSetMessage")]
        public void ValidateMessageHeaderAndSingleDataSetMessageWithVariantType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskVariant);

            // Act  
            // Check NetworkMessageHeader & SingleDataSetMessage 
            uaNetworkMessage.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetClassId);
            uaNetworkMessage.SingleDataSetMessage = "true";

            // Assert
            CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
        }

        [Test(Description = "Validate NetworkMessageHeader & SingleDataSetMessage")]
        public void ValidateMessageHeaderAndSingleDataSetMessageWithDataValueType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskDataValue);

            // Act  
            // Check NetworkMessageHeader & SingleDataSetMessage 
            uaNetworkMessage.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetClassId);
            uaNetworkMessage.SingleDataSetMessage = "true";

            // Assert
            CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
        }

        [Test(Description = "Validate NetworkMessageHeader & SingleDataSetMessage")]
        public void ValidateMessageHeaderAndSingleDataSetMessageWithRawDataType()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskRawData);

            // Act  
            // Check NetworkMessageHeader & SingleDataSetMessage 
            uaNetworkMessage.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetClassId);
            uaNetworkMessage.SingleDataSetMessage = "true";

            // Assert
            CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
        }

        [Test(Description = "Validate NetworkMessageHeader & DataSetMessageHeader")]
        public void ValidateNetworkMessageHeaderAndDataSetMessageHeader()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskDataValue);

            // Act  
            // Check NetworkMessageHeader & DataSetMessageHeader 
            uaNetworkMessage.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.NetworkMessageHeader
                |JsonNetworkMessageContentMask.DataSetMessageHeader);

            // Assert
            CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
        }

        [Test(Description = "Validate DataSetMessageHeader")]
        public void ValidateDataSetMessageHeader()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskDataValue);

            // Act  
            // Check SingleDataSetMessage 
            uaNetworkMessage.SetNetworkMessageContentMask(JsonNetworkMessageContentMask.DataSetMessageHeader);

            // since the networkMessageHeader is missing the reader shall not filter by PublisherId
            m_singleDataSetReaders[0].PublisherId = Variant.Null;

            // Assert
            CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
        }

        [Test(Description = "Validate NetworkMessageHeeader & DataSetMessageHeader")]
        public void ValidateNetworkAndDataSetMessageHeader()
        {
            // Arrange
            JsonNetworkMessage uaNetworkMessage = CreateNetworkMessage(fieldContentMaskDataValue);

            // Act  
            // TODO: Check SingleDataSetMessage
            JsonNetworkMessageContentMask jsonNetworkMessageContent = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.DataSetMessageHeader
                | JsonNetworkMessageContentMask.PublisherId;
            uaNetworkMessage.SetNetworkMessageContentMask(jsonNetworkMessageContent);

            // since the networkMessageHeader there and PublisherId is encoded filter by publisher id of the connection
            m_singleDataSetReaders[0].PublisherId = m_publisherSingleDataSetConnection.PublisherId;
            // set the same DataSetFieldContentMask as used for encoding
            m_singleDataSetReaders[0].DataSetFieldContentMask = (uint)fieldContentMaskDataValue;

            // Assert
            CompareEncodeDecode(uaNetworkMessage, m_singleDataSetReaders);
        }

        #region Private methods

        /// <summary>
        /// Load RawData data type into datasets
        /// </summary>
        private void LoadData()
        {
            Assert.IsNotNull(m_singleDataSetPublisherApplication, "m_publisherApplication should not be null");

            #region DataSet Simple
            // DataSet 'Simple' fill with data
            DataValue booleanValue = new DataValue(new Variant(true));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggle", NamespaceIndexSimple), Attributes.Value, booleanValue);
            DataValue scalarInt32XValue = new DataValue(new Variant(100));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int32", NamespaceIndexSimple), Attributes.Value, scalarInt32XValue);
            DataValue scalarInt32YValue = new DataValue(new Variant(50));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int32Fast", NamespaceIndexSimple), Attributes.Value, scalarInt32YValue);
            DataValue dateTimeValue = new DataValue(new Variant(DateTime.UtcNow));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("DateTime", NamespaceIndexSimple), Attributes.Value, dateTimeValue);
            #endregion

            #region DataSet AllTypes
            // DataSet 'AllTypes' fill with data
            DataValue allTypesBooleanValue = new DataValue(new Variant(false));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggle", NamespaceIndexAllTypes), Attributes.Value, allTypesBooleanValue);
            DataValue byteValue = new DataValue(new Variant((byte)10));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("Byte", NamespaceIndexAllTypes), Attributes.Value, byteValue);
            DataValue int16Value = new DataValue(new Variant((short)100));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int16", NamespaceIndexAllTypes), Attributes.Value, int16Value);
            DataValue int32Value = new DataValue(new Variant((int)1000));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int32", NamespaceIndexAllTypes), Attributes.Value, int32Value);
            DataValue sByteValue = new DataValue(new Variant((sbyte)11));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("SByte", NamespaceIndexAllTypes), Attributes.Value, sByteValue);
            DataValue uInt16Value = new DataValue(new Variant((ushort)110));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("UInt16", NamespaceIndexAllTypes), Attributes.Value, uInt16Value);
            DataValue uInt32Value = new DataValue(new Variant((uint)1100));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("UInt32", NamespaceIndexAllTypes), Attributes.Value, uInt32Value);
            DataValue floatValue = new DataValue(new Variant((float)1100.5));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("Float", NamespaceIndexAllTypes), Attributes.Value, floatValue);
            DataValue doubleValue = new DataValue(new Variant((double)1100));
            m_singleDataSetPublisherApplication.DataStore.WritePublishedDataItem(new NodeId("Double", NamespaceIndexAllTypes), Attributes.Value, doubleValue);
            #endregion
        }

        /// <summary>
        /// Get first DataSetReaders from configuration
        /// </summary>
        /// <returns></returns>
        private List<DataSetReaderDataType> GetDataSetReaders(ReaderGroupDataType readerGroupDataType)
        {
            // Read the configured ReaderGroup
            Assert.IsNotNull(readerGroupDataType, "ReaderGroup should not be null");
            Assert.IsNotEmpty(readerGroupDataType.DataSetReaders, "ReaderGroup.DataSetReaders should not be empty");

            return readerGroupDataType.DataSetReaders;
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
            foreach (DataSetWriterDataType dataSetWriter in m_singleDataSetWriterGroup.DataSetWriters)
            {
                // 00 The DataSet fields are encoded as Variant data type
                // The Variant can contain a StatusCode instead of the expected DataType if the status of the field is Bad.
                // The Variant can contain a DataValue with the value and the statusCode if the status of the field is Uncertain.
                dataSetWriter.DataSetFieldContentMask = (uint)dataSetFieldContentMask;
            }

            JsonNetworkMessage uaNetworkMessage = (JsonNetworkMessage)m_singleDataSetPublisherConnection.CreateNetworkMessage(m_singleDataSetWriterGroup);
            Assert.IsNotNull(uaNetworkMessage, "networkMessageEncode should not be null");

            return uaNetworkMessage;
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="jsonNetworkMessage">the message to encode</param>
        /// <param name="dataSetReaders">The list of readers used to decode</param>
        private void CompareEncodeDecode(JsonNetworkMessage jsonNetworkMessage, IList<DataSetReaderDataType> dataSetReaders)
        {
            byte[] bytes = jsonNetworkMessage.Encode();

            JsonNetworkMessage uaNetworkMessageDecoded = new JsonNetworkMessage();
            uaNetworkMessageDecoded.Decode( bytes, dataSetReaders);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            Compare(jsonNetworkMessage, uaNetworkMessageDecoded);
        }

        /// <summary>
        /// Compare network messages options 
        /// </summary>
        /// <param name="jsonNetworkMessageEncode"></param>
        /// <param name="jsonNetworkMessageDecoded"></param>
        /// <returns></returns>
        private void Compare(JsonNetworkMessage jsonNetworkMessageEncode, JsonNetworkMessage jsonNetworkMessageDecoded)
        {
            JsonNetworkMessageContentMask networkMessageContentMask = jsonNetworkMessageEncode.NetworkMessageContentMask;

            //if ((networkMessageContentMask | JsonNetworkMessageContentMask.None) == JsonNetworkMessageContentMask.None)
            //{
            //    // nothing to check
            //    return;
            //}

            // Verify flags
            Assert.AreEqual(jsonNetworkMessageEncode.NetworkMessageContentMask & jsonNetworkMessageDecoded.NetworkMessageContentMask,
                jsonNetworkMessageDecoded.NetworkMessageContentMask, "NetworkMessageContentMask were not decoded correctly");

            #region Network Message Header
            if ((networkMessageContentMask & JsonNetworkMessageContentMask.NetworkMessageHeader) != 0)
            {
                if ((networkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
                {
                    Assert.AreEqual(jsonNetworkMessageEncode.PublisherId, jsonNetworkMessageDecoded.PublisherId, "PublisherId was not decoded correctly");
                }

                if ((networkMessageContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0)
                {
                    Assert.AreEqual(jsonNetworkMessageEncode.DataSetClassId, jsonNetworkMessageDecoded.DataSetClassId, "DataSetClassId was not decoded correctly");
                }
            }
            #endregion

            #region Payload header + Payload data
            List<DataSet> receivedDataSets = jsonNetworkMessageDecoded.ReceivedDataSets;
            
            Assert.IsNotNull(receivedDataSets, "ReceivedDataSets is null");

            // check the number of JsonDataSetMessage counts
            Assert.AreEqual(jsonNetworkMessageEncode.DataSetMessages.Count,
                receivedDataSets.Count, "JsonDataSetMessages.Count was not decoded correctly");

            // check if the encoded match the decoded DataSetWriterId's
            foreach (JsonDataSetMessage jsonDataSetMessage in jsonNetworkMessageEncode.DataSetMessages)
            {
                //// check dataset message headers      
                //JsonDataSetMessage jsonDataSetMessageDecoded =
                //    jsonNetworkMessageDecoded.DataSetMessages.FirstOrDefault(decoded =>
                //        ((JsonDataSetMessage)decoded).DataSetWriterId == jsonDataSetMessage.DataSetWriterId) as JsonDataSetMessage;

                //Assert.IsNotNull(jsonDataSetMessageDecoded, "Decoded message did not found jsonDataSetMessage.DataSetWriterId = {0}", jsonDataSetMessage.DataSetWriterId);

                // check payload data fields count 
                // get related dataset from subscriber DataSets
                DataSet decodedDataSet = receivedDataSets.FirstOrDefault(dataSet => dataSet.Name == jsonDataSetMessage.DataSet.Name);
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
                    //Assert.IsNotNull(fieldEncoded.Value.Value, "jsonDataSetMessage.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                    //   index, jsonDataSetMessage.DataSetWriterId);
                    //Assert.IsNotNull(fieldDecoded.Value.Value, "jsonDataSetMessageDecoded.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                    //  index, jsonDataSetMessage.DataSetWriterId);

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
            #endregion
        }
        #endregion
    }
}
