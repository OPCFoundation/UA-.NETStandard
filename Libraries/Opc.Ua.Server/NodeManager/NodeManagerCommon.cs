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
        /// <param name="sendInitialValues">Whether the subscription should send initial values after transfer.</param>
        /// <param name="monitoredItems">The set of monitoring items to update.</param>
        /// <param name="processedItems">The list of bool with items that were already processed.</param>
        /// <param name="errors">Any errors.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        protected virtual IList<IMonitoredItem> TransferMonitoredItems(
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<bool> processedItems,
            IList<ServiceResult> errors)
        {
            if (monitoredItems == null) throw new ArgumentNullException(nameof(monitoredItems));
            if (processedItems == null) throw new ArgumentNullException(nameof(processedItems));

            IList<IMonitoredItem> transferredItems = new List<IMonitoredItem>();

            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                if (sendInitialValues)
                {
                    processedItems[ii] = DoCollectTransferredMonitoredItems(
                        monitoredItems[ii],
                        processedItems[ii],
                        transferredItems);
                }

                if (processedItems[ii] == true)
                {
                    errors[ii] = StatusCodes.Good;
                }
            }

            return transferredItems;
        }

        /// <summary>
        /// NodeManager specific implementation for reading the initial value into the monitored node
        /// </summary>
        /// <param name="monitoredItem">The monitoring item to update.</param>
        /// <param name="processedItem">Has the item allready been processed.</param>
        /// <param name="transferredItems">The transferred monitored items.</param>
        /// <returns></returns>
        protected abstract bool DoCollectTransferredMonitoredItems(
            IMonitoredItem monitoredItem,
            bool processedItem,
            IList<IMonitoredItem> transferredItems);
    }
}
