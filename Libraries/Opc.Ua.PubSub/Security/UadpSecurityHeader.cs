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

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// On-wire UADP SecurityHeader: SecurityFlags byte,
    /// SecurityTokenId (UInt32), the variable-length MessageNonce,
    /// and an optional SecurityFooterSize (UInt16) when the
    /// SecurityFooter bit is set.
    /// </summary>
    /// <remarks>
    /// Implements the SecurityHeader layout described by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3">
    /// Part 14 §7.2.4.4.3</see>, with the bit-level structure detailed
    /// by Annex
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.1.6">
    /// A.2.1.6</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.5">
    /// A.2.2.5</see>. The MessageNonce is preceded by a single-byte
    /// length prefix on the wire — this struct stores the nonce bytes
    /// without the length prefix; <see cref="WriteTo"/> and
    /// <see cref="TryRead"/> handle the prefix.
    /// </remarks>
    public readonly record struct UadpSecurityHeader
    {
        /// <summary>
        /// Initializes a new <see cref="UadpSecurityHeader"/>.
        /// </summary>
        /// <param name="securityFlags">SecurityFlags byte.</param>
        /// <param name="securityTokenId">SKS-issued token id.</param>
        /// <param name="messageNonce">Per-message nonce.</param>
        /// <param name="securityFooterSize">
        /// SecurityFooter size, valid only when the
        /// <see cref="UadpSecurityFlagsEncodingMask.SecurityFooterEnabled"/>
        /// flag is set.
        /// </param>
        public UadpSecurityHeader(
            byte securityFlags,
            uint securityTokenId,
            ReadOnlyMemory<byte> messageNonce,
            ushort securityFooterSize = 0)
        {
            if (messageNonce.Length > 255)
            {
                throw new ArgumentException(
                    "MessageNonce length is encoded in a single byte and cannot exceed 255.",
                    nameof(messageNonce));
            }
            SecurityFlags = securityFlags;
            SecurityTokenId = securityTokenId;
            MessageNonce = messageNonce;
            SecurityFooterSize = securityFooterSize;
        }

        /// <summary>SecurityFlags byte.</summary>
        public byte SecurityFlags { get; }

        /// <summary>SKS-issued token id.</summary>
        public uint SecurityTokenId { get; }

        /// <summary>Per-message nonce (without the length prefix).</summary>
        public ReadOnlyMemory<byte> MessageNonce { get; }

        /// <summary>SecurityFooter size in bytes.</summary>
        public ushort SecurityFooterSize { get; }

        /// <summary>
        /// Returns the encoded size in bytes of this header, including
        /// the SecurityFooterSize field when applicable.
        /// </summary>
        public int GetEncodedSize()
        {
            int size = 1 /* SecurityFlags */
                + 4 /* SecurityTokenId */
                + 1 /* nonce length */
                + MessageNonce.Length;
            if ((SecurityFlags & (byte)UadpSecurityFlagsEncodingMask.SecurityFooterEnabled) != 0)
            {
                size += 2;
            }
            return size;
        }

        /// <summary>
        /// Writes this header into <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">Destination span.</param>
        /// <param name="written">Bytes written.</param>
        public void WriteTo(Span<byte> buffer, out int written)
        {
            int size = GetEncodedSize();
            if (buffer.Length < size)
            {
                throw new ArgumentException(
                    "Destination buffer is shorter than the encoded SecurityHeader.",
                    nameof(buffer));
            }
            int offset = 0;
            buffer[offset++] = SecurityFlags;
            BinaryPrimitives.WriteUInt32LittleEndian(
                buffer.Slice(offset, 4),
                SecurityTokenId);
            offset += 4;
            buffer[offset++] = (byte)MessageNonce.Length;
            MessageNonce.Span.CopyTo(buffer.Slice(offset));
            offset += MessageNonce.Length;
            if ((SecurityFlags & (byte)UadpSecurityFlagsEncodingMask.SecurityFooterEnabled) != 0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    buffer.Slice(offset, 2),
                    SecurityFooterSize);
                offset += 2;
            }
            written = offset;
        }

        /// <summary>
        /// Reads a SecurityHeader from <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">Source bytes.</param>
        /// <param name="header">Decoded header.</param>
        /// <param name="consumed">Bytes consumed.</param>
        /// <returns>
        /// <see langword="true"/> on success; <see langword="false"/>
        /// when the buffer is truncated or malformed.
        /// </returns>
        public static bool TryRead(
            ReadOnlySpan<byte> buffer,
            out UadpSecurityHeader header,
            out int consumed)
        {
            header = default;
            consumed = 0;
            if (buffer.Length < 1 + 4 + 1)
            {
                return false;
            }
            byte flags = buffer[0];
            uint tokenId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(1, 4));
            byte nonceLength = buffer[5];
            int needed = 6 + nonceLength;
            if ((flags & (byte)UadpSecurityFlagsEncodingMask.SecurityFooterEnabled) != 0)
            {
                needed += 2;
            }
            if (buffer.Length < needed)
            {
                return false;
            }
            byte[] nonce = new byte[nonceLength];
            buffer.Slice(6, nonceLength).CopyTo(nonce);
            ushort footerSize = 0;
            int offset = 6 + nonceLength;
            if ((flags & (byte)UadpSecurityFlagsEncodingMask.SecurityFooterEnabled) != 0)
            {
                footerSize = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(offset, 2));
                offset += 2;
            }
            header = new UadpSecurityHeader(flags, tokenId, nonce, footerSize);
            consumed = offset;
            return true;
        }
    }
}
