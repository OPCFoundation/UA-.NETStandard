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

namespace Opc.Ua.Server
{
    /// <summary>
    /// A factory for <see cref="IDataChangeMonitoredItemQueue"> and </see> <see cref="IEventMonitoredItemQueue"/>
    /// </summary>
    public class MonitoredItemQueueFactory : IMonitoredItemQueueFactory
    {
        /// <inheritdoc/>
        public bool SupportsDurableQueues => false;

        /// <summary>
        /// Create monitored item queue factory
        /// </summary>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public MonitoredItemQueueFactory(ITelemetryContext telemetry)
        {
            m_telemetry = telemetry;
        }

        /// <inheritdoc/>
        public IDataChangeMonitoredItemQueue CreateDataChangeQueue(
            bool isDurable,
            uint monitoredItemId)
        {
            return new DataChangeMonitoredItemQueue(isDurable, monitoredItemId, m_telemetry);
        }

        /// <inheritdoc/>
        public IEventMonitoredItemQueue CreateEventQueue(bool isDurable, uint monitoredItemId)
        {
            return new EventMonitoredItemQueue(isDurable, monitoredItemId, m_telemetry);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Overridable method to dispose of resources.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            //only needed for managed resources
        }

        private readonly ITelemetryContext m_telemetry;
    }
}
