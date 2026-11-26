// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Client
{
    /// <summary>
    /// Options for discovery-driven managed client connections.
    /// </summary>
    public sealed class DiscoveryConnectOptions
    {
        /// <summary>
        /// Discovery URL used for GetEndpoints.
        /// </summary>
        public string DiscoveryUrl { get; set; } = string.Empty;

        /// <summary>
        /// Required endpoint security mode.
        /// </summary>
        public MessageSecurityMode SecurityMode { get; set; } = MessageSecurityMode.SignAndEncrypt;

        /// <summary>
        /// Required endpoint security policy URI.
        /// </summary>
        public string SecurityPolicyUri { get; set; } = SecurityPolicies.Basic256Sha256;

        /// <summary>
        /// Optional transport profile URI filter.
        /// </summary>
        public string? TransportProfileUri { get; set; }
    }
}
