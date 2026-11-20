/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for CRDT active/active address-space replication. Topology and
    /// variable values are modelled as a last-writer-wins map and gossiped
    /// between replicas; every replica is a writer.
    /// </summary>
    public sealed class ReplicatedAddressSpaceOptions : ReplicatedGossipOptions;
}
