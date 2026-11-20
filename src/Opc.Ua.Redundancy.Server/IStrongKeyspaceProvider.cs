// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Contributes additional key prefixes that a feature needs routed to the linearizable (Raft) store when the
    /// <c>RedundantServerSet</c> runs in the eventual/hybrid consistency mode. <c>UseRedundancyConsistency</c>
    /// aggregates every registered provider with the configured <c>StrongKeyPrefixes</c>.
    /// </summary>
    public interface IStrongKeyspaceProvider
    {
        /// <summary>
        /// Returns the key prefixes this feature requires to be linearizable.
        /// </summary>
        ArrayOf<string> GetStrongKeyPrefixes();
    }
}
