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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub.Tests
{
    [TestFixture(Description = "Tests for Encoding/Decoding of UadpNetworkMessage objects using mqtt")]
    public class MqttUadpNetworkMessageTests
    {
        private const UInt16 NamespaceIndexAllTypes = 3;

        private const string MqttAddressUrl = "mqtt://localhost:1883";

        [Test(Description = "Validate NetworkMessageHeader & PublisherId with PublisherId as parameter")]
        public void ValidateMessageHeaderAndPublisherIdWithParameters(
           [Values(DataSetFieldContentMask.None, DataSetFieldContentMask.RawData,
            DataSetFieldContentMask.ServerPicoSeconds, DataSetFieldContentMask.ServerTimestamp, DataSetFieldContentMask.SourcePicoSeconds,
            DataSetFieldContentMask.SourceTimestamp, DataSetFieldContentMask.StatusCode,
            DataSetFieldContentMask.ServerPicoSeconds| DataSetFieldContentMask.ServerTimestamp| DataSetFieldContentMask.SourcePicoSeconds| DataSetFieldContentMask.SourceTimestamp| DataSetFieldContentMask.StatusCode)]
                DataSetFieldContentMask dataSetFieldContentMask,
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            // Arrange
            UadpNetworkMessageContentMask uadpNetworkMessageContentMask =  UadpNetworkMessageContentMask.PublisherId;

            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.SequenceNumber;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            //LoadData(publisherApplication);

            IUaPubSubConnection connection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(connection, "Pubsub first connection should not be null");

            // Act  
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = connection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            // set PublisherId
            uaNetworkMessage.PublisherId = publisherId;

            //bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.DataSetMessageHeader) != 0
            //     && (uadpDataSetMessageContentMask & UadpDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                MqttAddressUrl, publisherId: publisherId, writerGroupId: 1, setDataSetWriterId: true,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");
            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");

            // Assert
            CompareEncodeDecode(uaNetworkMessage, dataSetReaders);
        }

        /// <summary>
        /// Compare encoded/decoded network messages
        /// </summary>
        /// <param name="uadpNetworkMessageEncode"></param>
        /// <param name="uadpNetworkMessageDecoded"></param>
        private void CompareEncodeDecode(UadpNetworkMessage uadpNetworkMessage, IList<DataSetReaderDataType> dataSetReaders)
        {
            byte[] bytes = uadpNetworkMessage.Encode();

            UadpNetworkMessage uaNetworkMessageDecoded = new UadpNetworkMessage();
            uaNetworkMessageDecoded.Decode(bytes, dataSetReaders);

            // compare uaNetworkMessage with uaNetworkMessageDecoded
            // TODO Fix: this might be broken after refactor
            Compare(uadpNetworkMessage, uaNetworkMessageDecoded, uaNetworkMessageDecoded.ReceivedDataSets);
        }

        /// <summary>
        /// Compare network messages options 
        /// </summary>
        /// <param name="uadpNetworkMessageEncode"></param>
        /// <param name="uadpNetworkMessageDecoded"></param>
        /// <returns></returns>
        private void Compare(UadpNetworkMessage uadpNetworkMessageEncode, UadpNetworkMessage uadpNetworkMessageDecoded, List<DataSet> subscribedDataSets)
        {
            UadpNetworkMessageContentMask networkMessageContentMask = uadpNetworkMessageEncode.NetworkMessageContentMask;

            if ((networkMessageContentMask | UadpNetworkMessageContentMask.None) == UadpNetworkMessageContentMask.None)
            {
                //nothing to check
                return;
            }

            // Verify flags
            Assert.AreEqual(uadpNetworkMessageEncode.UADPFlags, uadpNetworkMessageDecoded.UADPFlags, "UADPFlags were not decoded correctly");

            #region Network Message Header
            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PublisherId) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.PublisherId, uadpNetworkMessageDecoded.PublisherId, "PublisherId was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.DataSetClassId) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.DataSetClassId, uadpNetworkMessageDecoded.DataSetClassId, "DataSetClassId was not decoded correctly");
            }
            #endregion

            #region Group Message Header
            if ((networkMessageContentMask & (UadpNetworkMessageContentMask.GroupHeader |
                                              UadpNetworkMessageContentMask.WriterGroupId |
                                              UadpNetworkMessageContentMask.GroupVersion |
                                              UadpNetworkMessageContentMask.NetworkMessageNumber |
                                              UadpNetworkMessageContentMask.SequenceNumber)) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.GroupFlags, uadpNetworkMessageDecoded.GroupFlags, "GroupFlags was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.WriterGroupId) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.WriterGroupId, uadpNetworkMessageDecoded.WriterGroupId, "WriterGroupId was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.GroupVersion) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.GroupVersion, uadpNetworkMessageDecoded.GroupVersion, "GroupVersion was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.NetworkMessageNumber) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.NetworkMessageNumber, uadpNetworkMessageDecoded.NetworkMessageNumber, "NetworkMessageNumber was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.SequenceNumber) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.SequenceNumber, uadpNetworkMessageDecoded.SequenceNumber, "SequenceNumber was not decoded correctly");
            }
            #endregion

            #region Payload header + Payload data

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                // check the number of UadpDataSetMessage counts
                Assert.AreEqual(uadpNetworkMessageEncode.DataSetMessages.Count,
                    uadpNetworkMessageDecoded.DataSetMessages.Count, "UadpDataSetMessages.Count was not decoded correctly");

                Assert.IsNotNull(subscribedDataSets, "SubscribedDataSets is null");

                // check if the encoded match the decoded DataSetWriterId's
                foreach (UadpDataSetMessage uadpDataSetMessage in uadpNetworkMessageEncode.DataSetMessages)
                {
                    UadpDataSetMessage uadpDataSetMessageDecoded =
                        uadpNetworkMessageDecoded.DataSetMessages.FirstOrDefault(decoded =>
                            ((UadpDataSetMessage)decoded).DataSetWriterId == uadpDataSetMessage.DataSetWriterId) as UadpDataSetMessage;

                    Assert.IsNotNull(uadpDataSetMessageDecoded, "Decoded message did not found uadpDataSetMessage.DataSetWriterId = {0}", uadpDataSetMessage.DataSetWriterId);

                    // check payload data size in bytes
                    Assert.AreEqual(uadpDataSetMessage.PayloadSizeInStream, uadpDataSetMessageDecoded.PayloadSizeInStream,
                        "PayloadSizeInStream was not decoded correctly, DataSetWriterId = {0}", uadpDataSetMessage.DataSetWriterId);

                    // check payload data fields count 
                    // get related dataset from subscriber DataSets
                    DataSet decodedDataSet = subscribedDataSets.FirstOrDefault(dataSet => dataSet.Name == uadpDataSetMessage.DataSet.Name);
                    Assert.IsNotNull(decodedDataSet, "DataSet '{0}' is missing from subscriber datasets!", uadpDataSetMessage.DataSet.Name);

                    Assert.AreEqual(uadpDataSetMessage.DataSet.Fields.Length, decodedDataSet.Fields.Length,
                        "DataSet.Fields.Length was not decoded correctly, DataSetWriterId = {0}", uadpDataSetMessage.DataSetWriterId);

                    // check the fields data consistency
                    // at this time the DataSetField has just value!?
                    for (int index = 0; index < uadpDataSetMessage.DataSet.Fields.Length; index++)
                    {
                        Field fieldEncoded = uadpDataSetMessage.DataSet.Fields[index];
                        Field fieldDecoded = decodedDataSet.Fields[index];
                        Assert.IsNotNull(fieldEncoded, "uadpDataSetMessage.DataSet.Fields[{0}] is null,  DataSetWriterId = {1}",
                            index, uadpDataSetMessage.DataSetWriterId);
                        Assert.IsNotNull(fieldDecoded, "uadpDataSetMessageDecoded.DataSet.Fields[{0}] is null,  DataSetWriterId = {1}",
                            index, uadpDataSetMessage.DataSetWriterId);

                        DataValue dataValueEncoded = fieldEncoded.Value;
                        DataValue dataValueDecoded = fieldDecoded.Value;
                        Assert.IsNotNull(fieldEncoded.Value, "uadpDataSetMessage.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                           index, uadpDataSetMessage.DataSetWriterId);
                        Assert.IsNotNull(fieldDecoded.Value, "uadpDataSetMessageDecoded.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                          index, uadpDataSetMessage.DataSetWriterId);

                        // check dataValues values
                        Assert.IsNotNull(fieldEncoded.Value.Value, "uadpDataSetMessage.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                           index, uadpDataSetMessage.DataSetWriterId);
                        Assert.IsNotNull(fieldDecoded.Value.Value, "uadpDataSetMessageDecoded.DataSet.Fields[{0}].Value is null,  DataSetWriterId = {1}",
                          index, uadpDataSetMessage.DataSetWriterId);

                        Assert.AreEqual(dataValueEncoded.Value, dataValueDecoded.Value, "Wrong: Fields[{0}].DataValue.Value; DataSetWriterId = {1}", index, uadpDataSetMessage.DataSetWriterId);

                        // Checks just for DataValue type only 
                        if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.StatusCode) ==
                            DataSetFieldContentMask.StatusCode)
                        {
                            // check dataValues StatusCode
                            Assert.AreEqual(dataValueEncoded.StatusCode, dataValueDecoded.StatusCode,
                                "Wrong: Fields[{0}].DataValue.StatusCode; DataSetWriterId = {1}", index, uadpDataSetMessage.DataSetWriterId);
                        }

                        // check dataValues SourceTimestamp
                        if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.SourceTimestamp) ==
                            DataSetFieldContentMask.SourceTimestamp)
                        {
                            Assert.AreEqual(dataValueEncoded.SourceTimestamp, dataValueDecoded.SourceTimestamp,
                                "Wrong: Fields[{0}].DataValue.SourceTimestamp; DataSetWriterId = {1}", index, uadpDataSetMessage.DataSetWriterId);
                        }

                        // check dataValues ServerTimestamp
                        if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.ServerTimestamp) ==
                            DataSetFieldContentMask.ServerTimestamp)
                        {
                            // check dataValues ServerTimestamp
                            Assert.AreEqual(dataValueEncoded.ServerTimestamp, dataValueDecoded.ServerTimestamp,
                               "Wrong: Fields[{0}].DataValue.ServerTimestamp; DataSetWriterId = {1}", index, uadpDataSetMessage.DataSetWriterId);
                        }

                        // check dataValues SourcePicoseconds
                        if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.SourcePicoSeconds) ==
                            DataSetFieldContentMask.SourcePicoSeconds)
                        {
                            Assert.AreEqual(dataValueEncoded.SourcePicoseconds, dataValueDecoded.SourcePicoseconds,
                               "Wrong: Fields[{0}].DataValue.SourcePicoseconds; DataSetWriterId = {1}", index, uadpDataSetMessage.DataSetWriterId);
                        }

                        // check dataValues ServerPicoSeconds
                        if ((uadpDataSetMessage.FieldContentMask & DataSetFieldContentMask.ServerPicoSeconds) ==
                            DataSetFieldContentMask.ServerPicoSeconds)
                        {
                            // check dataValues ServerPicoseconds
                            Assert.AreEqual(dataValueEncoded.ServerPicoseconds, dataValueDecoded.ServerPicoseconds,
                               "Wrong: Fields[{0}].DataValue.ServerPicoseconds; DataSetWriterId = {1}", index, uadpDataSetMessage.DataSetWriterId);
                        }
                    }
                }
            }
            #endregion

            #region Extended network message header
            if ((networkMessageContentMask & UadpNetworkMessageContentMask.Timestamp) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.Timestamp, uadpNetworkMessageDecoded.Timestamp, "Timestamp was not decoded correctly");
            }

            if ((networkMessageContentMask & UadpNetworkMessageContentMask.PicoSeconds) != 0)
            {
                Assert.AreEqual(uadpNetworkMessageEncode.PicoSeconds, uadpNetworkMessageDecoded.PicoSeconds, "PicoSeconds was not decoded correctly");
            }

            #endregion
        }
    }
}
