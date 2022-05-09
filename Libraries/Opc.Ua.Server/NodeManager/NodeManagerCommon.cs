/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server
{
    /// <summary>
    /// A class which contains common NodeManager functionality
    /// </summary>
    public abstract class NodeManagerCommon
    {
        /// <summary>
        /// Transfers a set of MonitoredItems
        /// </summary>
        /// <param name="systemContext"></param>
        /// <param name="sendInitialValues"></param>
        /// <param name="monitoredItems"></param>
        /// <param name="processedItems"></param>
        /// <param name="errors"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        protected virtual IList<IMonitoredItem> TransferMonitoredItems(
            ISystemContext systemContext,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            if (systemContext == null) throw new ArgumentNullException(nameof(systemContext));
            if (monitoredItems == null) throw new ArgumentNullException(nameof(monitoredItems));
            if (processedItems == null) throw new ArgumentNullException(nameof(processedItems));

            IList<IMonitoredItem> transferredItems = new List<IMonitoredItem>();

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                if (sendInitialValues)
                {
                    (errors[ii], processedItems[ii], transferredItems) = DoReadInitialValue(
                        (ServerSystemContext)systemContext,
                        monitoredItems[ii],
                        errors[ii],
                        processedItems[ii],
                        transferredItems);
                }
                else
                {
                    errors[ii] = StatusCodes.Good;
                }
            }

            return transferredItems;

        }

        /// <summary>
        /// Initiates resending data for all monitored items
        /// </summary>
        /// <param name="systemContext">The context.</param>
        /// <param name="dataChangeMonitoredItems">The datachange monitored items for which resending is initiated</param>
        /// <exception cref="ArgumentNullException"></exception>
        protected virtual void ResendData(ISystemContext systemContext, List<IDataChangeMonitoredItem2> dataChangeMonitoredItems)
        {
            if (systemContext == null) throw new ArgumentNullException(nameof(systemContext));
            if (dataChangeMonitoredItems == null) throw new ArgumentNullException(nameof(dataChangeMonitoredItems));

            for (int ii = 0; ii < dataChangeMonitoredItems.Count; ii++)
            {
                DoReadInitialValue(
                    (ServerSystemContext)systemContext,
                    dataChangeMonitoredItems[ii],
                    null,
                    false,
                    null);
            }
        }


        /// <summary>
        /// NodeManager specific implementation for reading the initial value into the monitored node
        /// </summary>
        /// <param name="systemContext"></param>
        /// <param name="monitoredItem"></param>
        /// <param name="errorCode"></param>
        /// <param name="processedItem"></param>
        /// <param name="transferredItems"></param>
        /// <returns></returns>
        protected abstract Tuple<ServiceResult, bool, IList<IMonitoredItem>> DoReadInitialValue(
            ServerSystemContext systemContext,
            IMonitoredItem monitoredItem,
            ServiceResult errorCode,
            bool processedItem,
            IList<IMonitoredItem> transferredItems);
    }
}
