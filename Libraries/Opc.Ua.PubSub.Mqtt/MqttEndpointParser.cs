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

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// Dedicated parser for <c>mqtt://</c>, <c>mqtts://</c>, <c>ws://</c>,
    /// and <c>wss://</c> URLs.
    /// Used instead of <see cref="Uri"/> directly so the scheme
    /// validation and default-port selection are explicit and so we
    /// can reject malformed inputs with a precise
    /// <see cref="FormatException"/> message.
    /// </summary>
    /// <remarks>
    /// Implements the URI parsing surface of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 Broker transport (MQTT)</see>. Default ports
    /// follow the MQTT specification (<c>1883</c> plaintext,
    /// <c>8883</c> TLS).
    /// </remarks>
    public static class MqttEndpointParser
    {
        /// <summary>
        /// MQTT scheme for plaintext TCP transport.
        /// </summary>
        public const string MqttScheme = "mqtt";

        /// <summary>
        /// MQTT scheme for TLS-protected TCP transport.
        /// </summary>
        public const string MqttsScheme = "mqtts";

        /// <summary>
        /// MQTT scheme for secure WebSocket transport.
        /// </summary>
        public const string WssScheme = "wss";

        /// <summary>
        /// MQTT scheme for plaintext WebSocket transport.
        /// </summary>
        public const string WsScheme = "ws";

        /// <summary>
        /// Default MQTT plaintext port.
        /// </summary>
        public const int DefaultPlaintextPort = 1883;

        /// <summary>
        /// Default MQTT TLS port.
        /// </summary>
        public const int DefaultTlsPort = 8883;

        /// <summary>
        /// Default secure WebSocket MQTT port.
        /// </summary>
        public const int DefaultWebSocketTlsPort = 443;

        /// <summary>
        /// Default plaintext WebSocket MQTT port.
        /// </summary>
        public const int DefaultWebSocketPlaintextPort = 80;

        /// <summary>
        /// Parses <paramref name="url"/> into a strongly-typed
        /// <see cref="MqttEndpoint"/>.
        /// </summary>
        /// <param name="url">
        /// URL to parse (<c>mqtt://</c>, <c>mqtts://</c>, <c>ws://</c>, or <c>wss://</c>).
        /// </param>
        /// <returns>The parsed endpoint.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="url"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="FormatException">
        /// <paramref name="url"/> is malformed or uses a scheme other
        /// than <c>mqtt</c> / <c>mqtts</c> / <c>ws</c> / <c>wss</c>.
        /// </exception>
        public static MqttEndpoint Parse(string url)
        {
            if (url is null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            if (url.Length == 0)
            {
                throw new FormatException("MQTT endpoint URL cannot be empty.");
            }

            int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0)
            {
                throw new FormatException(
                    "MQTT endpoint must be of the form mqtt[s]://host[:port].");
            }
            string scheme = url.Substring(0, schemeEnd);
            bool useTls;
            int defaultPort;
            bool isWebSocket;
            if (string.Equals(scheme, MqttScheme, StringComparison.OrdinalIgnoreCase))
            {
                isWebSocket = false;
                useTls = false;
                defaultPort = DefaultPlaintextPort;
            }
            else if (string.Equals(scheme, MqttsScheme, StringComparison.OrdinalIgnoreCase))
            {
                isWebSocket = false;
                useTls = true;
                defaultPort = DefaultTlsPort;
            }
            else if (string.Equals(scheme, WsScheme, StringComparison.OrdinalIgnoreCase))
            {
                isWebSocket = true;
                useTls = false;
                defaultPort = DefaultWebSocketPlaintextPort;
            }
            else if (string.Equals(scheme, WssScheme, StringComparison.OrdinalIgnoreCase))
            {
                isWebSocket = true;
                useTls = true;
                defaultPort = DefaultWebSocketTlsPort;
            }
            else
            {
                throw new FormatException(
                    "MQTT endpoint scheme must be 'mqtt', 'mqtts', 'ws', or 'wss'.");
            }

            string authority = url.Substring(schemeEnd + 3);
            string path = string.Empty;
            int pathStart = authority.IndexOf('/', StringComparison.Ordinal);
            if (pathStart >= 0)
            {
                path = authority.Substring(pathStart);
                authority = authority.Substring(0, pathStart);
            }
            if (authority.Length == 0)
            {
                throw new FormatException("MQTT endpoint is missing the host component.");
            }

            string host;
            int port;
            if (authority[0] == '[')
            {
                int hostEnd = authority.IndexOf(']', StringComparison.Ordinal);
                if (hostEnd < 0)
                {
                    throw new FormatException(
                        "MQTT endpoint has an unterminated IPv6 literal.");
                }
                host = authority.Substring(1, hostEnd - 1);
                if (host.Length == 0)
                {
                    throw new FormatException("MQTT endpoint has an empty IPv6 literal.");
                }
                if (hostEnd + 1 < authority.Length)
                {
                    if (authority[hostEnd + 1] != ':')
                    {
                        throw new FormatException(
                            "MQTT endpoint has an unexpected character after the IPv6 literal.");
                    }
                    port = ParsePort(authority.Substring(hostEnd + 2));
                }
                else
                {
                    port = defaultPort;
                }
            }
            else
            {
                int colon = authority.LastIndexOf(':');
                if (colon >= 0)
                {
                    host = authority.Substring(0, colon);
                    port = ParsePort(authority.Substring(colon + 1));
                }
                else
                {
                    host = authority;
                    port = defaultPort;
                }
            }
            if (host.Length == 0)
            {
                throw new FormatException("MQTT endpoint is missing the host component.");
            }

            string canonicalScheme = isWebSocket
                ? useTls ? WssScheme : WsScheme
                : useTls ? MqttsScheme : MqttScheme;
            string canonical = string.Concat(
                canonicalScheme,
                "://",
                host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host,
                ":",
                port.ToString(CultureInfo.InvariantCulture),
                isWebSocket ? path : string.Empty);
            Uri uri;
            try
            {
                uri = new Uri(canonical, UriKind.Absolute);
            }
            catch (UriFormatException ex)
            {
                throw new FormatException(
                    "MQTT endpoint host component could not be normalised.",
                    ex);
            }
            return new MqttEndpoint(uri, useTls);
        }

        private static int ParsePort(string text)
        {
            if (text.Length == 0)
            {
                throw new FormatException("MQTT endpoint has an empty port component.");
            }
            if (!int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int port) ||
                port <= 0 ||
                port > 65535)
            {
                throw new FormatException(
                    "MQTT endpoint has an invalid port component (must be 1..65535).");
            }
            return port;
        }
    }
}
