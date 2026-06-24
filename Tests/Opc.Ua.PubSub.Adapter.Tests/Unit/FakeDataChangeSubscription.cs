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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Tests.Unit
{
    /// <summary>
    /// In-memory <see cref="IDataChangeSubscription"/> test double that
    /// records monitored items added to it, hands out incrementing client
    /// handles, and lets a test raise <see cref="DataChanged"/> notifications.
    /// </summary>
    internal sealed class FakeDataChangeSubscription : IDataChangeSubscription
    {
        private uint m_nextHandle = 1;

        public event EventHandler<DataChangeEventArgs>? DataChanged;

        public List<(NodeId NodeId, uint AttributeId, double SamplingMs)> MonitoredItems { get; }
            = [];

        public int ApplyChangesCount { get; private set; }

        public bool Disposed { get; private set; }

        public ValueTask<uint> AddMonitoredItemAsync(
            NodeId nodeId,
            uint attributeId,
            double samplingIntervalMs,
            CancellationToken ct = default)
        {
            uint handle = m_nextHandle++;
            MonitoredItems.Add((nodeId, attributeId, samplingIntervalMs));
            return new ValueTask<uint>(handle);
        }

        public ValueTask ApplyChangesAsync(CancellationToken ct = default)
        {
            ApplyChangesCount++;
            return default;
        }

        public void Raise(uint clientHandle, NodeId nodeId, DataValue value)
        {
            DataChanged?.Invoke(
                this, new DataChangeEventArgs(clientHandle, nodeId, value));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }
    }
}
