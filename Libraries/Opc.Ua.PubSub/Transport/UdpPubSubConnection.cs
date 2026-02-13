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
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub.Transport
{
    /// <summary>
    /// UADP implementation of <see cref="UaPubSubConnection"/> class.
    /// </summary>
    internal sealed class UdpPubSubConnection : UaPubSubConnection, IUadpDiscoveryMessages
    {
        private List<UdpClient> m_publisherUdpClients = [];
        private List<UdpClient> m_subscriberUdpClients = [];
        private UdpDiscoverySubscriber m_udpDiscoverySubscriber;
        private UdpDiscoveryPublisher m_udpDiscoveryPublisher;
        private static int s_sequenceNumber;
        private static int s_dataSetSequenceNumber;

        /// <summary>
        /// Create new instance of <see cref="UdpPubSubConnection"/>
        /// from <see cref="PubSubConnectionDataType"/> configuration data
        /// </summary>
        public UdpPubSubConnection(
            UaPubSubApplication uaPubSubApplication,
            PubSubConnectionDataType pubSubConnectionDataType,
            ITelemetryContext telemetry)
            : base(
                uaPubSubApplication,
                pubSubConnectionDataType,
                telemetry,
                telemetry.CreateLogger<UdpPubSubConnection>())
        {
            m_transportProtocol = TransportProtocol.UDP;

            m_logger.LogInformation(
                "UdpPubSubConnection with name '{Name}' was created.",
                pubSubConnectionDataType.Name);

            Initialize();
        }

        /// <summary>
        /// Get or set the event handler
        /// </summary>
        public GetPublisherEndpointsEventHandler GetPublisherEndpoints { get; set; }

        /// <summary>
        /// Get the NetworkInterface name from configured <see cref="PubSubConnectionDataType"/>.Address.
        /// </summary>
        public string NetworkInterfaceName { get; set; }

        /// <summary>
        /// Get the <see cref="IPEndPoint"/> from configured <see cref="PubSubConnectionDataType"/>.Address.
        /// </summary>
        public IPEndPoint NetworkAddressEndPoint { get; private set; }

        /// <summary>
        /// Get the port from configured <see cref="PubSubConnectionDataType"/>.Address
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Gets the list of publisher UDP clients.
        /// Returns a read-only list of active UDP clients used for publishing.
        /// Can be used to configure socket settings such as ReceiveBuffer size.
        /// </summary>
        public IReadOnlyList<UdpClient> PublisherUdpClients
        {
            get
            {
                lock (Lock)
                {
                    return m_publisherUdpClients.AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Gets the list of subscriber UDP clients.
        /// Returns a read-only list of active UDP clients used for subscribing.
        /// Can be used to configure socket settings such as ReceiveBuffer size.
        /// </summary>
        public IReadOnlyList<UdpClient> SubscriberUdpClients
        {
            get
            {
                lock (Lock)
                {
                    return m_subscriberUdpClients.AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Perform specific Start tasks
        /// </summary>
        protected override async Task InternalStart()
        {
            //cleanup all existing UdpClient previously open
            await InternalStop().ConfigureAwait(false);

            if (NetworkAddressEndPoint == null)
            {
                return;
            }

            //publisher initialization
            if (Publishers.Count > 0)
            {
                lock (Lock)
                {
                    m_publisherUdpClients = UdpClientCreator.GetUdpClients(
                        UsedInContext.Publisher,
                        NetworkInterfaceName,
                        NetworkAddressEndPoint,
                        Telemetry,
                        m_logger);
                }

                m_udpDiscoveryPublisher = new UdpDiscoveryPublisher(this, Telemetry);
                await m_udpDiscoveryPublisher.StartAsync(MessageContext).ConfigureAwait(false);
            }

            //subscriber initialization
            if (GetAllDataSetReaders().Count > 0)
            {
                lock (Lock)
                {
                    m_subscriberUdpClients = UdpClientCreator.GetUdpClients(
                        UsedInContext.Subscriber,
                        NetworkInterfaceName,
                        NetworkAddressEndPoint,
                        Telemetry,
                        m_logger);

                    foreach (UdpClient subscriberUdpClient in m_subscriberUdpClients)
                    {
                        try
                        {
                            subscriberUdpClient.BeginReceive(
                                new AsyncCallback(OnUadpReceive),
                                subscriberUdpClient);
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogInformation(
                                "UdpClient '{Endpoint}' Cannot receive data. Exception: {Message}",
                                subscriberUdpClient.Client.LocalEndPoint,
                                ex.Message);
                        }
                    }
                }

                // initialize the discovery channel
                m_udpDiscoverySubscriber = new UdpDiscoverySubscriber(this, Telemetry);
                await m_udpDiscoverySubscriber.StartAsync(MessageContext).ConfigureAwait(false);

                // add handler to metaDataReceived event
                Application.MetaDataReceived += MetaDataReceived;
                Application.DataSetWriterConfigurationReceived
                    += DataSetWriterConfigurationReceived;
            }
        }

        /// <summary>
        /// Perform specific Stop tasks
        /// </summary>
        protected override async Task InternalStop()
        {
            lock (Lock)
            {
                foreach (
                    List<UdpClient> list in new List<List<UdpClient>>
                    {
                        m_publisherUdpClients,
                        m_subscriberUdpClients
                    })
                {
                    if (list != null && list.Count > 0)
                    {
                        foreach (UdpClient udpClient in list)
                        {
                            udpClient.Close();
                            udpClient.Dispose();
                        }
                        list.Clear();
                    }
                }
            }

            if (m_udpDiscoveryPublisher != null)
            {
                await m_udpDiscoveryPublisher.StopAsync().ConfigureAwait(false);
            }

            if (m_udpDiscoverySubscriber != null)
            {
                await m_udpDiscoverySubscriber.StopAsync().ConfigureAwait(false);

                // remove handler to metaDataReceived event
                Application.MetaDataReceived -= MetaDataReceived;
            }
        }

        /// <summary>
        /// Create the list of network messages built from the provided writerGroupConfiguration
        /// </summary>
        public override IList<UaNetworkMessage> CreateNetworkMessages(
            WriterGroupDataType writerGroupConfiguration,
            WriterGroupPublishState state)
        {
            if (ExtensionObject.ToEncodeable(writerGroupConfiguration.MessageSettings)
                is not UadpWriterGroupMessageDataType messageSettings)
            {
                //Wrong configuration of writer group MessageSettings
                return null;
            }

            if (ExtensionObject.ToEncodeable(writerGroupConfiguration.TransportSettings)
                is not DatagramWriterGroupTransportDataType)
            {
                //Wrong configuration of writer group TransportSettings
                return null;
            }
            var networkMessages = new List<UaNetworkMessage>();

            //Create list of dataSet messages to be sent
            var dataSetMessages = new List<UadpDataSetMessage>();
            foreach (DataSetWriterDataType dataSetWriter in writerGroupConfiguration.DataSetWriters)
            {
                //check if dataSetWriter enabled
                if (dataSetWriter.Enabled)
                {
                    DataSet dataSet = CreateDataSet(dataSetWriter, state);

                    if (dataSet != null)
                    {
                        bool hasMetaDataChanged = state.HasMetaDataChanged(
                            dataSetWriter,
                            dataSet.DataSetMetaData);

                        if (hasMetaDataChanged)
                        {
                            // add metadata network message
                            networkMessages.Add(
                                new UadpNetworkMessage(
                                    writerGroupConfiguration,
                                    dataSet.DataSetMetaData,
                                    m_logger)
                                {
                                    PublisherId = PubSubConnectionConfiguration.PublisherId.Value,
                                    DataSetWriterId = dataSetWriter.DataSetWriterId
                                });
                        }

                        // check MessageSettings to see how to encode DataSet
                        if (ExtensionObject.ToEncodeable(dataSetWriter.MessageSettings)
                            is UadpDataSetWriterMessageDataType dataSetMessageSettings)
                        {
                            var uadpDataSetMessage = new UadpDataSetMessage(dataSet, m_logger)
                            {
                                DataSetWriterId = dataSetWriter.DataSetWriterId
                            };
                            uadpDataSetMessage.SetMessageContentMask(
                                (UadpDataSetMessageContentMask)dataSetMessageSettings
                                    .DataSetMessageContentMask);
                            uadpDataSetMessage.SetFieldContentMask(
                                (DataSetFieldContentMask)dataSetWriter.DataSetFieldContentMask);
                            uadpDataSetMessage.SequenceNumber = (ushort)(
                                Utils.IncrementIdentifier(ref s_dataSetSequenceNumber) %
                                ushort.MaxValue);
                            uadpDataSetMessage.ConfiguredSize = dataSetMessageSettings
                                .ConfiguredSize;
                            uadpDataSetMessage.DataSetOffset = dataSetMessageSettings.DataSetOffset;
                            uadpDataSetMessage.Timestamp = DateTime.UtcNow;
                            uadpDataSetMessage.Status = StatusCodes.Good;
                            dataSetMessages.Add(uadpDataSetMessage);

                            state.OnMessagePublished(dataSetWriter, dataSet);
                        }
                    }
                }
            }

            //cancel send if no dataset message
            if (dataSetMessages.Count == 0)
            {
                return networkMessages;
            }

            var uadpNetworkMessage = new UadpNetworkMessage(
                writerGroupConfiguration,
                dataSetMessages,
                m_logger);
            uadpNetworkMessage.SetNetworkMessageContentMask(
                (UadpNetworkMessageContentMask)messageSettings.NetworkMessageContentMask);
            uadpNetworkMessage.WriterGroupId = writerGroupConfiguration.WriterGroupId;
            // Network message header
            uadpNetworkMessage.PublisherId = PubSubConnectionConfiguration.PublisherId.Value;
            uadpNetworkMessage.SequenceNumber = (ushort)(
                Utils.IncrementIdentifier(ref s_sequenceNumber) % ushort.MaxValue);

            // Writer group header
            uadpNetworkMessage.GroupVersion = messageSettings.GroupVersion;
            uadpNetworkMessage.NetworkMessageNumber = 1; //only one network message per publish

            networkMessages.Add(uadpNetworkMessage);

            return networkMessages;
        }

        /// <summary>
        /// Create and return the list of DataSetMetaData response messages
        /// </summary>
        public IList<UaNetworkMessage> CreateDataSetMetaDataNetworkMessages(
            ushort[] dataSetWriterIds)
        {
            var networkMessages = new List<UaNetworkMessage>();
            List<DataSetWriterDataType> writers = GetWriterGroupsDataType();

            foreach (ushort dataSetWriterId in dataSetWriterIds)
            {
                DataSetWriterDataType writer = writers.FirstOrDefault(
                    w => w.DataSetWriterId == dataSetWriterId);
                if (writer != null)
                {
                    WriterGroupDataType writerGroup = PubSubConnectionConfiguration.WriterGroups
                        .FirstOrDefault(wg =>
                            wg.DataSetWriters.Contains(writer));
                    if (writerGroup != null)
                    {
                        DataSetMetaDataType metaData = Application
                            .DataCollector.GetPublishedDataSet(writer.DataSetName)?
                            .DataSetMetaData;
                        if (metaData != null)
                        {
                            var networkMessage = new UadpNetworkMessage(writerGroup, metaData, m_logger)
                            {
                                PublisherId = PubSubConnectionConfiguration.PublisherId.Value,
                                DataSetWriterId = dataSetWriterId
                            };

                            networkMessages.Add(networkMessage);
                        }
                    }
                }
            }

            return networkMessages;
        }

        /// <summary>
        /// Create and return the list of DataSetWriterConfiguration response message
        /// </summary>
        /// <param name="dataSetWriterIds">DatasetWriter ids</param>
        public IList<UaNetworkMessage> CreateDataSetWriterCofigurationMessage(
            ushort[] dataSetWriterIds)
        {
            var networkMessages = new List<UaNetworkMessage>();

            foreach (
                DataSetWriterConfigurationResponse response in GetDataSetWriterDiscoveryResponses(
                    dataSetWriterIds))
            {
                var networkMessage = new UadpNetworkMessage(
                    response.DataSetWriterIds,
                    response.DataSetWriterConfig,
                    response.StatusCodes,
                    m_logger)
                {
                    PublisherId = PubSubConnectionConfiguration.PublisherId.Value
                };
                networkMessage.MessageStatusCodes.ToList().AddRange(response.StatusCodes);
                networkMessages.Add(networkMessage);
            }

            return networkMessages;
        }

        /// <summary>
        /// Publish the network message
        /// </summary>
        public override Task<bool> PublishNetworkMessageAsync(UaNetworkMessage networkMessage)
        {
            if (networkMessage == null ||
                m_publisherUdpClients == null ||
                m_publisherUdpClients.Count == 0)
            {
                return Task.FromResult(false);
            }

            try
            {
                lock (Lock)
                {
                    if (m_publisherUdpClients.Count > 0)
                    {
                        // Get encoded bytes
                        byte[] bytes = networkMessage.Encode(MessageContext);

                        foreach (UdpClient udpClient in m_publisherUdpClients)
                        {
                            try
                            {
                                udpClient.Send(bytes, bytes.Length, NetworkAddressEndPoint);

                                m_logger.LogInformation(
                                    "UdpPubSubConnection.PublishNetworkMessage bytes:{Length}, endpoint:{Endpoint}",
                                    bytes.Length,
                                    NetworkAddressEndPoint);
                            }
                            catch (Exception ex)
                            {
                                m_logger.LogError(ex, "UdpPubSubConnection.PublishNetworkMessage");
                                return Task.FromResult(false);
                            }
                        }
                        return Task.FromResult(true);
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "UdpPubSubConnection.PublishNetworkMessage");
                return Task.FromResult(false);
            }

            return Task.FromResult(false);
        }

        /// <summary>
        /// Always returns true since UDP is a connectionless protocol
        /// </summary>
        public override bool AreClientsConnected()
        {
            return true;
        }

        /// <summary>
        /// Set GetPublisherEndpoints callback used by the subscriber to receive PublisherEndpoints data from publisher
        /// </summary>
        public void GetPublisherEndpointsCallback(
            GetPublisherEndpointsEventHandler getPubliherEndpoints)
        {
            m_udpDiscoveryPublisher?.GetPublisherEndpoints = getPubliherEndpoints;
        }

        /// <summary>
        /// Set GetDataSetWriterConfiguration callback used by the subscriber to receive DataSetWriter ids from publisher
        /// </summary>
        public void GetDataSetWriterConfigurationCallback(
            GetDataSetWriterIdsEventHandler getDataSetWriterIds)
        {
            m_udpDiscoveryPublisher?.GetDataSetWriterIds = getDataSetWriterIds;
        }

        /// <summary>
        /// Create and return the list of EndpointDescription response messages
        /// To be used only by UADP Discovery response messages
        /// </summary>
        public UaNetworkMessage CreatePublisherEndpointsNetworkMessage(
            EndpointDescription[] endpoints,
            StatusCode publisherProvideEndpointsStatusCode,
            object publisherId)
        {
            if (PubSubConnectionConfiguration != null &&
                PubSubConnectionConfiguration.TransportProfileUri == Profiles
                    .PubSubUdpUadpTransport)
            {
                return new UadpNetworkMessage(endpoints, publisherProvideEndpointsStatusCode, m_logger)
                {
                    PublisherId = publisherId
                };
            }

            return null;
        }

        /// <summary>
        /// Request UADP Discovery Publisher endpoints only
        /// </summary>
        public void RequestPublisherEndpoints()
        {
            if (PubSubConnectionConfiguration != null &&
                PubSubConnectionConfiguration.TransportProfileUri == Profiles
                    .PubSubUdpUadpTransport &&
                m_udpDiscoverySubscriber != null)
            {
                // send discovery request publisher endpoints here for now
                m_udpDiscoverySubscriber.SendDiscoveryRequestPublisherEndpoints();
            }
        }

        /// <summary>
        /// Request UADP Discovery DataSetWriterConfiguration messages
        /// </summary>
        public void RequestDataSetWriterConfiguration()
        {
            if (PubSubConnectionConfiguration != null &&
                PubSubConnectionConfiguration.TransportProfileUri == Profiles
                    .PubSubUdpUadpTransport &&
                m_udpDiscoverySubscriber != null)
            {
                m_udpDiscoverySubscriber.SendDiscoveryRequestDataSetWriterConfiguration();
            }
        }

        /// <summary>
        /// Request DataSetMetaData
        /// </summary>
        public void RequestDataSetMetaData()
        {
            m_udpDiscoverySubscriber?.SendDiscoveryRequestDataSetMetaData();
        }

        /// <summary>
        /// Initialize Connection properties from connection configuration object
        /// </summary>
        private void Initialize()
        {
            if (ExtensionObject.ToEncodeable(PubSubConnectionConfiguration.Address)
                is not NetworkAddressUrlDataType networkAddressUrlState)
            {
                m_logger.LogError(
                    "The configuration for connection {Name} has invalid Address configuration.",
                    PubSubConnectionConfiguration.Name);
                return;
            }
            // set properties
            NetworkInterfaceName = networkAddressUrlState.NetworkInterface;
            NetworkAddressEndPoint = UdpClientCreator.GetEndPoint(networkAddressUrlState.Url, m_logger);

            if (NetworkAddressEndPoint == null)
            {
                m_logger.LogError(
                    "The configuration for connection {Name} with Url:'{Url}' resulted in an invalid endpoint.",
                    PubSubConnectionConfiguration.Name,
                    networkAddressUrlState.Url);
            }
        }

        /// <summary>
        /// Process the bytes received from UADP channel as Subscriber
        /// </summary>
        private void ProcessReceivedMessage(byte[] message, IPEndPoint source)
        {
            m_logger.LogInformation(
                "UdpPubSubConnection.ProcessReceivedMessage from source={Source}",
                source);

            List<DataSetReaderDataType> dataSetReaders = GetOperationalDataSetReaders();
            var dataSetReadersToDecode = new List<DataSetReaderDataType>();

            foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
            {
                // check if dataSetReaders have metadata information
                if (!ConfigurationVersionUtils.IsUsable(dataSetReader.DataSetMetaData))
                {
                    // check if it is possible to request the metadata information
                    if (dataSetReader.DataSetWriterId != 0)
                    {
                        m_udpDiscoverySubscriber.AddWriterIdForDataSetMetadata(
                            dataSetReader.DataSetWriterId);
                    }
                }
                else
                {
                    dataSetReadersToDecode.Add(dataSetReader);
                }
            }

            var networkMessage = new UadpNetworkMessage(m_logger);
            networkMessage.DataSetDecodeErrorOccurred += NetworkMessage_DataSetDecodeErrorOccurred;
            networkMessage.Decode(MessageContext, message, dataSetReadersToDecode);
            networkMessage.DataSetDecodeErrorOccurred -= NetworkMessage_DataSetDecodeErrorOccurred;

            // Process the decoded network message
            ProcessDecodedNetworkMessage(networkMessage, source.ToString());
        }

        /// <summary>
        /// Handle Receive event for an UADP channel on Subscriber Side
        /// </summary>
        private void OnUadpReceive(IAsyncResult result)
        {
            lock (Lock)
            {
                if (m_subscriberUdpClients == null || m_subscriberUdpClients.Count == 0)
                {
                    return;
                }
            }

            // this is what had been passed into BeginReceive as the second parameter:
            if (result.AsyncState is not UdpClient socket)
            {
                return;
            }

            // points towards whoever had sent the message:
            var source = new IPEndPoint(0, 0);
            // get the actual message and fill out the source:
            try
            {
                byte[] message = socket.EndReceive(result, ref source);

                if (message != null)
                {
                    m_logger.LogInformation(
                        "OnUadpReceive received message with length {Length} from {Address}",
                        message.Length,
                        source.Address);

                    if (message.Length > 1)
                    {
                        // raise RawData received event
                        var rawDataReceivedEventArgs = new RawDataReceivedEventArgs
                        {
                            Message = message,
                            Source = source.Address.ToString(),
                            TransportProtocol = TransportProtocol,
                            MessageMapping = MessageMapping.Uadp,
                            PubSubConnectionConfiguration = PubSubConnectionConfiguration
                        };

                        // trigger notification for received raw data
                        Application.RaiseRawDataReceivedEvent(rawDataReceivedEventArgs);

                        // check if the RawData message is marked as handled
                        if (rawDataReceivedEventArgs.Handled)
                        {
                            m_logger.LogInformation(
                                "UdpConnection message from source={Source} is marked as handled and will not be decoded.",
                                rawDataReceivedEventArgs.Source);
                            return;
                        }

                        // call on a new thread
                        _ = Task.Run(() => ProcessReceivedMessage(message, source));
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "OnUadpReceive from {Address}", source.Address);
            }

            try
            {
                // schedule the next receive operation once reading is done:
                socket.BeginReceive(new AsyncCallback(OnUadpReceive), socket);
            }
            catch (Exception ex)
            {
                m_logger.LogInformation(
                    "OnUadpReceive BeginReceive threw Exception {Message}",
                    ex.Message);

                lock (Lock)
                {
                    Renew(socket);
                }
            }
        }

        /// <summary>
        /// Re initializes the socket
        /// </summary>
        /// <param name="socket">The socket which should be reinitialized</param>
        private void Renew(UdpClient socket)
        {
            UdpClient newsocket = null;

            if (socket is UdpClientMulticast mcastSocket)
            {
                newsocket = new UdpClientMulticast(
                    mcastSocket.Address,
                    mcastSocket.MulticastAddress,
                    mcastSocket.Port,
                    Telemetry);
            }
            else if (socket is UdpClientBroadcast bcastSocket)
            {
                newsocket = new UdpClientBroadcast(
                    bcastSocket.Address,
                    bcastSocket.Port,
                    bcastSocket.PubSubContext,
                    Telemetry);
            }
            else if (socket is UdpClientUnicast ucastSocket)
            {
                newsocket = new UdpClientUnicast(
                    ucastSocket.Address,
                    ucastSocket.Port,
                    Telemetry);
            }
            m_subscriberUdpClients.Remove(socket);
            m_subscriberUdpClients.Add(newsocket);
            socket.Close();
            socket.Dispose();

            newsocket?.BeginReceive(new AsyncCallback(OnUadpReceive), newsocket);
        }

        /// <summary>
        /// Resets SequenceNumber
        /// </summary>
        internal static void ResetSequenceNumber()
        {
            s_sequenceNumber = 0;
            s_dataSetSequenceNumber = 0;
        }

        /// <summary>
        /// Handle <see cref="UaPubSubApplication.MetaDataReceived"/> event.
        /// </summary>
        private void MetaDataReceived(object sender, SubscribedDataEventArgs e)
        {
            if (m_udpDiscoverySubscriber != null && e.NetworkMessage.DataSetWriterId != null)
            {
                m_udpDiscoverySubscriber.RemoveWriterIdForDataSetMetadata(
                    e.NetworkMessage.DataSetWriterId.Value);
            }
        }

        /// <summary>
        /// Handler for DatasetWriterConfigurationReceived event.
        /// </summary>
        private void DataSetWriterConfigurationReceived(
            object sender,
            DataSetWriterConfigurationEventArgs e)
        {
            lock (Lock)
            {
                WriterGroupDataType config = e.DataSetWriterConfiguration;
                if (e.DataSetWriterConfiguration != null)
                {
                    m_udpDiscoverySubscriber.UpdateDataSetWriterConfiguration(config);
                }
            }
        }

        /// <summary>
        /// Handle <see cref="UaNetworkMessage.DataSetDecodeErrorOccurred"/> event.
        /// </summary>
        private void NetworkMessage_DataSetDecodeErrorOccurred(
            object sender,
            DataSetDecodeErrorEventArgs e)
        {
            if (e.DecodeErrorReason == DataSetDecodeErrorReason.MetadataMajorVersion)
            {
                // Resend metadata request
                // check if it is possible to request the metadata information
                if (e.DataSetReader.DataSetWriterId != 0)
                {
                    m_udpDiscoverySubscriber.AddWriterIdForDataSetMetadata(
                        e.DataSetReader.DataSetWriterId);
                }
            }
        }
    }
}
