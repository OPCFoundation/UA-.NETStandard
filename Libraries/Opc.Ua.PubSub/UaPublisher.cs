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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// A class responsible with calculating and triggering publish messages.
    /// </summary>
    internal class UaPublisher : IUaPublisher
    {
        private readonly Lock m_lock = new();
        private readonly ILogger m_logger;
        private readonly WriterGroupPublishState m_writerGroupPublishState;

        /// <summary>
        /// the component that triggers the publish messages
        /// </summary>
        private readonly IntervalRunner m_intervalRunner;

        /// <summary>
        /// Initializes a new instance of the <see cref="UaPublisher"/> class.
        /// </summary>
        internal UaPublisher(
            IUaPubSubConnection pubSubConnection,
            WriterGroupDataType writerGroupConfiguration,
            ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<UaPublisher>();
            PubSubConnection = pubSubConnection ??
                throw new ArgumentNullException(nameof(pubSubConnection));
            WriterGroupConfiguration =
                writerGroupConfiguration ??
                throw new ArgumentNullException(nameof(writerGroupConfiguration));
            m_writerGroupPublishState = new WriterGroupPublishState();

            m_intervalRunner = new IntervalRunner(
                WriterGroupConfiguration.Name,
                WriterGroupConfiguration.PublishingInterval,
                CanPublish,
                PublishMessages,
                telemetry);
        }

        /// <summary>
        /// Get reference to the associated parent <see cref="IUaPubSubConnection"/> instance.
        /// </summary>
        public IUaPubSubConnection PubSubConnection { get; }

        /// <summary>
        /// Get reference to the associated configuration object, the <see cref="WriterGroupDataType"/> instance.
        /// </summary>
        public WriterGroupDataType WriterGroupConfiguration { get; }

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

                m_intervalRunner.Dispose();
            }
        }

        /// <summary>
        /// Starts the publisher and makes it ready to send data.
        /// </summary>
        public void Start()
        {
            m_intervalRunner.Start();
            m_logger.LogInformation(
                "The UaPublisher for WriterGroup '{Name}' was started.",
                WriterGroupConfiguration.Name);
        }

        /// <summary>
        /// Stop the publishing thread.
        /// </summary>
        public virtual void Stop()
        {
            m_intervalRunner.Stop();

            m_logger.LogInformation(
                "The UaPublisher for WriterGroup '{Name}' was stopped.",
                WriterGroupConfiguration.Name);
        }

        /// <summary>
        /// Decide if the connection can publish
        /// </summary>
        private bool CanPublish()
        {
            lock (m_lock)
            {
                return PubSubConnection.CanPublish(WriterGroupConfiguration);
            }
        }

        /// <summary>
        /// Generate and publish the messages
        /// </summary>
        private void PublishMessages()
        {
            try
            {
                IList<UaNetworkMessage> networkMessages = PubSubConnection.CreateNetworkMessages(
                    WriterGroupConfiguration,
                    m_writerGroupPublishState);
                if (networkMessages != null)
                {
                    foreach (UaNetworkMessage uaNetworkMessage in networkMessages)
                    {
                        if (uaNetworkMessage != null)
                        {
                            bool success = PubSubConnection.PublishNetworkMessage(uaNetworkMessage);
                            m_logger.LogInformation(
                                "UaPublisher - PublishNetworkMessage, WriterGroupId:{WriterGroupId}; success = {Success}",
                                WriterGroupConfiguration.WriterGroupId,
                                success);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // Unexpected exception in PublishMessages
                m_logger.LogError(e, "UaPublisher.PublishMessages");
            }
        }
    }
}
