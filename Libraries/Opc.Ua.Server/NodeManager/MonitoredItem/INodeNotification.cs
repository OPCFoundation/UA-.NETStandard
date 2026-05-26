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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Base interface for node notifications sent to the channel.
    /// </summary>
    internal interface INodeNotification
    {
        ISystemContext Context { get; }
    }

    /// <summary>
    /// Represents a snapshot of the changing attributes of a node.
    /// </summary>
    internal class DataChangeSnapshot : INodeNotification
    {
        public ISystemContext Context { get; set; } = null!;
        public NodeId NodeId { get; set; }
        public NodeStateChangeMasks Changes { get; set; }

        /// <summary>
        /// Pre-read raw <see cref="DataValue"/>s keyed by attribute id, captured without
        /// any index range or data encoding applied. Each consumer (monitored item) applies
        /// its own <see cref="IDataChangeMonitoredItem2.IndexRange"/> and
        /// <see cref="IDataChangeMonitoredItem2.DataEncoding"/> at queue time.
        /// </summary>
        public Dictionary<uint, DataValue> AttributeSnapshots { get; set; } = new();
    }

    /// <summary>
    /// Represents a snapshot of a node event.
    /// </summary>
    internal class EventSnapshot : INodeNotification
    {
        public ISystemContext Context { get; set; } = null!;
        public IFilterTarget EventTargetSnapshot { get; set; } = null!;
    }
}
