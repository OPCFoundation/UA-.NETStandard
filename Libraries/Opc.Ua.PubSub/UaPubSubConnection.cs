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
using System.Threading.Tasks;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// Abstract class that represents a working connection for PubSub
    /// </summary>
    internal abstract class UaPubSubConnection : IUaPubSubConnection
    {
        protected readonly object Lock = new();
        private readonly List<IUaPublisher> m_publishers;
        protected TransportProtocol m_transportProtocol = TransportProtocol.NotAvailable;

        /// <summary>
        /// Create new instance of UaPubSubConnection with PubSubConnectionDataType configuration data
        /// </summary>
        internal UaPubSubConnection(
            UaPubSubApplication parentUaPubSubApplication,
            PubSubConnectionDataType pubSubConnectionDataType)
        {
            // set the default message context that uses the GlobalContext
            MessageContext = new ServiceMessageContext
            {
                NamespaceUris = ServiceMessageContext.GlobalContext.NamespaceUris,
                ServerUris = ServiceMessageContext.GlobalContext.ServerUris
            };

            Application =
                parentUaPubSubApplication ??
                throw new ArgumentNullException(nameof(parentUaPubSubApplication));
            Application.UaPubSubConfigurator.WriterGroupAdded
                += UaPubSubConfigurator_WriterGroupAdded;
            PubSubConnectionConfiguration = pubSubConnectionDataType;

            m_publishers = [];

            if (string.IsNullOrEmpty(pubSubConnectionDataType.Name))
            {
                pubSubConnectionDataType.Name = "<connection>";
                Utils.Trace(
                    "UaPubSubConnection() received a PubSubConnectionDataType object without name. '<connection>' will be used");
            }
        }

        /// <summary>
        /// Get the assigned transport protocol for this connection instance
        /// </summary>
        public TransportProtocol TransportProtocol => m_transportProtocol;

        /// <summary>
        /// Get the configuration object for this PubSub connection
        /// </summary>
        public PubSubConnectionDataType PubSubConnectionConfiguration { get; }

        /// <summary>
        /// Get reference to <see cref="UaPubSubApplication"/>
        /// </summary>
        public UaPubSubApplication Application { get; }

        /// <summary>
        /// Get flag that indicates if the Connection is in running state
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Get/Set the current <see cref="IServiceMessageContext"/>
        /// </summary>
        public IServiceMessageContext MessageContext { get; set; }

        /// <summary>
        /// Get the list of current publishers associated with this connection
        /// </summary>
        internal IReadOnlyCollection<IUaPublisher> Publishers => m_publishers.AsReadOnly();

        /// <summary>
        /// Releases all resources used by the current instance of the <see cref="UaPubSubConnection"/> class.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///  When overridden in a derived class, releases the unmanaged resources used by that class
        ///  and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing"> true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Application.UaPubSubConfigurator.WriterGroupAdded
                    -= UaPubSubConfigurator_WriterGroupAdded;
                Stop();
                // free managed resources
                foreach (UaPublisher publisher in m_publishers.OfType<UaPublisher>())
                {
                    publisher.Dispose();
                }

                Utils.Trace("Connection '{0}' was disposed.", PubSubConnectionConfiguration.Name);
            }
        }

        /// <summary>
        /// Start Publish/Subscribe jobs associated with this instance
        /// </summary>
        public void Start()
        {
            InternalStart().Wait();
            Utils.Trace("Connection '{0}' was started.", PubSubConnectionConfiguration.Name);

            lock (Lock)
            {
                IsRunning = true;
                foreach (IUaPublisher publisher in m_publishers)
                {
                    publisher.Start();
                }
            }
        }

        /// <summary>
        /// Stop Publish/Subscribe jobs associated with this instance
        /// </summary>
        public void Stop()
        {
            InternalStop().Wait();
            lock (Lock)
            {
                IsRunning = false;
                foreach (IUaPublisher publisher in m_publishers)
                {
                    publisher.Stop();
                }
            }
            Utils.Trace("Connection '{0}' was stopped.", PubSubConnectionConfiguration.Name);
        }

        /// <summary>
        /// Determine if the connection has anything to publish -> at least one WriterDataSet is configured as enabled for current writer group
        /// </summary>
        public bool CanPublish(WriterGroupDataType writerGroupConfiguration)
        {
            if (!IsRunning)
            {
                return false;
            }
            // check if connection status is operational
            if (Application.UaPubSubConfigurator
                    .FindStateForObject(PubSubConnectionConfiguration) !=
                PubSubState.Operational)
            {
                return false;
            }

            if (Application.UaPubSubConfigurator
                    .FindStateForObject(writerGroupConfiguration) != PubSubState.Operational)
            {
                return false;
            }

            foreach (DataSetWriterDataType writer in writerGroupConfiguration.DataSetWriters)
            {
                if (writer.Enabled)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Create the network messages built from the provided writerGroupConfiguration
        /// </summary>
        /// <param name="writerGroupConfiguration">The writer group configuration </param>
        /// <param name="state">The publish state for the writer group.</param>
        /// <returns>A list of the <see cref="UaNetworkMessage"/> created from the provided writerGroupConfiguration.</returns>
        public abstract IList<UaNetworkMessage> CreateNetworkMessages(
            WriterGroupDataType writerGroupConfiguration,
            WriterGroupPublishState state);

        /// <summary>
        /// Publish the network message
        /// </summary>
        /// <param name="networkMessage">The network message that needs to be published.</param>
        /// <returns>True if send was successful.</returns>
        public abstract bool PublishNetworkMessage(UaNetworkMessage networkMessage);

        /// <summary>
        /// Get flag that indicates if all the network clients are connected
        /// </summary>
        public abstract bool AreClientsConnected();

        /// <summary>
        /// Get current list of Operational DataSetReaders available in this UaSubscriber component
        /// </summary>
        public List<DataSetReaderDataType> GetOperationalDataSetReaders()
        {
            var readersList = new List<DataSetReaderDataType>();
            if (Application.UaPubSubConfigurator
                    .FindStateForObject(PubSubConnectionConfiguration) !=
                PubSubState.Operational)
            {
                return readersList;
            }
            foreach (ReaderGroupDataType readerGroup in PubSubConnectionConfiguration.ReaderGroups)
            {
                if (Application.UaPubSubConfigurator
                    .FindStateForObject(readerGroup) == PubSubState.Operational)
                {
                    foreach (DataSetReaderDataType reader in readerGroup.DataSetReaders)
                    {
                        // check if the reader is properly configured to receive data
                        if (Application.UaPubSubConfigurator
                            .FindStateForObject(reader) == PubSubState.Operational)
                        {
                            readersList.Add(reader);
                        }
                    }
                }
            }
            return readersList;
        }

        /// <summary>
        /// Perform specific Start tasks
        /// </summary>
        protected abstract Task InternalStart();

        /// <summary>
        /// Perform specific Stop tasks
        /// </summary>
        protected abstract Task InternalStop();

        /// <summary>
        /// Processes the decoded <see cref="UaNetworkMessage"/> and
        /// raises the <see cref="UaPubSubApplication.DataReceived"/> or <see cref="UaPubSubApplication.MetaDataReceived"/> or <see cref="UaPubSubApplication.DataSetWriterConfigurationReceived"/> or <see cref="UaPubSubApplication.PublisherEndpointsReceived"/>event.
        /// </summary>
        /// <param name="networkMessage">The network message that was received.</param>
        /// <param name="source">The source of the received event.</param>
        protected void ProcessDecodedNetworkMessage(UaNetworkMessage networkMessage, string source)
        {
            if (networkMessage.IsMetaDataMessage)
            {
                // update configuration of the corresponding reader objects found in this connection configuration
                foreach (DataSetReaderDataType reader in GetAllDataSetReaders())
                {
                    bool raiseChangedEvent = false;

                    lock (Lock)
                    {
                        // check if reader's MetaData shall be updated
                        if (reader.DataSetWriterId != 0 &&
                            reader.DataSetWriterId == networkMessage.DataSetWriterId &&
                            (
                                reader.DataSetMetaData == null ||
                                !Utils.IsEqual(
                                    reader.DataSetMetaData.ConfigurationVersion,
                                    networkMessage.DataSetMetaData.ConfigurationVersion)))
                        {
                            raiseChangedEvent = true;
                        }
                    }

                    if (raiseChangedEvent)
                    {
                        // raise event
                        var metaDataUpdatedEventArgs = new ConfigurationUpdatingEventArgs
                        {
                            ChangedProperty = ConfigurationProperty.DataSetMetaData,
                            Parent = reader,
                            NewValue = networkMessage.DataSetMetaData,
                            Cancel = false
                        };

                        // raise the ConfigurationUpdating event and see if configuration shall be changed
                        Application.RaiseConfigurationUpdatingEvent(metaDataUpdatedEventArgs);

                        // check to see if the event handler canceled the save of new MetaData
                        if (!metaDataUpdatedEventArgs.Cancel)
                        {
                            Utils.Trace(
                                "Connection '{0}' - The MetaData is updated for DataSetReader '{1}' with DataSetWriterId={2}",
                                source,
                                reader.Name,
                                networkMessage.DataSetWriterId);

                            lock (Lock)
                            {
                                reader.DataSetMetaData = networkMessage.DataSetMetaData;
                            }
                        }
                    }
                }

                var subscribedDataEventArgs = new SubscribedDataEventArgs
                {
                    NetworkMessage = networkMessage,
                    Source = source
                };

                // trigger notification for received DataSet MetaData
                Application.RaiseMetaDataReceivedEvent(subscribedDataEventArgs);

                Utils.Trace(
                    "Connection '{0}' - RaiseMetaDataReceivedEvent() from source={0}",
                    source,
                    subscribedDataEventArgs.NetworkMessage.DataSetMessages.Count);
            }
            else if (networkMessage.DataSetMessages != null &&
                networkMessage.DataSetMessages.Count > 0)
            {
                var subscribedDataEventArgs = new SubscribedDataEventArgs
                {
                    NetworkMessage = networkMessage,
                    Source = source
                };

                //trigger notification for received subscribed DataSet
                Application.RaiseDataReceivedEvent(subscribedDataEventArgs);

                Utils.Trace(
                    "Connection '{0}' - RaiseNetworkMessageDataReceivedEvent() from source={0}, with {1} DataSets",
                    source,
                    subscribedDataEventArgs.NetworkMessage.DataSetMessages.Count);
            }
            else if (networkMessage is Encoding.UadpNetworkMessage)
            {
                if (networkMessage is Encoding.UadpNetworkMessage uadpNetworkMessage)
                {
                    if (uadpNetworkMessage.UADPDiscoveryType ==
                            UADPNetworkMessageDiscoveryType.DataSetWriterConfiguration &&
                        uadpNetworkMessage
                            .UADPNetworkMessageType == UADPNetworkMessageType.DiscoveryResponse)
                    {
                        var eventArgs = new DataSetWriterConfigurationEventArgs
                        {
                            DataSetWriterIds = uadpNetworkMessage.DataSetWriterIds,
                            Source = source,
                            DataSetWriterConfiguration = uadpNetworkMessage
                                .DataSetWriterConfiguration,
                            PublisherId = uadpNetworkMessage.PublisherId,
                            StatusCodes = uadpNetworkMessage.MessageStatusCodes
                        };

                        //trigger notification for received configuration
                        Application.RaiseDatasetWriterConfigurationReceivedEvent(eventArgs);

                        Utils.Trace(
                            "Connection '{0}' - RaiseDataSetWriterConfigurationReceivedEvent() from source={0}, with {1} DataSetWriterConfiguration",
                            source,
                            eventArgs.DataSetWriterIds.Length);
                    }
                    else if (uadpNetworkMessage.UADPDiscoveryType ==
                        UADPNetworkMessageDiscoveryType.PublisherEndpoint &&
                        uadpNetworkMessage
                            .UADPNetworkMessageType == UADPNetworkMessageType.DiscoveryResponse)
                    {
                        var publisherEndpointsEventArgs = new PublisherEndpointsEventArgs
                        {
                            PublisherEndpoints = uadpNetworkMessage.PublisherEndpoints,
                            Source = source,
                            PublisherId = uadpNetworkMessage.PublisherId,
                            StatusCode = uadpNetworkMessage.PublisherProvideEndpoints
                        };

                        //trigger notification for received publisher endpoints
                        Application.RaisePublisherEndpointsReceivedEvent(
                            publisherEndpointsEventArgs);

                        Utils.Trace(
                            "Connection '{0}' - RaisePublisherEndpointsReceivedEvent() from source={0}, with {1} PublisherEndpoints",
                            source,
                            publisherEndpointsEventArgs.PublisherEndpoints.Length);
                    }
                }
            }
        }

        /// <summary>
        /// Get all dataset readers defined for this UaSubscriber component
        /// </summary>
        protected List<DataSetReaderDataType> GetAllDataSetReaders()
        {
            var readersList = new List<DataSetReaderDataType>();
            foreach (ReaderGroupDataType readerGroup in PubSubConnectionConfiguration.ReaderGroups)
            {
                readersList.AddRange(readerGroup.DataSetReaders);
            }
            return readersList;
        }

        /// <summary>
        /// Get all dataset writers defined for this UaPublisher component
        /// </summary>
        protected List<DataSetWriterDataType> GetWriterGroupsDataType()
        {
            var writerList = new List<DataSetWriterDataType>();

            foreach (WriterGroupDataType writerGroup in PubSubConnectionConfiguration.WriterGroups)
            {
                writerList.AddRange(writerGroup.DataSetWriters);
            }
            return writerList;
        }

        /// <summary>
        /// Get data set writer discovery responses
        /// </summary>
        protected IList<DataSetWriterConfigurationResponse> GetDataSetWriterDiscoveryResponses(
            ushort[] dataSetWriterIds)
        {
            var responses = new List<DataSetWriterConfigurationResponse>();

            var writerGroupsIds = PubSubConnectionConfiguration
                .WriterGroups.SelectMany(group => group.DataSetWriters)
                .Select(writer => writer.DataSetWriterId)
                .ToList();

            foreach (ushort dataSetWriterId in dataSetWriterIds)
            {
                var response = new DataSetWriterConfigurationResponse();

                if (!writerGroupsIds.Contains(dataSetWriterId))
                {
                    response.DataSetWriterIds = [dataSetWriterId];

                    response.StatusCodes = [StatusCodes.BadNotFound];
                }
                else
                {
                    response.DataSetWriterConfig = PubSubConnectionConfiguration.WriterGroups
                        .First(group =>
                            group.DataSetWriters
                            .First(writer => writer.DataSetWriterId == dataSetWriterId) != null);

                    response.DataSetWriterIds = [dataSetWriterId];

                    response.StatusCodes = [StatusCodes.Good];
                }

                responses.Add(response);
            }

            return responses;
        }

        /// <summary>
        /// Get the maximum KeepAlive value from all present WriterGroups
        /// </summary>
        protected double GetWriterGroupsMaxKeepAlive()
        {
            double maxKeepAlive = 0;
            foreach (WriterGroupDataType writerGroup in PubSubConnectionConfiguration.WriterGroups)
            {
                if (maxKeepAlive < writerGroup.KeepAliveTime)
                {
                    maxKeepAlive = writerGroup.KeepAliveTime;
                }
            }
            return maxKeepAlive;
        }

        /// <summary>
        /// Create and return the current DataSet for the provided dataSetWriter according to current WriterGroupPublishState
        /// </summary>
        protected DataSet CreateDataSet(
            DataSetWriterDataType dataSetWriter,
            WriterGroupPublishState state)
        {
            DataSet dataSet = null;
            //check if dataSetWriter enabled
            if (dataSetWriter.Enabled)
            {
                bool isDeltaFrame = state.IsDeltaFrame(dataSetWriter, out uint sequenceNumber);

                dataSet = Application.DataCollector.CollectData(dataSetWriter.DataSetName);

                if (dataSet != null)
                {
                    dataSet.SequenceNumber = sequenceNumber;
                    dataSet.IsDeltaFrame = isDeltaFrame;

                    if (isDeltaFrame)
                    {
                        dataSet = state.ExcludeUnchangedFields(dataSetWriter, dataSet);
                    }
                }
            }

            return dataSet;
        }

        /// <summary>
        /// Handler for <see cref="UaPubSubConfigurator.WriterGroupAdded"/> event.
        /// </summary>
        private void UaPubSubConfigurator_WriterGroupAdded(object sender, WriterGroupEventArgs e)
        {
            var pubSubConnectionDataType =
                Application.UaPubSubConfigurator
                    .FindObjectById(e.ConnectionId) as PubSubConnectionDataType;
            if (PubSubConnectionConfiguration == pubSubConnectionDataType)
            {
                var publisher = new UaPublisher(this, e.WriterGroupDataType);
                m_publishers.Add(publisher);
            }
        }
    }
}
