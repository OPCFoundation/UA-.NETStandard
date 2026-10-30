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

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Holds one decoded direct datagram with a borrowed UADP body.
    /// </summary>
    internal readonly struct DirectDatagram
    {
        /// <summary>
        /// Creates a decoded datagram view.
        /// </summary>
        internal DirectDatagram(Guid token, uint revision, uint sequence, ByteString body)
        {
            Token = token;
            Revision = revision;
            Sequence = sequence;
            Body = body;
        }

        /// <summary>
        /// Gets the authenticated association token.
        /// </summary>
        internal Guid Token { get; }

        /// <summary>
        /// Gets the active flow revision.
        /// </summary>
        internal uint Revision { get; }

        /// <summary>
        /// Gets the nonzero per-direction replay sequence.
        /// </summary>
        internal uint Sequence { get; }

        /// <summary>
        /// Gets the unchanged UADP NetworkMessage body.
        /// </summary>
        internal ByteString Body { get; }
    }

    /// <summary>
    /// Encodes and decodes the Part 31/32 direct datagram prefix and UADP body.
    /// </summary>
    internal static class DirectDatagramCodec
    {
        /// <summary>
        /// Decodes an exact datagram without copying the UADP body.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static DirectDatagram DecodeExact(
            ByteString input,
            FlowCodecLimits limits,
            int maxDatagramBytes)
        {
            ValidateBounds(limits, maxDatagramBytes);
            if (input.Length > maxDatagramBytes || input.Length > limits.MaxRecordBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            if (input.Length < HeaderSize + 1)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }

            ReadOnlySpan<byte> header = input.Span[..HeaderSize];
            var reader = new FlowBinaryReader(header[..AssociationTokenSize]);
            Guid token = reader.Guid();
            uint revision = BinaryPrimitives.ReadUInt32LittleEndian(header[AssociationTokenSize..]);
            uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(header[(AssociationTokenSize + sizeof(uint))..]);
            ValidateFields(token, revision, sequence);

            int bodyLength = input.Length - HeaderSize;
            if (bodyLength > limits.MaxDataBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            return new DirectDatagram(token, revision, sequence, input[HeaderSize..]);
        }

        /// <summary>
        /// Encodes the fixed token/revision/sequence prefix followed by unchanged UADP bytes.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static ByteString Encode(
            Guid token,
            uint revision,
            uint sequence,
            ByteString body,
            FlowCodecLimits limits,
            int maxDatagramBytes)
        {
            ValidateBounds(limits, maxDatagramBytes);
            ValidateFields(token, revision, sequence);
            if (body.IsNull || body.Length == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidLength);
            }
            if (body.Length > limits.MaxDataBytes ||
                (long)body.Length + HeaderSize > maxDatagramBytes ||
                (long)body.Length + HeaderSize > limits.MaxRecordBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }

            byte[] result = new byte[checked(HeaderSize + body.Length)];
            var writer = new FlowBinaryWriter(result.AsSpan(0, AssociationTokenSize), AssociationTokenSize);
            writer.Guid(token);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(AssociationTokenSize), revision);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(AssociationTokenSize + sizeof(uint)), sequence);
            body.Span.CopyTo(result.AsSpan(HeaderSize));
            return new ByteString(result);
        }

        /// <summary>
        /// Validates local parser limits and the actual path datagram capacity.
        /// </summary>
        private static void ValidateBounds(FlowCodecLimits limits, int maxDatagramBytes)
        {
            if (limits is null)
            {
                throw new ArgumentNullException(nameof(limits));
            }
            if (maxDatagramBytes < HeaderSize + 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDatagramBytes));
            }
        }

        /// <summary>
        /// Rejects empty association and replay fields.
        /// </summary>
        private static void ValidateFields(Guid token, uint revision, uint sequence)
        {
            if (token == Guid.Empty)
            {
                throw new FlowCodecException(FlowCodecError.InvalidFlow);
            }
            if (revision == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidRevision);
            }
            if (sequence == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidMessageId);
            }
        }

        /// <summary>
        /// The token, revision and sequence prefix size.
        /// </summary>
        internal const int HeaderSize = 24;

        /// <summary>
        /// The UA Binary association token size.
        /// </summary>
        internal const int AssociationTokenSize = 16;
    }
}
