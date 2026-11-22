// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Client
{
    /// <summary>
    /// Non-transparent network redundancy options for one logical Server.
    /// </summary>
    /// <remarks>
    /// See OPC 10000-4 §6.6.4. Non-transparent network redundancy reconnects the SecureChannel through an alternate
    /// endpoint while reusing the Session and Subscriptions. Transparent network redundancy is handled below the OPC UA
    /// stack by the network infrastructure.
    /// </remarks>
    public sealed record NetworkRedundancyOptions
    {
        /// <summary>
        /// Ordered alternate endpoints for the same logical server.
        /// </summary>
        public ArrayOf<ConfiguredEndpoint> AlternateEndpoints { get; init; } = [];
    }
}
