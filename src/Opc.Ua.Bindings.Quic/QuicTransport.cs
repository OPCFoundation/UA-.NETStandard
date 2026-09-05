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
using System.Net.Quic;
using System.Net.Security;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// The QUIC transport of the OPC UA Data Channels errata clause 7.
    /// </summary>
    /// <remarks>
    /// Experimental. The ALPN identifier and the TransportProfileUri are
    /// provisional, pending registration by the OPC Foundation.
    /// </remarks>
    public static class QuicTransport
    {
        /// <summary>
        /// True when this platform provides QUIC. On a platform without
        /// msquic the binding is unavailable and a client falls back to
        /// opc.tcp, subject to the rule that fallback shall not be a
        /// downgrade.
        /// </summary>
        public static bool IsSupported => QuicConnection.IsSupported;

        /// <summary>
        /// The ALPN protocol a peer offers and requires the other to
        /// select, so a QUIC endpoint serving another protocol on the
        /// same port is never mistaken for an OPC UA Server.
        /// </summary>
        public static SslApplicationProtocol ApplicationProtocol { get; }
            = new(DataChannelConstants.QuicAlpnProtocol);

        /// <summary>
        /// Builds the endpoint url of a QUIC endpoint.
        /// </summary>
        /// <param name="host">The host name.</param>
        /// <param name="port">The UDP port. It does not collide with TCP
        /// 4840.</param>
        /// <param name="path">The endpoint path, or null.</param>
        public static Uri CreateUrl(
            string host,
            int port = DataChannelConstants.QuicDefaultPort,
            string? path = null)
        {
            var builder = new UriBuilder
            {
                Scheme = DataChannelConstants.QuicScheme,
                Host = host,
                Port = port,
                Path = path ?? string.Empty
            };

            return builder.Uri;
        }

        /// <summary>
        /// True when a url names the QUIC transport.
        /// </summary>
        /// <param name="url">The url.</param>
        public static bool IsQuicUrl(Uri? url)
        {
            return url != null &&
                string.Equals(
                    url.Scheme,
                    DataChannelConstants.QuicScheme,
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Decides whether a fallback from a QUIC endpoint to another
        /// endpoint is permitted.
        /// </summary>
        /// <remarks>
        /// Fallback shall not be a downgrade. Making it unconditional
        /// would hand an off-path attacker a downgrade primitive:
        /// dropping UDP on port 4840 is a single firewall rule, and it
        /// would otherwise move every client to a transport of the
        /// attacker's choosing.
        /// </remarks>
        /// <param name="required">The endpoint the client required of the
        /// QUIC transport.</param>
        /// <param name="candidate">The endpoint being considered
        /// instead.</param>
        public static bool IsAcceptableFallback(
            EndpointDescription required,
            EndpointDescription candidate)
        {
            if (required == null)
            {
                throw new ArgumentNullException(nameof(required));
            }

            if (candidate == null)
            {
                throw new ArgumentNullException(nameof(candidate));
            }

            if (SecurityRank(candidate.SecurityMode) < SecurityRank(required.SecurityMode))
            {
                return false;
            }

            return string.Equals(
                candidate.SecurityPolicyUri,
                required.SecurityPolicyUri,
                StringComparison.Ordinal);
        }

        private static int SecurityRank(MessageSecurityMode mode)
        {
            switch (mode)
            {
                case MessageSecurityMode.SignAndEncrypt:
                    return 3;
                case MessageSecurityMode.Sign:
                    return 2;
                case MessageSecurityMode.None:
                    return 1;
                default:
                    return 0;
            }
        }
    }
}
