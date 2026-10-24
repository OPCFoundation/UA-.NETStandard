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
using System.Buffers.Binary;

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Holds a common record. The owner must keep body storage immutable while the record is in use.
    /// </summary>
    internal readonly struct FlowRecord
    {
        /// <summary>
        /// Creates a record view without copying its body.
        /// </summary>
        internal FlowRecord(FlowRecordKind kind, uint revision, uint messageId, ByteString body)
        {
            Kind = kind;
            Revision = revision;
            MessageId = messageId;
            Body = body;
        }

        /// <summary>
        /// Gets the body discriminator.
        /// </summary>
        internal FlowRecordKind Kind { get; }

        /// <summary>
        /// Gets the revision.
        /// </summary>
        internal uint Revision { get; }

        /// <summary>
        /// Gets the message or operation identifier.
        /// </summary>
        internal uint MessageId { get; }

        /// <summary>
        /// Gets the complete unchanged body, without an inner encoding wrapper.
        /// </summary>
        internal ByteString Body { get; }
    }

    /// <summary>
    /// Holds a validated fixed header before any body allocation.
    /// </summary>
    internal readonly struct FlowRecordHeader
    {
        /// <summary>
        /// Creates a validated header.
        /// </summary>
        internal FlowRecordHeader(FlowRecordKind kind, uint revision, uint messageId, int bodyLength)
        {
            Kind = kind;
            Revision = revision;
            MessageId = messageId;
            BodyLength = bodyLength;
        }

        /// <summary>
        /// Gets the body discriminator.
        /// </summary>
        internal FlowRecordKind Kind { get; }

        /// <summary>
        /// Gets the revision.
        /// </summary>
        internal uint Revision { get; }

        /// <summary>
        /// Gets the message identifier.
        /// </summary>
        internal uint MessageId { get; }

        /// <summary>
        /// Gets the bounded body length.
        /// </summary>
        internal int BodyLength { get; }

        /// <summary>
        /// Gets the complete common-record length.
        /// </summary>
        internal int TotalLength => checked(FlowRecordCodec.HeaderSize + BodyLength);
    }

    /// <summary>
    /// Encodes and inspects common records without interpreting application payloads or carrier security.
    /// </summary>
    internal static class FlowRecordCodec
    {
        /// <summary>
        /// Decodes exactly one record, borrowing the input storage. CONTROL syntax is decoded separately.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowRecord DecodeExact(ByteString input, FlowCodecLimits limits)
        {
            FlowRecordHeader header = ReadHeader(input.Span, limits);
            if (input.Length < header.TotalLength)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            if (input.Length != header.TotalLength)
            {
                throw new FlowCodecException(FlowCodecError.TrailingBytes);
            }
            return new FlowRecord(header.Kind, header.Revision, header.MessageId, input[HeaderSize..]);
        }

        /// <summary>
        /// Reads and bounds a header before its body is buffered.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static FlowRecordHeader ReadHeader(ReadOnlySpan<byte> input, FlowCodecLimits limits)
        {
            InspectPrefix(input, limits);
            if (input.Length < HeaderSize)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            var kind = (FlowRecordKind)input[4];
            uint revision = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(5, 4));
            uint messageId = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(9, 4));
            ValidateFields(kind, revision, messageId);
            int bodyLength = checked((int)(BinaryPrimitives.ReadUInt32LittleEndian(input) - 9));
            return new FlowRecordHeader(kind, revision, messageId, bodyLength);
        }

        /// <summary>
        /// Inspects a possibly incomplete prefix without allocating body storage.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static void InspectPrefix(ReadOnlySpan<byte> input, FlowCodecLimits limits)
        {
            if (limits is null)
            {
                throw new ArgumentNullException(nameof(limits));
            }
            if (input.Length < 4)
            {
                return;
            }
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(input);
            if (length < 9)
            {
                throw new FlowCodecException(FlowCodecError.InvalidLength);
            }
            ulong total = (ulong)length + 4;
            uint bodyLength = length - 9;
            if (total > (ulong)limits.MaxRecordBytes ||
                bodyLength > (uint)Math.Max(limits.MaxDataBytes, limits.MaxControlBytes))
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            if (input.Length < 5)
            {
                return;
            }
            int maximum = (FlowRecordKind)input[4] switch
            {
                FlowRecordKind.Data => limits.MaxDataBytes,
                FlowRecordKind.Control => limits.MaxControlBytes,
                _ => throw new FlowCodecException(FlowCodecError.UnknownKind)
            };
            if (bodyLength > (uint)maximum)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
        }

        /// <summary>
        /// Allocates one bounded record and preserves its body bytes exactly.
        /// </summary>
        internal static ByteString Encode(in FlowRecord record, FlowCodecLimits limits)
        {
            int length = GetEncodedLength(in record, limits);
            byte[] buffer = new byte[length];
            Write(in record, buffer, limits);
            return new ByteString(buffer);
        }

        /// <summary>
        /// Validates a record and obtains its allocation size without allocating.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static int GetEncodedLength(in FlowRecord record, FlowCodecLimits limits)
        {
            if (limits is null)
            {
                throw new ArgumentNullException(nameof(limits));
            }
            ValidateFields(record.Kind, record.Revision, record.MessageId);
            if (record.Body.IsNull)
            {
                throw new FlowCodecException(FlowCodecError.WrongType);
            }
            int maximum = record.Kind == FlowRecordKind.Data ? limits.MaxDataBytes : limits.MaxControlBytes;
            if (record.Body.Length > maximum ||
                (long)record.Body.Length + HeaderSize > limits.MaxRecordBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            return checked(HeaderSize + record.Body.Length);
        }

        /// <summary>
        /// Writes a complete record into caller-owned storage after validating its bounds.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static int Write(in FlowRecord record, Span<byte> destination, FlowCodecLimits limits)
        {
            int length = GetEncodedLength(in record, limits);
            if (destination.Length < length)
            {
                throw new ArgumentException("The record destination is too short.", nameof(destination));
            }
            // Copy first so an overlapping payload cannot be destroyed by header writes.
            record.Body.Span.CopyTo(destination.Slice(HeaderSize, record.Body.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(destination, checked((uint)(length - 4)));
            destination[4] = (byte)record.Kind;
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(5, 4), record.Revision);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(9, 4), record.MessageId);
            return length;
        }

        /// <summary>
        /// Validates fields whose meaning is independent of flow state.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static void ValidateFields(FlowRecordKind kind, uint revision, uint messageId)
        {
            if (kind is not (FlowRecordKind.Data or FlowRecordKind.Control))
            {
                throw new FlowCodecException(FlowCodecError.UnknownKind);
            }
            if (messageId == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidMessageId);
            }
            if (kind == FlowRecordKind.Data && revision == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidRevision);
            }
        }

        /// <summary>The complete fixed common-record header size.</summary>
        internal const int HeaderSize = 13;
    }
}
