// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Collections.Generic;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Encodes the capsule-based WebTransport Extended CONNECT request without dynamic QPACK state.
    /// </summary>
    internal static class QpackConnectRequestEncoder
    {
        /// <summary>
        /// Encodes the minimal request field section using static references and bounded ASCII literals.
        /// </summary>
        internal static bool TryEncode(
            string authority,
            string path,
            string? origin,
            int maximumEncodedBytes,
            out byte[] encoded)
        {
            ArgumentNullException.ThrowIfNull(authority);
            ArgumentNullException.ThrowIfNull(path);
            ArgumentOutOfRangeException.ThrowIfNegative(maximumEncodedBytes);
            if (authority.Length == 0 || path.Length == 0 || path[0] != '/')
            {
                throw new ArgumentException("An exact authority and absolute request path are required.");
            }

            List<byte> output = new(Math.Min(maximumEncodedBytes, 128));
            if (!TryAdd(output, 0, maximumEncodedBytes) ||
                !TryAdd(output, 0, maximumEncodedBytes) ||
                !WritePrefixedInteger(output, 0xc0, 6, 15, maximumEncodedBytes) ||
                !WriteLiteralNameValue(output, ":protocol", "webtransport", maximumEncodedBytes) ||
                !WritePrefixedInteger(output, 0xc0, 6, 23, maximumEncodedBytes) ||
                !WriteStaticNameValue(output, 0, authority, maximumEncodedBytes) ||
                !WriteStaticNameValue(output, 1, path, maximumEncodedBytes) ||
                origin != null &&
                    !WriteLiteralNameValue(output, "origin", origin, maximumEncodedBytes) ||
                !WriteLiteralNameValue(output, "capsule-protocol", "?1", maximumEncodedBytes))
            {
                encoded = [];
                return false;
            }

            encoded = output.ToArray();
            return true;
        }

        /// <summary>
        /// Encodes a successful response that explicitly signals Capsule Protocol use.
        /// </summary>
        internal static bool TryEncodeCapsuleConnectResponse(int maximumEncodedBytes, out byte[] encoded)
        {
            return TryEncodeCapsuleConnectResponse(200, maximumEncodedBytes, out encoded);
        }

        /// <summary>
        /// Encodes a bounded HTTP response and advertises Capsule Protocol only for a successful CONNECT.
        /// </summary>
        internal static bool TryEncodeCapsuleConnectResponse(
            int statusCode,
            int maximumEncodedBytes,
            out byte[] encoded)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maximumEncodedBytes);
            if (statusCode is < 200 or > 599)
            {
                throw new ArgumentOutOfRangeException(nameof(statusCode));
            }

            List<byte> output = new(Math.Min(maximumEncodedBytes, 64));
            if (!TryAdd(output, 0, maximumEncodedBytes) ||
                !TryAdd(output, 0, maximumEncodedBytes))
            {
                encoded = [];
                return false;
            }
            bool statusWritten = statusCode == 200
                ? WritePrefixedInteger(output, 0xc0, 6, 25, maximumEncodedBytes)
                : WriteLiteralNameValue(
                    output,
                    ":status",
                    statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    maximumEncodedBytes);
            if (!statusWritten ||
                statusCode == 200 &&
                    !WriteLiteralNameValue(output, "capsule-protocol", "?1", maximumEncodedBytes))
            {
                encoded = [];
                return false;
            }

            encoded = output.ToArray();
            return true;
        }

        private static bool WriteLiteralNameValue(
            List<byte> output,
            string name,
            string value,
            int maximumEncodedBytes)
        {
            return WritePrefixedInteger(
                    output,
                    0x20,
                    3,
                    (ulong)name.Length,
                    maximumEncodedBytes) &&
                WriteAscii(output, name, maximumEncodedBytes) &&
                WriteString(output, value, maximumEncodedBytes);
        }

        private static bool WriteStaticNameValue(
            List<byte> output,
            ulong staticNameIndex,
            string value,
            int maximumEncodedBytes)
        {
            return WritePrefixedInteger(
                    output,
                    0x50,
                    4,
                    staticNameIndex,
                    maximumEncodedBytes) &&
                WriteString(output, value, maximumEncodedBytes);
        }

        private static bool WriteString(List<byte> output, string value, int maximumEncodedBytes)
        {
            return WritePrefixedInteger(output, 0, 7, (ulong)value.Length, maximumEncodedBytes) &&
                WriteAscii(output, value, maximumEncodedBytes);
        }

        private static bool WriteAscii(List<byte> output, string value, int maximumEncodedBytes)
        {
            foreach (char character in value)
            {
                if (character > 0x7f || !TryAdd(output, (byte)character, maximumEncodedBytes))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool WritePrefixedInteger(
            List<byte> output,
            byte flags,
            int prefixBits,
            ulong value,
            int maximumEncodedBytes)
        {
            ulong prefixMaximum = (1UL << prefixBits) - 1;
            if (value < prefixMaximum)
            {
                return TryAdd(output, (byte)(flags | value), maximumEncodedBytes);
            }
            if (!TryAdd(output, (byte)(flags | prefixMaximum), maximumEncodedBytes))
            {
                return false;
            }

            value -= prefixMaximum;
            while (value >= 0x80)
            {
                if (!TryAdd(output, (byte)((value & 0x7f) | 0x80), maximumEncodedBytes))
                {
                    return false;
                }
                value >>= 7;
            }
            return TryAdd(output, (byte)value, maximumEncodedBytes);
        }

        private static bool TryAdd(List<byte> output, byte value, int maximumEncodedBytes)
        {
            if (output.Count == maximumEncodedBytes)
            {
                return false;
            }
            output.Add(value);
            return true;
        }
    }
}
