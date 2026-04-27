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

namespace Opc.Ua.Client.Subscriptions.MonitoredItems
{
    using Microsoft.Extensions.Options;
    using Opc.Ua.Client.Services;

    /// <summary>
    /// Context for monitored item manager. The monitored item
    /// manager manages the state of the monitored items in the
    /// subscription.
    /// </summary>
    internal interface IMonitoredItemManagerContext
    {
        /// <summary>
        /// Subscription id the monitored items are managed for
        /// </summary>
        uint Id { get; }

        /// <summary>
        /// Monitored item services
        /// </summary>
        IMonitoredItemServiceSet MonitoredItemServiceSet { get; }

        /// <summary>
        /// Method call services
        /// </summary>
        IMethodServiceSet MethodServiceSet { get; }

        /// <summary>
        /// Create monitored item
        /// </summary>
        /// <param name="name"></param>
        /// <param name="options"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        MonitoredItem CreateMonitoredItem(string name,
            IOptionsMonitor<MonitoredItemOptions> options,
            IMonitoredItemContext context);

        /// <summary>
        /// Update
        /// </summary>
        void Update();
    }
}
