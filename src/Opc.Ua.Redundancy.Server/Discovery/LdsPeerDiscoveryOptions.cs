// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: options for <see cref="LdsPeerDiscovery"/>, which discovers redundant
    /// peers from a Local Discovery Server (LDS / LDS-ME) via <c>FindServers</c> / <c>FindServersOnNetwork</c>
    /// (OPC 10000-4 §6.4).
    /// </summary>
    public sealed class LdsPeerDiscoveryOptions
    {
        /// <summary>
        /// Gets or sets this server's own ApplicationUri, which is excluded from the discovered peer set so a
        /// replica does not list itself.
        /// </summary>
        public string LocalApplicationUri { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether only server applications (<see cref="ApplicationType.Server"/>
        /// and <see cref="ApplicationType.ClientAndServer"/>) are kept. Defaults to <c>true</c>.
        /// </summary>
        public bool ServersOnly { get; set; } = true;
    }
}
