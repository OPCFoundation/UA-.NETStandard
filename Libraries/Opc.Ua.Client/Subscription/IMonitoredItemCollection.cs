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

using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    /// <summary>
    /// Collection of managed monitored items
    /// </summary>
    public interface IMonitoredItemCollection
    {
        /// <summary>
        /// Monitored item count
        /// </summary>
        uint Count { get; }

        /// <summary>
        /// Monitored items
        /// </summary>
        IEnumerable<IMonitoredItem> Items { get; }

        /// <summary>
        /// Try get monitored item by client handle
        /// </summary>
        /// <param name="clientHandle"></param>
        /// <param name="monitoredItem"></param>
        /// <returns></returns>
        bool TryGetMonitoredItemByClientHandle(uint clientHandle,
            out IMonitoredItem? monitoredItem);

        /// <summary>
        /// Try get monitored item by name
        /// </summary>
        /// <param name="name"></param>
        /// <param name="monitoredItem"></param>
        /// <returns></returns>
        bool TryGetMonitoredItemByName(string name,
            out IMonitoredItem? monitoredItem);

        /// <summary>
        /// Try add monitored item
        /// </summary>
        /// <param name="name"></param>
        /// <param name="options"></param>
        /// <param name="monitoredItem"></param>
        /// <returns></returns>
        bool TryAdd(string name,
            IOptionsMonitor<MonitoredItemOptions> options,
            out IMonitoredItem? monitoredItem);

        /// <summary>
        /// Try remove monitored item
        /// </summary>
        /// <param name="clientHandle"></param>
        /// <returns></returns>
        bool TryRemove(uint clientHandle);

        /// <summary>
        /// Update the state of the subscription. This applies the
        /// state provided to the entire subscription, adding and
        /// removing items that are not in the state, as well
        /// as updating any state of items that are in the state.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        IReadOnlyList<IMonitoredItem> Update(IReadOnlyList<(string Name,
            IOptionsMonitor<MonitoredItemOptions> Options)> state);
    }
}
