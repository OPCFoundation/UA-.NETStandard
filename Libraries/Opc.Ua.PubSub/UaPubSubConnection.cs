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
using Opc.Ua.PubSub.Configuration;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// This entity represents a working connection for PubSub
    /// </summary>
    internal abstract class UaPubSubConnection : IUaPubSubConnection,  IDisposable
    {
        #region Fields
        protected object m_lock = new object();
        private bool m_isRunning;
        private List<IUaPublisher> m_publishers;
        private PubSubConnectionDataType m_pubSubConnectionDataType;
        private UaPubSubApplication m_uaPubSubApplication;
        protected TransportProtocol m_transportProtocol = TransportProtocol.NotAvailable;
      //  protected UaPubSubMessageDecoder m_messageDecoder;
        #endregion

        #region Constructor
        /// <summary>
        /// Create new instance of UaPubSubConnection with PubSubConnectionDataType configuration data
        /// </summary>
        /// <param name="parentUaPubSubApplication"></param>
        /// <param name="pubSubConnectionDataType"></param>
        public UaPubSubConnection(UaPubSubApplication parentUaPubSubApplication, PubSubConnectionDataType pubSubConnectionDataType)
        {
            m_uaPubSubApplication = parentUaPubSubApplication;
            m_uaPubSubApplication.UaPubSubConfigurator.WriterGroupAdded += UaPubSubConfigurator_WriterGroupAdded;
            m_pubSubConnectionDataType = pubSubConnectionDataType;

            m_publishers = new List<IUaPublisher>();

            if (string.IsNullOrEmpty(pubSubConnectionDataType.Name))
            {
                pubSubConnectionDataType.Name = "<connection>";
            }
        }

        private void UaPubSubConfigurator_WriterGroupAdded(object sender, WriterGroupEventArgs e)
        {
            PubSubConnectionDataType pubSubConnectionDataType = m_uaPubSubApplication.UaPubSubConfigurator.FindObjectById(e.ConnectionId) 
                as PubSubConnectionDataType;
            if (m_pubSubConnectionDataType == pubSubConnectionDataType)
            {
                UaPublisher publisher = new UaPublisher(this, e.WriterGroupDataType);
                m_publishers.Add(publisher);
            }
        }

        #endregion

        #region Properties
        /// <summary>
        /// Get assigned transport protocol for this connection instance
        /// </summary>
        public TransportProtocol TransportProtocol
        {
            get { return m_transportProtocol; }
        }

        /// <summary>
        /// Get the configuration object for this PubSub connection
        /// </summary>
        public PubSubConnectionDataType PubSubConnectionConfiguration
        {
            get { return m_pubSubConnectionDataType; }
        }

        /// <summary>
        /// Get reference to <see cref="UaPubSubApplication"/>
        /// </summary>
        public UaPubSubApplication Application
        {
            get { return m_uaPubSubApplication; }
        }

        /// <summary>
        /// Get flag that indicates if the Connection is in running state
        /// </summary>
        public bool IsRunning
        {
            get { return m_isRunning; }
        }       
        #endregion

        #region Internal Properties
        /// <summary>
        /// Get the list of current publishers associated with this connection
        /// </summary>
        internal IReadOnlyCollection<IUaPublisher> Publishers
        {
            get { return m_publishers.AsReadOnly(); }
        }       
        #endregion

        #region Public Methods
        /// <summary>
        /// Start Publish/Subscribe jobs associated with this instance
        /// </summary>
        public void Start()
        {    
            lock (m_lock)
            {
                m_isRunning = true;
                foreach (var publisher in m_publishers)
                {
                    publisher.Start();
                }
            }
            InternalStart();
        }

        /// <summary>
        /// Stop Publish/Subscribe jobs associated with this instance
        /// </summary>
        public void Stop()
        {
            InternalStop();
            lock (m_lock)
            {
                m_isRunning = false;
                foreach (var publisher in m_publishers)
                {
                    publisher.Stop();
                }
            }
        }

        /// <summary>
        /// Determine if the connection has anything to publish -> at least one WriterDataSet is configured as enabled for current writer group
        /// </summary>
        /// <param name="writerGroupConfiguration"></param>
        /// <returns></returns>
        public bool CanPublish(WriterGroupDataType writerGroupConfiguration)
        {
            if (!m_isRunning)
            {
                return false;
            }
            // check if connection status is operational
            if (Application.UaPubSubConfigurator.FindStateForObject(m_pubSubConnectionDataType) != PubSubState.Operational)
            {
                return false;
            }

            if (writerGroupConfiguration.Enabled)
            {
                foreach (DataSetWriterDataType writer in writerGroupConfiguration.DataSetWriters)
                {
                    if (writer.Enabled)
                    {
                        return true;
                    }
                }
            }

            return false;
        }       
        
        /// <summary>
        /// Create the network message built from the provided writerGroupConfiguration
        /// </summary>
        /// <param name="writerGroupConfiguration"></param>
        /// <returns></returns>
        public abstract UaNetworkMessage CreateNetworkMessage(WriterGroupDataType writerGroupConfiguration);

        /// <summary>
        /// Publish the network message
        /// </summary>
        /// <param name="networkMessage"></param>
        /// <returns></returns>
        public abstract bool PublishNetworkMessage(UaNetworkMessage networkMessage);

        /// <summary>
        /// Get current list of dataset readers available in this UaSubscriber component
        /// </summary>
        public List<DataSetReaderDataType> GetOperationalDataSetReaders()
        {
            List<DataSetReaderDataType> readersList = new List<DataSetReaderDataType>();
            if (Application.UaPubSubConfigurator.FindStateForObject(m_pubSubConnectionDataType) != PubSubState.Operational)
            {
                return readersList;
            }
            foreach (ReaderGroupDataType readerGroup in m_pubSubConnectionDataType.ReaderGroups)
            {
                if (Application.UaPubSubConfigurator.FindStateForObject(readerGroup) == PubSubState.Operational)
                {
                    foreach (DataSetReaderDataType reader in readerGroup.DataSetReaders)
                    {
                        // check if reader is properlly configuread to receive data
                        if (reader.DataSetMetaData == null
                            || reader.DataSetMetaData.Fields == null
                            || reader.DataSetMetaData.Fields.Count == 0)
                        {
                            continue;
                        }
                        if (Application.UaPubSubConfigurator.FindStateForObject(reader) == PubSubState.Operational)
                        {
                            readersList.Add(reader);
                        }
                    }
                }
            }
            return readersList;
        }
        #endregion

        #region Protected Methods

        /// <summary>
        /// Initialize the connection object. Must be implemented by derived classes
        /// </summary>
        /// <returns></returns>
        protected abstract bool InternalInitialize();

        /// <summary>
        /// Perform specific Start tasks
        /// </summary>
        protected abstract void InternalStart();

        /// <summary>
        /// Perform specific Stop tasks
        /// </summary>
        protected abstract void InternalStop();

        /// <summary>
        /// Raises the <see cref="UaPubSubApplication.DataReceived"/> event.
        /// </summary>
        /// <param name="networkMessage"></param>
        /// <param name="source"></param>
        protected void RaiseNetworkMessageDataReceivedEvent(UaNetworkMessage networkMessage, string source)
        {
            if (networkMessage.ReceivedDataSets != null && networkMessage.ReceivedDataSets.Count > 0)
            {
                SubscribedDataEventArgs subscribedDataEventArgs = new SubscribedDataEventArgs() {
                    NetworkMessageSequenceNumber = networkMessage.SequenceNumber,
                    DataSets = networkMessage.ReceivedDataSets,
                    Source = source
                };

                //trigger notification for received subscribed data set
                Application.RaiseDataReceivedEvent(subscribedDataEventArgs);
                Utils.Trace(Utils.TraceMasks.Information,
                    "UaPubSubConnection.RaiseNetworkMessageDataReceivedEvent from source={0}, with {1} DataSets", source, subscribedDataEventArgs.DataSets.Count);
            }
            else
            {
                Utils.Trace(Utils.TraceMasks.Information,
                    "Message from source={0} cannot be decoded.", source);
            }
        }

        /// <summary>
        /// Get all dataset readers defined for this UaSubscriber component
        /// </summary>
        protected List<DataSetReaderDataType> GetAllDataSetReaders()
        {
            List<DataSetReaderDataType> readersList = new List<DataSetReaderDataType>();
            if (Application.UaPubSubConfigurator.FindStateForObject(m_pubSubConnectionDataType) != PubSubState.Operational)
            {
                return readersList;
            }
            foreach (ReaderGroupDataType readerGroup in m_pubSubConnectionDataType.ReaderGroups)
            {
                foreach (DataSetReaderDataType reader in readerGroup.DataSetReaders)
                {
                    readersList.Add(reader);
                }
            }
            return readersList;
        }

        /// <summary>
        /// Get the maximum KeepAlive value from all present WriterGroups
        /// </summary>
        protected double GetWriterGroupsMaxKeepAlive()
        {
            double maxKeepAlive = 0;
            foreach (WriterGroupDataType writerGroup in m_pubSubConnectionDataType.WriterGroups)
            {
                if (maxKeepAlive < writerGroup.KeepAliveTime)
                {
                    maxKeepAlive = writerGroup.KeepAliveTime;
                }
            }
            return maxKeepAlive;
        }
        #endregion

        #region IDisposable Implementation
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
                m_uaPubSubApplication.UaPubSubConfigurator.WriterGroupAdded -= UaPubSubConfigurator_WriterGroupAdded;
                Stop();
                // free managed resources
                foreach (UaPublisher publisher in m_publishers)
                {
                    publisher.Dispose();
                }
            }            
        }
        #endregion
    }
}
