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
using System.Threading;

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Identifies a complete record on an authenticated direct ordered stream.
    /// </summary>
    internal readonly struct DirectDeliveryFrame : IDisposable
    {
        /// <summary>
        /// Creates a decoded record and transfers its admission reservation.
        /// </summary>
        internal DirectDeliveryFrame(
            DirectDeliveryFrameKind kind,
            uint revision,
            uint operationId,
            ByteString body,
            DirectDeliveryFrameReservation? reservation = null,
            FlowControl? control = null)
        {
            Kind = kind;
            Revision = revision;
            OperationId = operationId;
            Body = body;
            Control = control;
            m_reservation = reservation;
        }

        /// <summary>
        /// Gets the direct-stream record kind.
        /// </summary>
        internal DirectDeliveryFrameKind Kind { get; }

        /// <summary>
        /// Gets the barrier revision, or zero for data records.
        /// </summary>
        internal uint Revision { get; }

        /// <summary>
        /// Gets the barrier operation identifier, or zero for data records.
        /// </summary>
        internal uint OperationId { get; }

        /// <summary>
        /// Gets the unchanged UADP message body, or the encoded binary control body for a barrier.
        /// </summary>
        internal ByteString Body { get; }

        /// <summary>
        /// Gets the decoded COMMIT or END control, or null for application DATA.
        /// </summary>
        internal FlowControl? Control { get; }

        /// <summary>
        /// Transfers the decoder's complete message charge into application ownership without a second copy.
        /// </summary>
        internal void TransferMessageReservation()
        {
            if (m_reservation == null)
            {
                throw new InvalidOperationException("The decoded message has no admission reservation.");
            }
            m_reservation.TransferMessageReservation();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_reservation?.Dispose();
        }

        private readonly DirectDeliveryFrameReservation? m_reservation;
    }

    /// <summary>
    /// Owns one admission reservation transferred from the incremental frame decoder.
    /// </summary>
    internal sealed class DirectDeliveryFrameReservation : IDisposable
    {
        /// <summary>
        /// Creates a reservation owner.
        /// </summary>
        internal DirectDeliveryFrameReservation(IDisposable reservation)
        {
            m_reservation = reservation ?? throw new ArgumentNullException(nameof(reservation));
        }

        /// <summary>
        /// Transfers only message reservations; control and parser owners remain independently scoped.
        /// </summary>
        internal void TransferMessageReservation()
        {
            if (m_reservation is not DataChannelApplicationBudget.DataChannelMessageReservation message)
            {
                throw new InvalidOperationException("The decoded frame does not own a message reservation.");
            }
            message.TransferOwnership();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Interlocked.Exchange(ref m_reservation, null)?.Dispose();
        }

        private IDisposable? m_reservation;
    }

    /// <summary>
    /// Identifies records on the direct ordered stream.
    /// </summary>
    internal enum DirectDeliveryFrameKind : byte
    {
        /// <summary>
        /// A complete unchanged UADP NetworkMessage.
        /// </summary>
        Data = 0,

        /// <summary>
        /// An ordered COMMIT or END barrier.
        /// </summary>
        Barrier = 1
    }

    /// <summary>
    /// Validates and encodes the Part 31/32 direct ordered-stream framing.
    /// </summary>
    internal static class DirectDeliveryFrameCodec
    {
        /// <summary>
        /// Decodes one complete frame without copying its body.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static DirectDeliveryFrame DecodeExact(ByteString input, FlowCodecLimits limits)
        {
            DirectDeliveryFrameHeader header = ReadHeader(input.Span, limits);
            if (input.Length < header.TotalLength)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            if (input.Length != header.TotalLength)
            {
                throw new FlowCodecException(FlowCodecError.TrailingBytes);
            }

            return DecodeBody(header.Kind, input[HeaderSize..], limits);
        }

        /// <summary>
        /// Reads and bounds a frame header before its body is retained.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static DirectDeliveryFrameHeader ReadHeader(ReadOnlySpan<byte> input, FlowCodecLimits limits)
        {
            InspectPrefix(input, limits);
            if (input.Length < HeaderSize)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }

            uint length = BinaryPrimitives.ReadUInt32LittleEndian(input);
            var kind = (DirectDeliveryFrameKind)input[sizeof(uint)];
            int bodyLength = checked((int)length - sizeof(byte));
            return new DirectDeliveryFrameHeader(kind, bodyLength);
        }

        /// <summary>
        /// Inspects an incomplete prefix without allocating record storage.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static void InspectPrefix(ReadOnlySpan<byte> input, FlowCodecLimits limits)
        {
            if (limits is null)
            {
                throw new ArgumentNullException(nameof(limits));
            }
            if (input.Length < LengthPrefixSize)
            {
                return;
            }

            uint length = BinaryPrimitives.ReadUInt32LittleEndian(input);
            if (length < 1)
            {
                throw new FlowCodecException(FlowCodecError.InvalidLength);
            }
            if ((ulong)length + LengthPrefixSize > (ulong)limits.MaxRecordBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }
            if (input.Length < HeaderSize)
            {
                return;
            }

            switch ((DirectDeliveryFrameKind)input[LengthPrefixSize])
            {
                case DirectDeliveryFrameKind.Data:
                    uint dataLength = length - sizeof(byte);
                    if (dataLength == 0)
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidLength);
                    }
                    if (dataLength > (uint)limits.MaxDataBytes)
                    {
                        throw new FlowCodecException(FlowCodecError.TooLarge);
                    }
                    break;
                case DirectDeliveryFrameKind.Barrier:
                    uint controlLength = length > sizeof(byte) + BarrierHeaderSize
                        ? length - sizeof(byte) - BarrierHeaderSize : 0;
                    if (controlLength is not (EndControlSize or CommitControlSize))
                    {
                        throw new FlowCodecException(FlowCodecError.InvalidLength);
                    }
                    if (controlLength > limits.MaxControlBytes)
                    {
                        throw new FlowCodecException(FlowCodecError.TooLarge);
                    }
                    break;
                default:
                    throw new FlowCodecException(FlowCodecError.UnknownKind);
            }
        }

        /// <summary>
        /// Encodes one UADP data frame while preserving its body bytes.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static ByteString EncodeData(ByteString body, FlowCodecLimits limits)
        {
            if (limits is null)
            {
                throw new ArgumentNullException(nameof(limits));
            }
            if (body.IsNull || body.Length == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidLength);
            }
            if (body.Length > limits.MaxDataBytes ||
                (long)body.Length + HeaderSize > limits.MaxRecordBytes)
            {
                throw new FlowCodecException(FlowCodecError.TooLarge);
            }

            int length = checked(HeaderSize + body.Length);
            byte[] result = new byte[length];
            BinaryPrimitives.WriteUInt32LittleEndian(result, checked((uint)(length - LengthPrefixSize)));
            result[LengthPrefixSize] = (byte)DirectDeliveryFrameKind.Data;
            body.Span.CopyTo(result.AsSpan(HeaderSize));
            return new ByteString(result);
        }

        /// <summary>
        /// Decodes a complete body after its frame header and bounds have been validated.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static DirectDeliveryFrame DecodeBody(
            DirectDeliveryFrameKind kind,
            ByteString body,
            FlowCodecLimits limits,
            DirectDeliveryFrameReservation? reservation = null)
        {
            if (body.IsNull)
            {
                throw new FlowCodecException(FlowCodecError.WrongType);
            }
            if (kind == DirectDeliveryFrameKind.Data)
            {
                if (body.IsEmpty)
                {
                    throw new FlowCodecException(FlowCodecError.InvalidLength);
                }
                return new DirectDeliveryFrame(kind, 0, 0, body, reservation);
            }
            if (kind != DirectDeliveryFrameKind.Barrier)
            {
                throw new FlowCodecException(FlowCodecError.UnknownKind);
            }
            if (body.Length < BarrierHeaderSize)
            {
                throw new FlowCodecException(FlowCodecError.InvalidLength);
            }
            uint revision = BinaryPrimitives.ReadUInt32LittleEndian(body.Span);
            uint operationId = BinaryPrimitives.ReadUInt32LittleEndian(body.Span[sizeof(uint)..]);
            ValidateBarrier(revision, operationId);
            ByteString controlBody = body[BarrierHeaderSize..];
            var record = new FlowRecord(FlowRecordKind.Control, revision, operationId, controlBody);
            FlowControl control = FlowControlCodec.Decode(in record, limits);
            ValidateBarrierControl(control);
            return new DirectDeliveryFrame(kind, revision, operationId, controlBody, reservation, control);
        }

        /// <summary>
        /// Encodes one ordered COMMIT or END barrier.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FlowCodecException"></exception>
        internal static ByteString EncodeBarrier(
            FlowControl control,
            uint revision,
            uint operationId,
            FlowCodecLimits limits)
        {
            if (limits is null)
            {
                throw new ArgumentNullException(nameof(limits));
            }
            if (control is null)
            {
                throw new ArgumentNullException(nameof(control));
            }
            ValidateBarrierControl(control);
            ValidateBarrier(revision, operationId);
            FlowRecord record = FlowControlCodec.Encode(control, revision, operationId, limits);
            return FlowRecordCodec.Encode(in record, limits);
        }

        /// <summary>
        /// Reads a nonempty 16-byte UA Binary association-token preface.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal static Guid ReadAssociationToken(ReadOnlySpan<byte> input)
        {
            if (input.Length < AssociationTokenSize)
            {
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            var reader = new FlowBinaryReader(input[..AssociationTokenSize]);
            Guid token = reader.Guid();
            if (token == Guid.Empty)
            {
                throw new FlowCodecException(FlowCodecError.InvalidFlow);
            }
            return token;
        }

        /// <summary>
        /// Writes one nonempty UA Binary association-token preface.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static int WriteAssociationToken(Guid token, Span<byte> destination)
        {
            if (token == Guid.Empty)
            {
                throw new ArgumentException("A nonempty association token is required.", nameof(token));
            }
            if (destination.Length < AssociationTokenSize)
            {
                throw new ArgumentException("The token destination is too short.", nameof(destination));
            }
            var writer = new FlowBinaryWriter(destination[..AssociationTokenSize], AssociationTokenSize);
            writer.Guid(token);
            return AssociationTokenSize;
        }

        /// <summary>
        /// Rejects barrier headers that cannot identify a live revision operation.
        /// </summary>
        private static void ValidateBarrier(uint revision, uint operationId)
        {
            if (revision == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidRevision);
            }
            if (operationId == 0)
            {
                throw new FlowCodecException(FlowCodecError.InvalidMessageId);
            }
        }

        /// <summary>
        /// Keeps all controls other than ordered COMMIT and END on the protected management path.
        /// </summary>
        private static void ValidateBarrierControl(FlowControl control)
        {
            if (control is not FlowCommit and not FlowSignal { Operation: FlowOperation.End })
            {
                throw new FlowCodecException(FlowCodecError.UnknownOperation);
            }
        }

        /// <summary>
        /// The length prefix and kind discriminator size.
        /// </summary>
        internal const int HeaderSize = sizeof(uint) + sizeof(byte);

        /// <summary>
        /// The length-prefix size.
        /// </summary>
        internal const int LengthPrefixSize = sizeof(uint);

        /// <summary>
        /// The COMMIT/END revision and operation identifier size.
        /// </summary>
        internal const int BarrierHeaderSize = 2 * sizeof(uint);

        /// <summary>
        /// The binary END opcode and flow identifier size, excluding the fixed control header.
        /// </summary>
        internal const int EndControlSize = sizeof(byte) + sizeof(uint);

        /// <summary>
        /// The binary COMMIT opcode, flow identifier and old-message cutoff size.
        /// </summary>
        internal const int CommitControlSize = EndControlSize + sizeof(uint);

        /// <summary>
        /// The association-token preface size.
        /// </summary>
        internal const int AssociationTokenSize = 16;
    }

    /// <summary>
    /// Holds a validated direct ordered-stream frame header.
    /// </summary>
    internal readonly struct DirectDeliveryFrameHeader
    {
        /// <summary>
        /// Creates a validated header.
        /// </summary>
        internal DirectDeliveryFrameHeader(DirectDeliveryFrameKind kind, int bodyLength)
        {
            Kind = kind;
            BodyLength = bodyLength;
        }

        /// <summary>
        /// Gets the direct-stream frame kind.
        /// </summary>
        internal DirectDeliveryFrameKind Kind { get; }

        /// <summary>
        /// Gets the body length after the kind byte.
        /// </summary>
        internal int BodyLength { get; }

        /// <summary>
        /// Gets the complete frame length including the length prefix and kind.
        /// </summary>
        internal int TotalLength => checked(DirectDeliveryFrameCodec.HeaderSize + BodyLength);
    }
}
