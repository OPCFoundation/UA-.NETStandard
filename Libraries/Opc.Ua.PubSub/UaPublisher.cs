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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// A class responsible with calculating and triggering publish messages.
    /// </summary>
    internal class UaPublisher : IUaPublisher, IDisposable
    {
        #region Fields
        private const int MinPublishingInterval = 10;
        private object m_lock = new object();
        // event used to trigger publish 
        private ManualResetEvent m_shutdownEvent;

        private IUaPubSubConnection m_pubSubConnection;
        private WriterGroupDataType m_writerGroupConfiguration;
        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="UaPublisher"/> class.
        /// </summary>
        internal UaPublisher(IUaPubSubConnection pubSubConnection, WriterGroupDataType writerGroupConfiguration)
        {
            m_pubSubConnection = pubSubConnection;
            m_writerGroupConfiguration = writerGroupConfiguration;            

            Initialize();
        }

        #endregion

        #region Public Properties
        /// <summary>
        /// Get reference to the associated parent <see cref="IUaPubSubConnection"/> instance.
        /// </summary>
        public IUaPubSubConnection PubSubConnection
        {
            get { return m_pubSubConnection; }
        }

        /// <summary>
        /// Get reference to the associated configuration object, the <see cref="WriterGroupDataType"/> instance.
        /// </summary>
        public WriterGroupDataType WriterGroupConfiguration
        {
            get { return m_writerGroupConfiguration; }
        }
        #endregion
        
        #region Public Methods

        /// <summary>
        /// Starts the publisher and makes it ready to send data.
        /// </summary>
        public void Start()
        {
            lock (m_lock)
            {
                m_shutdownEvent.Reset();

                Task.Run(() =>
                {
                    PublishData();
                });
            }
        }

        /// <summary>
        /// stop the publishing thread.
        /// </summary>
        public virtual void Stop()
        {
            lock (m_lock)
            {
                m_shutdownEvent.Set();
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_shutdownEvent = new ManualResetEvent(true);
        }

        /// <summary>
        /// Periodically checks if there is data to publish.
        /// </summary>
        private void PublishData()
        {
            try
            {
                do
                {
                    int sleepCycle = 0;

                    lock (m_lock)
                    {
                        if (m_writerGroupConfiguration != null)
                        {
                            sleepCycle = Convert.ToInt32(m_writerGroupConfiguration.PublishingInterval);
                        }
                    }

                    if (sleepCycle < MinPublishingInterval)
                    {
                        sleepCycle = MinPublishingInterval;
                    }

                    if (m_shutdownEvent.WaitOne(sleepCycle))
                    {
                        Utils.Trace(Utils.TraceMasks.Information, "UaPublisher: Publish Thread Exited Normally.");
                        break;
                    }

                    lock (m_lock)
                    {
                        if (m_pubSubConnection.CanPublish(m_writerGroupConfiguration))
                        {
                            // call on a new thread
                            Task.Run(() =>
                            {
                                PublishMessage();
                            });
                        }
                    }                    
                }
                while (true);
            }
            catch (Exception e)
            {
                // Unexpected exception in publish thread!
                Utils.Trace(e, "UaPublisher: Publish Thread Exited Unexpectedly");
            }
        }

        /// <summary>
        /// Generate and publish a message
        /// </summary>
        private void PublishMessage()
        {
            try
            {
                UaNetworkMessage uaNetworkMessage = m_pubSubConnection.CreateNetworkMessage(m_writerGroupConfiguration);
                if (uaNetworkMessage != null)
                {
                    bool success = m_pubSubConnection.PublishNetworkMessage(uaNetworkMessage);
                    Utils.Trace(Utils.TraceMasks.Information, 
                        "UaPublisher.PublishNetworkMessage, WriterGroupId:{0}; success = {1}", m_writerGroupConfiguration.WriterGroupId, success.ToString());
                }
            }
            catch (Exception e)
            {
                // Unexpected exception in PublishMessage
                Utils.Trace(e, "UaPublisher.PublishMessage");
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
                Stop();
                // free managed resources
                m_shutdownEvent.Dispose();
            }
        }
        #endregion
    }
}
