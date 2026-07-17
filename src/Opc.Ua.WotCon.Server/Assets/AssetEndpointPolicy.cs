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

namespace Opc.Ua.WotCon.Server.Assets
{
    /// <summary>
    /// Allow-list policy controlling which endpoint URIs the WoT
    /// Connectivity server is permitted to reach when a remote OPC UA
    /// client invokes <c>CreateAssetForEndpoint</c> or
    /// <c>ConnectionTest</c>. Defence-in-depth against server-side
    /// request forgery (SSRF) — the discovery provider sees the
    /// endpoint only after the policy validates it.
    /// </summary>
    /// <remarks>
    /// Safe defaults:
    ///   * Schemes restricted to <c>http</c>, <c>https</c>, <c>opc.tcp</c>.
    ///   * Loopback (127.0.0.0/8, ::1) blocked.
    ///   * Private ranges blocked: RFC1918 (10/8, 172.16/12, 192.168/16),
    ///     IPv4 link-local (169.254/16 — including the AWS / Azure IMDS
    ///     address 169.254.169.254), IPv6 ULA (fc00::/7), and IPv6
    ///     link-local (fe80::/10).
    ///   * Per-operation timeout of 30 seconds bounds discovery /
    ///     connection-test work even when the upstream provider hangs.
    /// </remarks>
    public sealed class AssetEndpointPolicy
    {
        /// <summary>
        /// The set of schemes the validator accepts. Case-insensitive
        /// per RFC 3986 §3.1.
        /// </summary>
        public ISet<string> AllowedSchemes { get; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "http",
                "https",
                "opc.tcp"
            };

        /// <summary>
        /// When <c>true</c> the validator permits loopback addresses
        /// (127.0.0.0/8 / ::1) and the literal host names
        /// <c>localhost</c>, <c>ip6-localhost</c>, <c>ip6-loopback</c>.
        /// Default <c>false</c> — operators must opt in explicitly to
        /// expose the server's own listeners to a remote caller.
        /// </summary>
        public bool AllowLoopback { get; set; }

        /// <summary>
        /// When <c>true</c> the validator permits private-range IP
        /// literals (RFC1918, RFC4193 ULA, IPv4 / IPv6 link-local).
        /// Default <c>false</c> so the IMDS attack surface
        /// (e.g. <c>169.254.169.254</c>) is closed by default.
        /// </summary>
        public bool AllowPrivateAddresses { get; set; }

        /// <summary>
        /// Exclusive allow-list of host names. When non-empty, only
        /// hosts that appear in this set (case-insensitive
        /// comparison) pass the validator regardless of the
        /// scheme / loopback / private-range gates. Use this to pin
        /// the server to a known set of devices.
        /// </summary>
        public ISet<string> AllowedHosts { get; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Hosts that are always denied even when the rest of the
        /// policy would accept them. Evaluated after
        /// <see cref="AllowedHosts"/> so an operator can layer an
        /// explicit deny on top of a broad allow.
        /// </summary>
        public ISet<string> BlockedHosts { get; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Hard upper bound on the time a single discovery /
        /// connection-test call to <see cref="IWotAssetDiscoveryProvider"/>
        /// is allowed to take. Default 30 s. Use <see cref="TimeSpan.Zero"/>
        /// to disable the per-operation timeout (the caller's
        /// cancellation token still applies).
        /// </summary>
        public TimeSpan MaxOperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
