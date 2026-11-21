// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            return MonitoredItemId == other.MonitoredItemId &&
                EventTypeId.Equals(other.EventTypeId) &&
                SourceNodeId.Equals(other.SourceNodeId);
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
