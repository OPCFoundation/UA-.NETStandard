#if OPCUA_CLIENT_V2
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
    using System;

    /// <summary>
    /// The current monitored item inside a subscription
    /// </summary>
    public interface IMonitoredItem
    {
        /// <summary>
        /// Name of the item in the subscription
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Order of the item in the subscription
        /// </summary>
        uint Order { get; }

        /// <summary>
        /// The identifier assigned by the server.
        /// </summary>
        uint ServerId { get; }

        /// <summary>
        /// Whether the item has been created on the server.
        /// </summary>
        bool Created { get; }

        /// <summary>
        /// The last error result associated with the item
        /// </summary>
        ServiceResult Error { get; }

        /// <summary>
        /// The filter result of the last change applied.
        /// </summary>
        MonitoringFilterResult? FilterResult { get; }

        /// <summary>
        /// The monitoring mode.
        /// </summary>
        MonitoringMode CurrentMonitoringMode { get; }

        /// <summary>
        /// The sampling interval.
        /// </summary>
        TimeSpan CurrentSamplingInterval { get; }

        /// <summary>
        /// The length of the queue used to buffer values.
        /// </summary>
        uint CurrentQueueSize { get; }

        /// <summary>
        /// The identifier assigned by the client.
        /// </summary>
        uint ClientHandle { get; }
    }
}
#endif
