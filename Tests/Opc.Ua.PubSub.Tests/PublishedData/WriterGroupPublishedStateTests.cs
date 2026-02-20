/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.PubSub.PublishedData;
using Opc.Ua.PubSub.Tests.Encoding;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using PubSubEncoding = Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Tests.PublishedData
{
    public class WriterGroupPublishedStateTests
    {
        /// <summary>
        /// PubSub message type mapping
        /// </summary>
        public enum PubSubMessageType
        {
            Uadp,
            Json
        }

        private const ushort kNamespaceIndexAllTypes = 3;

        [Test(
            Description = "Publish Uadp | Json DataSetMessages with KeyFrameCount and delta frames")]
        public void PublishDataSetMessages(
            [Values(
                PubSubMessageType.Uadp,
                PubSubMessageType.Json)] PubSubMessageType pubSubMessageType,
            [Values(1, 2, 3, 4)] int keyFrameCount)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            //Arrange
            Variant publisherId = 1;
            const ushort writerGroupId = 1;

            const string addressUrl = "http://localhost:1883";

            const DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            var dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData2("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = null;

            if (pubSubMessageType == PubSubMessageType.Uadp)
            {
                const UadpNetworkMessageContentMask uadpNetworkMessageContentMask =
                    UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader;
                const UadpDataSetMessageContentMask uadpDataSetMessageContentMask
                    = UadpDataSetMessageContentMask.None;

                publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                    Profiles.PubSubMqttUadpTransport,
                    addressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                    uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes,
                    keyFrameCount: Convert.ToUInt32(keyFrameCount));
            }

            if (pubSubMessageType == PubSubMessageType.Json)
            {
                const JsonNetworkMessageContentMask jsonNetworkMessageContentMask =
                    JsonNetworkMessageContentMask.NetworkMessageHeader |
                    JsonNetworkMessageContentMask.PublisherId |
                    JsonNetworkMessageContentMask.DataSetMessageHeader;
                const JsonDataSetMessageContentMask jsonDataSetMessageContentMask =
                    JsonDataSetMessageContentMask.DataSetWriterId;

                publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                    Profiles.PubSubMqttJsonTransport,
                    addressUrl,
                    publisherId: publisherId,
                    writerGroupId: writerGroupId,
                    jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                    jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                    dataSetFieldContentMask: dataSetFieldContentMask,
                    dataSetMetaDataArray: dataSetMetaDataArray,
                    nameSpaceIndexForData: kNamespaceIndexAllTypes,
                    keyFrameCount: Convert.ToUInt32(keyFrameCount));
            }

            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Create publisher application for multiple datasets
            var publisherApplication = UaPubSubApplication.Create(publisherConfiguration, telemetry);
            MessagesHelper.LoadData(publisherApplication, kNamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections[0];
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            Assert.IsNotNull(
                publisherConfiguration.Connections[0],
                "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(
                publisherConfiguration.Connections[0].WriterGroups[0],
                "publisherConfiguration first writer group of first connection should not be null");

            var writerGroupPublishState = new WriterGroupPublishState();
            IList<UaNetworkMessage> networkMessages = publisherConnection.CreateNetworkMessages(
                publisherConfiguration.Connections[0].WriterGroups[0],
                writerGroupPublishState);
            Assert.IsNotNull(
                networkMessages,
                "connection.CreateNetworkMessages shall not return null");
            Assert.GreaterOrEqual(
                networkMessages.Count,
                1,
                "connection.CreateNetworkMessages shall have at least one network message");

            List<UaNetworkMessage> uaNetworkMessages = null;

            object uaNetworkMessagesList;
            if (pubSubMessageType == PubSubMessageType.Uadp)
            {
                uaNetworkMessagesList = MessagesHelper.GetUaDataNetworkMessages(
                    networkMessages.Cast<PubSubEncoding.UadpNetworkMessage>().ToList());
                Assert.IsNotNull(uaNetworkMessagesList, "uaNetworkMessagesList should not be null");
                uaNetworkMessages =
                [
                    .. (IEnumerable<UaNetworkMessage>)uaNetworkMessagesList
                ];
            }
            if (pubSubMessageType == PubSubMessageType.Json)
            {
                uaNetworkMessagesList = MessagesHelper.GetUaDataNetworkMessages(
                    networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>().ToList());
                uaNetworkMessages =
                [
                    .. (IEnumerable<UaNetworkMessage>)uaNetworkMessagesList
                ];
            }
            Assert.IsNotNull(
                uaNetworkMessages,
                "uaNetworkMessages should not be null. Data entry is missing from configuration!?");

            // get datastore data
            var dataStoreData = new Dictionary<NodeId, DataValue>();
            foreach (UaNetworkMessage uaDataNetworkMessage in uaNetworkMessages)
            {
                Dictionary<NodeId, DataValue> dataSetsData = MessagesHelper.GetDataStoreData(
                    publisherApplication,
                    uaDataNetworkMessage,
                    kNamespaceIndexAllTypes);
                foreach (NodeId nodeId in dataSetsData.Keys)
                {
                    if (!dataStoreData.ContainsKey(nodeId))
                    {
                        dataStoreData.Add(nodeId, dataSetsData[nodeId]);
                    }
                }
            }
            Assert.IsNotEmpty(dataStoreData, "datastore entries should be greater than 0");

            // check if received data is valid
            foreach (UaNetworkMessage uaDataNetworkMessage in uaNetworkMessages)
            {
                ValidateDataSetMessageData(uaDataNetworkMessage, dataStoreData);
            }

            for (int keyCount = 0; keyCount < keyFrameCount - 1; keyCount++)
            {
                // change the values and get one more time the dataset(s) data
                MessagesHelper.UpdateSnapshotData(publisherApplication, kNamespaceIndexAllTypes);
                networkMessages = publisherConnection.CreateNetworkMessages(
                    publisherConfiguration.Connections[0].WriterGroups[0],
                    writerGroupPublishState);
                Assert.IsNotNull(
                    networkMessages,
                    "connection.CreateNetworkMessages shall not be null");
                Assert.GreaterOrEqual(
                    networkMessages.Count,
                    1,
                    "connection.CreateNetworkMessages should have at least one network message");

                if (pubSubMessageType == PubSubMessageType.Uadp)
                {
                    uaNetworkMessagesList = MessagesHelper.GetUaDataNetworkMessages(
                        networkMessages.Cast<PubSubEncoding.UadpNetworkMessage>().ToList());
                    Assert.IsNotNull(
                        uaNetworkMessagesList,
                        "uaNetworkMessagesList shall not be null");
                    uaNetworkMessages =
                    [
                        .. (IEnumerable<UaNetworkMessage>)uaNetworkMessagesList
                    ];
                }
                if (pubSubMessageType == PubSubMessageType.Json)
                {
                    uaNetworkMessagesList = MessagesHelper.GetUaDataNetworkMessages(
                        networkMessages.Cast<PubSubEncoding.JsonNetworkMessage>().ToList());
                    uaNetworkMessages =
                    [
                        .. (IEnumerable<UaNetworkMessage>)uaNetworkMessagesList
                    ];
                }
                Assert.IsNotNull(
                    uaNetworkMessages,
                    "uaNetworkMessages should not be null. Data entry is missing from configuration!?");

                // check if delta received data is valid
                Dictionary<NodeId, DataValue> snapshotData = MessagesHelper.GetSnapshotData(
                    publisherApplication,
                    kNamespaceIndexAllTypes);
                foreach (UaNetworkMessage uaDataNetworkMessage in uaNetworkMessages)
                {
                    ValidateDataSetMessageData(
                        uaDataNetworkMessage,
                        keyFrameCount == 1 ? dataStoreData : snapshotData,
                        keyFrameCount,
                        writerGroupPublishState);
                }
            }

            // check one more time if delta received data is valid
            Dictionary<NodeId, DataValue> snapshotDataCopy = MessagesHelper.GetSnapshotData(
                publisherApplication,
                kNamespaceIndexAllTypes);
            foreach (UaNetworkMessage uaDataNetworkMessage in uaNetworkMessages)
            {
                ValidateDataSetMessageData(
                    uaDataNetworkMessage,
                    keyFrameCount == 1 ? dataStoreData : snapshotDataCopy,
                    keyFrameCount,
                    writerGroupPublishState);
            }
        }

        /// <summary>
        /// Validate dataset message data
        /// </summary>
        private static void ValidateDataSetMessageData(
            UaNetworkMessage uaDataNetworkMessage,
            Dictionary<NodeId, DataValue> dataStoreData,
            int keyFrameCount = 1,
            WriterGroupPublishState writerGroupPublishState = null)
        {
            IEnumerable<object> writerGroupDataSetStates = null;
            if (writerGroupPublishState != null)
            {
                object dataSetStates = writerGroupPublishState
                    .GetType()
                    .GetField("m_dataSetStates", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(writerGroupPublishState);

                object dataSetStatesValues = dataSetStates
                    .GetType()
                    .GetProperty("Values", BindingFlags.Instance | BindingFlags.Public)
                    .GetValue(dataSetStates);

                writerGroupDataSetStates = (IEnumerable<object>)dataSetStatesValues;
            }

            foreach (UaDataSetMessage datasetMessage in uaDataNetworkMessage.DataSetMessages)
            {
                if (datasetMessage.DataSet.IsDeltaFrame)
                {
                    Assert.Greater(keyFrameCount, 1, "keyFrameCount > 1 if dataset is delta!");
                    Assert.IsNotNull(
                        writerGroupPublishState,
                        "WriterGroupPublishState should not be null");
                    Assert.IsNotNull(
                        writerGroupDataSetStates,
                        "writerGroupDataSetStates that contains last saved detaset should not be null");

                    DataSet lastDataSetFound = null;
                    foreach (object dataSetState in writerGroupDataSetStates)
                    {
                        object writerGroupLastDataSet = dataSetState
                            .GetType()
                            .GetField("LastDataSet", BindingFlags.Instance | BindingFlags.Public)
                            .GetValue(dataSetState);
                        if (writerGroupLastDataSet != null)
                        {
                            string dataSetName =
                                writerGroupLastDataSet
                                    .GetType()
                                    .GetProperty(
                                        "Name",
                                        BindingFlags.Instance | BindingFlags.Public)
                                    .GetValue(writerGroupLastDataSet) as string;
                            if (!string.IsNullOrEmpty(dataSetName) &&
                                datasetMessage.DataSet.Name == dataSetName)
                            {
                                lastDataSetFound = writerGroupLastDataSet as DataSet;
                            }
                        }
                    }
                    Assert.IsNotNull(
                        lastDataSetFound,
                        "lastDataSetFound dataset should not be null");

                    int fieldIndex = 0;
                    foreach (Field field in datasetMessage.DataSet.Fields)
                    {
                        // ghost field should still be hold it in the state.LastDataSet
                        Field lastDataSetField = lastDataSetFound.Fields[fieldIndex++];
                        Assert.IsNotNull(
                            lastDataSetField,
                            "lastDataSetField should not be null even if the partial field is missing due to delta");
                        // for delta frames dataset might contains partial filled data
                        if (field == null)
                        {
                            continue;
                        }
                        var targetNodeId = new NodeId(
                            field.FieldMetaData.Name,
                            kNamespaceIndexAllTypes);
                        Assert.IsTrue(
                            dataStoreData.ContainsKey(targetNodeId),
                            "field name: '{0}' should be exists in partial received dataset",
                            field.FieldMetaData.Name);
                        Assert.IsNotNull(
                            dataStoreData[targetNodeId],
                            "field: '{0}' should not be null",
                            field.FieldMetaData.Name);
                        Assert.AreEqual(
                            field.Value.Value,
                            dataStoreData[targetNodeId].Value,
                            "field: '{0}' value: {1} should be equal to datastore value: {2}",
                            field.FieldMetaData.Name,
                            field.Value,
                            dataStoreData[targetNodeId].Value);
                        Assert.AreEqual(
                            lastDataSetField.Value.Value,
                            dataStoreData[targetNodeId].Value,
                            "lastDataSetField: '{0}' value: {1} should be equal to datastore value: {2}",
                            lastDataSetField.FieldMetaData.Name,
                            lastDataSetField.Value,
                            dataStoreData[targetNodeId].Value);
                    }
                }
                else
                {
                    Assert.AreEqual(keyFrameCount, 1, "keyFrameCount = 1 if dataset is not delta!");
                    foreach (Field field in datasetMessage.DataSet.Fields)
                    {
                        Assert.IsNotNull(
                            field,
                            "field {0}: should not be null if dataset is not delta!",
                            field.FieldMetaData.Name);
                        var targetNodeId = new NodeId(
                            field.FieldMetaData.Name,
                            kNamespaceIndexAllTypes);
                        Assert.IsTrue(
                            dataStoreData.ContainsKey(targetNodeId),
                            "field name: {0} should be exists in partial received dataset",
                            field.FieldMetaData.Name);
                        Assert.IsNotNull(
                            dataStoreData[targetNodeId],
                            "field {0}: should not be null",
                            field.FieldMetaData.Name);
                        Assert.AreEqual(
                            field.Value.Value,
                            dataStoreData[targetNodeId].Value,
                            "field: '{0}' value: {1} should be equal to datastore value: {2}",
                            field.FieldMetaData.Name,
                            field.Value,
                            dataStoreData[targetNodeId].Value);
                    }
                }
            }
        }

        /// <summary>
        /// Tests that key frames are sent after KeyFrameCount intervals even when there are no data changes.
        /// This verifies the fix for issue #2622: KeyFrame is not sent if no changed values
        /// </summary>
        [Test(Description = "Verify KeyFrame is sent after KeyFrameCount intervals without data changes")]
        public void KeyFrameSentWithoutDataChanges([Values(3, 5)] int keyFrameCount)
        {
            // Arrange - create a simple DataSetWriter with specified KeyFrameCount
            var writer = new DataSetWriterDataType
            {
                DataSetWriterId = 1,
                KeyFrameCount = (uint)keyFrameCount
            };

            var writerGroupPublishState = new WriterGroupPublishState();

            // Act & Assert

            // First call should be a key frame (interval 0)
            bool isDelta = writerGroupPublishState.IsDeltaFrame(writer, out uint seq1);
            Assert.That(isDelta, Is.False, "First message should be a key frame");
            Assert.That(seq1, Is.EqualTo(1), "First sequence number should be 1");

            // Subsequent calls before KeyFrameCount should be delta frames
            for (int i = 1; i < keyFrameCount; i++)
            {
                isDelta = writerGroupPublishState.IsDeltaFrame(writer, out uint seqDelta);
                Assert.That(isDelta, Is.True, $"Message {i + 1} should be a delta frame");
                Assert.That(seqDelta, Is.EqualTo(i + 1), $"Sequence number should be {i + 1}");
            }

            // After KeyFrameCount intervals, we should get another key frame
            isDelta = writerGroupPublishState.IsDeltaFrame(writer, out uint seqKeyFrame);
            Assert.That(isDelta, Is.False, $"Message {keyFrameCount + 1} should be a key frame");
            Assert.That(seqKeyFrame, Is.EqualTo(keyFrameCount + 1), $"Sequence number should be {keyFrameCount + 1}");

            // Verify the cycle continues correctly
            for (int i = 1; i < keyFrameCount; i++)
            {
                isDelta = writerGroupPublishState.IsDeltaFrame(writer, out _);
                Assert.That(isDelta, Is.True, $"Message {keyFrameCount + i + 1} should be a delta frame");
            }

            // And another key frame
            isDelta = writerGroupPublishState.IsDeltaFrame(writer, out uint seqKeyFrame2);
            Assert.That(isDelta, Is.False, $"Message {(2 * keyFrameCount) + 1} should be a key frame");
            Assert.That(seqKeyFrame2, Is.EqualTo((2 * keyFrameCount) + 1), $"Sequence number should be {(2 * keyFrameCount) + 1}");
        }
    }
}
