/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: default immutable <see cref="IStoredNode"/> implementation.
    /// </summary>
    public sealed class StoredNode : IStoredNode
    {
        /// <summary>
        /// Creates a stored node from an identifier and a binary payload.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="payload">The binary-encoded node payload.</param>
        public StoredNode(NodeId nodeId, ByteString payload)
        {
            NodeId = nodeId;
            Payload = payload;
        }

        /// <inheritdoc/>
        public NodeId NodeId { get; }

        /// <inheritdoc/>
        public ByteString Payload { get; }
    }
}
