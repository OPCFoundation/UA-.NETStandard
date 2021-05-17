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
using System.Threading.Tasks;
using System.Timers;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// A class responsible with calculating and triggering publish messages.
    /// </summary>
    internal class UaPublisher : IUaPublisher
    {
        #region Fields
        private const int kMinPublishingInterval = 10;
        private object m_lock = new object();
        // event used to trigger publish 

        private Timer m_PublishingTimer;
        private ElapsedEventHandler m_periodicPublishHandler = null;

        private IUaPubSubConnection m_pubSubConnection;
        private WriterGroupDataType m_writerGroupConfiguration;
        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="UaPublisher"/> class.
        /// </summary>
        internal UaPublisher(IUaPubSubConnection pubSubConnection, WriterGroupDataType writerGroupConfiguration)
        {
            if (pubSubConnection == null)
            {
                throw new ArgumentNullException(nameof(pubSubConnection));
            }
            if (writerGroupConfiguration == null)
            {
                throw new ArgumentNullException(nameof(writerGroupConfiguration));
            }

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
                if (m_PublishingTimer != null)
                {
                    Utils.SilentDispose(m_PublishingTimer);
                    m_PublishingTimer = null;
                }
            }
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// Starts the publisher and makes it ready to send data.
        /// </summary>
        public void Start()
        {
            StartPublishingTimer();
            Utils.Trace("The UaPublisher for WriterGroup '{0}' was started.", m_writerGroupConfiguration.Name);
        }

        /// <summary>
        /// Stop the publishing thread.
        /// </summary>
        public virtual void Stop()
        {
            StopPublishingTimer();
            Utils.Trace("The UaPublisher for WriterGroup '{0}' was stopped.", m_writerGroupConfiguration.Name);
        }
        #endregion

        #region Private Methods

        /// <summary>
        /// Start the publish timer.
        /// </summary>
        private void StartPublishingTimer()
        {
            int sleepCycle = 0;

            lock (m_lock)
            {
                if (m_PublishingTimer != null)
                {
                    m_PublishingTimer.Dispose();
                    m_PublishingTimer = null;
                }

                m_PublishingTimer = new Timer();

                if (m_writerGroupConfiguration != null)
                {
                    sleepCycle = Convert.ToInt32(m_writerGroupConfiguration.PublishingInterval);
                }
            }

            if (sleepCycle < kMinPublishingInterval)
            {
                sleepCycle = kMinPublishingInterval;
            }

            lock (m_lock)
            {
                m_PublishingTimer.Elapsed += m_periodicPublishHandler;
                m_PublishingTimer.Interval = sleepCycle;
                m_PublishingTimer.Enabled = true;
            }
        }

        /// <summary>
        /// Stop the publish timer.
        /// </summary>
        private void StopPublishingTimer()
        {
            lock (m_lock)
            {
                if (m_PublishingTimer != null)
                {
                    m_PublishingTimer.Elapsed -= m_periodicPublishHandler;
                    m_PublishingTimer.Enabled = false;
                }
            }
        }

        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
            m_periodicPublishHandler = new ElapsedEventHandler(PeriodicTimerPublishData);
        }

        /// <summary>
        /// Periodically checks if there is data to publish.
        /// </summary>
        private void PeriodicTimerPublishData(object source, ElapsedEventArgs e)
        {
            try
            {
                lock (m_lock)
                {
                    if (m_pubSubConnection.CanPublish(m_writerGroupConfiguration))
                    {
                        // call on a new thread
                        Task.Run(() =>
                        {
                            PublishMessages();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception in publish thread!
                Utils.Trace(ex, "UaPublisher: PeriodicPublishData Exited Unexpectedly");
            }
        }

        /// <summary>
        /// Generate and publish a messages
        /// </summary>
        private void PublishMessages()
        {
            try
            {
                IList<UaNetworkMessage> networkMessages = m_pubSubConnection.CreateNetworkMessages(m_writerGroupConfiguration);
                if (networkMessages != null)
                {
                    foreach(UaNetworkMessage uaNetworkMessage in networkMessages)
                    {
                        if (uaNetworkMessage != null)
                        {
                            bool success = m_pubSubConnection.PublishNetworkMessage(uaNetworkMessage);
                            Utils.Trace(Utils.TraceMasks.Information,
                                "UaPublisher.PublishNetworkMessage, WriterGroupId:{0}; success = {1}", m_writerGroupConfiguration.WriterGroupId, success.ToString());
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // Unexpected exception in PublishMessage
                Utils.Trace(e, "UaPublisher.PublishMessage");
            }
        }
        #endregion
    }
}
