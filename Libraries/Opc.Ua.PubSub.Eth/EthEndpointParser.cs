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
using System.Net.NetworkInformation;

namespace Opc.Ua.PubSub.Eth
{
    /// <summary>
    /// Dedicated parser for
    /// <c>opc.eth://&lt;mac&gt;[?vid=&lt;0-4095&gt;&amp;pcp=&lt;0-7&gt;]</c>
    /// URLs. Validates the destination MAC address and the optional
    /// IEEE 802.1Q VLAN identifier (VID) and priority (PCP), and
    /// classifies the address as unicast, multicast, or broadcast so
    /// the transport layer can select membership / filtering without
    /// re-parsing on every connect.
    /// </summary>
    /// <remarks>
    /// Implements the addressing of the OPC UA Part 14 Ethernet mapping.
    /// The MAC address accepts the hyphen form
    /// <c>xx-xx-xx-xx-xx-xx</c>, the colon form
    /// <c>xx:xx:xx:xx:xx:xx</c>, and the bare 12 hexadecimal digit form
    /// <c>xxxxxxxxxxxx</c>. VLAN parameters are supplied through the
    /// query string (<c>?vid=&amp;pcp=</c>); the legacy
    /// <c>&lt;mac&gt;:&lt;vid&gt;.&lt;pcp&gt;</c> suffix is also accepted
    /// for backward compatibility.
    /// </remarks>
    public static class EthEndpointParser
    {
        /// <summary>
        /// URL scheme handled by this parser.
        /// </summary>
        public const string Scheme = "opc.eth";

        private const string SchemePrefix = "opc.eth://";

        /// <summary>
        /// Parses the supplied URL into an <see cref="EthEndpoint"/>.
        /// </summary>
        /// <param name="url">
        /// URL of the form
        /// <c>opc.eth://&lt;mac&gt;[?vid=&lt;0-4095&gt;&amp;pcp=&lt;0-7&gt;]</c>.
        /// An optional trailing path component is accepted for forward
        /// compatibility but ignored.
        /// </param>
        /// <returns>The parsed endpoint.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="url"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="FormatException">
        /// <paramref name="url"/> does not start with <c>opc.eth://</c>,
        /// or the MAC / VLAN components are malformed or out of range.
        /// </exception>
        public static EthEndpoint Parse(string url)
        {
            if (url is null)
            {
                throw new ArgumentNullException(nameof(url));
            }
            if (url.Length == 0)
            {
                throw new FormatException("PubSub Ethernet URL must not be empty.");
            }
            if (!url.StartsWith(SchemePrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("PubSub Ethernet URL must start with 'opc.eth://'.");
            }
            string remainder = url[SchemePrefix.Length..];
            if (remainder.Length == 0)
            {
                throw new FormatException(
                    "PubSub Ethernet URL is missing the MAC address component.");
            }

            string? query = null;
            int queryStart = remainder.IndexOf('?', StringComparison.Ordinal);
            if (queryStart >= 0)
            {
                query = remainder[(queryStart + 1)..];
                remainder = remainder[..queryStart];
            }
            int pathStart = remainder.IndexOf('/', StringComparison.Ordinal);
            if (pathStart >= 0)
            {
                remainder = remainder[..pathStart];
            }
            if (remainder.Length == 0)
            {
                throw new FormatException(
                    "PubSub Ethernet URL is missing the MAC address component.");
            }

            byte[] mac = ParseMac(remainder, out int consumed);

            ushort? vlanId = null;
            byte? priority = null;

            string suffix = remainder[consumed..];
            if (suffix.Length > 0)
            {
                if (suffix[0] != ':')
                {
                    throw new FormatException(
                        $"PubSub Ethernet URL has unexpected characters after the MAC address: '{suffix}'.");
                }
                ParseVlanSuffix(suffix[1..], ref vlanId, ref priority);
            }

            if (query is not null)
            {
                ParseQuery(query, ref vlanId, ref priority);
            }

            var address = new PhysicalAddress(mac);
            EthAddressType type = ClassifyAddress(mac);
            return new EthEndpoint(address, vlanId, priority, type, url);
        }

        /// <summary>
        /// Classifies the supplied <see cref="PhysicalAddress"/> as
        /// unicast, multicast, or broadcast per the Ethernet I/G bit and
        /// the all-ones broadcast address. Exposed so consumers can
        /// re-classify addresses obtained from sources other than
        /// <see cref="Parse"/>.
        /// </summary>
        /// <param name="address">Address to classify.</param>
        /// <returns>The address type.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="address"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="address"/> is not a six-octet MAC address.
        /// </exception>
        public static EthAddressType ClassifyAddress(PhysicalAddress address)
        {
            if (address is null)
            {
                throw new ArgumentNullException(nameof(address));
            }
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length != 6)
            {
                throw new ArgumentException(
                    "Ethernet MAC address must be six octets.", nameof(address));
            }
            return ClassifyAddress(bytes);
        }

        internal static EthAddressType ClassifyAddress(ReadOnlySpan<byte> mac)
        {
            bool allOnes = true;
            for (int i = 0; i < mac.Length; i++)
            {
                if (mac[i] != 0xFF)
                {
                    allOnes = false;
                    break;
                }
            }
            if (allOnes)
            {
                return EthAddressType.Broadcast;
            }
            return (mac[0] & 0x01) != 0
                ? EthAddressType.Multicast
                : EthAddressType.Unicast;
        }

        private static byte[] ParseMac(string text, out int consumed)
        {
            if (TryParseSeparatedMac(text, out byte[]? separated))
            {
                consumed = 17;
                return separated!;
            }
            if (TryParseHexMac(text, out byte[]? hex))
            {
                consumed = 12;
                return hex!;
            }
            throw new FormatException(
                $"PubSub Ethernet URL has an invalid MAC address '{text}'. Expected forms: " +
                "'xx-xx-xx-xx-xx-xx', 'xx:xx:xx:xx:xx:xx', or 'xxxxxxxxxxxx'.");
        }

        private static bool TryParseSeparatedMac(string text, out byte[]? mac)
        {
            mac = null;
            if (text.Length < 17)
            {
                return false;
            }
            char separator = text[2];
            if (separator != '-' && separator != ':')
            {
                return false;
            }
            var bytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                int offset = i * 3;
                if (i > 0 && text[offset - 1] != separator)
                {
                    return false;
                }
                if (!TryHex(text[offset], out int high) || !TryHex(text[offset + 1], out int low))
                {
                    return false;
                }
                bytes[i] = (byte)((high << 4) | low);
            }
            mac = bytes;
            return true;
        }

        private static bool TryParseHexMac(string text, out byte[]? mac)
        {
            mac = null;
            if (text.Length < 12)
            {
                return false;
            }
            var bytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                if (!TryHex(text[i * 2], out int high) || !TryHex(text[i * 2 + 1], out int low))
                {
                    return false;
                }
                bytes[i] = (byte)((high << 4) | low);
            }
            // A 13th hexadecimal digit would make the length ambiguous.
            if (text.Length > 12 && TryHex(text[12], out _))
            {
                return false;
            }
            mac = bytes;
            return true;
        }

        private static void ParseVlanSuffix(string text, ref ushort? vlanId, ref byte? priority)
        {
            if (text.Length == 0)
            {
                throw new FormatException("PubSub Ethernet URL has an empty VLAN suffix.");
            }
            int dot = text.IndexOf('.', StringComparison.Ordinal);
            string vidText = dot >= 0 ? text[..dot] : text;
            vlanId = ParseVid(vidText);
            if (dot >= 0)
            {
                priority = ParsePcp(text[(dot + 1)..]);
            }
        }

        private static void ParseQuery(string query, ref ushort? vlanId, ref byte? priority)
        {
            string[] segments = query.Split('&');
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (segment.Length == 0)
                {
                    continue;
                }
                int equals = segment.IndexOf('=', StringComparison.Ordinal);
                if (equals < 0)
                {
                    throw new FormatException(
                        $"PubSub Ethernet URL has an invalid query segment '{segment}'.");
                }
                string key = segment[..equals];
                string value = segment[(equals + 1)..];
                if (string.Equals(key, "vid", StringComparison.OrdinalIgnoreCase))
                {
                    vlanId = ParseVid(value);
                }
                else if (string.Equals(key, "pcp", StringComparison.OrdinalIgnoreCase))
                {
                    priority = ParsePcp(value);
                }
                else
                {
                    throw new FormatException(
                        $"PubSub Ethernet URL has an unknown query parameter '{key}'.");
                }
            }
        }

        private static ushort ParseVid(string text)
        {
            if (!ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort vid)
                || vid > 4095)
            {
                throw new FormatException(
                    $"PubSub Ethernet URL has an invalid VLAN id '{text}' (must be 0-4095).");
            }
            return vid;
        }

        private static byte ParsePcp(string text)
        {
            if (!byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte pcp)
                || pcp > 7)
            {
                throw new FormatException(
                    $"PubSub Ethernet URL has an invalid priority '{text}' (must be 0-7).");
            }
            return pcp;
        }

        private static bool TryHex(char c, out int value)
        {
            if (c >= '0' && c <= '9')
            {
                value = c - '0';
                return true;
            }
            if (c >= 'a' && c <= 'f')
            {
                value = c - 'a' + 10;
                return true;
            }
            if (c >= 'A' && c <= 'F')
            {
                value = c - 'A' + 10;
                return true;
            }
            value = 0;
            return false;
        }
    }
}
