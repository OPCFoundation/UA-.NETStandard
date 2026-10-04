/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
        public string Stack { get; private set; }

        /// <summary>
        /// The stack version.
        /// </summary>
        public string Version { get; private set; }

        /// <summary>
        /// The application URI of the peer.
        /// </summary>
        public string ApplicationUri { get; private set; }

        /// <summary>
        /// The BuildInfo.SoftwareVersion the peer's server reports.
        /// </summary>
        public string SoftwareVersion { get; private set; }

        /// <summary>
        /// The security policy URIs the peer implements (its server offers
        /// them, and its client of the same build can use them), or null
        /// when the peer does not declare them.
        /// </summary>
        public IReadOnlyList<string> Policies { get; private set; }

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
            List<string> policies = null;
            if (root.TryGetProperty("policies", out JsonElement array) &&
                array.ValueKind == JsonValueKind.Array)
            {
                policies = [.. array.EnumerateArray().Select(e => e.GetString())];
            }
            return new PeerInfo
            {
                Stack = Get(root, "stack"),
                Version = Get(root, "version"),
                ApplicationUri = Get(root, "applicationUri"),
                SoftwareVersion = Get(root, "softwareVersion"),
                Policies = policies
            };
        }

        private static string Get(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out JsonElement value) ? value.GetString() : null;
        }
    }
}
