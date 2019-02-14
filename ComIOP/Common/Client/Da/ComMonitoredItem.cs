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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Opc.Ua.Server;


namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// A handle that describes how to access a node/attribute via an i/o manager.
    /// </summary>
    public class ComMonitoredItem : MonitoredItem
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with its node type.
        /// </summary>
        public ComMonitoredItem(
            IServerInternal server,
            INodeManager nodeManager,
            object mangerHandle,
            uint subscriptionId,
            uint id,
            Session session,
            ReadValueId itemToMonitor,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringMode monitoringMode,
            uint clientHandle,
            MonitoringFilter originalFilter,
            MonitoringFilter filterToUse,
            Range range,
            double samplingInterval,
            uint queueSize,
            bool discardOldest,
            double sourceSamplingInterval)
            : base(server,
                    nodeManager,
                    mangerHandle,
                    subscriptionId,
                    id,
                    session,
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
                    sourceSamplingInterval)
        {
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Publishes a single data change notifications.
        /// </summary>
        protected override bool Publish(OperationContext context,
            Queue<MonitoredItemNotification> notifications,
            Queue<DiagnosticInfo> diagnostics,
            DataValue value,
            ServiceResult error)
        {
            bool result = base.Publish(context, notifications, diagnostics, value, error);
            return result;
        }
        #endregion
    }
}
