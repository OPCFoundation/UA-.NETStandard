// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Opc.Ua.Interop.Tests
{
    /// <summary>
    /// The "PEER-INFO {json}" line a peer prints before it is ready: which
    /// stack it is and what its server reports about itself, so the tests
    /// compare against the peer instead of hard-coding the 1.5.378 peer.
    /// </summary>
    public sealed class PeerInfo
    {
        /// <summary>
        /// The stack value of the 1.5.378 peer.
        /// </summary>
        public const string LegacyStack = "UA-.NETStandard";

        /// <summary>
        /// The stack, e.g. UA-.NETStandard, node-opcua or Eclipse Milo.
        /// </summary>
        public string Stack { get; private set; } = null!;

        /// <summary>
        /// The stack version.
        /// </summary>
        public string Version { get; private set; } = null!;

        /// <summary>
        /// The application URI of the peer.
        /// </summary>
        public string ApplicationUri { get; private set; } = null!;

        /// <summary>
        /// The BuildInfo.SoftwareVersion the peer's server reports.
        /// </summary>
        public string SoftwareVersion { get; private set; } = null!;

        /// <summary>
        /// The security policy URIs the peer implements (its server offers
        /// them, and its client of the same build can use them), or null
        /// when the peer does not declare them.
        /// </summary>
        public IReadOnlyList<string> Policies { get; private set; } = null!;

        /// <summary>
        /// Whether the peer is the 1.5.x .NET peer.
        /// </summary>
        public bool IsLegacy => Stack == LegacyStack;

        /// <summary>
        /// Whether the peer declares the security policy in
        /// <see cref="Policies"/>.
        /// </summary>
        public bool DeclaresPolicy(string policyUri)
        {
            return Policies != null && Policies.Contains(policyUri, StringComparer.Ordinal);
        }

        public static PeerInfo Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            List<string>? policies = null;
            if (root.TryGetProperty("policies", out JsonElement array) &&
                array.ValueKind == JsonValueKind.Array)
            {
                policies = [.. array.EnumerateArray().Select(e => e.GetString()!)];
            }
            return new PeerInfo
            {
                Stack = Get(root, "stack"),
                Version = Get(root, "version"),
                ApplicationUri = Get(root, "applicationUri"),
                SoftwareVersion = Get(root, "softwareVersion"),
                Policies = policies!
            };
        }

        private static string Get(JsonElement root, string name)
        {
            return (root.TryGetProperty(name, out JsonElement value) ? value.GetString() : null)!;
        }
    }
}
