/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// Validates a remote-supplied endpoint string against a <see cref="WotEndpointPolicy"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// DNS resolution is intentionally **not** performed during validation. Resolving the host name
    /// to an IP at validation time and then re-resolving it at connect time is itself a TOCTOU SSRF
    /// vector — a hostile DNS could return a public IP to the validator and a private IP to the
    /// connector. Operators who need IP-range enforcement must either pin
    /// <see cref="WotEndpointPolicy.AllowedHosts"/> to IP literals or accept that the IP-range gates
    /// only fire when the host portion of the URI itself is an IP literal.
    /// </para>
    /// <para>
    /// A host is compared in its IDNA A-label form, which is the name the request actually
    /// resolves. An allow list accepts either spelling of the same name, because both denote one
    /// host; a block list refuses either, because refusing only one of them refuses nothing.
    /// </para>
    /// </remarks>
    public static class WotEndpointValidator
    {
        /// <summary>
        /// Gets the ASCII form of a URI's host: the IDNA A-label of a registered name, or the
        /// literal of an IP address. A URI with no authority has an empty host.
        /// </summary>
        /// <remarks>
        /// <see cref="Uri.IdnHost"/> answers on every modern target, but on .NET Framework it
        /// returns the Unicode host unchanged unless IRI parsing is switched on in configuration.
        /// The explicit <see cref="IdnMapping"/> pass is what makes the result the same on every
        /// target rather than dependent on a machine's configuration file.
        /// </remarks>
        /// <param name="uri">The parsed absolute URI.</param>
        /// <returns>The ASCII host, without IPv6 brackets.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <c>null</c>.</exception>
        public static string ToAsciiHost(Uri uri)
        {
            if (uri is null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            if (uri.HostNameType == UriHostNameType.Unknown || string.IsNullOrEmpty(uri.Host))
            {
                return string.Empty;
            }
            return uri.HostNameType == UriHostNameType.IPv6
                ? Unbracket(uri.IdnHost)
                : ToAsciiName(uri.IdnHost);
        }

        /// <summary>
        /// Converts a registered name to its IDNA A-label, leaving one that is already ASCII
        /// alone.
        /// </summary>
        /// <remarks>
        /// <see cref="Uri.IdnHost"/> answers on every modern target, but on .NET Framework it
        /// returns the Unicode host unchanged unless IRI parsing is switched on in configuration,
        /// so the explicit pass is what makes the result the same on every target rather than
        /// dependent on a machine's configuration file. A name IDNA refuses is handed back
        /// unchanged: it is not a name that resolves, and inventing a percent-encoded spelling
        /// would name something else instead of letting the policy refuse it.
        /// </remarks>
        /// <param name="host">The host as the URI parser reported it.</param>
        /// <returns>The ASCII form of the name.</returns>
        internal static string ToAsciiName(string host)
        {
            if (IsAscii(host))
            {
                return host;
            }
            try
            {
                return new IdnMapping { AllowUnassigned = true }.GetAscii(host);
            }
            catch (ArgumentException)
            {
                return host;
            }
        }

        /// <summary>
        /// Removes the brackets an IPv6 literal is written with inside a URI, which a connect
        /// call does not want and which one spelling of the host already omits.
        /// </summary>
        /// <param name="host">The host, with or without brackets.</param>
        /// <returns>The bare IPv6 literal.</returns>
        internal static string Unbracket(string host)
        {
            return host.Length > 1 && host[0] == '[' && host[host.Length - 1] == ']'
                ? host.Substring(1, host.Length - 2)
                : host;
        }

        private static bool IsAscii(string value)
        {
            foreach (char character in value)
            {
                if (character > '\u007F')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Validates <paramref name="endpoint"/>, returning the normalized <see cref="Uri"/> in
        /// <paramref name="normalized"/> on success.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static ServiceResult Validate(
            string? endpoint,
            WotEndpointPolicy policy,
            out Uri? normalized)
        {
            normalized = null;

            if (policy is null)
            {
                throw new ArgumentNullException(nameof(policy));
            }
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return ServiceResult.Create(StatusCodes.BadInvalidArgument, "Endpoint is required.");
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
            string asciiHost = ToAsciiHost(uri);
            if (policy.AllowedHosts.Count > 0 &&
                !policy.AllowedHosts.Contains(asciiHost) &&
                !policy.AllowedHosts.Contains(host))
            {
                return ServiceResult.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Endpoint host '{0}' is not in the policy's AllowedHosts set.",
                    asciiHost);
            }
            if (policy.BlockedHosts.Contains(asciiHost) || policy.BlockedHosts.Contains(host))
            {
                return ServiceResult.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Endpoint host '{0}' is in the policy's BlockedHosts set.",
                    asciiHost);
            }

            // Every remaining gate reads the ASCII host, because that is the
            // name the request resolves. Reading the Unicode spelling here
            // would let 'ü.example' pass a check that 'xn--tda.example' fails.
            host = asciiHost;

            if (IPAddress.TryParse(host, out IPAddress? parsedIp))
            {
                IPAddress ip = parsedIp.IsIPv4MappedToIPv6 ? parsedIp.MapToIPv4() : parsedIp;
                if (!policy.AllowLoopback && IPAddress.IsLoopback(ip))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Endpoint host '{0}' is a loopback address; set WotEndpointPolicy.AllowLoopback = true to permit.",
                        host);
                }
                if (!policy.AllowPrivateAddresses && IsPrivateAddress(ip))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadSecurityChecksFailed,
                        "Endpoint host '{0}' is in a private / link-local range; " +
                        "set WotEndpointPolicy.AllowPrivateAddresses = true to permit.",
                        host);
                }
            }
            else if (IsLocalHostName(host) && !policy.AllowLoopback)
            {
                return ServiceResult.Create(
                    StatusCodes.BadSecurityChecksFailed,
                    "Endpoint host '{0}' is a localhost alias; set WotEndpointPolicy.AllowLoopback = true to permit.",
                    host);
            }

            normalized = uri;
            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns <c>true</c> for IPv4 RFC1918 / RFC6598 CGNAT / RFC3927 link-local, IPv6
        /// RFC4193 ULA, and IPv6 RFC4291 link-local addresses.
        /// </summary>
        private static bool IsPrivateAddress(IPAddress ip)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] bytes = ip.GetAddressBytes();
                if (bytes[0] == 10)
                {
                    return true;
                }
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                {
                    return true;
                }
                if (bytes[0] == 192 && bytes[1] == 168)
                {
                    return true;
                }
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    return true;
                }
                if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                {
                    return true;
                }
                return false;
            }
            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                byte[] bytes = ip.GetAddressBytes();
                if ((bytes[0] & 0xFE) == 0xFC)
                {
                    return true;
                }
                if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsLocalHostName(string host)
        {
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "ip6-localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "ip6-loopback", StringComparison.OrdinalIgnoreCase);
        }
    }
}
