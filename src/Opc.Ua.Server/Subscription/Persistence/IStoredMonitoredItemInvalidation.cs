// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Extends persisted monitored-item state with terminal invalidation without changing legacy implementations.
    /// </summary>
    public interface IStoredMonitoredItemInvalidation : IStoredMonitoredItem
    {
        /// <summary>
        /// Gets or sets whether the client must recreate the item, including after server restart.
        /// </summary>
        /// <remarks>
        /// Terminal items also have IsDeleted and IsDetached set. Definitions without this capability
        /// restore as nonterminal for compatibility.
        /// </remarks>
        bool IsTerminal { get; set; }
    }
}
