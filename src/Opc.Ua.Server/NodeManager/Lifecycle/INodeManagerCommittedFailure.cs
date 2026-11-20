// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Common recovery contract for legacy single-operation committed exceptions.
    /// New change-set callers receive the same result directly instead of an exception after publication.
    /// </summary>
    public interface INodeManagerCommittedFailure
    {
        /// <summary>
        /// Gets the authoritative committed result, or null only for a manually constructed legacy exception
        /// that supplied no result. A missing result must not be interpreted as an aborted publication.
        /// </summary>
        LifecycleResult? CommittedResult { get; }
    }
}
