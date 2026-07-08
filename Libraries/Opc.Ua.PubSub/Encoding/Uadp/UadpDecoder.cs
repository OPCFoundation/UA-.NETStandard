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
 *
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Decoder for UADP NetworkMessages received over a transport.
    /// </summary>
    /// <remarks>
    /// Implements the inverse of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4">
    /// Part 14 §7.2.4 UADP Message Mapping</see>. Returns
    /// <c>null</c> for non-UADP frames, malformed inputs, version
    /// mismatches, unsupported PublisherId types, and inbound
    /// chunked NetworkMessages — the caller is expected to feed the
    /// chunk into the <see cref="UadpReassembler"/>. Discovery frames
    /// are routed to <see cref="UadpDiscoveryCoder"/>.
    /// </remarks>
    public sealed class UadpDecoder : INetworkMessageDecoder
    {
        /// <inheritdoc/>
        public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

        /// <inheritdoc/>
        public ValueTask<PubSubNetworkMessage?> TryDecodeAsync(
            ReadOnlyMemory<byte> frame,
            PubSubNetworkMessageContext context,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            cancellationToken.ThrowIfCancellationRequested();

            PubSubNetworkMessage? decoded = Decode(frame, context);
            return new ValueTask<PubSubNetworkMessage?>(decoded);
        }

        /// <summary>
        /// Synchronously decodes a UADP frame. Returns <c>null</c> on
        /// any soft-rejection condition.
        /// </summary>
        /// <param name="frame">Raw inbound bytes.</param>
        /// <param name="context">Network message context.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static PubSubNetworkMessage? Decode(
            ReadOnlyMemory<byte> frame,
            PubSubNetworkMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            PubSubNetworkMessage? result = DecodeInternal(frame, context);
            if (result is null)
            {
                if (!frame.IsEmpty)
                {
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                }
            }
            else
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);
                if (result.DataSetMessages.Count > 0)
                {
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedDataSetMessages,
                        result.DataSetMessages.Count);
                }
            }
            return result;
        }

        private static PubSubNetworkMessage? DecodeInternal(
            ReadOnlyMemory<byte> frame,
            PubSubNetworkMessageContext context)
        {
            if (frame.IsEmpty)
            {
                return null;
            }

            var reader = new UadpBinaryReader(frame.ToArray(), 0, frame.Length);
            if (!reader.TryReadByte(out byte rawFlags))
            {
                return null;
            }
            (byte version, UadpFlagsEncodingMask uadpFlags) =
                UadpFlagsEncodingMaskExtensions.Split(rawFlags);
            if (version != 1)
            {
                return null;
            }

            ExtendedFlags1EncodingMask ext1 = 0;
            ExtendedFlags2EncodingMask ext2 = 0;

            if ((uadpFlags & UadpFlagsEncodingMask.ExtendedFlags1Enabled) != 0)
            {
                if (!reader.TryReadByte(out byte ext1Byte))
                {
                    return null;
                }
                ext1 = (ExtendedFlags1EncodingMask)ext1Byte;
            }
            if ((ext1 & ExtendedFlags1EncodingMask.ExtendedFlags2Enabled) != 0)
            {
                if (!reader.TryReadByte(out byte ext2Byte))
                {
                    return null;
                }
                ext2 = (ExtendedFlags2EncodingMask)ext2Byte;
            }

            PublisherIdType publisherIdType = PublisherIdType.Byte;
            PublisherId publisherId = PublisherId.FromByte(0);
            if ((uadpFlags & UadpFlagsEncodingMask.PublisherIdEnabled) != 0)
            {
                if (!ExtendedFlags1EncodingMaskExtensions.TryGetPublisherIdType(
                    (byte)(ext1 & ExtendedFlags1EncodingMask.PublisherIdTypeMask),
                    out publisherIdType))
                {
                    return null;
                }
                if (!TryReadPublisherId(ref reader, publisherIdType,
                    out publisherId))
                {
                    return null;
                }
            }

            Uuid dataSetClassId = Uuid.Empty;
            if ((ext1 & ExtendedFlags1EncodingMask.DataSetClassIdEnabled) != 0)
            {
                if (!reader.TryReadGuid(out Guid g))
                {
                    return null;
                }
                dataSetClassId = (Uuid)g;
            }

            if ((ext2 & ExtendedFlags2EncodingMask
                .NetworkMessageWithDiscoveryRequest) != 0 ||
                (ext2 & ExtendedFlags2EncodingMask
                    .NetworkMessageWithDiscoveryResponse) != 0)
            {
                var header = new UadpDecodedHeader
                {
                    PublisherId = publisherId,
                    DataSetClassId = dataSetClassId
                };
                return UadpDiscoveryCoder.TryDecode(
                    ref reader, ext2, header, context);
            }

            if ((ext2 & ExtendedFlags2EncodingMask.ChunkMessage) != 0)
            {
                return null;
            }

            ushort? writerGroupId = null;
            uint groupVersion = 0;
            ushort networkMessageNumber = 0;
            ushort sequenceNumber = 0;
            UadpNetworkMessageContentMask contentMask = 0;

            if ((uadpFlags & UadpFlagsEncodingMask.PublisherIdEnabled) != 0)
            {
                contentMask |= UadpNetworkMessageContentMask.PublisherId;
            }
            if ((ext1 & ExtendedFlags1EncodingMask.DataSetClassIdEnabled) != 0)
            {
                contentMask |= UadpNetworkMessageContentMask.DataSetClassId;
            }

            GroupFlagsEncodingMask groupFlags = 0;
            if ((uadpFlags & UadpFlagsEncodingMask.GroupHeaderEnabled) != 0)
            {
                contentMask |= UadpNetworkMessageContentMask.GroupHeader;
                if (!reader.TryReadByte(out byte gfByte))
                {
                    return null;
                }
                groupFlags = (GroupFlagsEncodingMask)gfByte;

                if ((groupFlags & GroupFlagsEncodingMask.WriterGroupIdEnabled) != 0)
                {
                    if (!reader.TryReadUInt16Le(out ushort wgid))
                    {
                        return null;
                    }
                    writerGroupId = wgid;
                    contentMask |= UadpNetworkMessageContentMask.WriterGroupId;
                }
                if ((groupFlags & GroupFlagsEncodingMask.GroupVersionEnabled) != 0)
                {
                    if (!reader.TryReadUInt32Le(out uint gv))
                    {
                        return null;
                    }
                    groupVersion = gv;
                    contentMask |= UadpNetworkMessageContentMask.GroupVersion;
                }
                if ((groupFlags & GroupFlagsEncodingMask
                    .NetworkMessageNumberEnabled) != 0)
                {
                    if (!reader.TryReadUInt16Le(out ushort nmn))
                    {
                        return null;
                    }
                    networkMessageNumber = nmn;
                    contentMask |= UadpNetworkMessageContentMask.NetworkMessageNumber;
                }
                if ((groupFlags & GroupFlagsEncodingMask.SequenceNumberEnabled) != 0)
                {
                    if (!reader.TryReadUInt16Le(out ushort sn))
                    {
                        return null;
                    }
                    sequenceNumber = sn;
                    contentMask |= UadpNetworkMessageContentMask.SequenceNumber;
                }
            }

            ushort[]? payloadWriterIds = null;
            if ((uadpFlags & UadpFlagsEncodingMask.PayloadHeaderEnabled) != 0)
            {
                contentMask |= UadpNetworkMessageContentMask.PayloadHeader;
                if (!reader.TryReadByte(out byte count))
                {
                    return null;
                }
                payloadWriterIds = new ushort[count];
                for (int i = 0; i < count; i++)
                {
                    if (!reader.TryReadUInt16Le(out ushort wid))
                    {
                        return null;
                    }
                    payloadWriterIds[i] = wid;
                }
            }

            DateTimeUtc? timestamp = null;
            if ((ext1 & ExtendedFlags1EncodingMask.TimestampEnabled) != 0)
            {
                if (!reader.TryReadInt64Le(out long ts))
                {
                    return null;
                }
                timestamp = (DateTimeUtc)ts;
                contentMask |= UadpNetworkMessageContentMask.Timestamp;
            }

            ushort picoSeconds = 0;
            if ((ext1 & ExtendedFlags1EncodingMask.PicoSecondsEnabled) != 0)
            {
                if (!reader.TryReadUInt16Le(out ushort ps))
                {
                    return null;
                }
                picoSeconds = ps;
                contentMask |= UadpNetworkMessageContentMask.PicoSeconds;
            }

            ArrayOf<DataSetField>? promotedFields = null;
            if ((ext2 & ExtendedFlags2EncodingMask.PromotedFields) != 0)
            {
                promotedFields = ReadPromotedFields(ref reader, context);
                if (promotedFields is null)
                {
                    return null;
                }
                contentMask |= UadpNetworkMessageContentMask.PromotedFields;
            }

            int payloadCount = payloadWriterIds?.Length ?? 1;
            if ((ext2 & ExtendedFlags2EncodingMask.ActionHeaderEnabled) != 0)
            {
                if (payloadCount != 1)
                {
                    return null;
                }
                var header = new UadpDecodedHeader
                {
                    PublisherId = publisherId,
                    WriterGroupId = writerGroupId,
                    DataSetClassId = dataSetClassId
                };
                return UadpActionCoder.TryDecode(
                    ref reader, header, payloadWriterIds?[0] ?? 0, context);
            }

            ushort[]? payloadSizes = null;
            if (payloadWriterIds is not null && payloadWriterIds.Length > 1)
            {
                payloadSizes = new ushort[payloadCount];
                for (int i = 0; i < payloadCount; i++)
                {
                    if (!reader.TryReadUInt16Le(out ushort sz))
                    {
                        return null;
                    }
                    payloadSizes[i] = sz;
                }
            }

            var dataSetMessages = new List<PubSubDataSetMessage>(payloadCount);
            for (int i = 0; i < payloadCount; i++)
            {
                ushort writerId = payloadWriterIds?[i] ?? 0;
                int expected = payloadSizes?[i] ?? 0;
                int before = reader.Position;

                UadpDataSetMessage? dsm = DecodeDataSetMessage(
                    ref reader, writerId, publisherId,
                    writerGroupId ?? 0, dataSetClassId, context);
                if (dsm is null)
                {
                    return null;
                }
                dataSetMessages.Add(dsm);

                if (expected > 0)
                {
                    int actual = reader.Position - before;
                    if (actual < expected)
                    {
                        reader.Advance(expected - actual);
                    }
                }
            }

            return new UadpNetworkMessage
            {
                UadpVersion = version,
                ContentMask = contentMask,
                PublisherId = publisherId,
                WriterGroupId = writerGroupId,
                GroupVersion = groupVersion,
                NetworkMessageNumber = networkMessageNumber,
                SequenceNumber = sequenceNumber,
                DataSetClassId = dataSetClassId,
                Timestamp = timestamp.GetValueOrDefault(),
                PicoSeconds = picoSeconds,
                PromotedFields = promotedFields ?? [],
                DataSetMessages = dataSetMessages,
                MessageType = UadpNetworkMessageType.DataSetMessage
            };
        }

        /// <summary>
        /// Parses just the UADP NetworkMessage prefix (Common Header,
        /// optional Extended Flags, optional PublisherId, optional
        /// DataSetClassId, optional GroupHeader, optional PayloadHeader
        /// writer-ids, optional Timestamp / PicoSeconds / PromotedFields,
        /// and optional PayloadHeader sizes) and reports the offset at
        /// which the SecurityHeader / DataSetMessages region begins.
        /// </summary>
        /// <remarks>
        /// Used by <c>PubSubConnection</c> to split an inbound frame
        /// when <c>ExtendedFlags1.SecurityEnabled</c> is set so the
        /// security wrapper can verify, decrypt and rebuild a cleartext
        /// frame that the full decoder can then process.
        /// </remarks>
        /// <param name="frame">Inbound frame bytes.</param>
        /// <param name="prefixLength">
        /// On success, the byte length of the prefix region.
        /// </param>
        /// <param name="securityEnabled">
        /// On success, <see langword="true"/> when
        /// <c>ExtendedFlags1.SecurityEnabled</c> is set.
        /// </param>
        /// <param name="publisherId">
        /// On success, the parsed publisher identity (used by the
        /// reassembler routing key on chunked frames).
        /// </param>
        /// <param name="writerGroupId">
        /// On success, the WriterGroupId carried in the optional
        /// GroupHeader (0 when absent).
        /// </param>
        /// <returns><see langword="true"/> when parsing succeeded.</returns>
        public static bool TryReadOuterPrefix(
            ReadOnlyMemory<byte> frame,
            out int prefixLength,
            out bool securityEnabled,
            out PublisherId publisherId,
            out ushort writerGroupId)
        {
            return TryReadOuterPrefix(
                frame,
                out prefixLength,
                out securityEnabled,
                out _,
                out publisherId,
                out writerGroupId);
        }

        /// <summary>
        /// Variant of <see cref="TryReadOuterPrefix(ReadOnlyMemory{byte},
        /// out int, out bool, out PublisherId, out ushort)"/> that also
        /// reports whether the frame carries the
        /// <c>ExtendedFlags2.ChunkMessage</c> bit. Used by the connection
        /// to route inbound chunked NetworkMessages through the
        /// reassembler.
        /// </summary>
        public static bool TryReadOuterPrefix(
            ReadOnlyMemory<byte> frame,
            out int prefixLength,
            out bool securityEnabled,
            out bool chunkMessage,
            out PublisherId publisherId,
            out ushort writerGroupId)
        {
            prefixLength = 0;
            securityEnabled = false;
            chunkMessage = false;
            publisherId = PublisherId.Null;
            writerGroupId = 0;

            if (frame.IsEmpty)
            {
                return false;
            }
            var reader = new UadpBinaryReader(frame.ToArray(), 0, frame.Length);
            if (!reader.TryReadByte(out byte rawFlags))
            {
                return false;
            }
            (byte version, UadpFlagsEncodingMask uadpFlags) =
                UadpFlagsEncodingMaskExtensions.Split(rawFlags);
            if (version != 1)
            {
                return false;
            }

            ExtendedFlags1EncodingMask ext1 = 0;
            ExtendedFlags2EncodingMask ext2 = 0;
            if ((uadpFlags & UadpFlagsEncodingMask.ExtendedFlags1Enabled) != 0)
            {
                if (!reader.TryReadByte(out byte ext1Byte))
                {
                    return false;
                }
                ext1 = (ExtendedFlags1EncodingMask)ext1Byte;
            }
            if ((ext1 & ExtendedFlags1EncodingMask.ExtendedFlags2Enabled) != 0)
            {
                if (!reader.TryReadByte(out byte ext2Byte))
                {
                    return false;
                }
                ext2 = (ExtendedFlags2EncodingMask)ext2Byte;
            }

            securityEnabled = (ext1 & ExtendedFlags1EncodingMask.SecurityEnabled) != 0;
            chunkMessage = (ext2 & ExtendedFlags2EncodingMask.ChunkMessage) != 0;

            if ((uadpFlags & UadpFlagsEncodingMask.PublisherIdEnabled) != 0)
            {
                if (!ExtendedFlags1EncodingMaskExtensions.TryGetPublisherIdType(
                    (byte)(ext1 & ExtendedFlags1EncodingMask.PublisherIdTypeMask),
                    out PublisherIdType pidType))
                {
                    return false;
                }
                if (!TryReadPublisherId(ref reader, pidType, out publisherId))
                {
                    return false;
                }
            }

            if ((ext1 & ExtendedFlags1EncodingMask.DataSetClassIdEnabled) != 0 && !reader.TryReadGuid(out _))
            {
                return false;
            }

            // Discovery frames are not in scope for security wrapping
            // — keep them detectable so the caller can route them elsewhere.
            if ((ext2 & ExtendedFlags2EncodingMask
                .NetworkMessageWithDiscoveryRequest) != 0
                || (ext2 & ExtendedFlags2EncodingMask
                    .NetworkMessageWithDiscoveryResponse) != 0)
            {
                prefixLength = reader.Position;
                return true;
            }
            int payloadCount = 0;
            if ((uadpFlags & UadpFlagsEncodingMask.GroupHeaderEnabled) != 0)
            {
                if (!reader.TryReadByte(out byte gfByte))
                {
                    return false;
                }
                var groupFlags = (GroupFlagsEncodingMask)gfByte;
                if ((groupFlags & GroupFlagsEncodingMask.WriterGroupIdEnabled) != 0)
                {
                    if (!reader.TryReadUInt16Le(out ushort wgid))
                    {
                        return false;
                    }
                    writerGroupId = wgid;
                }
                if ((groupFlags & GroupFlagsEncodingMask.GroupVersionEnabled) != 0
                    && !reader.TryReadUInt32Le(out _))
                {
                    return false;
                }
                if ((groupFlags & GroupFlagsEncodingMask.NetworkMessageNumberEnabled) != 0
                    && !reader.TryReadUInt16Le(out _))
                {
                    return false;
                }
                if ((groupFlags & GroupFlagsEncodingMask.SequenceNumberEnabled) != 0
                    && !reader.TryReadUInt16Le(out _))
                {
                    return false;
                }
            }

            if (chunkMessage)
            {
                // Chunked envelopes carry only the optional GroupHeader
                // before the inner chunk payload. Stop the prefix here.
                prefixLength = reader.Position;
                return true;
            }

            ushort[]? payloadWriterIds = null;
            if ((uadpFlags & UadpFlagsEncodingMask.PayloadHeaderEnabled) != 0)
            {
                if (!reader.TryReadByte(out byte count))
                {
                    return false;
                }
                payloadCount = count;
                payloadWriterIds = new ushort[count];
                for (int i = 0; i < count; i++)
                {
                    if (!reader.TryReadUInt16Le(out ushort wid))
                    {
                        return false;
                    }
                    payloadWriterIds[i] = wid;
                }
            }

            if ((ext1 & ExtendedFlags1EncodingMask.TimestampEnabled) != 0
                && !reader.TryReadInt64Le(out _))
            {
                return false;
            }
            if ((ext1 & ExtendedFlags1EncodingMask.PicoSecondsEnabled) != 0
                && !reader.TryReadUInt16Le(out _))
            {
                return false;
            }

            if ((ext2 & ExtendedFlags2EncodingMask.PromotedFields) != 0)
            {
                // PromotedFields is prefixed by a UInt16 byte-size we
                // can skip over without decoding individual Variants.
                if (!reader.TryReadUInt16Le(out ushort promotedSize)
                    || promotedSize > reader.Remaining)
                {
                    return false;
                }
                reader.Advance(promotedSize);
            }

            if ((ext2 & ExtendedFlags2EncodingMask.ActionHeaderEnabled) != 0)
            {
                prefixLength = reader.Position;
                return true;
            }

            if (payloadWriterIds is not null && payloadWriterIds.Length > 1)
            {
                for (int i = 0; i < payloadCount; i++)
                {
                    if (!reader.TryReadUInt16Le(out _))
                    {
                        return false;
                    }
                }
            }

            prefixLength = reader.Position;
            return true;
        }


        private static bool TryReadPublisherId(
            ref UadpBinaryReader reader,
            PublisherIdType type,
            out PublisherId publisherId)
        {
            publisherId = PublisherId.FromByte(0);
            switch (type)
            {
                case PublisherIdType.Byte:
                    if (!reader.TryReadByte(out byte b))
                    {
                        return false;
                    }
                    publisherId = PublisherId.FromByte(b);
                    return true;
                case PublisherIdType.UInt16:
                    if (!reader.TryReadUInt16Le(out ushort u16))
                    {
                        return false;
                    }
                    publisherId = PublisherId.FromUInt16(u16);
                    return true;
                case PublisherIdType.UInt32:
                    if (!reader.TryReadUInt32Le(out uint u32))
                    {
                        return false;
                    }
                    publisherId = PublisherId.FromUInt32(u32);
                    return true;
                case PublisherIdType.UInt64:
                    if (!reader.TryReadUInt64Le(out ulong u64))
                    {
                        return false;
                    }
                    publisherId = PublisherId.FromUInt64(u64);
                    return true;
                case PublisherIdType.String:
                    if (!reader.TryReadString(out string? s))
                    {
                        return false;
                    }
                    publisherId = PublisherId.FromString(s ?? string.Empty);
                    return true;
                default:
                    return false;
            }
        }

        private static ArrayOf<DataSetField>? ReadPromotedFields(
            ref UadpBinaryReader reader,
            PubSubNetworkMessageContext context)
        {
            if (!reader.TryReadUInt16Le(out ushort size))
            {
                return null;
            }
            int start = reader.Position;
            int end = start + size;
            if (size > reader.Remaining)
            {
                return null;
            }
            var fields = new List<DataSetField>();
            while (reader.Position < end)
            {
                Variant value;
                try
                {
                    value = reader.ReadVariant(context.MessageContext);
                }
                catch (ServiceResultException)
                {
                    return null;
                }
                fields.Add(new DataSetField { Value = value });
            }
            return fields;
        }

        private static UadpDataSetMessage? DecodeDataSetMessage(
            ref UadpBinaryReader reader,
            ushort writerId,
            PublisherId publisherId,
            ushort writerGroupId,
            Uuid dataSetClassId,
            PubSubNetworkMessageContext context)
        {
            if (!reader.TryReadByte(out byte flags1Byte))
            {
                return null;
            }
            var flags1 = (DataSetFlags1EncodingMask)flags1Byte;

            DataSetFlags2EncodingMask flags2 = 0;
            if ((flags1 & DataSetFlags1EncodingMask.DataSetFlags2Enabled) != 0)
            {
                if (!reader.TryReadByte(out byte flags2Byte))
                {
                    return null;
                }
                flags2 = (DataSetFlags2EncodingMask)flags2Byte;
            }

            UadpDataSetMessageContentMask contentMask = 0;
            uint sequenceNumber = 0;
            if ((flags1 & DataSetFlags1EncodingMask.SequenceNumberEnabled) != 0)
            {
                if (!reader.TryReadUInt16Le(out ushort sn))
                {
                    return null;
                }
                sequenceNumber = sn;
                contentMask |= UadpDataSetMessageContentMask.SequenceNumber;
            }
            DateTimeUtc timestamp = default;
            if ((flags2 & DataSetFlags2EncodingMask.TimestampEnabled) != 0)
            {
                if (!reader.TryReadInt64Le(out long ts))
                {
                    return null;
                }
                timestamp = (DateTimeUtc)ts;
                contentMask |= UadpDataSetMessageContentMask.Timestamp;
            }
            ushort picoSeconds = 0;
            if ((flags2 & DataSetFlags2EncodingMask.PicoSecondsEnabled) != 0)
            {
                if (!reader.TryReadUInt16Le(out ushort ps))
                {
                    return null;
                }
                picoSeconds = ps;
                contentMask |= UadpDataSetMessageContentMask.PicoSeconds;
            }
            StatusCode status = StatusCodes.Good;
            if ((flags1 & DataSetFlags1EncodingMask.StatusEnabled) != 0)
            {
                if (!reader.TryReadUInt16Le(out ushort statusBits))
                {
                    return null;
                }
                status = new StatusCode((uint)statusBits << 16);
                contentMask |= UadpDataSetMessageContentMask.Status;
            }
            uint majorVersion = 0;
            uint minorVersion = 0;
            if ((flags1 & DataSetFlags1EncodingMask.MajorVersionEnabled) != 0)
            {
                if (!reader.TryReadUInt32Le(out uint mv))
                {
                    return null;
                }
                majorVersion = mv;
                contentMask |= UadpDataSetMessageContentMask.MajorVersion;
            }
            if ((flags1 & DataSetFlags1EncodingMask.MinorVersionEnabled) != 0)
            {
                if (!reader.TryReadUInt32Le(out uint mv))
                {
                    return null;
                }
                minorVersion = mv;
                contentMask |= UadpDataSetMessageContentMask.MinorVersion;
            }

            if (!DataSetFlags1EncodingMaskExtensions.TryGetFieldEncoding(
                flags1Byte, out PubSubFieldEncoding encoding))
            {
                return null;
            }
            if (!DataSetFlags2EncodingMaskExtensions.TryGetMessageType(
                (byte)flags2, out PubSubDataSetMessageType messageType))
            {
                return null;
            }

            DataSetMetaDataType? metaData = ResolveMetaData(
                publisherId, writerGroupId, writerId, dataSetClassId,
                majorVersion, context);

            ArrayOf<DataSetField>? fields = UadpFieldDecoder.DecodeFields(
                ref reader, encoding, messageType, metaData, context.MessageContext);
            if (fields is null)
            {
                return null;
            }

            return new UadpDataSetMessage
            {
                DataSetWriterId = writerId,
                SequenceNumber = sequenceNumber,
                Timestamp = timestamp,
                PicoSeconds = picoSeconds,
                Status = status,
                MessageType = messageType,
                MetaDataVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = majorVersion,
                    MinorVersion = minorVersion
                },
                Fields = fields.Value,
                ContentMask = contentMask,
                FieldEncoding = encoding,
                ConfiguredSize = 0
            };
        }

        private static DataSetMetaDataType? ResolveMetaData(
            PublisherId publisherId,
            ushort writerGroupId,
            ushort writerId,
            Uuid dataSetClassId,
            uint majorVersion,
            PubSubNetworkMessageContext context)
        {
            var key = new DataSetMetaDataKey(
                publisherId, writerGroupId, writerId,
                dataSetClassId, majorVersion);
            MetaDataMatchResult result = context.MetaDataRegistry.TryGet(
                key, out DataSetMetaDataType? metaData);
            if (result == MetaDataMatchResult.MajorVersionMismatch)
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ResolverErrors);
                return null;
            }
            if (result == MetaDataMatchResult.NotFound)
            {
                return null;
            }
            return metaData;
        }
    }
}
