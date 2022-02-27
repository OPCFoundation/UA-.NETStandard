/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using Opc.Ua;
using Opc.Ua.Server;

namespace MemoryBuffer
{
    /// <summary>
    /// Provides a basic monitored item implementation which does not support queuing.
    /// </summary>
    public class MemoryBufferMonitoredItem : MonitoredItem
    {
		/// <summary>
		/// Initializes the object with its node type.
		/// </summary>
        public MemoryBufferMonitoredItem(
            IServerInternal     server,
            INodeManager        nodeManager,
            object              mangerHandle,
            uint                offset,
            uint                subscriptionId,
            uint                id,
            ReadValueId         itemToMonitor,
            DiagnosticsMasks    diagnosticsMasks,
            TimestampsToReturn  timestampsToReturn,
            MonitoringMode      monitoringMode,
            uint                clientHandle,
            MonitoringFilter    originalFilter,
            MonitoringFilter    filterToUse,
            Opc.Ua.Range        range,
            double              samplingInterval,
            uint                queueSize,
            bool                discardOldest,
            double              minimumSamplingInterval)
        :
            base(
                server,
                nodeManager,
                mangerHandle,
                subscriptionId,
                id,
                itemToMonitor,
                diagnosticsMasks,
                timestampsToReturn,
                monitoringMode,
                clientHandle,
                originalFilter,
                filterToUse,
                range,
                samplingInterval,
                queueSize,
                discardOldest,
                minimumSamplingInterval)
		{
            m_offset = offset;
        }

        /// <summary>
        /// Modifies the monitored item parameters,
        /// </summary>
        public ServiceResult Modify(
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            uint clientHandle,
            double samplingInterval)
        {
            return base.ModifyAttributes(diagnosticsMasks,
                timestampsToReturn,
                clientHandle,
                null,
                null,
                null,
                samplingInterval,
                0,
                false);
        }

        /// <summary>
        /// The offset in the memory buffer.
        /// </summary>
        public uint Offset
        {
            get
            {
                return m_offset;
            }
        }

        private uint m_offset;
    }
}
