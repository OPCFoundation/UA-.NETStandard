// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Opt-in interface implemented by node managers that expose their local
    /// address space to a distributed address-space synchronizer.
    /// </summary>
    public interface ILocalAddressSpaceSource
    {
        /// <summary>
        /// Creates an adapter over the node manager's local address space.
        /// </summary>
        /// <returns>The local address space adapter.</returns>
        ILocalAddressSpace CreateLocalAddressSpace();
    }

    /// <summary>
    /// Supplies explicit node ownership for a distributed address-space source
    /// whose node manager does not partition nodes by namespace URI.
    /// </summary>
    public interface ILocalAddressSpaceOwnership
    {
        /// <summary>
        /// Gets a stable identifier for this ownership partition across replicas.
        /// </summary>
        string PartitionId { get; }

        /// <summary>
        /// Returns whether the source owns and may replicate the specified node.
        /// </summary>
        /// <param name="nodeId">The node identifier to test.</param>
        /// <returns><c>true</c> when the source owns the node.</returns>
        bool OwnsNode(NodeId nodeId);
    }
}
