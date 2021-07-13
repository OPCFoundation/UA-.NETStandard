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
using System.Threading;
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
        private DateTime m_nextPublishTime = DateTime.MinValue;
        private const int kMinPublishingInterval = 10;
        private object m_lock = new object();
        // event used to trigger publish 

        private CancellationTokenSource m_cancellationToken = new CancellationTokenSource();

        private IUaPubSubConnection m_pubSubConnection;
        private WriterGroupDataType m_writerGroupConfiguration;
        private WriterGroupPublishState m_writerGroupPublishState;
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
            m_writerGroupPublishState = new WriterGroupPublishState();

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

                if (m_cancellationToken != null)
                {
                    m_cancellationToken.Dispose();
                    m_cancellationToken = null;
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
            Task.Run(() => PublishData());
            Utils.Trace("The UaPublisher for WriterGroup '{0}' was started.", m_writerGroupConfiguration.Name);
        }

        /// <summary>
        /// Stop the publishing thread.
        /// </summary>
        public virtual void Stop()
        {
            lock (m_lock)
            {
                m_cancellationToken?.Cancel();
            }

            Utils.Trace("The UaPublisher for WriterGroup '{0}' was stopped.", m_writerGroupConfiguration.Name);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Sets private members to default values.
        /// </summary>
        private void Initialize()
        {
        }

        /// <summary>
        /// Periodically checks if there is data to publish.
        /// </summary>
        private async Task PublishData()
        {
            do
            {
                int sleepCycle = 0;
                DateTime now = DateTime.UtcNow;
                DateTime nextPublishTime = DateTime.MinValue;

                lock (m_lock)
                {
                    if (m_writerGroupConfiguration != null)
                    {
                        sleepCycle = Convert.ToInt32(m_writerGroupConfiguration.PublishingInterval);
                    }

                    nextPublishTime = m_nextPublishTime;
                }

                if (nextPublishTime > now)
                {
                    sleepCycle = (int)Math.Min((nextPublishTime - now).TotalMilliseconds, sleepCycle);
                    sleepCycle = (int)Math.Max(kMinPublishingInterval, sleepCycle);
                    await Task.Delay(TimeSpan.FromMilliseconds(sleepCycle), m_cancellationToken.Token).ConfigureAwait(false);
                }

                lock (m_lock)
                {
                    var nextCycle = Convert.ToInt32(m_writerGroupConfiguration.PublishingInterval);
                    m_nextPublishTime = DateTime.UtcNow.AddMilliseconds(nextCycle);

                    if (m_pubSubConnection.CanPublish(m_writerGroupConfiguration))
                    {
                        // call on a new thread
                        Task.Run(() => {
                            PublishMessages();
                        });
                    }
                }
            }
            while (true);
        }

        /// <summary>
        /// Generate and publish a messages
        /// </summary>
        private void PublishMessages()
        {
            try
            {
                IList<UaNetworkMessage> networkMessages = m_pubSubConnection.CreateNetworkMessages(m_writerGroupConfiguration, m_writerGroupPublishState);
                if (networkMessages != null)
                {
                    foreach (UaNetworkMessage uaNetworkMessage in networkMessages)
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
