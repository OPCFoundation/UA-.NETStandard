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

using NUnit.Framework;
using Opc.Ua.PubSub.PublishedData;
using System;
using System.Linq;
using System.IO;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture(Description = "Tests for Encoding/Decoding of UadpDataSeMessage objects")]
    public class UadpDataSetMessageTests
    {
        private string PublisherConfigurationFileName = Path.Combine("Configuration", "PublisherConfiguration.xml");
        private string SubscriberConfigurationFileName = Path.Combine("Configuration", "SubscriberConfiguration.xml");

        private PubSubConfigurationDataType m_publisherConfiguration;
        private UaPubSubApplication m_publisherApplication;
        private WriterGroupDataType m_firstWriterGroup;
        private IUaPubSubConnection m_firstPublisherConnection;

        private PubSubConfigurationDataType m_subscriberConfiguration;
        private UaPubSubApplication m_subscriberApplication;
        private ReaderGroupDataType m_firstReaderGroup;
        private DataSetReaderDataType m_firstDataSetReaderType;
        
        private const ushort NamespaceIndexSimple = 2;

        /// <summary>
        /// just for test match the DataSet1->DataSetWriterId
        /// </summary>
        private const ushort TestDataSetWriterId = 1;
        private const ushort MessageContentMask = 0x3f;

        [OneTimeSetUp()]
        public void MyTestInitialize()
        {
            // Create a publisher application
            // todo refactor to use the MessagesHelper create configuration
            string publisherConfigurationFile = Utils.GetAbsoluteFilePath(PublisherConfigurationFileName, true, true, false);
            m_publisherApplication = UaPubSubApplication.Create(publisherConfigurationFile);
            Assert.IsNotNull(m_publisherApplication, "m_publisherApplication should not be null");

            // Get the publisher configuration
            m_publisherConfiguration = m_publisherApplication.UaPubSubConfigurator.PubSubConfiguration;
            Assert.IsNotNull(m_publisherConfiguration, "m_publisherConfiguration should not be null");

            // Get first connection
            Assert.IsNotNull(m_publisherConfiguration.Connections, "m_publisherConfiguration.Connections should not be null");
            Assert.IsNotEmpty(m_publisherConfiguration.Connections, "m_publisherConfiguration.Connections should not be empty");
            m_firstPublisherConnection = m_publisherApplication.PubSubConnections[0];
            Assert.IsNotNull(m_firstPublisherConnection, "m_firstPublisherConnection should not be null");
            
            // Read the first writer group
            Assert.IsNotEmpty(m_publisherConfiguration.Connections[0].WriterGroups, "pubSubConfigConnection.WriterGroups should not be empty");
            m_firstWriterGroup = m_publisherConfiguration.Connections[0].WriterGroups[0];
            Assert.IsNotNull(m_firstWriterGroup, "m_firstWriterGroup should not be null");

            Assert.IsNotNull(m_publisherConfiguration.PublishedDataSets, "m_publisherConfiguration.PublishedDataSets should not be null");
            Assert.IsNotEmpty(m_publisherConfiguration.PublishedDataSets, "m_publisherConfiguration.PublishedDataSets should not be empty");

            // Create a subscriber application
            string subscriberConfigurationFile = Utils.GetAbsoluteFilePath(SubscriberConfigurationFileName, true, true, false);
            m_subscriberApplication = UaPubSubApplication.Create(subscriberConfigurationFile);
            Assert.IsNotNull(m_subscriberApplication, "m_subscriberApplication should not be null");

            // Get the subscriber configuration
            m_subscriberConfiguration = m_subscriberApplication.UaPubSubConfigurator.PubSubConfiguration;
            Assert.IsNotNull(m_subscriberConfiguration, "m_subscriberConfiguration should not be null");

            // Read the first reader group
            m_firstReaderGroup = m_subscriberConfiguration.Connections[0].ReaderGroups[0];
            Assert.IsNotNull(m_firstWriterGroup, "m_firstReaderGroup should not be null");

            m_firstDataSetReaderType = GetFirstDataSetReader();
        }

        [Test(Description = "Validate dataset message mask with Variant data type;" +
                            "Change the Uadp dataset message mask into the [0,63] range that covers all options(properties)")]
        public void ValidateDataSetMessageMask(
            [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode
            )]
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act  
            // change network message mask
            for (uint dataSetMessageContentMask = 0; dataSetMessageContentMask < MessageContentMask; dataSetMessageContentMask++)
            {
                uadpDataSetMessage.SetMessageContentMask((UadpDataSetMessageContentMask)dataSetMessageContentMask);

                // Assert
                CompareEncodeDecode(uadpDataSetMessage);
            }
        }

        [Test(Description = "Validate TimeStamp")]
        public void ValidateDataSetTimeStamp(
            [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode
            )]
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act  
            uadpDataSetMessage.SetMessageContentMask(UadpDataSetMessageContentMask.Timestamp);
            uadpDataSetMessage.Timestamp = DateTime.UtcNow;

            // Assert
            CompareEncodeDecode(uadpDataSetMessage);
        }

        [Test(Description = "Validate PicoSeconds")]
        public void ValidatePicoSeconds(
            [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode
            )]
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act  
            uadpDataSetMessage.SetMessageContentMask(UadpDataSetMessageContentMask.PicoSeconds);
            uadpDataSetMessage.PicoSeconds = 10;

            // Assert
            CompareEncodeDecode(uadpDataSetMessage);
        }

        [Test(Description = "Validate Status")]
        public void ValidateStatus(
            [Values(UadpDataSetMessageContentMask.None, UadpDataSetMessageContentMask.Timestamp,
                UadpDataSetMessageContentMask.MajorVersion, UadpDataSetMessageContentMask.MinorVersion,
                UadpDataSetMessageContentMask.SequenceNumber,
                UadpDataSetMessageContentMask.MajorVersion | UadpDataSetMessageContentMask.MinorVersion,
                UadpDataSetMessageContentMask.MajorVersion | UadpDataSetMessageContentMask.MinorVersion | UadpDataSetMessageContentMask.SequenceNumber)]
            UadpDataSetMessageContentMask messageContentMask,
            [Values(StatusCodes.Good, StatusCodes.UncertainDataSubNormal, StatusCodes.BadAggregateListMismatch, StatusCodes.BadUnknownResponse,
            StatusCodes.Bad, StatusCodes.BadAggregateConfigurationRejected, StatusCodes.BadAggregateInvalidInputs, StatusCodes.BadAlreadyExists)]
            uint statusCode
            )
        {
            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(DataSetFieldContentMask.None);

            // Act  
            uadpDataSetMessage.SetMessageContentMask(messageContentMask | UadpDataSetMessageContentMask.Status);
            uadpDataSetMessage.Status = statusCode;

            // Assert
            CompareEncodeDecode(uadpDataSetMessage);
        }

        [Test(Description = "Validate MajorVersion")]
        public void ValidateMajorVersion(
            [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode
            )]
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act  
            uadpDataSetMessage.SetMessageContentMask(UadpDataSetMessageContentMask.MajorVersion);
            uadpDataSetMessage.MetaDataVersion.MajorVersion = 2;

            // Assert
            CompareEncodeDecode(uadpDataSetMessage);
        }

        [Test(Description = "Validate MinorVersion")]
        public void ValidateMinorVersion(
            [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode
            )]
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act  
            uadpDataSetMessage.SetMessageContentMask(UadpDataSetMessageContentMask.MinorVersion);
            uadpDataSetMessage.MetaDataVersion.MinorVersion = 101;

            // Assert
            CompareEncodeDecode(uadpDataSetMessage);
        }

        [Test(Description = "Validate SequenceNumber")]
        public void ValidateSequenceNumber(
            [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData, // list here all possible DataSetFieldContentMask
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode
            )]
            DataSetFieldContentMask dataSetFieldContentMask)
        {
            // Arrange
            UadpDataSetMessage uadpDataSetMessage = GetFirstDataSetMessage(dataSetFieldContentMask);

            // Act  
            uadpDataSetMessage.SetMessageContentMask(UadpDataSetMessageContentMask.SequenceNumber);
            uadpDataSetMessage.SequenceNumber = 1000;

            // Assert
            CompareEncodeDecode(uadpDataSetMessage);
        }

        #region Private Methods        

        /// <summary>
        /// Load Variant data type into datasets
        /// </summary>
        private void LoadData()
        {
            Assert.IsNotNull(m_publisherApplication, "m_publisherApplication should not be null");

            #region DataSet Simple
            // DataSet 'Simple' fill with data
            DataValue booleanValue = new DataValue(new Variant(true), StatusCodes.Good);
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("BoolToggle", NamespaceIndexSimple), Attributes.Value, booleanValue);
            DataValue scalarInt32XValue = new DataValue(new Variant(100), StatusCodes.Good);
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int32", NamespaceIndexSimple), Attributes.Value, scalarInt32XValue);
            DataValue scalarInt32YValue = new DataValue(new Variant(50), StatusCodes.Good);
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("Int32Fast", NamespaceIndexSimple), Attributes.Value, scalarInt32YValue);
            DataValue dateTimeValue = new DataValue(new Variant(DateTime.UtcNow), StatusCodes.Good);
            m_publisherApplication.DataStore.WritePublishedDataItem(new NodeId("DateTime", NamespaceIndexSimple), Attributes.Value, dateTimeValue);
            #endregion
        }

        /// <summary>
        /// Get first DataSetReaders from configuration
        /// </summary>
        /// <returns></returns>
        private DataSetReaderDataType GetFirstDataSetReader()
        {
            // Read the first configured ReaderGroup
            Assert.IsNotNull(m_firstReaderGroup, "m_firstReaderGroup should not be null");
            Assert.IsNotEmpty(m_firstReaderGroup.DataSetReaders, "m_firstReaderGroup.DataSetReaders should not be empty");
            Assert.IsNotNull(m_firstReaderGroup.DataSetReaders[0], "m_firstReaderGroup.DataSetReaders[0] should not be null");

            return m_firstReaderGroup.DataSetReaders[0];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fieldContentMask"> a DataSetFieldContentMask specifying what type of encoding is chosen for field values
        /// If none of the flags are set, the fields are represented as Variant.
        /// If the RawData flag is set, the fields are represented as RawData and all other bits are ignored.
        /// If one of the bits StatusCode, SourceTimestamp, ServerTimestamp, SourcePicoSeconds, ServerPicoSeconds is set, 
        ///    the fields are represented as DataValue.
        /// </param>
        /// <returns></returns>
        private UadpDataSetMessage GetFirstDataSetMessage(DataSetFieldContentMask fieldContentMask)
        {
            LoadData();

            // set the configurable field content mask to allow only Variant data type
            foreach (DataSetWriterDataType dataSetWriter in m_firstWriterGroup.DataSetWriters)
            {
                // 00 The DataSet fields are encoded as Variant data type
                // The Variant can contain a StatusCode instead of the expected DataType if the status of the field is Bad.
                // The Variant can contain a DataValue with the value and the statusCode if the status of the field is Uncertain.
                dataSetWriter.DataSetFieldContentMask = (uint)fieldContentMask;
            }

            UadpNetworkMessage uaNetworkMessage = (UadpNetworkMessage)m_firstPublisherConnection.CreateNetworkMessage(m_firstWriterGroup);
            Assert.IsNotNull(uaNetworkMessage, "networkMessageEncode should not be null");

            // read first dataset message
            UaDataSetMessage[] uadpDataSetMessages = uaNetworkMessage.DataSetMessages.ToArray();
            Assert.IsNotEmpty(uadpDataSetMessages, "uadpDataSetMessages collection should not be empty");

            UaDataSetMessage uadpDataSetMessage = uadpDataSetMessages[0];
            Assert.IsNotNull(uadpDataSetMessage, "uadpDataSetMessage should not be null");

            return uadpDataSetMessage as UadpDataSetMessage;
        }

        /// <summary>
        /// Compare encoded/decoded dataset messages
        /// </summary>
        /// <param name="uadpDataSetMessage"></param>
        /// <returns></returns>
        private void CompareEncodeDecode(UadpDataSetMessage uadpDataSetMessage)
        {
            ServiceMessageContext messageContextEncode = new ServiceMessageContext();
            BinaryEncoder encoder = new BinaryEncoder(messageContextEncode);
            uadpDataSetMessage.Encode(encoder);
            byte[] bytes = ReadBytes(encoder.BaseStream);
            encoder.Dispose();

            UadpDataSetMessage uaDataSetMessageDecoded = new UadpDataSetMessage();
            BinaryDecoder decoder = new BinaryDecoder(bytes, messageContextEncode);

            // workaround
            uaDataSetMessageDecoded.DataSetWriterId = TestDataSetWriterId;
            uaDataSetMessageDecoded.DecodePossibleDataSetReader(decoder, m_firstDataSetReaderType);
            decoder.Dispose();

            // compare uadpDataSetMessage with uaDataSetMessageDecoded
            CompareUadpDataSetMessages(uadpDataSetMessage, uaDataSetMessageDecoded);
        }


        /// <summary>
        /// Compare dataset messages options 
        /// </summary>
        /// <param name="uadpDataSetMessageEncode"></param>
        /// <param name="uadpDataSetMessageDecoded"></param>
        /// <returns></returns>
        private void CompareUadpDataSetMessages(UadpDataSetMessage uadpDataSetMessageEncode, UadpDataSetMessage uadpDataSetMessageDecoded)
        {
            DataSet dataSetDecoded = uadpDataSetMessageDecoded.DataSet;
            UadpDataSetMessageContentMask dataSetMessageContentMask = uadpDataSetMessageEncode.DataSetMessageContentMask;

            Assert.AreEqual(uadpDataSetMessageEncode.DataSetFlags1, uadpDataSetMessageDecoded.DataSetFlags1,
                    "DataSetMessages DataSetFlags1 do not match:");
            Assert.AreEqual(uadpDataSetMessageEncode.DataSetFlags2, uadpDataSetMessageDecoded.DataSetFlags2,
                   "DataSetMessages DataSetFlags2 do not match:");
            
            if ((dataSetMessageContentMask & UadpDataSetMessageContentMask.Timestamp) ==
                UadpDataSetMessageContentMask.Timestamp)
            {
                Assert.AreEqual(uadpDataSetMessageEncode.Timestamp, uadpDataSetMessageDecoded.Timestamp,
                    "DataSetMessages TimeStamp do not match:");
            }

            if ((dataSetMessageContentMask & UadpDataSetMessageContentMask.PicoSeconds) ==
                UadpDataSetMessageContentMask.PicoSeconds)
            {
                Assert.AreEqual(uadpDataSetMessageEncode.PicoSeconds, uadpDataSetMessageDecoded.PicoSeconds,
                    "DataSetMessages PicoSeconds do not match:");
            }

            if ((dataSetMessageContentMask & UadpDataSetMessageContentMask.Status) ==
                UadpDataSetMessageContentMask.Status)
            {
                Assert.AreEqual(uadpDataSetMessageEncode.Status, uadpDataSetMessageDecoded.Status,
                    "DataSetMessages Status do not match:");
            }

            if ((dataSetMessageContentMask & UadpDataSetMessageContentMask.MajorVersion) ==
                UadpDataSetMessageContentMask.MajorVersion)
            {
                Assert.AreEqual(uadpDataSetMessageEncode.MetaDataVersion.MajorVersion, uadpDataSetMessageDecoded.MetaDataVersion.MajorVersion,
                    "DataSetMessages ConfigurationMajorVersion do not match:");
            }

            if ((dataSetMessageContentMask & UadpDataSetMessageContentMask.MinorVersion) ==
                UadpDataSetMessageContentMask.MinorVersion)
            {
                Assert.AreEqual(uadpDataSetMessageEncode.MetaDataVersion.MinorVersion, uadpDataSetMessageDecoded.MetaDataVersion.MinorVersion,
                    "DataSetMessages ConfigurationMajorVersion do not match:");
            }

            // check also the payload data
            Assert.AreEqual(uadpDataSetMessageEncode.DataSet.Fields.Length, dataSetDecoded.Fields.Length,
                "DataSetMessages DataSet fields size do not match:");

            for (int index = 0; index < uadpDataSetMessageEncode.DataSet.Fields.Length; index++)
            {
                Field dataSetFieldEncoded = uadpDataSetMessageEncode.DataSet.Fields[index];
                Field dataSetFieldDecoded = dataSetDecoded.Fields[index];

                Assert.IsNotNull(dataSetFieldEncoded.Value, "DataSetFieldEncoded.Value is null");
                Assert.IsNotNull(dataSetFieldDecoded.Value, "DataSetFieldDecoded.Value is null");
                object encodedValue = dataSetFieldEncoded.Value.Value;
                object decodedValue = dataSetFieldDecoded.Value.Value;

                Assert.AreEqual(encodedValue, decodedValue,
                    "DataSetMessages Field.Value does not match value field at position: {0} {1}|{2}", index, encodedValue, decodedValue);
            }
        }

        /// <summary>
        /// Read All bytes from a given stream
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        private byte[] ReadBytes(Stream stream)
        {
            stream.Position = 0;
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
        #endregion
    }
}
