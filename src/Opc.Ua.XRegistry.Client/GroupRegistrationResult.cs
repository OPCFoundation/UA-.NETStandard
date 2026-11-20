// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.XRegistry.Client
{
    /// <summary>
    /// The outcome of resolving a resource group under a registry root.
    /// </summary>
    /// <param name="GroupNodeId">The NodeId of the group.</param>
    /// <param name="Created">
    /// <c>true</c> when this call created the group, <c>false</c> when an existing group with the
    /// same group id was returned.
    /// </param>
    public readonly record struct GroupRegistrationResult(NodeId GroupNodeId, bool Created);
}
