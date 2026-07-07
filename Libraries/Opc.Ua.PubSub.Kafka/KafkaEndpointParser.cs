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
using System.Text;

namespace Opc.Ua.PubSub.Kafka
{
    /// <summary>
    /// Dedicated parser for <c>kafka://</c> and <c>kafkas://</c> URLs.
    /// Used instead of <see cref="Uri"/> directly because a Kafka
    /// endpoint carries a comma-separated bootstrap server list, which is
    /// not a valid single <see cref="Uri"/> authority; the parser also
    /// makes the scheme validation and default-port selection explicit
    /// and rejects malformed inputs with a precise
    /// <see cref="FormatException"/> message.
    /// </summary>
    /// <remarks>
    /// Implements the URI parsing surface of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/Annex-B.2">
    /// Part 14 Annex B.2 Apache Kafka transport</see>. The default port
    /// follows the Apache Kafka convention (<c>9092</c>).
    /// </remarks>
    public static class KafkaEndpointParser
    {
        /// <summary>
        /// Kafka scheme for plaintext transport.
        /// </summary>
        public const string KafkaScheme = "kafka";

        /// <summary>
        /// Kafka scheme for TLS-protected transport.
        /// </summary>
        public const string KafkasScheme = "kafkas";

        /// <summary>
        /// Default Kafka broker port.
        /// </summary>
        public const int DefaultPort = 9092;

        /// <summary>
        /// Parses <paramref name="url"/> into a strongly-typed
        /// <see cref="KafkaEndpoint"/>.
        /// </summary>
        /// <param name="url">
        /// URL to parse (<c>kafka://host[:port][,host[:port]...]</c> or
        /// <c>kafkas://...</c>).
        /// </param>
        /// <returns>The parsed endpoint.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="url"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="FormatException">
        /// <paramref name="url"/> is malformed or uses a scheme other
        /// than <c>kafka</c> / <c>kafkas</c>.
        /// </exception>
        public static KafkaEndpoint Parse(string url)
        {
            if (url is null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            if (url.Length == 0)
            {
                throw new FormatException("Kafka endpoint URL cannot be empty.");
            }

            int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0)
            {
                throw new FormatException(
                    "Kafka endpoint must be of the form kafka[s]://host[:port][,host[:port]...].");
            }
            string scheme = url.Substring(0, schemeEnd);
            bool useTls;
            if (string.Equals(scheme, KafkaScheme, StringComparison.OrdinalIgnoreCase))
            {
                useTls = false;
            }
            else if (string.Equals(scheme, KafkasScheme, StringComparison.OrdinalIgnoreCase))
            {
                useTls = true;
            }
            else
            {
                throw new FormatException(
                    "Kafka endpoint scheme must be 'kafka' or 'kafkas'.");
            }

            string authority = url.Substring(schemeEnd + 3);
            int pathStart = authority.IndexOf('/', StringComparison.Ordinal);
            if (pathStart >= 0)
            {
                authority = authority.Substring(0, pathStart);
            }
            if (authority.Length == 0)
            {
                throw new FormatException("Kafka endpoint is missing the bootstrap server list.");
            }

            string[] entries = authority.Split(',');
            var builder = new StringBuilder();
            foreach (string rawEntry in entries)
            {
                string entry = rawEntry.Trim();
                if (entry.Length == 0)
                {
                    throw new FormatException("Kafka endpoint has an empty bootstrap server entry.");
                }
                (string host, int port) = ParseHostPort(entry);
                if (builder.Length > 0)
                {
                    builder.Append(',');
                }
                builder.Append(host);
                builder.Append(':');
                builder.Append(port.ToString(CultureInfo.InvariantCulture));
            }
            return new KafkaEndpoint(builder.ToString(), useTls);
        }

        private static (string Host, int Port) ParseHostPort(string entry)
        {
            if (entry[0] == '[')
            {
                int hostEnd = entry.IndexOf(']', StringComparison.Ordinal);
                if (hostEnd < 0)
                {
                    throw new FormatException(
                        "Kafka endpoint has an unterminated IPv6 literal.");
                }
                string ipv6Host = entry.Substring(1, hostEnd - 1);
                if (ipv6Host.Length == 0)
                {
                    throw new FormatException("Kafka endpoint has an empty IPv6 literal.");
                }
                int ipv6Port;
                if (hostEnd + 1 < entry.Length)
                {
                    if (entry[hostEnd + 1] != ':')
                    {
                        throw new FormatException(
                            "Kafka endpoint has an unexpected character after the IPv6 literal.");
                    }
                    ipv6Port = ParsePort(entry.Substring(hostEnd + 2));
                }
                else
                {
                    ipv6Port = DefaultPort;
                }
                return (string.Concat("[", ipv6Host, "]"), ipv6Port);
            }

            int colon = entry.LastIndexOf(':');
            string host;
            int port;
            if (colon >= 0)
            {
                host = entry.Substring(0, colon);
                port = ParsePort(entry.Substring(colon + 1));
            }
            else
            {
                host = entry;
                port = DefaultPort;
            }
            if (host.Length == 0)
            {
                throw new FormatException("Kafka endpoint is missing the host component.");
            }
            return (host, port);
        }

        private static int ParsePort(string text)
        {
            if (text.Length == 0)
            {
                throw new FormatException("Kafka endpoint has an empty port component.");
            }
            if (!int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int port)
                || port <= 0
                || port > 65535)
            {
                throw new FormatException(
                    "Kafka endpoint has an invalid port component (must be 1..65535).");
            }
            return port;
        }
    }
}
