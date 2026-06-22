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
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Diagnostics;

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Serialises a <see cref="UadpNetworkMessage"/> to a UADP wire frame.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4">
    /// Part 14 §7.2.4 UADP Message Mapping</see>. Security wrapping is
    /// out-of-scope for this Phase-2 encoder; chunking is delegated to
    /// the UADP chunker. Discovery NetworkMessages are routed to
    /// <see cref="UadpDiscoveryCoder"/>.
    /// </remarks>
    public sealed class UadpEncoder : INetworkMessageEncoder
    {
        private const int kInitialBufferSize = 4096;
        private const int kMaxBufferSize = 1 << 20;

        /// <inheritdoc/>
        public string TransportProfileUri => Profiles.PubSubUdpUadpTransport;

        /// <inheritdoc/>
        public int EstimatedHeaderOverhead => 64;

        /// <inheritdoc/>
        public ValueTask<ReadOnlyMemory<byte>> EncodeAsync(
            PubSubNetworkMessage networkMessage,
            PubSubNetworkMessageContext context,
            CancellationToken cancellationToken = default)
        {
            if (networkMessage is null)
            {
                throw new ArgumentNullException(nameof(networkMessage));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            cancellationToken.ThrowIfCancellationRequested();

            if (networkMessage is UadpDiscoveryRequestMessage
                or UadpDiscoveryResponseMessage)
            {
                ReadOnlyMemory<byte> discovery =
                    UadpDiscoveryCoder.Encode(networkMessage, context);
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.SentNetworkMessages);
                return new ValueTask<ReadOnlyMemory<byte>>(discovery);
            }

            if (networkMessage is UadpActionRequestMessage
                or UadpActionResponseMessage)
            {
                ReadOnlyMemory<byte> action =
                    UadpActionCoder.Encode(networkMessage, context);
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.SentNetworkMessages);
                return new ValueTask<ReadOnlyMemory<byte>>(action);
            }

            if (networkMessage is not UadpNetworkMessage uadp)
            {
                throw new ArgumentException(
                    "UadpEncoder only accepts UadpNetworkMessage, discovery, or action instances.",
                    nameof(networkMessage));
            }

            ReadOnlyMemory<byte> encoded = EncodeData(uadp, context);
            context.Diagnostics.Increment(
                PubSubDiagnosticsCounterKind.SentNetworkMessages);
            if (uadp.DataSetMessages.Count > 0)
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.SentDataSetMessages,
                    uadp.DataSetMessages.Count);
            }
            return new ValueTask<ReadOnlyMemory<byte>>(encoded);
        }

        /// <summary>
        /// Encodes a <see cref="UadpNetworkMessage"/> with the
        /// <c>ExtendedFlags1.SecurityEnabled</c> bit set in the header
        /// and reports the byte offset at which the DataSetMessages
        /// portion (the region that must be encrypted by
        /// <see cref="Security.UadpSecurityWrapper"/>) begins. Callers
        /// split the returned buffer at <paramref name="payloadOffset"/>
        /// and hand the two slices to the wrapper.
        /// </summary>
        /// <param name="networkMessage">UADP message to encode.</param>
        /// <param name="context">Network message context.</param>
        /// <param name="payloadOffset">Boundary between outer prefix and inner payload.</param>
        /// <returns>The complete encoded buffer.</returns>
        public static ReadOnlyMemory<byte> EncodeWithSecurityBoundary(
            UadpNetworkMessage networkMessage,
            PubSubNetworkMessageContext context,
            out int payloadOffset)
        {
            if (networkMessage is null)
            {
                throw new ArgumentNullException(nameof(networkMessage));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            UadpNetworkMessage withFlag = networkMessage.SecurityEnabled
                ? networkMessage
                : networkMessage with { SecurityEnabled = true };
            return EncodeData(withFlag, context, out payloadOffset);
        }

        /// <summary>
        /// Encodes a UADP data or action NetworkMessage with the
        /// <c>ExtendedFlags1.SecurityEnabled</c> bit set and reports the
        /// byte offset at which the security wrapper must insert the
        /// SecurityHeader.
        /// </summary>
        /// <param name="networkMessage">UADP data or action message.</param>
        /// <param name="context">Network message context.</param>
        /// <param name="payloadOffset">Boundary between outer prefix and inner payload.</param>
        public static ReadOnlyMemory<byte> EncodeWithSecurityBoundary(
            PubSubNetworkMessage networkMessage,
            PubSubNetworkMessageContext context,
            out int payloadOffset)
        {
            if (networkMessage is UadpNetworkMessage uadp)
            {
                return EncodeWithSecurityBoundary(uadp, context, out payloadOffset);
            }
            if (networkMessage is UadpActionRequestMessage
                or UadpActionResponseMessage)
            {
                return UadpActionCoder.Encode(
                    networkMessage, context, securityEnabled: true, out payloadOffset);
            }
            throw new ArgumentException(
                "Security wrapping is supported for UADP data and action messages.",
                nameof(networkMessage));
        }

        /// <summary>
        /// Encodes a data NetworkMessage (non-discovery) and returns the
        /// resulting bytes copied to a heap-allocated array. Internal
        /// callers (e.g. the chunker) reuse this entry point.
        /// </summary>
        /// <param name="message">Source UADP message.</param>
        /// <param name="context">Network message context.</param>
        internal static byte[] EncodeData(
            UadpNetworkMessage message,
            PubSubNetworkMessageContext context)
        {
            return EncodeData(message, context, out _);
        }

        /// <summary>
        /// Encodes a data NetworkMessage and additionally reports the
        /// byte offset at which the DataSetMessages payload begins.
        /// Callers wiring a <see cref="Security.UadpSecurityWrapper"/>
        /// split the returned buffer at this boundary so the wrapper
        /// can insert the SecurityHeader and encrypt the payload.
        /// </summary>
        /// <param name="message">Source UADP message.</param>
        /// <param name="context">Network message context.</param>
        /// <param name="payloadOffset">
        /// Offset within the returned buffer where the DataSetMessages
        /// portion starts (i.e. immediately after the PayloadHeader
        /// sizes reservation).
        /// </param>
        internal static byte[] EncodeData(
            UadpNetworkMessage message,
            PubSubNetworkMessageContext context,
            out int payloadOffset)
        {
            if (message.UadpVersion != 1)
            {
                throw new InvalidOperationException(
                    $"Only UADP version 1 is supported; got {message.UadpVersion}.");
            }

            byte[] rented = ArrayPool<byte>.Shared.Rent(kInitialBufferSize);
            try
            {
                int written = 0;
                int localOffset = 0;
                while (true)
                {
                    try
                    {
                        written = EncodeIntoBuffer(message, context, rented, out localOffset);
                        break;
                    }
                    catch (ArgumentException)
                    {
                        if (rented.Length >= kMaxBufferSize)
                        {
                            throw new InvalidOperationException(
                                "UADP NetworkMessage exceeds maximum buffer size.");
                        }
                        ArrayPool<byte>.Shared.Return(rented);
                        rented = ArrayPool<byte>.Shared.Rent(rented.Length * 2);
                    }
                }
                var result = new byte[written];
                Buffer.BlockCopy(rented, 0, result, 0, written);
                payloadOffset = localOffset;
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private static int EncodeIntoBuffer(
            UadpNetworkMessage message,
            PubSubNetworkMessageContext context,
            byte[] buffer)
        {
            return EncodeIntoBuffer(message, context, buffer, out _);
        }

        private static int EncodeIntoBuffer(
            UadpNetworkMessage message,
            PubSubNetworkMessageContext context,
            byte[] buffer,
            out int payloadOffset)
        {
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);

            (UadpFlagsEncodingMask uadpFlags,
                ExtendedFlags1EncodingMask ext1,
                ExtendedFlags2EncodingMask ext2,
                GroupFlagsEncodingMask groupFlags,
                PublisherIdType publisherIdType)
                = DeriveFlags(message);

            WriteHeader(ref writer, message, uadpFlags, ext1, ext2, publisherIdType);
            WriteGroupHeader(ref writer, message, uadpFlags, groupFlags);

            int payloadHeaderSizesPos = -1;
            int payloadCount = message.DataSetMessages.Count;
            bool hasPayloadHeader =
                (message.ContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0;

            if (hasPayloadHeader)
            {
                writer.WriteByte((byte)payloadCount);
                for (int i = 0; i < payloadCount; i++)
                {
                    writer.WriteUInt16Le(message.DataSetMessages[i].DataSetWriterId);
                }
            }

            WriteExtendedHeader(ref writer, message, ext1, context);

            if (hasPayloadHeader && payloadCount > 1)
            {
                payloadHeaderSizesPos = writer.Reserve(2 * payloadCount);
            }

            payloadOffset = writer.Position;

            var sizes = new ushort[payloadCount];
            for (int i = 0; i < payloadCount; i++)
            {
                int beforeMessage = writer.Position;
                if (message.DataSetMessages[i] is not UadpDataSetMessage uadpMsg)
                {
                    throw new InvalidOperationException(
                        "DataSetMessage at index " + i.ToString(
                            System.Globalization.CultureInfo.InvariantCulture) +
                        " is not a UadpDataSetMessage.");
                }
                WriteDataSetMessage(ref writer, uadpMsg, message, context);
                int afterMessage = writer.Position;
                sizes[i] = checked((ushort)(afterMessage - beforeMessage));
            }

            if (payloadHeaderSizesPos >= 0)
            {
                for (int i = 0; i < payloadCount; i++)
                {
                    writer.PatchUInt16Le(payloadHeaderSizesPos + (2 * i), sizes[i]);
                }
            }

            return writer.Position;
        }

        private static (
            UadpFlagsEncodingMask uadpFlags,
            ExtendedFlags1EncodingMask ext1,
            ExtendedFlags2EncodingMask ext2,
            GroupFlagsEncodingMask groupFlags,
            PublisherIdType publisherIdType) DeriveFlags(
            UadpNetworkMessage message)
        {
            UadpFlagsEncodingMask uadpFlags = 0;
            ExtendedFlags1EncodingMask ext1 = 0;
            ExtendedFlags2EncodingMask ext2 = 0;
            GroupFlagsEncodingMask groupFlags = 0;
            PublisherIdType publisherIdType = message.PublisherId.Type;

            if ((message.ContentMask & UadpNetworkMessageContentMask.PublisherId) != 0
                && !message.PublisherId.IsNull)
            {
                uadpFlags |= UadpFlagsEncodingMask.PublisherIdEnabled;
                if (publisherIdType != PublisherIdType.Byte)
                {
                    ext1 |= (ExtendedFlags1EncodingMask)
                        ExtendedFlags1EncodingMaskExtensions.EncodePublisherIdType(publisherIdType);
                }
            }

            if ((message.ContentMask & UadpNetworkMessageContentMask.DataSetClassId) != 0)
            {
                ext1 |= ExtendedFlags1EncodingMask.DataSetClassIdEnabled;
            }

            if ((message.ContentMask & UadpNetworkMessageContentMask.Timestamp) != 0)
            {
                ext1 |= ExtendedFlags1EncodingMask.TimestampEnabled;
            }
            if ((message.ContentMask & UadpNetworkMessageContentMask.PicoSeconds) != 0)
            {
                ext1 |= ExtendedFlags1EncodingMask.PicoSecondsEnabled;
            }

            if ((message.ContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            {
                uadpFlags |= UadpFlagsEncodingMask.PayloadHeaderEnabled;
            }

            const UadpNetworkMessageContentMask groupBits =
                UadpNetworkMessageContentMask.GroupHeader |
                UadpNetworkMessageContentMask.WriterGroupId |
                UadpNetworkMessageContentMask.GroupVersion |
                UadpNetworkMessageContentMask.NetworkMessageNumber |
                UadpNetworkMessageContentMask.SequenceNumber;
            if ((message.ContentMask & groupBits) != 0)
            {
                uadpFlags |= UadpFlagsEncodingMask.GroupHeaderEnabled;
            }

            if ((message.ContentMask & UadpNetworkMessageContentMask.WriterGroupId) != 0)
            {
                groupFlags |= GroupFlagsEncodingMask.WriterGroupIdEnabled;
            }
            if ((message.ContentMask & UadpNetworkMessageContentMask.GroupVersion) != 0)
            {
                groupFlags |= GroupFlagsEncodingMask.GroupVersionEnabled;
            }
            if ((message.ContentMask & UadpNetworkMessageContentMask.NetworkMessageNumber) != 0)
            {
                groupFlags |= GroupFlagsEncodingMask.NetworkMessageNumberEnabled;
            }
            if ((message.ContentMask & UadpNetworkMessageContentMask.SequenceNumber) != 0)
            {
                groupFlags |= GroupFlagsEncodingMask.SequenceNumberEnabled;
            }

            if ((message.ContentMask & UadpNetworkMessageContentMask.PromotedFields) != 0 ||
                message.PromotedFields.Count > 0)
            {
                ext2 |= ExtendedFlags2EncodingMask.PromotedFields;
            }

            if (message.SecurityEnabled)
            {
                ext1 |= ExtendedFlags1EncodingMask.SecurityEnabled;
            }

            if (ext1 != 0 || ext2 != 0)
            {
                uadpFlags |= UadpFlagsEncodingMask.ExtendedFlags1Enabled;
            }
            if (ext2 != 0)
            {
                ext1 |= ExtendedFlags1EncodingMask.ExtendedFlags2Enabled;
            }

            return (uadpFlags, ext1, ext2, groupFlags, publisherIdType);
        }

        private static void WriteHeader(
            ref UadpBinaryWriter writer,
            UadpNetworkMessage message,
            UadpFlagsEncodingMask uadpFlags,
            ExtendedFlags1EncodingMask ext1,
            ExtendedFlags2EncodingMask ext2,
            PublisherIdType publisherIdType)
        {
            writer.WriteByte(UadpFlagsEncodingMaskExtensions.Combine(
                message.UadpVersion, uadpFlags));

            if ((uadpFlags & UadpFlagsEncodingMask.ExtendedFlags1Enabled) != 0)
            {
                writer.WriteByte((byte)ext1);
            }
            if ((ext1 & ExtendedFlags1EncodingMask.ExtendedFlags2Enabled) != 0)
            {
                writer.WriteByte((byte)ext2);
            }

            if ((uadpFlags & UadpFlagsEncodingMask.PublisherIdEnabled) != 0)
            {
                WritePublisherId(ref writer, message.PublisherId, publisherIdType);
            }

            if ((ext1 & ExtendedFlags1EncodingMask.DataSetClassIdEnabled) != 0)
            {
                writer.WriteGuid((Guid)message.DataSetClassId);
            }
        }

        private static void WritePublisherId(
            ref UadpBinaryWriter writer,
            PublisherId publisherId,
            PublisherIdType type)
        {
            switch (type)
            {
                case PublisherIdType.Byte:
                    if (publisherId.TryGetByte(out byte b))
                    {
                        writer.WriteByte(b);
                    }
                    else
                    {
                        writer.WriteByte(0);
                    }
                    break;
                case PublisherIdType.UInt16:
                    if (publisherId.TryGetUInt16(out ushort u16))
                    {
                        writer.WriteUInt16Le(u16);
                    }
                    else
                    {
                        writer.WriteUInt16Le(0);
                    }
                    break;
                case PublisherIdType.UInt32:
                    if (publisherId.TryGetUInt32(out uint u32))
                    {
                        writer.WriteUInt32Le(u32);
                    }
                    else
                    {
                        writer.WriteUInt32Le(0);
                    }
                    break;
                case PublisherIdType.UInt64:
                    if (publisherId.TryGetUInt64(out ulong u64))
                    {
                        writer.WriteUInt64Le(u64);
                    }
                    else
                    {
                        writer.WriteUInt64Le(0);
                    }
                    break;
                case PublisherIdType.String:
                    publisherId.TryGetString(out string? s);
                    writer.WriteString(s);
                    break;
                case PublisherIdType.Guid:
                    if (publisherId.TryGetGuid(out Guid g))
                    {
                        writer.WriteGuid(g);
                    }
                    else
                    {
                        writer.WriteGuid(Guid.Empty);
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported PublisherIdType {type}.");
            }
        }

        private static void WriteGroupHeader(
            ref UadpBinaryWriter writer,
            UadpNetworkMessage message,
            UadpFlagsEncodingMask uadpFlags,
            GroupFlagsEncodingMask groupFlags)
        {
            if ((uadpFlags & UadpFlagsEncodingMask.GroupHeaderEnabled) == 0)
            {
                return;
            }

            writer.WriteByte((byte)groupFlags);

            if ((groupFlags & GroupFlagsEncodingMask.WriterGroupIdEnabled) != 0)
            {
                writer.WriteUInt16Le(message.WriterGroupId ?? 0);
            }
            if ((groupFlags & GroupFlagsEncodingMask.GroupVersionEnabled) != 0)
            {
                writer.WriteUInt32Le(message.GroupVersion);
            }
            if ((groupFlags & GroupFlagsEncodingMask.NetworkMessageNumberEnabled) != 0)
            {
                writer.WriteUInt16Le(message.NetworkMessageNumber);
            }
            if ((groupFlags & GroupFlagsEncodingMask.SequenceNumberEnabled) != 0)
            {
                writer.WriteUInt16Le(message.SequenceNumber);
            }
        }

        private static void WriteExtendedHeader(
            ref UadpBinaryWriter writer,
            UadpNetworkMessage message,
            ExtendedFlags1EncodingMask ext1,
            PubSubNetworkMessageContext context)
        {
            if ((ext1 & ExtendedFlags1EncodingMask.TimestampEnabled) != 0)
            {
                writer.WriteInt64Le(message.Timestamp.Value);
            }
            if ((ext1 & ExtendedFlags1EncodingMask.PicoSecondsEnabled) != 0)
            {
                writer.WriteUInt16Le(message.PicoSeconds);
            }
            if ((message.ContentMask & UadpNetworkMessageContentMask.PromotedFields) != 0)
            {
                WritePromotedFields(ref writer, message.PromotedFields, context);
            }
        }

        private static void WritePromotedFields(
            ref UadpBinaryWriter writer,
            ArrayOf<DataSetField> fields,
            PubSubNetworkMessageContext context)
        {
            int sizePos = writer.Reserve(2);
            int beforeFields = writer.Position;
            foreach (DataSetField field in fields)
            {
                writer.WriteVariant(field.Value, context.MessageContext);
            }
            int afterFields = writer.Position;
            writer.PatchUInt16Le(sizePos, checked((ushort)(afterFields - beforeFields)));
        }

        private static void WriteDataSetMessage(
            ref UadpBinaryWriter writer,
            UadpDataSetMessage message,
            UadpNetworkMessage parent,
            PubSubNetworkMessageContext context)
        {
            (DataSetFlags1EncodingMask flags1, DataSetFlags2EncodingMask flags2) =
                DeriveDataSetFlags(message);

            writer.WriteByte((byte)flags1);
            if ((flags1 & DataSetFlags1EncodingMask.DataSetFlags2Enabled) != 0)
            {
                writer.WriteByte((byte)flags2);
            }
            if ((flags1 & DataSetFlags1EncodingMask.SequenceNumberEnabled) != 0)
            {
                writer.WriteUInt16Le((ushort)(message.SequenceNumber & 0xFFFF));
            }
            if ((flags2 & DataSetFlags2EncodingMask.TimestampEnabled) != 0)
            {
                writer.WriteInt64Le(message.Timestamp.Value);
            }
            if ((flags2 & DataSetFlags2EncodingMask.PicoSecondsEnabled) != 0)
            {
                writer.WriteUInt16Le(message.PicoSeconds);
            }
            if ((flags1 & DataSetFlags1EncodingMask.StatusEnabled) != 0)
            {
                writer.WriteUInt16Le((ushort)(message.Status.Code >> 16));
            }
            if ((flags1 & DataSetFlags1EncodingMask.MajorVersionEnabled) != 0)
            {
                writer.WriteUInt32Le(message.MetaDataVersion.MajorVersion);
            }
            if ((flags1 & DataSetFlags1EncodingMask.MinorVersionEnabled) != 0)
            {
                writer.WriteUInt32Le(message.MetaDataVersion.MinorVersion);
            }

            int payloadStart = writer.Position;
            UadpFieldEncoder.EncodeFields(
                ref writer, message.Fields, message.FieldEncoding,
                message.MessageType,
                ResolveMetaData(message, parent, context), context.MessageContext,
                message.FieldContentMask);

            ApplyConfiguredSize(ref writer, message, payloadStart);
        }

        private static (DataSetFlags1EncodingMask, DataSetFlags2EncodingMask) DeriveDataSetFlags(
            UadpDataSetMessage message)
        {
            DataSetFlags1EncodingMask flags1 = DataSetFlags1EncodingMask.MessageIsValid;
            DataSetFlags2EncodingMask flags2 = 0;

            flags1 |= (DataSetFlags1EncodingMask)
                DataSetFlags1EncodingMaskExtensions.EncodeFieldEncoding(message.FieldEncoding);

            if ((message.ContentMask & UadpDataSetMessageContentMask.SequenceNumber) != 0)
            {
                flags1 |= DataSetFlags1EncodingMask.SequenceNumberEnabled;
            }
            if ((message.ContentMask & UadpDataSetMessageContentMask.Status) != 0)
            {
                flags1 |= DataSetFlags1EncodingMask.StatusEnabled;
            }
            if ((message.ContentMask & UadpDataSetMessageContentMask.MajorVersion) != 0)
            {
                flags1 |= DataSetFlags1EncodingMask.MajorVersionEnabled;
            }
            if ((message.ContentMask & UadpDataSetMessageContentMask.MinorVersion) != 0)
            {
                flags1 |= DataSetFlags1EncodingMask.MinorVersionEnabled;
            }

            if ((message.ContentMask & UadpDataSetMessageContentMask.Timestamp) != 0)
            {
                flags2 |= DataSetFlags2EncodingMask.TimestampEnabled;
            }
            if ((message.ContentMask & UadpDataSetMessageContentMask.PicoSeconds) != 0)
            {
                flags2 |= DataSetFlags2EncodingMask.PicoSecondsEnabled;
            }
            flags2 |= (DataSetFlags2EncodingMask)
                DataSetFlags2EncodingMaskExtensions.EncodeMessageType(message.MessageType);

            bool needFlags2 = flags2 != 0;
            if (needFlags2)
            {
                flags1 |= DataSetFlags1EncodingMask.DataSetFlags2Enabled;
            }
            return (flags1, flags2);
        }

        private static DataSetMetaDataType? ResolveMetaData(
            UadpDataSetMessage message,
            UadpNetworkMessage parent,
            PubSubNetworkMessageContext context)
        {
            if (message.FieldEncoding != PubSubFieldEncoding.RawData)
            {
                return null;
            }
            var key = new MetaData.DataSetMetaDataKey(
                parent.PublisherId,
                parent.WriterGroupId ?? 0,
                message.DataSetWriterId,
                parent.DataSetClassId,
                message.MetaDataVersion.MajorVersion);
            MetaData.MetaDataMatchResult match =
                context.MetaDataRegistry.TryGet(key, out DataSetMetaDataType? meta);
            if (match == MetaData.MetaDataMatchResult.Match ||
                match == MetaData.MetaDataMatchResult.MinorVersionMismatch)
            {
                return meta;
            }
            return null;
        }

        private static void ApplyConfiguredSize(
            ref UadpBinaryWriter writer,
            UadpDataSetMessage message,
            int payloadStart)
        {
            if (message.ConfiguredSize == 0)
            {
                return;
            }
            int actual = writer.Position - payloadStart;
            int target = checked((int)message.ConfiguredSize);
            if (actual > target)
            {
                throw new InvalidOperationException(
                    "Encoded DataSet payload exceeds ConfiguredSize.");
            }
            int padding = target - actual;
            for (int i = 0; i < padding; i++)
            {
                writer.WriteByte(0);
            }
        }

        /// <summary>
        /// Wraps a raw chunk frame produced by <see cref="UadpChunker"/>
        /// in a self-contained UADP envelope carrying the
        /// <see cref="ExtendedFlags2EncodingMask.ChunkMessage"/> bit so
        /// that receivers can route it through
        /// <see cref="UadpReassembler"/>.
        /// </summary>
        /// <remarks>
        /// Implements
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.4">
        /// Part 14 §7.2.4.4.4 ChunkedNetworkMessage</see>. The chunker
        /// emits header-prefixed payload bytes only; transport-level
        /// routing requires a real UADP envelope around each chunk.
        /// </remarks>
        /// <param name="chunkFrame">Chunk frame produced by
        /// <see cref="UadpChunker.Split"/>.</param>
        /// <param name="publisherId">Publisher identity copied into the
        /// envelope so the receiver can compute the reassembly key.</param>
        /// <param name="writerGroupId">WriterGroupId carried in the
        /// optional GroupHeader. When <c>null</c> the GroupHeader is
        /// omitted.</param>
        /// <returns>The fully framed envelope plus chunk payload.</returns>
        public static ReadOnlyMemory<byte> WriteChunkEnvelope(
            ReadOnlyMemory<byte> chunkFrame,
            PublisherId publisherId,
            ushort? writerGroupId)
        {
            if (chunkFrame.IsEmpty)
            {
                throw new ArgumentException(
                    "Chunk frame must not be empty.",
                    nameof(chunkFrame));
            }
            if (publisherId.IsNull)
            {
                throw new ArgumentException(
                    "PublisherId must not be null.",
                    nameof(publisherId));
            }

            PublisherIdType pidType = publisherId.Type;
            var uadpFlags = UadpFlagsEncodingMask.PublisherIdEnabled
                | UadpFlagsEncodingMask.ExtendedFlags1Enabled;
            if (writerGroupId.HasValue)
            {
                uadpFlags |= UadpFlagsEncodingMask.GroupHeaderEnabled;
            }
            byte ext1 = (byte)ExtendedFlags1EncodingMask.ExtendedFlags2Enabled;
            if (pidType != PublisherIdType.Byte)
            {
                ext1 |= ExtendedFlags1EncodingMaskExtensions
                    .EncodePublisherIdType(pidType);
            }
            byte ext2 = (byte)ExtendedFlags2EncodingMask.ChunkMessage;

            int envelopeSize = 1 + 1 + 1
                + EstimatePublisherIdSize(publisherId, pidType)
                + (writerGroupId.HasValue ? 3 : 0);
            byte[] result = new byte[envelopeSize + chunkFrame.Length];
            var writer = new UadpBinaryWriter(result, 0, result.Length);

            byte version = 1;
            writer.WriteByte(
                (byte)((byte)uadpFlags | (version & 0x0F)));
            writer.WriteByte(ext1);
            writer.WriteByte(ext2);
            WritePublisherId(ref writer, publisherId, pidType);
            if (writerGroupId.HasValue)
            {
                writer.WriteByte((byte)GroupFlagsEncodingMask.WriterGroupIdEnabled);
                writer.WriteUInt16Le(writerGroupId.Value);
            }
            writer.WriteBytes(chunkFrame.Span);
            return result;
        }

        private static int EstimatePublisherIdSize(
            PublisherId publisherId, PublisherIdType type)
        {
            switch (type)
            {
                case PublisherIdType.Byte:
                    return 1;
                case PublisherIdType.UInt16:
                    return 2;
                case PublisherIdType.UInt32:
                    return 4;
                case PublisherIdType.UInt64:
                    return 8;
                case PublisherIdType.Guid:
                    return 16;
                case PublisherIdType.String:
                    string? s = publisherId.TryGetString(out string? str) ? str : null;
                    int byteLen = s is null ? 0 : System.Text.Encoding.UTF8.GetByteCount(s);
                    return 4 + byteLen;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported PublisherIdType {type}.");
            }
        }
    }
}
