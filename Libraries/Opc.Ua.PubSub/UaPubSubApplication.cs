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
using System.IO;
using Opc.Ua.PubSub.Configuration;
using Opc.Ua.PubSub.PublishedData;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// A class that runs an OPC UA PubSub application.
    /// </summary>
    public class UaPubSubApplication : IDisposable
    {
        #region Fields
        private object m_lock = new object();
        private List<IUaPubSubConnection> m_uaPubSubConnections;
        private DataCollector m_dataCollector;
        private IUaPubSubDataStore m_dataStore;
        private UaPubSubConfigurator m_uaPubSubConfigurator;
        #endregion

        #region Events
        /// <summary>
        /// Event that is triggered when the <see cref="UaPubSubApplication"/> receives and decodes subscribed DataSets
        /// </summary>
        public event EventHandler<SubscribedDataEventArgs> DataReceived;
        #endregion

        #region Constructors

        /// <summary>
        ///  Initializes a new instance of the <see cref="UaPubSubApplication"/> class.
        /// </summary>
        /// <param name="dataStore"> The current implementation of <see cref="IUaPubSubDataStore"/> used by this instance of pub sub application</param>
        private UaPubSubApplication(IUaPubSubDataStore dataStore = null)
        {
            m_uaPubSubConnections = new List<IUaPubSubConnection>();
            if (dataStore != null)
            {
                m_dataStore = dataStore;
            }
            else
            {
                m_dataStore = new UaPubSubDataStore();
            }
            m_dataCollector = new DataCollector(m_dataStore);
            m_uaPubSubConfigurator = new UaPubSubConfigurator();
            m_uaPubSubConfigurator.ConnectionAdded += UaPubSubConfigurator_ConnectionAdded;
            m_uaPubSubConfigurator.ConnectionRemoved += UaPubSubConfigurator_ConnectionRemoved;
            m_uaPubSubConfigurator.PublishedDataSetAdded += UaPubSubConfigurator_PublishedDataSetAdded;
            m_uaPubSubConfigurator.PublishedDataSetRemoved += UaPubSubConfigurator_PublishedDataSetRemoved;
        }

        #endregion

        #region Public Properties
        /// <summary>
        /// Get the list of SupportedTransportProfiles
        /// </summary>
        public static string[] SupportedTransportProfiles
        {
            get { return new string[] { Profiles.UadpTransport }; }
        }

        /// <summary>
        /// Get reference to the associated <see cref="UaPubSubConfigurator"/> instance.
        /// </summary>
        public UaPubSubConfigurator UaPubSubConfigurator {  get { return m_uaPubSubConfigurator; } }
        
        /// <summary>
        /// Get reference to current DataStore. Write here all node values needed to be published by this PubSubApplication
        /// </summary>
        public IUaPubSubDataStore DataStore {  get { return m_dataStore; } }
        #endregion

        #region Internal Properties
        /// <summary>
        /// Get the read only list of <see cref="UaPubSubConnection"/> created for this Application instance 
        /// </summary>
        internal ReadOnlyList<IUaPubSubConnection> PubSubConnections
        {
            get
            {
                return new ReadOnlyList<IUaPubSubConnection>(m_uaPubSubConnections);
            }
        }

        /// <summary>
        /// Get reference to current configured DataCollector for this UaPubSubApplication
        /// </summary>
        internal DataCollector DataCollector { get { return m_dataCollector; } }
        #endregion

        #region Public Static Create Methods
        /// <summary>
        /// Creates a new <see cref="UaPubSubApplication"/> and associates it with a custom implementation of <see cref="IUaPubSubDataStore"/>.
        /// </summary>
        /// <param name="dataStore"> The current implementation of <see cref="IUaPubSubDataStore"/> used by this instance of pub sub application</param>
        /// <returns>New instance of <see cref="UaPubSubApplication"/></returns>
        public static UaPubSubApplication Create(IUaPubSubDataStore dataStore)
        {     
            return Create(new PubSubConfigurationDataType(), dataStore);
        }

        /// <summary>
        /// Creates a new <see cref="UaPubSubApplication"/> by loading the configuration parameters from the specified path.
        /// </summary>
        /// <param name="configFilePath">The path of the configuration path.</param>
        /// <param name="dataStore"> The current implementation of <see cref="IUaPubSubDataStore"/> used by this instance of pub sub application</param>
        /// <returns>New instance of <see cref="UaPubSubApplication"/></returns>
        public static UaPubSubApplication Create(string configFilePath, IUaPubSubDataStore dataStore = null)
        {
            // validate input argument 
            if (configFilePath == null)
            {
                throw new ArgumentException(nameof(configFilePath));
            }
            if (!File.Exists(configFilePath))
            {
                throw new ArgumentException("The specified file {0} does not exist", configFilePath);
            }
            PubSubConfigurationDataType pubSubConfiguration = UaPubSubConfigurationHelper.LoadConfiguration(configFilePath);

            return Create(pubSubConfiguration, dataStore);
        }

        /// <summary>
        /// Creates a new <see cref="UaPubSubApplication"/> by loading the configuration parameters from the 
        /// specified <see cref="PubSubConfigurationDataType"/> parameter.
        /// </summary>
        /// <param name="pubSubConfiguration">The configuration object.</param>
        /// <param name="dataStore"> The current implementation of <see cref="IUaPubSubDataStore"/> used by this instance of pub sub application</param>
        /// <returns>New instance of <see cref="UaPubSubApplication"/></returns>
        public static UaPubSubApplication Create(PubSubConfigurationDataType pubSubConfiguration = null, IUaPubSubDataStore dataStore = null)
        {
            // if no argument received, start with empty configuration
            if (pubSubConfiguration == null)
            {
                pubSubConfiguration = new PubSubConfigurationDataType();
            }

            UaPubSubApplication uaPubSubApplication = new UaPubSubApplication(dataStore);
            uaPubSubApplication.m_uaPubSubConfigurator.LoadConfiguration(pubSubConfiguration);            
            return uaPubSubApplication;
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Start Publish/Subscribe jobs associated with this instance
        /// </summary>
        public void Start()
        {
            foreach(var connection in m_uaPubSubConnections)
            {
                connection.Start();
            }
        }

        /// <summary>
        /// Stop Publish/Subscribe jobs associated with this instance
        /// </summary>
        public void Stop()
        {
            foreach (var connection in m_uaPubSubConnections)
            {
                connection.Stop();
            }
        }        
        #endregion

        #region Internal Methods

        /// <summary>
        /// Raise DataReceived event
        /// </summary>
        /// <param name="e"></param>
        internal void RaiseDataReceivedEvent(SubscribedDataEventArgs e)
        {
            try
            {
                if (DataReceived != null)
                {
                    DataReceived(this, e);
                }
            }
            catch (Exception ex)
            {
                Utils.Trace(ex, "UaPubSubApplication.RaiseSubscriptionRecievedEvent");
            }
        }
        #endregion

        #region Private Methods - UaPubSubConfigurator event handlers
        /// <summary>
        /// Handler for <see cref="UaPubSubConfigurator.PublishedDataSetAdded"/> event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UaPubSubConfigurator_PublishedDataSetAdded(object sender, PublishedDataSetEventArgs e)
        {
            DataCollector.AddPublishedDataSet(e.PublishedDataSetDataType);
        }

        /// <summary>
        /// Handler for <see cref="UaPubSubConfigurator.PublishedDataSetRemoved"/> event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UaPubSubConfigurator_PublishedDataSetRemoved(object sender, PublishedDataSetEventArgs e)
        {
            DataCollector.RemovePublishedDataSet(e.PublishedDataSetDataType);
        }

        /// <summary>
        /// Handler for <see cref="UaPubSubConfigurator.ConnectionRemoved"/> event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UaPubSubConfigurator_ConnectionRemoved(object sender, ConnectionEventArgs e)
        {
            IUaPubSubConnection removedUaPubSubConnection = null;
            foreach (var connection in m_uaPubSubConnections)
            {
                if (connection.PubSubConnectionConfiguration.Equals(e.PubSubConnectionDataType))
                {
                    removedUaPubSubConnection = connection;
                    break;
                }
            }
            if (removedUaPubSubConnection != null)
            {
                m_uaPubSubConnections.Remove(removedUaPubSubConnection);
                removedUaPubSubConnection.Dispose();
            }
        }

        /// <summary>
        /// Handler for <see cref="UaPubSubConfigurator.ConnectionAdded"/> event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void UaPubSubConfigurator_ConnectionAdded(object sender, ConnectionEventArgs e)
        {
            UaPubSubConnection newUaPubSubConnection = ObjectFactory.CreateConnection(this, e.PubSubConnectionDataType);
            if (newUaPubSubConnection != null)
            {
                m_uaPubSubConnections.Add(newUaPubSubConnection);
            }
        }
        #endregion       

        #region IDisposable Implementation
        /// <summary>
        /// Releases all resources used by the current instance of the <see cref="UaPublisher"/> class.
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
                m_uaPubSubConfigurator.ConnectionAdded -= UaPubSubConfigurator_ConnectionAdded;
                m_uaPubSubConfigurator.ConnectionRemoved -= UaPubSubConfigurator_ConnectionRemoved;
                m_uaPubSubConfigurator.PublishedDataSetAdded -= UaPubSubConfigurator_PublishedDataSetAdded;
                m_uaPubSubConfigurator.PublishedDataSetRemoved -= UaPubSubConfigurator_PublishedDataSetRemoved;

                Stop();
                // free managed resources
                foreach(var connection in m_uaPubSubConnections)
                {
                    connection.Dispose();
                }
                m_uaPubSubConnections.Clear();
            }
        }       
        #endregion
    }
}
