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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Encodes PubSub network messages into the experimental Avro frame format.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_1")]
    public sealed class AvroNetworkMessageEncoder : INetworkMessageEncoder
    {
        private const string Magic = "OPC-UA-PubSub-Avro";
        private const ushort Version = 1;

        /// <inheritdoc/>
        public string TransportProfileUri
        {
            get { return AvroNetworkMessage.PubSubMqttAvroTransport; }
        }

        /// <inheritdoc/>
        public int EstimatedHeaderOverhead
        {
            get { return 128; }
        }

        /// <summary>
        /// Gets the SchemaId cache and per-destination announcement tracker used by the encoder.
        /// </summary>
        public SchemaCache SchemaCache { get; } = new();

        /// <summary>
        /// Gets or sets the destination identity used for announce-once tracking.
        /// </summary>
        public string DestinationId { get; set; } = string.Empty;

        /// <summary>
        /// Gets the announcement produced by the most recent encode call, if one was needed.
        /// </summary>
        public AvroSchemaAnnouncement? LastSchemaAnnouncement { get; private set; }

        /// <inheritdoc/>
        public async ValueTask<ReadOnlyMemory<byte>> EncodeAsync(
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

            if (networkMessage is not AvroNetworkMessage message)
            {
                throw new ArgumentException(
                    "Network message type is not supported by the Avro encoder.",
                    nameof(networkMessage));
            }

            AvroSchemaAnnouncement announcement = SchemaExchangeMessages.CreateAvroAnnouncement(message);
            LastSchemaAnnouncement = SchemaCache.MarkAnnounced(DestinationId, announcement.SchemaId)
                ? announcement
                : null;
            SchemaCache.Add(announcement);

            using MemoryStream stream = new();
            using AvroEncoder encoder = new(stream, context.MessageContext, leaveOpen: true);
            encoder.WriteString(null, Magic);
            encoder.WriteUInt16(null, Version);
            WritePublisherId(encoder, message.PublisherId);
            WriteNullableUInt16(encoder, message.WriterGroupId);
            encoder.WriteGuid(null, message.DataSetClassId);
            encoder.WriteString(null, string.IsNullOrEmpty(message.SchemaId) ? null : message.SchemaId);
            encoder.WriteInt32(null, message.DataSetMessages.Count);

            for (int i = 0; i < message.DataSetMessages.Count; i++)
            {
                if (message.DataSetMessages[i] is not AvroDataSetMessage dataSetMessage)
                {
                    throw new ArgumentException(
                        "DataSetMessage entries must be AvroDataSetMessage instances.",
                        nameof(networkMessage));
                }
                WriteDataSetMessage(encoder, message, dataSetMessage, context);
            }
            encoder.Close();
            await Task.CompletedTask.ConfigureAwait(false);
            return stream.ToArray();
        }

        private static void WriteDataSetMessage(
            AvroEncoder encoder,
            AvroNetworkMessage envelope,
            AvroDataSetMessage message,
            PubSubNetworkMessageContext context)
        {
            encoder.WriteUInt16(null, message.DataSetWriterId);
            encoder.WriteEnumerated(null, message.MessageType);
            encoder.WriteUInt32(null, message.MetaDataVersion.MajorVersion);
            encoder.WriteUInt32(null, message.MetaDataVersion.MinorVersion);
            encoder.WriteUInt32(null, message.SequenceNumber);
            encoder.WriteStatusCode(null, message.Status);
            encoder.WriteDateTime(null, message.Timestamp);
            encoder.WriteInt64(null, (long)(uint)message.FieldContentMask);

            if (message.MessageType == PubSubDataSetMessageType.KeepAlive)
            {
                encoder.WriteInt32(null, 0);
                return;
            }

            DataSetMetaDataType? metaData = PubSubMessageEncoding.ResolveMetaData(
                envelope,
                message,
                context,
                envelope.DataSetClassId);
            encoder.WriteInt32(null, message.Fields.Count);
            for (int i = 0; i < message.Fields.Count; i++)
            {
                DataSetField field = message.Fields[i];
                string fieldName = PubSubMessageEncoding.ResolveFieldName(field, metaData, i);
                PubSubFieldEncoding fieldEncoding = SelectFieldEncoding(field, message.FieldContentMask);
                encoder.WriteString(null, fieldName);
                encoder.WriteInt32(null, field.FieldIndex >= 0 ? field.FieldIndex : i);
                encoder.WriteEnumerated(null, fieldEncoding);
                WriteFieldValue(encoder, field, fieldEncoding, message.FieldContentMask, metaData, fieldName, i);
            }
        }

        private static PubSubFieldEncoding SelectFieldEncoding(
            DataSetField field,
            DataSetFieldContentMask fieldContentMask)
        {
            if (field.Encoding == PubSubFieldEncoding.DataValue
                || HasDataValueMembers(fieldContentMask))
            {
                return PubSubFieldEncoding.DataValue;
            }
            return field.Encoding == PubSubFieldEncoding.RawData
                ? PubSubFieldEncoding.RawData
                : PubSubFieldEncoding.Variant;
        }

        private static bool HasDataValueMembers(DataSetFieldContentMask mask)
        {
            const DataSetFieldContentMask dataValueBits = DataSetFieldContentMask.StatusCode
                | DataSetFieldContentMask.SourceTimestamp
                | DataSetFieldContentMask.SourcePicoSeconds
                | DataSetFieldContentMask.ServerTimestamp
                | DataSetFieldContentMask.ServerPicoSeconds;
            return (mask & dataValueBits) != 0;
        }

        private static void WriteFieldValue(
            AvroEncoder encoder,
            DataSetField field,
            PubSubFieldEncoding fieldEncoding,
            DataSetFieldContentMask fieldContentMask,
            DataSetMetaDataType? metaData,
            string fieldName,
            int index)
        {
            using MemoryStream stream = new();
            using AvroEncoder valueEncoder = new(stream, encoder.Context, leaveOpen: true);
            switch (fieldEncoding)
            {
                case PubSubFieldEncoding.RawData:
                    TypeInfo? typeInfo = PubSubMessageEncoding.ResolveFieldType(metaData, fieldName, index);
                    if (typeInfo is null)
                    {
                        throw new ArgumentException(
                            $"RawData Avro field '{fieldName}' requires DataSetMetaData.");
                    }
                    valueEncoder.WriteVariantValue(null, field.Value);
                    break;
                case PubSubFieldEncoding.DataValue:
                    valueEncoder.WriteDataValue(
                        null,
                        PubSubMessageEncoding.BuildDataValue(field, fieldContentMask));
                    break;
                case PubSubFieldEncoding.Variant:
                default:
                    valueEncoder.WriteVariant(null, field.Value);
                    break;
            }
            valueEncoder.Close();
            encoder.WriteByteString(null, ByteString.From(stream.ToArray()));
        }

        private static void WriteNullableUInt16(AvroEncoder encoder, ushort? value)
        {
            encoder.WriteBoolean(null, value.HasValue);
            if (value.HasValue)
            {
                encoder.WriteUInt16(null, value.Value);
            }
        }

        private static void WritePublisherId(AvroEncoder encoder, PublisherId publisherId)
        {
            encoder.WriteEnumerated(null, publisherId.Type);
            switch (publisherId.Type)
            {
                case PublisherIdType.Byte:
                    publisherId.TryGetByte(out byte b);
                    encoder.WriteByte(null, b);
                    break;
                case PublisherIdType.UInt16:
                    publisherId.TryGetUInt16(out ushort u16);
                    encoder.WriteUInt16(null, u16);
                    break;
                case PublisherIdType.UInt32:
                    publisherId.TryGetUInt32(out uint u32);
                    encoder.WriteUInt32(null, u32);
                    break;
                case PublisherIdType.UInt64:
                    publisherId.TryGetUInt64(out ulong u64);
                    encoder.WriteUInt64(null, u64);
                    break;
                case PublisherIdType.String:
                    publisherId.TryGetString(out string? s);
                    encoder.WriteString(null, s ?? string.Empty);
                    break;
                case PublisherIdType.Guid:
                    publisherId.TryGetGuid(out Guid g);
                    encoder.WriteGuid(null, new Uuid(g));
                    break;
            }
        }
    }
}
