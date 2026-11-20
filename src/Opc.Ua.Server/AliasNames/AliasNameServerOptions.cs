// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// Configures the standard server's OPC UA Part 17 alias-name address space.
    /// </summary>
    public sealed class AliasNameServerOptions
    {
        /// <summary>
        /// Whether the configuration node manager creates browsable alias and category
        /// nodes from the server's registered alias stores. Defaults to <see langword="false"/>,
        /// leaving aliases available through the standard query methods only.
        /// </summary>
        /// <remarks>
        /// Stores and aliases must be registered before address-space creation. Materialized
        /// nodes follow later store mutations by default. Disabling
        /// <see cref="RefreshAliasNodesOnChange"/> preserves a startup browse snapshot.
        /// </remarks>
        public bool MaterializeAliasNodes { get; set; }

        /// <summary>
        /// Whether an opted-in materialized address space follows later store changes.
        /// Defaults to true; disable to preserve the startup snapshot. Has no effect when
        /// <see cref="MaterializeAliasNodes"/> is false. Refreshes are coalesced and
        /// store query failures are logged without deleting the previous browse snapshot.
        /// </summary>
        public bool RefreshAliasNodesOnChange { get; set; } = true;
    }
}
