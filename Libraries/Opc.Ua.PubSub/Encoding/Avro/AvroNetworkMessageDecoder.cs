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

using Opc.Ua;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Diagnostics;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Decodes experimental Avro PubSub network frames and resolves referenced DataSet schemas.
    /// </summary>
    public sealed class AvroNetworkMessageDecoder : INetworkMessageDecoder
    {
        private const string Magic = "OPC-UA-PubSub-Avro";
        private const ushort Version = 1;

        /// <inheritdoc/>
        public string TransportProfileUri
        {
            get { return AvroNetworkMessage.PubSubMqttAvroTransport; }
        }

        /// <summary>
        /// Gets the SchemaId cache used by the decoder.
        /// </summary>
        public SchemaCache SchemaCache { get; } = new();

        /// <summary>
        /// Gets or sets the resolver invoked when a referenced SchemaId is not cached.
        /// </summary>
        public ISchemaResolver? SchemaResolver { get; set; }

        /// <summary>
        /// Ingests an Avro schema announcement into the decoder cache.
        /// </summary>
        /// <param name="announcement">The schema announcement to ingest.</param>
        public void Ingest(AvroSchemaAnnouncement announcement)
        {
            SchemaCache.Add(announcement);
        }

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
            return new ValueTask<PubSubNetworkMessage?>(DecodeCore(frame, context));
        }

        private PubSubNetworkMessage? DecodeCore(
            ReadOnlyMemory<byte> frame,
            PubSubNetworkMessageContext context)
        {
            try
            {
                using AvroDecoder decoder = new(frame.ToArray(), context.MessageContext);
                string? magic = decoder.ReadString(null);
                ushort version = decoder.ReadUInt16(null);
                if (!string.Equals(magic, Magic, StringComparison.Ordinal) || version != Version)
                {
                    context.Diagnostics.Increment(
                        PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                    return null;
                }

                PublisherId publisherId = ReadPublisherId(decoder);
                ushort? writerGroupId = ReadNullableUInt16(decoder);
                Uuid dataSetClassId = decoder.ReadGuid(null);
                string schemaId = decoder.ReadString(null) ?? string.Empty;
                if (SchemaCache.TryParseKey(schemaId, out ByteString schemaIdBytes)
                    && !SchemaCache.TryGetOrResolve(schemaIdBytes, SchemaResolver, out _))
                {
                    throw new FormatException($"Avro schema '{schemaId}' is not available.");
                }
                int count = decoder.ReadInt32(null);
                if (count < 0)
                {
                    throw new FormatException("Avro NetworkMessage contains a negative DataSetMessage count.");
                }
                if (decoder.Context.MaxArrayLength > 0 && count > decoder.Context.MaxArrayLength)
                {
                    throw new FormatException(
                        $"Avro NetworkMessage DataSetMessage count {count} exceeds MaxArrayLength {decoder.Context.MaxArrayLength}.");
                }

                var messages = new List<PubSubDataSetMessage>(count);
                var envelope = new AvroNetworkMessage
                {
                    PublisherId = publisherId,
                    WriterGroupId = writerGroupId,
                    DataSetClassId = dataSetClassId,
                    SchemaId = schemaId
                };

                for (int i = 0; i < count; i++)
                {
                    messages.Add(ReadDataSetMessage(decoder, envelope, context));
                }

                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedDataSetMessages,
                    messages.Count);

                return envelope with { DataSetMessages = messages };
            }
            catch (Exception ex) when (ex is FormatException
                or EndOfStreamException
                or OverflowException
                or ArgumentException
                or NotSupportedException
                or ServiceResultException)
            {
                context.Diagnostics.Increment(
                    PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                context.Diagnostics.RecordError(
                    (StatusCode)StatusCodes.BadDecodingError,
                    ex.Message);
                return null;
            }
        }

        private static AvroDataSetMessage ReadDataSetMessage(
            AvroDecoder decoder,
            AvroNetworkMessage envelope,
            PubSubNetworkMessageContext context)
        {
            ushort dataSetWriterId = decoder.ReadUInt16(null);
            PubSubDataSetMessageType messageType = decoder.ReadEnumerated<PubSubDataSetMessageType>(null);
            uint majorVersion = decoder.ReadUInt32(null);
            uint minorVersion = decoder.ReadUInt32(null);
            uint sequenceNumber = decoder.ReadUInt32(null);
            StatusCode status = decoder.ReadStatusCode(null);
            DateTimeUtc timestamp = decoder.ReadDateTime(null);
            DataSetFieldContentMask fieldContentMask =
                (DataSetFieldContentMask)unchecked((uint)decoder.ReadInt64(null));

            var message = new AvroDataSetMessage
            {
                DataSetWriterId = dataSetWriterId,
                MessageType = messageType,
                MetaDataVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = majorVersion,
                    MinorVersion = minorVersion
                },
                SequenceNumber = sequenceNumber,
                Status = status,
                Timestamp = timestamp,
                FieldContentMask = fieldContentMask
            };

            int fieldCount = decoder.ReadInt32(null);
            if (fieldCount < 0)
            {
                throw new FormatException("Avro DataSetMessage contains a negative field count.");
            }
            if (decoder.Context.MaxArrayLength > 0 && fieldCount > decoder.Context.MaxArrayLength)
            {
                throw new FormatException(
                    $"Avro DataSetMessage field count {fieldCount} exceeds MaxArrayLength {decoder.Context.MaxArrayLength}.");
            }
            if (messageType == PubSubDataSetMessageType.KeepAlive)
            {
                return message;
            }

            DataSetMetaDataType? metaData = PubSubMessageEncoding.ResolveMetaData(
                envelope,
                message,
                context,
                envelope.DataSetClassId);
            var fields = new List<DataSetField>(fieldCount);
            for (int i = 0; i < fieldCount; i++)
            {
                string name = decoder.ReadString(null) ?? string.Empty;
                int fieldIndex = decoder.ReadInt32(null);
                PubSubFieldEncoding fieldEncoding = decoder.ReadEnumerated<PubSubFieldEncoding>(null);
                fields.Add(ReadFieldValue(
                    decoder,
                    fieldEncoding,
                    fieldContentMask,
                    metaData,
                    name,
                    fieldIndex >= 0 ? fieldIndex : i));
            }

            return message with { Fields = fields };
        }

        private static DataSetField ReadFieldValue(
            AvroDecoder decoder,
            PubSubFieldEncoding fieldEncoding,
            DataSetFieldContentMask fieldContentMask,
            DataSetMetaDataType? metaData,
            string name,
            int fieldIndex)
        {
            ByteString valueBytes = decoder.ReadByteString(null);
            using AvroDecoder valueDecoder = new(valueBytes.Span.ToArray(), decoder.Context);
            switch (fieldEncoding)
            {
                case PubSubFieldEncoding.RawData:
                    TypeInfo? typeInfo = PubSubMessageEncoding.ResolveFieldType(
                        metaData,
                        name,
                        fieldIndex);
                    if (typeInfo is null)
                    {
                        throw new FormatException(
                            $"RawData Avro field '{name}' requires DataSetMetaData.");
                    }
                    return new DataSetField
                    {
                        Name = name,
                        FieldIndex = fieldIndex,
                        Value = valueDecoder.ReadVariantValue(null, typeInfo.Value),
                        Encoding = PubSubFieldEncoding.RawData
                    };
                case PubSubFieldEncoding.DataValue:
                    return PubSubMessageEncoding.FromDataValue(
                        name,
                        fieldIndex,
                        valueDecoder.ReadDataValue(null),
                        fieldContentMask);
                case PubSubFieldEncoding.Variant:
                default:
                    return new DataSetField
                    {
                        Name = name,
                        FieldIndex = fieldIndex,
                        Value = valueDecoder.ReadVariant(null),
                        Encoding = PubSubFieldEncoding.Variant
                    };
            }
        }

        private static ushort? ReadNullableUInt16(AvroDecoder decoder)
        {
            return decoder.ReadBoolean(null) ? decoder.ReadUInt16(null) : null;
        }

        private static PublisherId ReadPublisherId(AvroDecoder decoder)
        {
            PublisherIdType type = decoder.ReadEnumerated<PublisherIdType>(null);
            return type switch
            {
                PublisherIdType.Byte => PublisherId.FromByte(decoder.ReadByte(null)),
                PublisherIdType.UInt16 => PublisherId.FromUInt16(decoder.ReadUInt16(null)),
                PublisherIdType.UInt32 => PublisherId.FromUInt32(decoder.ReadUInt32(null)),
                PublisherIdType.UInt64 => PublisherId.FromUInt64(decoder.ReadUInt64(null)),
                PublisherIdType.String => PublisherId.FromString(decoder.ReadString(null) ?? string.Empty),
                PublisherIdType.Guid => PublisherId.FromGuid(decoder.ReadGuid(null).Guid),
                _ => throw new FormatException($"Invalid PublisherId type {type}.")
            };
        }
    }
}
