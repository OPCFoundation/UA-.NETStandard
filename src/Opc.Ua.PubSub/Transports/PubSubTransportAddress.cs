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

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Parsed PubSub transport address (scheme + host + port + optional
    /// path). Lives at configuration time; transports consume it to
    /// open sockets / sessions without re-parsing the raw URI on every
    /// connect.
    /// </summary>
    /// <remarks>
    /// Implements the addressing model of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.2">
    /// Part 14 §7.3.2 UDP datagram transport</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 Broker transport (MQTT)</see>. Uses dedicated
    /// parsing instead of <see cref="Uri"/> because the address must
    /// validate unicast / multicast / broadcast classes for the UDP
    /// scheme explicitly. Only the structural fields are modelled here;
    /// detection of address class is performed by the UDP transport
    /// layer.
    /// </remarks>
    public readonly record struct PubSubTransportAddress
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubTransportAddress"/>.
        /// </summary>
        /// <param name="scheme">URI scheme (e.g. <c>opc.udp</c>, <c>mqtt</c>, <c>mqtts</c>).</param>
        /// <param name="host">Host portion (IP literal or DNS name).</param>
        /// <param name="port">TCP / UDP port.</param>
        /// <param name="path">Optional path component for broker schemes.</param>
        public PubSubTransportAddress(string scheme, string host, int port, string? path = null)
        {
            if (scheme is null)
            {
                throw new ArgumentNullException(nameof(scheme));
            }
            if (scheme.Length == 0)
            {
                throw new ArgumentException("Value cannot be empty.", nameof(scheme));
            }
            if (host is null)
            {
                throw new ArgumentNullException(nameof(host));
            }
            if (host.Length == 0)
            {
                throw new ArgumentException("Value cannot be empty.", nameof(host));
            }
            Scheme = scheme;
            Host = host;
            Port = port;
            Path = path;
        }

        /// <summary>
        /// URI scheme (e.g. <c>opc.udp</c>, <c>mqtt</c>, <c>mqtts</c>).
        /// </summary>
        public string Scheme { get; init; }

        /// <summary>
        /// Host portion (IP literal or DNS name).
        /// </summary>
        public string Host { get; init; }

        /// <summary>
        /// TCP / UDP port; <c>0</c> when the scheme implies a default.
        /// </summary>
        public int Port { get; init; }

        /// <summary>
        /// Optional path component for broker schemes. For UDP the
        /// component is always <see langword="null"/>.
        /// </summary>
        public string? Path { get; init; }

        /// <summary>
        /// Parses a PubSub URL into its scheme, host, port, and path
        /// parts. Recognises <c>opc.udp</c>, <c>mqtt</c>, and <c>mqtts</c>;
        /// other schemes are accepted structurally but the consuming
        /// transport will reject unknown ones.
        /// </summary>
        /// <param name="url">URL to parse.</param>
        /// <returns>The parsed address.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="url"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="FormatException">
        /// <paramref name="url"/> does not contain a scheme / host
        /// separator, or the port component cannot be parsed.
        /// </exception>
        /// <exception cref="ArgumentException"></exception>
        public static PubSubTransportAddress Parse(string url)
        {
            if (url is null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            if (url.Length == 0)
            {
                throw new ArgumentException("Value cannot be empty.", nameof(url));
            }
            int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0)
            {
                throw new FormatException(
                    "PubSub address must be of the form scheme://host[:port][/path].");
            }
            string scheme = url[..schemeEnd];
            string remainder = url[(schemeEnd + 3)..];
            if (remainder.Length == 0)
            {
                throw new FormatException("PubSub address is missing the host component.");
            }
            string? path = null;
            int pathStart = remainder.IndexOf('/', StringComparison.Ordinal);
            if (pathStart >= 0)
            {
                path = remainder[pathStart..];
                remainder = remainder[..pathStart];
            }
            string host;
            int port = 0;
            if (remainder.StartsWith('['))
            {
                int hostEnd = remainder.IndexOf(']', StringComparison.Ordinal);
                if (hostEnd < 0)
                {
                    throw new FormatException("PubSub address has an unterminated IPv6 literal.");
                }
                host = remainder[1..hostEnd];
                if (hostEnd + 1 < remainder.Length)
                {
                    if (remainder[hostEnd + 1] != ':')
                    {
                        throw new FormatException(
                            "PubSub address has an unexpected character after the IPv6 literal.");
                    }
                    port = ParsePort(remainder[(hostEnd + 2)..]);
                }
            }
            else
            {
                int colon = remainder.LastIndexOf(':');
                if (colon >= 0)
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
                throw new FormatException("PubSub address is missing the host component.");
            }
            return new PubSubTransportAddress(scheme, host, port, path);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            string host = Host.Contains(':', StringComparison.Ordinal) && !Host.StartsWith('[')
                ? $"[{Host}]"
                : Host;
            string portText = Port > 0
                ? $":{Port.ToString(CultureInfo.InvariantCulture)}"
                : string.Empty;
            return $"{Scheme}://{host}{portText}{Path ?? string.Empty}";
        }

        private static int ParsePort(string text)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) ||
                port < 0 ||
                port > 65535)
            {
                throw new FormatException("PubSub address has an invalid port component.");
            }
            return port;
        }
    }
}
