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
using System.Net;
using System.Net.Sockets;

namespace Opc.Ua.WotCon.Server.Assets
{
    /// <summary>
    /// Validates a remote-supplied endpoint string against an
    /// <see cref="AssetEndpointPolicy"/>. Defence-in-depth against
    /// server-side request forgery (SSRF): rejects pathological
    /// URIs, unsupported schemes, loopback / private-range hosts,
    /// and policy-blocked hosts before any discovery provider sees
    /// the value.
    /// </summary>
    /// <remarks>
    /// DNS resolution is intentionally **not** performed during
    /// validation. Resolving the host name to an IP at validation
    /// time and then re-resolving it at connect time is itself a
    /// TOCTOU SSRF vector — a hostile DNS could return a public IP
    /// to the validator and a private IP to the connector. Operators
    /// who need IP-range enforcement must either pin
    /// <see cref="AssetEndpointPolicy.AllowedHosts"/> to IP literals
    /// or accept that the IP-range gates only fire when the host
    /// portion of the URI itself is an IP literal.
    /// </remarks>
    public static class AssetEndpointValidator
    {
        /// <summary>
        /// Validates <paramref name="endpoint"/>, returning the
        /// normalized <see cref="Uri"/> in <paramref name="normalized"/>
        /// on success. Returns:
        ///   * <c>Bad_InvalidArgument</c> when the endpoint string is
        ///     empty or not a syntactically valid absolute URI.
        ///   * <c>Bad_SecurityChecksFailed</c> when the URI is well
        ///     formed but a policy gate rejects it.
        /// </summary>
        public static ServiceResult Validate(
            string? endpoint,
            AssetEndpointPolicy policy,
            out Uri? normalized)
        {
            normalized = null;

            if (policy is null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Endpoint is required.");
            }
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Endpoint is not a syntactically valid absolute URI.");
            }
            if (!policy.AllowedSchemes.Contains(uri.Scheme))
            {
                return ServiceResult.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Endpoint scheme '{0}' is not in the policy's AllowedSchemes set.",
                    uri.Scheme);
            }

            string host = uri.Host ?? string.Empty;

            // Allowed-host allow-list (exclusive).
            if (policy.AllowedHosts.Count > 0 && !policy.AllowedHosts.Contains(host))
            {
                return ServiceResult.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Endpoint host '{0}' is not in the policy's AllowedHosts set.",
                    host);
            }

            // Always-deny list (evaluated after the allow-list so an
            // operator can layer an explicit deny on top of a broad
            // allow).
            if (policy.BlockedHosts.Contains(host))
            {
                return ServiceResult.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Endpoint host '{0}' is in the policy's BlockedHosts set.",
                    host);
            }

            // Loopback / private-range gates only apply when the host
            // is an IP literal. We do NOT resolve DNS here (TOCTOU).
            if (IPAddress.TryParse(host, out IPAddress? ip))
            {
                if (!policy.AllowLoopback && IPAddress.IsLoopback(ip))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Endpoint host '{0}' is a loopback address; " +
                        "set AssetEndpointPolicy.AllowLoopback = true to permit.",
                        host);
                }
                if (!policy.AllowPrivateAddresses && IsPrivateAddress(ip))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Endpoint host '{0}' is in a private / link-local range; " +
                        "set AssetEndpointPolicy.AllowPrivateAddresses = true to permit.",
                        host);
                }
            }
            else if (IsLocalHostName(host))
            {
                // Catch the common literal 'localhost' even though
                // resolution is otherwise skipped — it is unambiguous.
                if (!policy.AllowLoopback)
                {
                    return ServiceResult.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Endpoint host '{0}' is a localhost alias; " +
                        "set AssetEndpointPolicy.AllowLoopback = true to permit.",
                        host);
                }
            }

            normalized = uri;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns <c>true</c> for IPv4 RFC1918 / RFC3927 link-local,
        /// IPv6 RFC4193 ULA, and IPv6 RFC4291 link-local addresses.
        /// </summary>
        private static bool IsPrivateAddress(IPAddress ip)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] bytes = ip.GetAddressBytes();
                // 10.0.0.0/8
                if (bytes[0] == 10)
                {
                    return true;
                }
                // 172.16.0.0/12 (172.16.x.x to 172.31.x.x)
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                {
                    return true;
                }
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168)
                {
                    return true;
                }
                // 169.254.0.0/16 link-local (includes IMDS at .169.254)
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    return true;
                }
                return false;
            }
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                byte[] bytes = ip.GetAddressBytes();
                // fc00::/7 unique-local addresses (ULA)
                if ((bytes[0] & 0xFE) == 0xFC)
                {
                    return true;
                }
                // fe80::/10 link-local
                if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
                {
                    return true;
                }
                return false;
            }
            return false;
        }

        private static bool IsLocalHostName(string host)
        {
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "ip6-localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "ip6-loopback", StringComparison.OrdinalIgnoreCase);
        }
    }
}
