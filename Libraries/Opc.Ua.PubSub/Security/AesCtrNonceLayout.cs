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
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Opc.Ua.PubSub.Encoding;
using SystemEncoding = System.Text.Encoding;

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Encodes and decodes the 12-byte AES-CTR <c>MessageNonce</c>
    /// described by Part 14 Table 156. The first 4 bytes carry a
    /// publisher-chosen <c>MessageRandom</c> in big-endian order; the
    /// trailing 8 bytes carry the low 64 bits of the publisher
    /// identifier in little-endian order so byte-comparison is stable
    /// across encodings.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.2">
    /// Part 14 §7.2.4.4.3.2 (Table 156) PubSub nonce composition</see>.
    /// </remarks>
    public static class AesCtrNonceLayout
    {
        /// <summary>
        /// Length of the encoded nonce in bytes.
        /// </summary>
        public const int NonceLength = 12;

        /// <summary>
        /// Length of the <c>MessageRandom</c> prefix in bytes.
        /// </summary>
        public const int MessageRandomLength = 4;

        /// <summary>
        /// Length of the publisher-id projection in bytes.
        /// </summary>
        public const int PublisherIdLength = 8;

        /// <summary>
        /// Writes the 12-byte nonce <c>[messageRandom (4 BE) ||
        /// publisherIdLow64 (8 LE)]</c> into <paramref name="nonce"/>.
        /// </summary>
        /// <param name="messageRandom">Per-message random value.</param>
        /// <param name="publisherIdLow64">
        /// Low 64-bits of the PublisherId projection from
        /// <see cref="ToLow64"/>.
        /// </param>
        /// <param name="nonce">Destination span (must be 12 bytes).</param>
        public static void Build(
            uint messageRandom,
            ulong publisherIdLow64,
            Span<byte> nonce)
        {
            if (nonce.Length != NonceLength)
            {
                throw new ArgumentException(
                    $"Nonce buffer must be exactly {NonceLength} bytes.",
                    nameof(nonce));
            }
            BinaryPrimitives.WriteUInt32BigEndian(
                nonce.Slice(0, MessageRandomLength),
                messageRandom);
            BinaryPrimitives.WriteUInt64LittleEndian(
                nonce.Slice(MessageRandomLength, PublisherIdLength),
                publisherIdLow64);
        }

        /// <summary>
        /// Parses the 12-byte nonce produced by
        /// <see cref="Build"/>.
        /// </summary>
        /// <param name="nonce">Source span (must be 12 bytes).</param>
        /// <returns>The parsed components.</returns>
        public static (uint MessageRandom, ulong PublisherIdLow64) Parse(
            ReadOnlySpan<byte> nonce)
        {
            if (nonce.Length != NonceLength)
            {
                throw new ArgumentException(
                    $"Nonce buffer must be exactly {NonceLength} bytes.",
                    nameof(nonce));
            }
            uint messageRandom = BinaryPrimitives.ReadUInt32BigEndian(
                nonce.Slice(0, MessageRandomLength));
            ulong publisherIdLow64 = BinaryPrimitives.ReadUInt64LittleEndian(
                nonce.Slice(MessageRandomLength, PublisherIdLength));
            return (messageRandom, publisherIdLow64);
        }

        /// <summary>
        /// Projects a <see cref="PublisherId"/> to the stable 64-bit
        /// value that occupies the second half of the nonce. Numeric
        /// PublisherIds are zero-extended; <c>String</c> values use
        /// the first 8 bytes of their UTF-8 encoding (zero-padded);
        /// <c>Guid</c> values use the first 8 bytes of the canonical
        /// guid layout.
        /// </summary>
        /// <param name="publisherId">PublisherId to project.</param>
        /// <returns>Stable 64-bit projection.</returns>
        public static ulong ToLow64(in PublisherId publisherId)
        {
            switch (publisherId.Type)
            {
                case PublisherIdType.Byte:
                    if (publisherId.TryGetByte(out byte b))
                    {
                        return b;
                    }
                    break;
                case PublisherIdType.UInt16:
                    if (publisherId.TryGetUInt16(out ushort u16))
                    {
                        return u16;
                    }
                    break;
                case PublisherIdType.UInt32:
                    if (publisherId.TryGetUInt32(out uint u32))
                    {
                        return u32;
                    }
                    break;
                case PublisherIdType.UInt64:
                    if (publisherId.TryGetUInt64(out ulong u64))
                    {
                        return u64;
                    }
                    break;
                case PublisherIdType.String:
                    if (publisherId.TryGetString(out string? s) && s != null)
                    {
                        return ProjectString(s);
                    }
                    break;
                case PublisherIdType.Guid:
                    if (publisherId.TryGetGuid(out Guid g))
                    {
                        return ProjectGuid(g);
                    }
                    break;
            }
            return 0UL;
        }

        private static ulong ProjectString(string value)
        {
            Span<byte> buffer = stackalloc byte[PublisherIdLength];
            int written = 0;
#if NET6_0_OR_GREATER
            written = SystemEncoding.UTF8.GetBytes(value.AsSpan(), buffer);
            if (written < PublisherIdLength)
            {
                buffer.Slice(written).Clear();
            }
#else
            byte[] utf8 = SystemEncoding.UTF8.GetBytes(value);
            int copy = utf8.Length < PublisherIdLength ? utf8.Length : PublisherIdLength;
            utf8.AsSpan(0, copy).CopyTo(buffer);
            if (copy < PublisherIdLength)
            {
                buffer.Slice(copy).Clear();
            }
            written = copy;
#endif
            _ = written;
            return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
        }

        private static ulong ProjectGuid(Guid value)
        {
            Span<byte> buffer = stackalloc byte[16];
#if NET6_0_OR_GREATER
            if (!value.TryWriteBytes(buffer))
            {
                throw new InvalidOperationException(
                    "Failed to serialise Guid for PublisherId projection.");
            }
#else
            byte[] guidBytes = value.ToByteArray();
            guidBytes.AsSpan().CopyTo(buffer);
#endif
            return BinaryPrimitives.ReadUInt64LittleEndian(
                buffer.Slice(0, PublisherIdLength));
        }

        /// <summary>
        /// Renders a 12-byte nonce as a hexadecimal string. Useful for
        /// diagnostics — never log the encrypting key.
        /// </summary>
        /// <param name="nonce">Nonce bytes.</param>
        /// <returns>Hexadecimal representation.</returns>
        public static string ToDiagnosticString(ReadOnlySpan<byte> nonce)
        {
            if (nonce.Length != NonceLength)
            {
                return string.Empty;
            }
            var sb = new StringBuilder(NonceLength * 2);
            for (int i = 0; i < nonce.Length; i++)
            {
                sb.Append(nonce[i].ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
