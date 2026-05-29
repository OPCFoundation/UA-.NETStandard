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

using System;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Cache key for an event-delivery <see cref="PermissionType.ReceiveEvents"/>
    /// verdict scoped to a single monitored item and a single
    /// (EventTypeId, SourceNode) pair. Used by
    /// <see cref="MonitoredNode2"/> so a busy notifier does not re-run the
    /// two role-permission lookups required by Part 3 §8.55 on every
    /// queued event.
    /// </summary>
    internal readonly struct EventPermissionCacheKey : IEquatable<EventPermissionCacheKey>
    {
        /// <summary>
        /// The id of the receiving monitored item.
        /// </summary>
        public uint MonitoredItemId { get; }

        /// <summary>
        /// The EventTypeId carried in the event payload.
        /// </summary>
        public NodeId EventTypeId { get; }

        /// <summary>
        /// The SourceNode carried in the event payload.
        /// </summary>
        public NodeId SourceNodeId { get; }

        /// <summary>
        /// Creates a new key.
        /// </summary>
        public EventPermissionCacheKey(uint monitoredItemId, NodeId eventTypeId, NodeId sourceNodeId)
        {
            MonitoredItemId = monitoredItemId;
            EventTypeId = eventTypeId;
            SourceNodeId = sourceNodeId;
        }

        /// <inheritdoc/>
        public bool Equals(EventPermissionCacheKey other)
        {
            return MonitoredItemId == other.MonitoredItemId
                && EventTypeId.Equals(other.EventTypeId)
                && SourceNodeId.Equals(other.SourceNodeId);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is EventPermissionCacheKey other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(MonitoredItemId, EventTypeId, SourceNodeId);
        }
    }
}
