/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.PubSub.Adapter.Session
{
    /// <summary>
    /// Carries a single data change reported by an
    /// <see cref="IDataChangeSubscription"/> for one of its monitored
    /// items.
    /// </summary>
    public sealed class DataChangeEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new <see cref="DataChangeEventArgs"/>.
        /// </summary>
        /// <param name="clientHandle">
        /// The client handle of the monitored item that changed.
        /// </param>
        /// <param name="nodeId">
        /// The node identifier the monitored item observes.
        /// </param>
        /// <param name="value">
        /// The latest data value reported by the server.
        /// </param>
        public DataChangeEventArgs(uint clientHandle, NodeId nodeId, DataValue value)
        {
            ClientHandle = clientHandle;
            NodeId = nodeId;
            Value = value;
        }

        /// <summary>
        /// The client handle of the monitored item that changed, as returned by
        /// <see cref="IDataChangeSubscription.AddMonitoredItemAsync"/>.
        /// </summary>
        public uint ClientHandle { get; }

        /// <summary>
        /// The node identifier the monitored item observes.
        /// </summary>
        public NodeId NodeId { get; }

        /// <summary>
        /// The latest data value reported by the server for the monitored item.
        /// </summary>
        public DataValue Value { get; }
    }
}
