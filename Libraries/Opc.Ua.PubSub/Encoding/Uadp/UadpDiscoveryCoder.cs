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

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// Discovery-message subtype carried by a UADP NetworkMessage.
    /// Differentiates plain data messages from discovery requests and
    /// responses.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6">
    /// Part 14 §7.2.4.6</see>. The numeric values mirror the ExtendedFlags2
    /// discovery bits so that an enum can be combined with the flags
    /// helpers without an extra translation step.
    /// </remarks>
    public enum UadpDiscoveryType
    {
        /// <summary>
        /// Not a discovery message.
        /// </summary>
        None = 0,

        /// <summary>
        /// PublisherEndpoints discovery message (response carries the
        /// publisher's transport endpoints).
        /// </summary>
        PublisherEndpoints = 1,

        /// <summary>
        /// DataSetMetaData discovery message — request lists the
        /// DataSetWriterIds, response carries each writer's metadata.
        /// </summary>
        DataSetMetaData = 2,

        /// <summary>
        /// DataSetWriterConfiguration discovery message — request lists
        /// the DataSetWriterIds, response carries the writer
        /// configuration block.
        /// </summary>
        DataSetWriterConfiguration = 3,

        /// <summary>
        /// ApplicationInformation discovery response — publisher
        /// announces its application identity, transport profiles and
        /// supported security policies. See
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.4.6.7">
        /// Part 14 §7.2.4.6.7</see>.
        /// </summary>
        ApplicationInformation = 4,

        /// <summary>
        /// PubSubConnection announcement — publisher advertises one of
        /// its connection configurations so subscribers can self-bind.
        /// See
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.4.6.8">
        /// Part 14 §7.2.4.6.8</see>.
        /// </summary>
        PubSubConnection = 5,

        /// <summary>
        /// Generic discovery probe (request side). See
        /// <see href="https://reference.opcfoundation.org/Core/Part14/v105/docs/7.2.4.6.12">
        /// Part 14 §7.2.4.6.12</see>.
        /// </summary>
        Probe = 6
    }

    /// <summary>
    /// Stateless encode + decode for UADP discovery NetworkMessages.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6">
    /// Part 14 §7.2.4.6</see>. The non-discovery encoder/decoder
    /// route messages here when the ExtendedFlags2 discovery bits are
    /// set.
    /// </remarks>
    public static class UadpDiscoveryCoder
    {
        /// <summary>
        /// Encodes a discovery NetworkMessage.
        /// </summary>
        /// <param name="message">Source message; must be a
        /// <see cref="UadpDiscoveryRequestMessage"/> or
        /// <see cref="UadpDiscoveryResponseMessage"/>.</param>
        /// <param name="context">Network message context.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static byte[] Encode(
            PubSubNetworkMessage message,
            PubSubNetworkMessageContext context)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            return message switch
            {
                UadpDiscoveryRequestMessage request =>
                    EncodeRequest(request, context),
                UadpDiscoveryResponseMessage response =>
                    EncodeResponse(response, context),
                _ => throw new InvalidOperationException(
                    "Discovery encoding requires a UadpDiscoveryRequestMessage " +
                    "or UadpDiscoveryResponseMessage instance.")
            };
        }

        /// <summary>
        /// Attempts to decode a discovery NetworkMessage from the
        /// supplied frame after the common UADP header has been read.
        /// </summary>
        /// <param name="reader">Reader positioned right after the
        /// shared UADP header (PublisherId already consumed).</param>
        /// <param name="ext2">Decoded ExtendedFlags2 from the
        /// header.</param>
        /// <param name="header">Pre-decoded UADP header common to all
        /// NetworkMessages.</param>
        /// <param name="context">Network message context.</param>
        /// <returns>The decoded message, or <c>null</c> on malformed
        /// input.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        internal static PubSubNetworkMessage? TryDecode(
            ref UadpBinaryReader reader,
            ExtendedFlags2EncodingMask ext2,
            UadpDecodedHeader header,
            PubSubNetworkMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if ((ext2 & ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryRequest) != 0)
            {
                return TryDecodeRequest(ref reader, header, context);
            }
            if ((ext2 & ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryResponse) != 0)
            {
                return TryDecodeResponse(ref reader, header, context);
            }
            return null;
        }

        private static byte[] EncodeRequest(
            UadpDiscoveryRequestMessage message,
            PubSubNetworkMessageContext context)
        {
            byte[] buffer = new byte[1024];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            UadpDiscoveryWire.WriteCommonHeader(
                ref writer, message,
                ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryRequest);

            writer.WriteByte((byte)message.DiscoveryType);
            writer.WriteUInt32Le((uint)message.DataSetWriterIds.Count);
            foreach (ushort id in message.DataSetWriterIds)
            {
                writer.WriteUInt16Le(id);
            }
            if (message.DiscoveryType == UadpDiscoveryType.Probe)
            {
                WriteProbeFilter(ref writer, message.ProbeFilter);
            }
            _ = context;
            return TrimToWritten(buffer, writer.Position);
        }

        private static byte[] EncodeResponse(
            UadpDiscoveryResponseMessage message,
            PubSubNetworkMessageContext context)
        {
            byte[] buffer = new byte[8192];
            var writer = new UadpBinaryWriter(buffer, 0, buffer.Length);
            UadpDiscoveryWire.WriteCommonHeader(
                ref writer, message,
                ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryResponse);

            writer.WriteByte((byte)message.DiscoveryType);
            writer.WriteUInt16Le(message.SequenceNumber);

            switch (message.DiscoveryType)
            {
                case UadpDiscoveryType.DataSetMetaData:
                    WriteMetaData(ref writer, message, context.MessageContext);
                    break;
                case UadpDiscoveryType.DataSetWriterConfiguration:
                    WriteWriterConfiguration(ref writer, message, context.MessageContext);
                    break;
                case UadpDiscoveryType.PublisherEndpoints:
                    WritePublisherEndpoints(ref writer, message, context.MessageContext);
                    break;
                case UadpDiscoveryType.ApplicationInformation:
                    WriteApplicationInformation(ref writer, message, context.MessageContext);
                    break;
                case UadpDiscoveryType.PubSubConnection:
                    WriteConnection(ref writer, message, context.MessageContext);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported discovery type {message.DiscoveryType}.");
            }
            return TrimToWritten(buffer, writer.Position);
        }

        private static UadpDiscoveryRequestMessage? TryDecodeRequest(
            ref UadpBinaryReader reader,
            UadpDecodedHeader header,
            PubSubNetworkMessageContext context)
        {
            _ = context;
            if (!reader.TryReadByte(out byte typeByte))
            {
                return null;
            }
            if (!reader.TryReadUInt32Le(out uint count))
            {
                return null;
            }
            if (count > int.MaxValue)
            {
                return null;
            }
            int countInt = (int)count;
            ushort[] ids = new ushort[countInt];
            for (int i = 0; i < countInt; i++)
            {
                if (!reader.TryReadUInt16Le(out ushort id))
                {
                    return null;
                }
                ids[i] = id;
            }
            UadpDiscoveryProbeFilter? filter = null;
            if ((UadpDiscoveryType)typeByte == UadpDiscoveryType.Probe)
            {
                filter = TryReadProbeFilter(ref reader);
                if (filter is null)
                {
                    return null;
                }
            }

            return new UadpDiscoveryRequestMessage
            {
                PublisherId = header.PublisherId,
                WriterGroupId = header.WriterGroupId,
                DataSetClassId = header.DataSetClassId,
                MessageType = UadpNetworkMessageType.DiscoveryRequest,
                DiscoveryType = (UadpDiscoveryType)typeByte,
                DataSetWriterIds = ids,
                ProbeFilter = filter
            };
        }

        private static UadpDiscoveryResponseMessage? TryDecodeResponse(
            ref UadpBinaryReader reader,
            UadpDecodedHeader header,
            PubSubNetworkMessageContext context)
        {
            if (!reader.TryReadByte(out byte typeByte))
            {
                return null;
            }
            if (!reader.TryReadUInt16Le(out ushort sequenceNumber))
            {
                return null;
            }

            var discoveryType = (UadpDiscoveryType)typeByte;
            var response = new UadpDiscoveryResponseMessage
            {
                PublisherId = header.PublisherId,
                WriterGroupId = header.WriterGroupId,
                DataSetClassId = header.DataSetClassId,
                MessageType = UadpNetworkMessageType.DiscoveryResponse,
                DiscoveryType = discoveryType,
                SequenceNumber = sequenceNumber
            };

            try
            {
                response = discoveryType switch
                {
                    UadpDiscoveryType.DataSetMetaData =>
                        ReadMetaData(ref reader, response, context.MessageContext),
                    UadpDiscoveryType.DataSetWriterConfiguration =>
                        ReadWriterConfiguration(ref reader, response, context.MessageContext),
                    UadpDiscoveryType.PublisherEndpoints =>
                        ReadPublisherEndpoints(ref reader, response, context.MessageContext),
                    UadpDiscoveryType.ApplicationInformation =>
                        ReadApplicationInformation(ref reader, response, context.MessageContext),
                    UadpDiscoveryType.PubSubConnection =>
                        ReadConnection(ref reader, response, context.MessageContext),
                    _ => response
                };
            }
            catch
            {
                return null;
            }
            return response;
        }

        private static void WriteMetaData(
            ref UadpBinaryWriter writer,
            UadpDiscoveryResponseMessage message,
            IServiceMessageContext context)
        {
            writer.WriteUInt16Le(message.DataSetWriterId);
            UadpDiscoveryWire.WriteEncodeable(ref writer, message.DataSetMetaData, context);
            writer.WriteUInt32Le(message.StatusCode.Code);
        }

        private static void WriteWriterConfiguration(
            ref UadpBinaryWriter writer,
            UadpDiscoveryResponseMessage message,
            IServiceMessageContext context)
        {
            writer.WriteUInt32Le((uint)message.DataSetWriterIds.Count);
            foreach (ushort id in message.DataSetWriterIds)
            {
                writer.WriteUInt16Le(id);
            }
            UadpDiscoveryWire.WriteEncodeable(ref writer, message.WriterConfiguration, context);
            writer.WriteUInt32Le((uint)message.DataSetWriterIds.Count);
            foreach (ushort _ in message.DataSetWriterIds)
            {
                writer.WriteUInt32Le(message.StatusCode.Code);
            }
        }

        private static void WritePublisherEndpoints(
            ref UadpBinaryWriter writer,
            UadpDiscoveryResponseMessage message,
            IServiceMessageContext context)
        {
            writer.WriteUInt32Le((uint)message.PublisherEndpoints.Count);
            foreach (EndpointDescription endpoint in message.PublisherEndpoints)
            {
                UadpDiscoveryWire.WriteEncodeable(ref writer, endpoint, context);
            }
            writer.WriteUInt32Le(message.StatusCode.Code);
        }

        private static UadpDiscoveryResponseMessage ReadMetaData(
            ref UadpBinaryReader reader,
            UadpDiscoveryResponseMessage message,
            IServiceMessageContext context)
        {
            if (!reader.TryReadUInt16Le(out ushort writerId))
            {
                throw new InvalidOperationException("Failed reading DataSetWriterId.");
            }
            DataSetMetaDataType meta = UadpDiscoveryWire.ReadEncodeable<DataSetMetaDataType>(
                ref reader, context);
            if (!reader.TryReadUInt32Le(out uint statusCode))
            {
                throw new InvalidOperationException("Failed reading StatusCode.");
            }
            return message with
            {
                DataSetWriterId = writerId,
                DataSetMetaData = meta,
                StatusCode = new StatusCode(statusCode)
            };
        }

        private static UadpDiscoveryResponseMessage ReadWriterConfiguration(
            ref UadpBinaryReader reader,
            UadpDiscoveryResponseMessage message,
            IServiceMessageContext context)
        {
            if (!reader.TryReadUInt32Le(out uint count))
            {
                throw new InvalidOperationException("Failed reading writer-id count.");
            }
            if (count > int.MaxValue)
            {
                throw new InvalidOperationException("Writer-id count is too large.");
            }
            int countInt = (int)count;
            ushort[] ids = new ushort[countInt];
            for (int i = 0; i < countInt; i++)
            {
                if (!reader.TryReadUInt16Le(out ushort id))
                {
                    throw new InvalidOperationException("Failed reading writer id.");
                }
                ids[i] = id;
            }
            WriterGroupDataType cfg = UadpDiscoveryWire.ReadEncodeable<WriterGroupDataType>(
                ref reader, context);
            if (!reader.TryReadUInt32Le(out uint statusCount))
            {
                throw new InvalidOperationException("Failed reading StatusCode count.");
            }
            if (statusCount != count)
            {
                throw new InvalidOperationException("StatusCode count does not match writer-id count.");
            }
            uint statusCode = 0;
            int statusCountInt = (int)statusCount;
            for (int i = 0; i < statusCountInt; i++)
            {
                if (!reader.TryReadUInt32Le(out uint code))
                {
                    throw new InvalidOperationException("Failed reading StatusCode.");
                }
                if (i == 0)
                {
                    statusCode = code;
                }
            }
            return message with
            {
                DataSetWriterIds = ids,
                WriterConfiguration = cfg,
                StatusCode = new StatusCode(statusCode)
            };
        }

        private static UadpDiscoveryResponseMessage ReadPublisherEndpoints(
            ref UadpBinaryReader reader,
            UadpDiscoveryResponseMessage message,
            IServiceMessageContext context)
        {
            if (!reader.TryReadUInt32Le(out uint count))
            {
                throw new InvalidOperationException("Failed reading endpoint count.");
            }
            if (count > int.MaxValue)
            {
                throw new InvalidOperationException("Endpoint count is too large.");
            }
            int countInt = (int)count;
            var list = new EndpointDescription[countInt];
            for (int i = 0; i < countInt; i++)
            {
                list[i] = UadpDiscoveryWire.ReadEncodeable<EndpointDescription>(
                    ref reader, context);
            }
            if (!reader.TryReadUInt32Le(out uint statusCode))
            {
                throw new InvalidOperationException("Failed reading StatusCode.");
            }
            return message with
            {
                PublisherEndpoints = list,
                StatusCode = new StatusCode(statusCode)
            };
        }

        private static void WriteApplicationInformation(
            ref UadpBinaryWriter writer,
            UadpDiscoveryResponseMessage message,
            IServiceMessageContext context)
        {
            if (message.ApplicationStatus is not null)
            {
                WriteApplicationStatus(ref writer, message.ApplicationStatus);
                return;
            }
            UadpApplicationInformation info = message.ApplicationInformation
                ?? new UadpApplicationInformation();
            writer.WriteUInt16Le(1);
            var description = new ApplicationDescription
            {
                ApplicationUri = info.ApplicationUri,
                ProductUri = info.ProductUri,
                ApplicationName = info.ApplicationName,
                ApplicationType = info.ApplicationType
            };
            UadpDiscoveryWire.WriteEncodeable(ref writer, description, context);
            WriteStringArray(ref writer, info.Capabilities);
        }

        private static UadpDiscoveryResponseMessage ReadApplicationInformation(
            ref UadpBinaryReader reader,
            UadpDiscoveryResponseMessage message,
            IServiceMessageContext context)
        {
            if (!reader.TryReadUInt16Le(out ushort applicationInformationType))
            {
                throw new InvalidOperationException("Failed reading ApplicationInformationType.");
            }
            if (applicationInformationType == 2)
            {
                return ReadApplicationStatus(ref reader, message);
            }
            if (applicationInformationType != 1)
            {
                throw new InvalidOperationException("Unsupported ApplicationInformationType.");
            }
            ApplicationDescription description =
                UadpDiscoveryWire.ReadEncodeable<ApplicationDescription>(ref reader, context);
            string[] capabilities = ReadStringArray(ref reader);
            return message with
            {
                ApplicationInformation = new UadpApplicationInformation
                {
                    ApplicationName = description.ApplicationName,
                    ApplicationUri = description.ApplicationUri ?? string.Empty,
                    ProductUri = description.ProductUri ?? string.Empty,
                    ApplicationType = description.ApplicationType,
                    Capabilities = capabilities
                }
            };
        }

        private static void WriteApplicationStatus(
            ref UadpBinaryWriter writer,
            UadpApplicationStatus status)
        {
            writer.WriteUInt16Le(2);
            writer.WriteByte(status.IsCyclic ? (byte)1 : (byte)0);
            writer.WriteUInt32Le((uint)status.Status);
            if (status.IsCyclic)
            {
                writer.WriteInt64Le(status.NextReportTime.Value);
                writer.WriteInt64Le(status.Timestamp.Value);
            }
        }

        private static UadpDiscoveryResponseMessage ReadApplicationStatus(
            ref UadpBinaryReader reader,
            UadpDiscoveryResponseMessage message)
        {
            if (!reader.TryReadByte(out byte isCyclicByte))
            {
                throw new InvalidOperationException("Failed reading IsCyclic.");
            }
            if (!reader.TryReadUInt32Le(out uint statusValue))
            {
                throw new InvalidOperationException("Failed reading PubSubState.");
            }
            bool isCyclic = isCyclicByte != 0;
            DateTimeUtc nextReportTime = DateTimeUtc.MinValue;
            DateTimeUtc timestamp = DateTimeUtc.MinValue;
            if (isCyclic)
            {
                if (!reader.TryReadInt64Le(out long nextReportTimeValue) ||
                    !reader.TryReadInt64Le(out long timestampValue))
                {
                    throw new InvalidOperationException("Failed reading cyclic status timestamps.");
                }
                nextReportTime = new DateTimeUtc(nextReportTimeValue);
                timestamp = new DateTimeUtc(timestampValue);
            }
            return message with
            {
                ApplicationStatus = new UadpApplicationStatus
                {
                    IsCyclic = isCyclic,
                    Status = (PubSubState)statusValue,
                    NextReportTime = nextReportTime,
                    Timestamp = timestamp
                }
            };
        }

        private static void WriteConnection(
            ref UadpBinaryWriter writer,
            UadpDiscoveryResponseMessage message,
            IServiceMessageContext context)
        {
            UadpDiscoveryWire.WriteEncodeable(ref writer, message.Connection, context);
            writer.WriteUInt32Le(message.StatusCode.Code);
        }

        private static UadpDiscoveryResponseMessage ReadConnection(
            ref UadpBinaryReader reader,
            UadpDiscoveryResponseMessage message,
            IServiceMessageContext context)
        {
            PubSubConnectionDataType cfg =
                UadpDiscoveryWire.ReadEncodeable<PubSubConnectionDataType>(ref reader, context);
            if (!reader.TryReadUInt32Le(out uint statusCode))
            {
                throw new InvalidOperationException("Failed reading StatusCode.");
            }
            return message with
            {
                Connection = cfg,
                StatusCode = new StatusCode(statusCode)
            };
        }

        private static void WriteProbeFilter(
            ref UadpBinaryWriter writer,
            UadpDiscoveryProbeFilter? filter)
        {
            UadpDiscoveryProbeFilter f = filter ?? new UadpDiscoveryProbeFilter();
            writer.WriteString(f.ApplicationUri);
            writer.WriteString(f.ProductUri);
            writer.WriteString(f.Capability);
            writer.WriteByte(f.WriterGroupId.HasValue ? (byte)1 : (byte)0);
            if (f.WriterGroupId.HasValue)
            {
                writer.WriteUInt16Le(f.WriterGroupId.Value);
            }
            writer.WriteByte(f.IncludeWriterGroups ? (byte)1 : (byte)0);
            writer.WriteByte(f.IncludeDataSetWriters ? (byte)1 : (byte)0);
            WriteStringArray(ref writer, f.TransportProfileUris);
        }

        private static UadpDiscoveryProbeFilter? TryReadProbeFilter(
            ref UadpBinaryReader reader)
        {
            if (!reader.TryReadString(out string? appUri))
            {
                return null;
            }
            if (!reader.TryReadString(out string? productUri))
            {
                return null;
            }
            if (!reader.TryReadString(out string? capability))
            {
                return null;
            }
            ushort? writerGroupId = null;
            bool includeWriterGroups = false;
            bool includeDataSetWriters = false;
            string[] transportProfileUris = [];
            if (reader.Remaining > 0)
            {
                if (!reader.TryReadByte(out byte hasWriterGroupId))
                {
                    return null;
                }
                if (hasWriterGroupId != 0)
                {
                    if (!reader.TryReadUInt16Le(out ushort id))
                    {
                        return null;
                    }
                    writerGroupId = id;
                }
                if (!reader.TryReadByte(out byte includeGroupsByte) ||
                    !reader.TryReadByte(out byte includeWritersByte))
                {
                    return null;
                }
                includeWriterGroups = includeGroupsByte != 0;
                includeDataSetWriters = includeWritersByte != 0;
                transportProfileUris = ReadStringArray(ref reader);
            }
            return new UadpDiscoveryProbeFilter
            {
                ApplicationUri = appUri ?? string.Empty,
                ProductUri = productUri ?? string.Empty,
                Capability = capability ?? string.Empty,
                WriterGroupId = writerGroupId,
                IncludeWriterGroups = includeWriterGroups,
                IncludeDataSetWriters = includeDataSetWriters,
                TransportProfileUris = transportProfileUris
            };
        }

        private static void WriteStringArray(
            ref UadpBinaryWriter writer,
            ArrayOf<string> values)
        {
            writer.WriteUInt32Le((uint)values.Count);
            for (int i = 0; i < values.Count; i++)
            {
                writer.WriteString(values[i] ?? string.Empty);
            }
        }

        private static string[] ReadStringArray(ref UadpBinaryReader reader)
        {
            if (!reader.TryReadUInt32Le(out uint count))
            {
                throw new InvalidOperationException("Failed reading string-array count.");
            }
            if (count > int.MaxValue)
            {
                throw new InvalidOperationException("String-array count is too large.");
            }
            int countInt = (int)count;
            string[] result = new string[countInt];
            for (int i = 0; i < countInt; i++)
            {
                if (!reader.TryReadString(out string? entry))
                {
                    throw new InvalidOperationException("Failed reading string-array entry.");
                }
                result[i] = entry ?? string.Empty;
            }
            return result;
        }

        private static byte[] TrimToWritten(byte[] buffer, int written)
        {
            byte[] result = new byte[written];
            Buffer.BlockCopy(buffer, 0, result, 0, written);
            return result;
        }
    }

    /// <summary>
    /// Common UADP header values needed by the discovery decoder after
    /// the data decoder has already parsed the shared bytes.
    /// </summary>
    public readonly record struct UadpDecodedHeader
    {
        /// <summary>
        /// PublisherId carried in the header.
        /// </summary>
        public PublisherId PublisherId { get; init; }

        /// <summary>
        /// WriterGroupId from the GroupHeader (if present).
        /// </summary>
        public ushort? WriterGroupId { get; init; }

        /// <summary>
        /// DataSetClassId carried by ExtendedFlags1 (if present).
        /// </summary>
        public Uuid DataSetClassId { get; init; }
    }

    internal static class UadpDiscoveryWire
    {
        public static void WriteCommonHeader(
            ref UadpBinaryWriter writer,
            UadpDiscoveryRequestMessage message,
            ExtendedFlags2EncodingMask discoveryBit)
        {
            WriteCommonHeader(
                ref writer, message.UadpVersion, message.PublisherId,
                message.DataSetClassId, discoveryBit);
        }

        public static void WriteCommonHeader(
            ref UadpBinaryWriter writer,
            UadpDiscoveryResponseMessage message,
            ExtendedFlags2EncodingMask discoveryBit)
        {
            WriteCommonHeader(
                ref writer, message.UadpVersion, message.PublisherId,
                message.DataSetClassId, discoveryBit);
        }

        public static void WriteCommonHeader(
            ref UadpBinaryWriter writer,
            byte uadpVersion,
            PublisherId publisherId,
            Uuid dataSetClassId,
            ExtendedFlags2EncodingMask extendedFlags2,
            bool securityEnabled,
            bool payloadHeaderEnabled,
            ushort? writerGroupId)
        {
            WriteCommonHeaderCore(
                ref writer, uadpVersion, publisherId, dataSetClassId, extendedFlags2,
                securityEnabled, payloadHeaderEnabled, writerGroupId);
        }

        private static void WriteCommonHeader(
            ref UadpBinaryWriter writer,
            byte uadpVersion,
            PublisherId publisherId,
            Uuid dataSetClassId,
            ExtendedFlags2EncodingMask discoveryBit)
        {
            WriteCommonHeaderCore(
                ref writer, uadpVersion, publisherId, dataSetClassId, discoveryBit,
                securityEnabled: false, payloadHeaderEnabled: false, writerGroupId: null);
        }

        private static void WriteCommonHeaderCore(
            ref UadpBinaryWriter writer,
            byte uadpVersion,
            PublisherId publisherId,
            Uuid dataSetClassId,
            ExtendedFlags2EncodingMask extendedFlags2,
            bool securityEnabled,
            bool payloadHeaderEnabled,
            ushort? writerGroupId)
        {
            UadpFlagsEncodingMask uadpFlags =
                UadpFlagsEncodingMask.PublisherIdEnabled |
                UadpFlagsEncodingMask.ExtendedFlags1Enabled;
            if (payloadHeaderEnabled)
            {
                uadpFlags |= UadpFlagsEncodingMask.PayloadHeaderEnabled;
            }
            if (writerGroupId.HasValue)
            {
                uadpFlags |= UadpFlagsEncodingMask.GroupHeaderEnabled;
            }
            ExtendedFlags1EncodingMask ext1 =
                ExtendedFlags1EncodingMask.ExtendedFlags2Enabled;

            PublisherIdType type = publisherId.Type;
            if (type != PublisherIdType.Byte)
            {
                ext1 |= (ExtendedFlags1EncodingMask)
                    ExtendedFlags1EncodingMaskExtensions.EncodePublisherIdType(type);
            }
            if (((Guid)dataSetClassId) != Guid.Empty)
            {
                ext1 |= ExtendedFlags1EncodingMask.DataSetClassIdEnabled;
            }
            if (securityEnabled)
            {
                ext1 |= ExtendedFlags1EncodingMask.SecurityEnabled;
            }

            writer.WriteByte(UadpFlagsEncodingMaskExtensions.Combine(uadpVersion, uadpFlags));
            writer.WriteByte((byte)ext1);
            writer.WriteByte((byte)extendedFlags2);

            WritePublisherIdValue(ref writer, publisherId, type);

            if ((ext1 & ExtendedFlags1EncodingMask.DataSetClassIdEnabled) != 0)
            {
                writer.WriteGuid((Guid)dataSetClassId);
            }
            if (writerGroupId.HasValue)
            {
                writer.WriteByte((byte)GroupFlagsEncodingMask.WriterGroupIdEnabled);
                writer.WriteUInt16Le(writerGroupId.Value);
            }
        }

        private static void WritePublisherIdValue(
            ref UadpBinaryWriter writer, PublisherId publisherId, PublisherIdType type)
        {
            switch (type)
            {
                case PublisherIdType.Byte:
                    publisherId.TryGetByte(out byte b);
                    writer.WriteByte(b);
                    break;
                case PublisherIdType.UInt16:
                    publisherId.TryGetUInt16(out ushort u16);
                    writer.WriteUInt16Le(u16);
                    break;
                case PublisherIdType.UInt32:
                    publisherId.TryGetUInt32(out uint u32);
                    writer.WriteUInt32Le(u32);
                    break;
                case PublisherIdType.UInt64:
                    publisherId.TryGetUInt64(out ulong u64);
                    writer.WriteUInt64Le(u64);
                    break;
                case PublisherIdType.String:
                    publisherId.TryGetString(out string? s);
                    writer.WriteString(s);
                    break;
                case PublisherIdType.Guid:
                    publisherId.TryGetGuid(out Guid g);
                    writer.WriteGuid(g);
                    break;
                default:
                    writer.WriteByte(0);
                    break;
            }
        }

        public static void WriteEncodeable(
            ref UadpBinaryWriter writer, IEncodeable? value, IServiceMessageContext context)
        {
            int sizePos = writer.Reserve(4);
            int before = writer.Position;
            byte[] buffer = writer.Buffer;
            int absoluteStart = writer.Origin + writer.Position;
            int available = writer.Capacity - writer.Position;
            int written;
            using (var encoder = new BinaryEncoder(buffer, absoluteStart, available, context))
            {
                if (value is not null)
                {
                    value.Encode(encoder);
                }
                written = encoder.Close();
            }
            writer.Advance(written);
            int after = writer.Position;
            writer.PatchUInt32Le(sizePos, checked((uint)(after - before)));
        }

        public static T ReadEncodeable<T>(ref UadpBinaryReader reader, IServiceMessageContext ctx)
            where T : class, IEncodeable, new()
        {
            if (!reader.TryReadUInt32Le(out uint length))
            {
                throw new InvalidOperationException("Failed reading payload length.");
            }
            if (length > reader.Remaining)
            {
                throw new InvalidOperationException("Payload length exceeds buffer.");
            }
            int absoluteStart = reader.Origin + reader.Position;
            T value = new();
            using (var decoder = new BinaryDecoder(
                reader.Buffer, absoluteStart, (int)length, ctx))
            {
                value.Decode(decoder);
            }
            reader.Advance((int)length);
            return value;
        }
    }
}
