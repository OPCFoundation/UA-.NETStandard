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
    /// Encodes and decodes the 8-byte AES-CTR <c>MessageNonce</c>
    /// carried in the UADP SecurityHeader, described by Part 14
    /// Table 156 as <c>Random[4] || SequenceNumber (UInt32)</c>. The
    /// first 4 bytes carry a publisher-chosen <c>MessageRandom</c>
    /// (CSPRNG) in big-endian order; the trailing 4 bytes carry the
    /// per-key <c>SequenceNumber</c> encoded as an OPC UA UInt32
    /// (little-endian). Because the sequence number increments for
    /// every message produced under a given key, no two nonces repeat
    /// within a key's lifetime.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3.2">
    /// Part 14 §7.2.4.4.3.2 (Tables 156 and 157) PubSub nonce composition</see>.
    /// The AES-CTR counter block is
    /// <c>KeyNonce[4] || MessageNonce[8] || BlockCounter[4]</c>, where
    /// <c>KeyNonce</c> is the nonce portion of the key data returned by
    /// <c>GetSecurityKeys</c>. The receiver extracts the sequence number
    /// from the (signed) nonce and feeds it to the replay window.
    /// </remarks>
    public static class AesCtrNonceLayout
    {
        /// <summary>
        /// Length of the SecurityHeader <c>MessageNonce</c> in bytes.
        /// For AES-CTR mode the length of the SecurityHeader Nonce
        /// shall be 8 bytes (Part 14 Table 157).
        /// </summary>
        public const int NonceLength = 8;

        /// <summary>
        /// Length of the <c>KeyNonce</c> portion of the key data returned
        /// by <c>GetSecurityKeys</c> (Part 14 Table 157).
        /// </summary>
        public const int KeyNonceLength = 4;

        /// <summary>
        /// Length of the counter block prefix
        /// <c>KeyNonce || MessageNonce</c> that precedes the 4-byte
        /// block counter (Part 14 Table 157).
        /// </summary>
        public const int CounterNonceLength = KeyNonceLength + NonceLength;

        /// <summary>
        /// Length of the <c>MessageRandom</c> prefix in bytes.
        /// </summary>
        public const int MessageRandomLength = 4;

        /// <summary>
        /// Length of the <c>SequenceNumber</c> suffix in bytes.
        /// </summary>
        public const int SequenceNumberLength = 4;

        /// <summary>
        /// Length of the publisher-id projection in bytes.
        /// </summary>
        public const int PublisherIdLength = 8;

        /// <summary>
        /// Writes the 8-byte nonce <code>[messageRandom (4 BE) ||
        /// sequenceNumber (UInt32 LE)]</code> into
        /// <paramref name="nonce"/>.
        /// </summary>
        /// <param name="messageRandom">Per-message random value.</param>
        /// <param name="messageSequenceNumber">
        /// Per-key message sequence number; must fit a UInt32.
        /// </param>
        /// <param name="nonce">Destination span (must be 8 bytes).</param>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static void Build(
            uint messageRandom,
            ulong messageSequenceNumber,
            Span<byte> nonce)
        {
            if (nonce.Length != NonceLength)
            {
                throw new ArgumentException(
                    $"Nonce buffer must be exactly {NonceLength} bytes.",
                    nameof(nonce));
            }
            if (messageSequenceNumber > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(messageSequenceNumber),
                    "The AES-CTR SequenceNumber is a UInt32.");
            }
            BinaryPrimitives.WriteUInt32BigEndian(
                nonce[..MessageRandomLength],
                messageRandom);
            BinaryPrimitives.WriteUInt32LittleEndian(
                nonce.Slice(MessageRandomLength, SequenceNumberLength),
                (uint)messageSequenceNumber);
        }

        /// <summary>
        /// Parses the 8-byte nonce produced by
        /// <see cref="Build"/>.
        /// </summary>
        /// <param name="nonce">Source span (must be 8 bytes).</param>
        /// <returns>The parsed components.</returns>
        /// <exception cref="ArgumentException"></exception>
        public static AesCtrNonceComponents Parse(
            ReadOnlySpan<byte> nonce)
        {
            if (nonce.Length != NonceLength)
            {
                throw new ArgumentException(
                    $"Nonce buffer must be exactly {NonceLength} bytes.",
                    nameof(nonce));
            }
            uint messageRandom = BinaryPrimitives.ReadUInt32BigEndian(
                nonce[..MessageRandomLength]);
            uint messageSequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(
                nonce.Slice(MessageRandomLength, SequenceNumberLength));
            return new AesCtrNonceComponents(messageRandom, messageSequenceNumber);
        }

        /// <summary>
        /// Writes the counter block prefix <c>KeyNonce || MessageNonce</c>
        /// (Part 14 Table 157) that the AES-CTR primitive extends with the
        /// big-endian block counter.
        /// </summary>
        /// <param name="keyNonce">The KeyNonce portion of the key data.</param>
        /// <param name="messageNonce">The SecurityHeader nonce.</param>
        /// <param name="destination">
        /// Destination span; must be exactly
        /// <c>keyNonce.Length + messageNonce.Length</c> bytes.
        /// </param>
        /// <exception cref="ArgumentException"></exception>
        internal static void WriteCounterNonce(
            ReadOnlySpan<byte> keyNonce,
            ReadOnlySpan<byte> messageNonce,
            Span<byte> destination)
        {
            if (destination.Length != keyNonce.Length + messageNonce.Length)
            {
                throw new ArgumentException(
                    "Destination must hold the KeyNonce and the MessageNonce.",
                    nameof(destination));
            }
            keyNonce.CopyTo(destination);
            messageNonce.CopyTo(destination[keyNonce.Length..]);
        }

        /// <summary>
        /// Length of the <c>KeyNonce</c> portion of the key data for
        /// <paramref name="policy"/>. AES-CTR is the only encryption the
        /// UADP message security defines and its KeyNonce is 4 bytes
        /// (Part 14 Table 157); a policy without a message nonce has no
        /// KeyNonce either.
        /// </summary>
        internal static int GetKeyNonceLength(IPubSubSecurityPolicy policy)
        {
            return policy.NonceLength == 0 ? 0 : KeyNonceLength;
        }

        /// <summary>
        /// Projects a <see cref="PublisherId"/> to a stable 64-bit
        /// value. Numeric PublisherIds are zero-extended; <c>String</c>
        /// values use the first 8 bytes of their UTF-8 encoding
        /// (zero-padded); <c>Guid</c> values use the first 8 bytes of
        /// the canonical guid layout. Retained as a diagnostic /
        /// domain-separation helper — the default nonce suffix is the
        /// monotonic <c>MessageSequenceNumber</c>, not this projection.
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
                buffer[written..].Clear();
            }
#else
            byte[] utf8 = SystemEncoding.UTF8.GetBytes(value);
            int copy = utf8.Length < PublisherIdLength ? utf8.Length : PublisherIdLength;
            utf8.AsSpan(0, copy).CopyTo(buffer);
            if (copy < PublisherIdLength)
            {
                buffer[copy..].Clear();
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
                buffer[..PublisherIdLength]);
        }

        /// <summary>
        /// Renders an 8-byte nonce as a hexadecimal string. Useful for
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

    /// <summary>
    /// The two components carried by an AES-CTR <c>MessageNonce</c>
    /// parsed from its 8-byte wire layout by
    /// <see cref="AesCtrNonceLayout.Parse"/>.
    /// </summary>
    /// <param name="MessageRandom">
    /// Publisher-chosen 4-byte CSPRNG value carried in the nonce prefix.
    /// </param>
    /// <param name="MessageSequenceNumber">
    /// Per-key UInt32 message sequence number carried in the nonce
    /// suffix.
    /// </param>
    public readonly record struct AesCtrNonceComponents(
        uint MessageRandom,
        ulong MessageSequenceNumber);
}
