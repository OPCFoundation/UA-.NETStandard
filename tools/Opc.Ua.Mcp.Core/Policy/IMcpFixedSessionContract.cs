/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Explicit host contract for registered custom sessions that do not change identity during generic mutations.
    /// </summary>
    /// <remarks>
    /// Register an implementation through dependency injection only for sessions whose host guarantees no
    /// reconnect, retarget, authentication change, or namespace/type/ownership metadata change between
    /// classification and dispatch. Returning true grants no operation authority and supplies no SDK generation gate.
    /// Unknown or independently mutable sessions must return false. Stack ManagedSession instances always
    /// use the SDK's generation-bound context instead, regardless of this contract.
    /// </remarks>
    public interface IMcpFixedSessionContract
    {
        /// <summary>
        /// Confirms the fixed-session contract for this exact named registration.
        /// The contract is checked before classification and again immediately before mutation dispatch.
        /// A known change to the host's classification-data epoch must withdraw the contract.
        /// </summary>
        bool IsFixed(OpcUaSessionManager.SessionInfo registration);
    }
}
