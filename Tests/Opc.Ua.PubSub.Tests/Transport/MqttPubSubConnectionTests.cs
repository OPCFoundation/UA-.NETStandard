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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.PubSub.Tests.Encoding;

namespace Opc.Ua.PubSub.Tests.Transport
{
    [TestFixture(Description = "Tests for Mqtt connections")]
    public partial class MqttPubSubConnectionTests
    {
        private const UInt16 NamespaceIndexAllTypes = 3;

        private ManualResetEvent m_shutdownEvent;
        private const int EstimatedPublishingTime = 60000;

        [OneTimeSetUp()]
        public void MyTestInitialize()
        {

        }

        [Test(Description = "Validate mqtt local pub/sub connection with uadp data.")]
        [Ignore("A mosquitto tool should be installed local in order to run correctly.")]
        public void ValidateMqttLocalPubSubConnectionWithUadp(
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            RestartMosquitto("mosquitto");

            //Arrange
            UInt16 writerGroupId = 1;

            string mqttLocalBrokerUrl = "mqtt://localhost:1883";

            ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V500);

            UadpNetworkMessageContentMask uadpNetworkMessageContentMask = UadpNetworkMessageContentMask.PublisherId
                | UadpNetworkMessageContentMask.WriterGroupId
                | UadpNetworkMessageContentMask.PayloadHeader;
            UadpDataSetMessageContentMask uadpDataSetMessageContentMask = UadpDataSetMessageContentMask.None;

            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttUadpTransport,
                mqttLocalBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(publisherConfiguration, publisherId);
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttPublisherConnection.ConnectionProperties, "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            WriterGroupDataType writerGroup = MessagesHelper.GetWriterGroup(mqttPublisherConnection, writerGroupId);

            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            UadpNetworkMessage uaNetworkMessage = publisherConnection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                UadpNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "CreateNetworkMessage did not return an UadpNetworkMessage.");

            bool hasDataSetWriterId = (uadpNetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttUadpTransport,
                mqttLocalBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                uadpNetworkMessageContentMask: uadpNetworkMessageContentMask,
                uadpDataSetMessageContentMask: uadpDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubcriberConnection = MessagesHelper.GetConnection(subscriberConfiguration, publisherId);
            Assert.IsNotNull(mqttSubcriberConnection, "The MQTT subscriber connection is invalid.");
            mqttSubcriberConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttSubcriberConnection.ConnectionProperties, "The MQTT subscriber connection properties are not valid.");

            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First();
            Assert.IsNotNull(subscriberConnection, "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(uaNetworkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The UADP message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();
            
        }

        [Test(Description = "Validate mqtt local pub/sub connection with json data.")]
        [Ignore("A mosquitto tool should be installed local in order to run correctly.")]
        public void ValidateMqttLocalPubSubConnectionWithJson(
            [Values((byte)1, (UInt16)1, (UInt32)1, (UInt64)1, "abc")] object publisherId)
        {
            RestartMosquitto("mosquitto");

            //Arrange
            UInt16 writerGroupId = 1;

            string mqttLocalBrokerUrl = "mqtt://localhost:1883";

            ITransportProtocolConfiguration mqttConfiguration = new MqttClientProtocolConfiguration(version: EnumMqttProtocolVersion.V500);

            JsonNetworkMessageContentMask jsonNetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader
                | JsonNetworkMessageContentMask.PublisherId
                | JsonNetworkMessageContentMask.DataSetMessageHeader;
            JsonDataSetMessageContentMask jsonDataSetMessageContentMask = JsonDataSetMessageContentMask.None;

            DataSetFieldContentMask dataSetFieldContentMask = DataSetFieldContentMask.None;

            DataSetMetaDataType[] dataSetMetaDataArray = new DataSetMetaDataType[]
            {
                MessagesHelper.CreateDataSetMetaData1("DataSet1"),
                MessagesHelper.CreateDataSetMetaData2("DataSet2"),
                MessagesHelper.CreateDataSetMetaData3("DataSet3"),
                // MessagesHelper.CreateDataSetMetaDataAllTypes("DataSet4")
            };

            PubSubConfigurationDataType publisherConfiguration = MessagesHelper.CreatePublisherConfiguration(
                Profiles.PubSubMqttJsonTransport,
                mqttLocalBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(publisherConfiguration, "publisherConfiguration should not be null");

            // Configure the mqtt publisher configuration with the MQTTbroker
            PubSubConnectionDataType mqttPublisherConnection = MessagesHelper.GetConnection(publisherConfiguration, publisherId);
            Assert.IsNotNull(mqttPublisherConnection, "The MQTT publisher connection is invalid.");
            mqttPublisherConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttPublisherConnection.ConnectionProperties, "The MQTT publisher connection properties are not valid.");

            // Create publisher application for multiple datasets
            UaPubSubApplication publisherApplication = UaPubSubApplication.Create(publisherConfiguration);
            MessagesHelper.LoadData(publisherApplication, NamespaceIndexAllTypes);

            IUaPubSubConnection publisherConnection = publisherApplication.PubSubConnections.First();
            Assert.IsNotNull(publisherConnection, "Publisher first connection should not be null");

            WriterGroupDataType writerGroup = MessagesHelper.GetWriterGroup(mqttPublisherConnection, writerGroupId);

            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration first connection should not be null");
            Assert.IsNotNull(publisherConfiguration.Connections.First(), "publisherConfiguration  first writer group of first connection should not be null");
            JsonNetworkMessage uaNetworkMessage = publisherConnection.CreateNetworkMessage(publisherConfiguration.Connections.First().WriterGroups.First()) as
                JsonNetworkMessage;
            Assert.IsNotNull(uaNetworkMessage, "CreateNetworkMessage did not return an JsonNetworkMessage.");

            bool hasDataSetWriterId = (jsonNetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0
                && (jsonDataSetMessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0;

            PubSubConfigurationDataType subscriberConfiguration = MessagesHelper.CreateSubscriberConfiguration(
                Profiles.PubSubMqttJsonTransport,
                mqttLocalBrokerUrl, publisherId: publisherId, writerGroupId: writerGroupId, setDataSetWriterId: hasDataSetWriterId,
                jsonNetworkMessageContentMask: jsonNetworkMessageContentMask,
                jsonDataSetMessageContentMask: jsonDataSetMessageContentMask,
                dataSetFieldContentMask: dataSetFieldContentMask,
                dataSetMetaDataArray: dataSetMetaDataArray, nameSpaceIndexForData: NamespaceIndexAllTypes);
            Assert.IsNotNull(subscriberConfiguration, "subscriberConfiguration should not be null");

            // Create subscriber application for multiple datasets
            UaPubSubApplication subscriberApplication = UaPubSubApplication.Create(subscriberConfiguration);
            Assert.IsNotNull(subscriberApplication, "subscriberApplication should not be null");
            Assert.IsNotNull(subscriberApplication.PubSubConnections.First(), "subscriberConfiguration first connection should not be null");

            // Configure the mqtt subscriber configuration with the MQTTbroker
            PubSubConnectionDataType mqttSubcriberConnection = MessagesHelper.GetConnection(subscriberConfiguration, publisherId);
            Assert.IsNotNull(mqttSubcriberConnection, "The MQTT subscriber connection is invalid.");
            mqttSubcriberConnection.ConnectionProperties = mqttConfiguration.KeyValuePairs;
            Assert.IsNotNull(mqttSubcriberConnection.ConnectionProperties, "The MQTT subscriber connection properties are not valid.");

            var dataSetReaders = subscriberApplication.PubSubConnections.First().GetOperationalDataSetReaders();
            Assert.IsNotNull(dataSetReaders, "dataSetReaders should not be null");
            IUaPubSubConnection subscriberConnection = subscriberApplication.PubSubConnections.First();
            Assert.IsNotNull(subscriberConnection, "Subscriber first connection should not be null");

            //Act
            // it will signal if the uadp message was received from local ip
            m_shutdownEvent = new ManualResetEvent(false);

            subscriberApplication.DataReceived += UaPubSubApplication_DataReceived;
            subscriberConnection.Start();

            publisherConnection.Start();
            publisherConnection.PublishNetworkMessage(uaNetworkMessage);

            //Assert
            if (!m_shutdownEvent.WaitOne(EstimatedPublishingTime))
            {
                Assert.Fail("The JSON message was not received");
            }

            subscriberConnection.Stop();
            publisherConnection.Stop();

        }

        #region Private methods

        /// <summary>
        /// Data received handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UaPubSubApplication_DataReceived(object sender, SubscribedDataEventArgs e)
        {
            m_shutdownEvent.Set();
        }

        /// <summary>
        /// Start/stop local mosquitto
        /// </summary>
        /// <param name="processName"></param>
        /// <param name="arguments"></param>
        private void RestartMosquitto(string processName, string arguments = "")
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                if (processes.Length > 0)
                {
                    Process mosquittoProcess = processes[0];
                    mosquittoProcess.Kill();
                }

                using (Process process = new Process())
                {
                    ProcessStartInfo startInfo =
                   new ProcessStartInfo(
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        Path.Combine(processName, $"{processName}.exe")));
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    //startInfo.CreateNoWindow = true;
                    //startInfo.RedirectStandardOutput = true;
                    //startInfo.UseShellExecute = true;
                    //startInfo.Verb = "runas";
                    startInfo.Arguments = arguments;
                    process.StartInfo = startInfo;
                    process.Start();
                }
            }
            catch (Exception)
            {
                Assert.Fail("The mosquitto could not be restarted!");
            }

        }


        #endregion
    }
}
