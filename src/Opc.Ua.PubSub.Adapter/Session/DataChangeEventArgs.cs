/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
