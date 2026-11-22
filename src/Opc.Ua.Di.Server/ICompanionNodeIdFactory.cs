// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Di.Server
{
    /// <summary>
    /// Marks a thread-safe instance allocator shared by companion builders on one DI owner.
    /// </summary>
    /// <remarks>
    /// Allocations must remain unique while instances are staged and use the configured
    /// application namespace. All builders sharing an owner must select that same namespace.
    /// Existing family-specific allocator markers can derive from this marker.
    /// </remarks>
    public interface ICompanionNodeIdFactory : INodeIdFactory
    {
    }

    /// <summary>
    /// Releases temporary allocations after a companion build succeeds or is rolled back.
    /// </summary>
    /// <remarks>
    /// This optional capability does not add members to existing allocator marker contracts.
    /// The allocator must only release reservations still owned by the supplied node.
    /// </remarks>
    public interface ICompanionNodeIdReservationOwner
    {
        /// <summary>
        /// Releases the node's uncommitted reservations without removing registered address-space nodes.
        /// </summary>
        /// <param name="node">The node whose own reservations may be released.</param>
        void ReleaseNodeIdReservation(NodeState node);
    }
}
