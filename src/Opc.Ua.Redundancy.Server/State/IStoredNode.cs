// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a portable, serialized representation of a single node held in an
    /// <see cref="INodeStateStore"/>. The <see cref="Payload"/> is produced
    /// by <c>NodeState.SaveAsBinary</c> and consumed by
    /// <c>NodeState.LoadAsBinary</c>, so the store never needs to reason
    /// about node types (AOT-safe, reflection-free).
    /// </summary>
    public interface IStoredNode
    {
        /// <summary>
        /// The identifier of the stored node.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// The opaque, binary-encoded node payload.
        /// </summary>
        ByteString Payload { get; }
    }
}
