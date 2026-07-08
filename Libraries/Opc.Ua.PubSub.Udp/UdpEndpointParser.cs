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
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.PubSub.Udp
{
    /// <summary>
    /// Dedicated parser for <c>opc.udp://&lt;host&gt;[:&lt;port&gt;][/&lt;path&gt;]</c>
    /// URLs. Validates IPv4 / IPv6 literals, DNS host names, and
    /// classifies the address as unicast, multicast, broadcast, or
    /// subnet-broadcast so that the transport layer can pick the right
    /// socket options without re-parsing on every connect.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2.2">
    /// Part 14 §7.3.2.2 UDP multicast / broadcast</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2.3">
    /// Part 14 §7.3.2.3 UDP unicast</see>. Uses a hand-written parser
    /// rather than <see cref="Uri"/> because the latter rejects
    /// link-local IPv6 syntax in some TFMs and does not give us a
    /// uniform multicast / broadcast classification.
    /// </remarks>
    public static class UdpEndpointParser
    {
        /// <summary>
        /// Default UDP port assigned when the URL omits the
        /// <c>:port</c> component.
        /// </summary>
        public const int DefaultPort = 4840;

        /// <summary>
        /// Default DTLS PubSub port assigned when the URL omits the
        /// <c>:port</c> component.
        /// </summary>
        public const int DefaultDtlsPort = 4843;

        /// <summary>
        /// URL scheme handled by this parser.
        /// </summary>
        public const string Scheme = "opc.udp";

        /// <summary>
        /// DTLS URL scheme reserved for Part 14 §7.3.2.4 unicast endpoints.
        /// </summary>
        public const string DtlsScheme = "opc.dtls";

        private const string SchemePrefix = "opc.udp://";
        private const string DtlsSchemePrefix = "opc.dtls://";

        /// <summary>
        /// Parses the supplied URL into a <see cref="UdpEndpoint"/>.
        /// </summary>
        /// <param name="url">
        /// URL of the form <c>opc.udp://&lt;host&gt;[:&lt;port&gt;][/&lt;path&gt;]</c>.
        /// The optional path component is accepted for forward
        /// compatibility but is ignored by the transport.
        /// </param>
        /// <returns>The parsed endpoint.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="url"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="FormatException">
        /// <paramref name="url"/> does not start with
        /// <c>opc.udp://</c>, the host or port component is malformed,
        /// or the host fails to resolve to an IP address that the
        /// transport can use.
        /// </exception>
        public static UdpEndpoint Parse(string url)
        {
            if (url is null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            if (url.Length == 0)
            {
                throw new FormatException("PubSub UDP URL must not be empty.");
            }
            bool isDtls = url.StartsWith(DtlsSchemePrefix, StringComparison.OrdinalIgnoreCase);
            bool isUdp = url.StartsWith(SchemePrefix, StringComparison.OrdinalIgnoreCase);
            if (!isUdp && !isDtls)
            {
                throw new FormatException(
                    "PubSub UDP URL must start with 'opc.udp://' or 'opc.dtls://'.");
            }
            string remainder = isDtls ? url[DtlsSchemePrefix.Length..] : url[SchemePrefix.Length..];
            if (remainder.Length == 0)
            {
                throw new FormatException("PubSub UDP URL is missing the host component.");
            }
            int pathStart = remainder.IndexOf('/', StringComparison.Ordinal);
            if (pathStart >= 0)
            {
                remainder = remainder[..pathStart];
            }
            if (remainder.Length == 0)
            {
                throw new FormatException("PubSub UDP URL is missing the host component.");
            }
            string host;
            int port = isDtls ? DefaultDtlsPort : DefaultPort;
            if (remainder[0] == '[')
            {
                int hostEnd = remainder.IndexOf(']', StringComparison.Ordinal);
                if (hostEnd < 0)
                {
                    throw new FormatException(
                        "PubSub UDP URL has an unterminated IPv6 literal.");
                }
                host = remainder[1..hostEnd];
                if (host.Length == 0)
                {
                    throw new FormatException(
                        "PubSub UDP URL has an empty IPv6 literal.");
                }
                if (hostEnd + 1 < remainder.Length)
                {
                    if (remainder[hostEnd + 1] != ':')
                    {
                        throw new FormatException(
                            "PubSub UDP URL has an unexpected character after the IPv6 literal.");
                    }
                    port = ParsePort(remainder[(hostEnd + 2)..]);
                }
            }
            else
            {
                int colon = remainder.LastIndexOf(':');
                if (colon == 0)
                {
                    throw new FormatException(
                        "PubSub UDP URL is missing the host component.");
                }
                if (colon > 0)
                {
                    host = remainder[..colon];
                    port = ParsePort(remainder[(colon + 1)..]);
                }
                else
                {
                    host = remainder;
                }
            }
            if (host.Length == 0)
            {
                throw new FormatException("PubSub UDP URL is missing the host component.");
            }
            IPAddress address = ResolveHost(host);
            UdpAddressType type = ClassifyAddress(address);
            return new UdpEndpoint(address, port, type, url, isDtls, isDtls ? DtlsTransportOptions.DefaultProfileName : null);
        }

        /// <summary>
        /// Classifies the supplied <see cref="IPAddress"/> per Part 14
        /// §7.3.2.2 / §7.3.2.3. Exposed so consumers can re-classify
        /// addresses obtained from sources other than
        /// <see cref="Parse"/>.
        /// </summary>
        /// <param name="address">Address to classify.</param>
        /// <returns>The address type.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="address"/> is <see langword="null"/>.
        /// </exception>
        public static UdpAddressType ClassifyAddress(IPAddress address)
        {
            if (address is null)
            {
                throw new ArgumentNullException(nameof(address));
            }
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                byte[] octets = address.GetAddressBytes();
                if (octets[0] >= 224 && octets[0] <= 239)
                {
                    return UdpAddressType.Multicast;
                }
                if (octets[0] == 255 && octets[1] == 255 && octets[2] == 255 && octets[3] == 255)
                {
                    return UdpAddressType.Broadcast;
                }
                if (octets[3] == 255)
                {
                    return UdpAddressType.SubnetBroadcast;
                }
                return UdpAddressType.Unicast;
            }
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6Multicast)
                {
                    return UdpAddressType.Multicast;
                }
                return UdpAddressType.Unicast;
            }
            return UdpAddressType.Unicast;
        }

        private static int ParsePort(string text)
        {
            if (text.Length == 0)
            {
                throw new FormatException("PubSub UDP URL is missing the port component.");
            }
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) ||
                port < 1 ||
                port > 65535)
            {
                throw new FormatException(
                    $"PubSub UDP URL has an invalid port component '{text}' (must be 1-65535).");
            }
            return port;
        }

        private static IPAddress ResolveHost(string host)
        {
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return IPAddress.Loopback;
            }
            if (IPAddress.TryParse(host, out IPAddress? literal))
            {
                return literal;
            }
            try
            {
                IPHostEntry entry = Dns.GetHostEntry(host);
                for (int i = 0; i < entry.AddressList.Length; i++)
                {
                    IPAddress candidate = entry.AddressList[i];
                    if (candidate.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return candidate;
                    }
                }
                for (int i = 0; i < entry.AddressList.Length; i++)
                {
                    IPAddress candidate = entry.AddressList[i];
                    if (candidate.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        return candidate;
                    }
                }
            }
            catch (SocketException ex)
            {
                throw new FormatException(
                    $"PubSub UDP URL host '{host}' could not be resolved.",
                    ex);
            }
            catch (ArgumentException ex)
            {
                throw new FormatException(
                    $"PubSub UDP URL host '{host}' is not a valid DNS name.",
                    ex);
            }
            throw new FormatException(
                $"PubSub UDP URL host '{host}' did not resolve to any usable IP address.");
        }
    }
}
