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
#if NET8_0_OR_GREATER
using Opc.Ua;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Apache.Arrow;
using Apache.Arrow.Arrays;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using Opc.Ua.PubSub.Diagnostics;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Decodes the experimental Arrow PubSub IPC-stream mapping. It expects
    /// one DataSet schema per stream, leading per-row header columns, and
    /// typed RawData field columns; unsupported Arrow/OPC UA type pairings
    /// throw rather than falling back to blobs or JSON.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_Arrow")]
    public sealed class ArrowNetworkMessageDecoder : INetworkMessageDecoder
    {
        private const string Magic = "OPC-UA-PubSub-Arrow";
        private const string Version = "1";
        private const int HeaderColumnCount = 5;

        // Bounds the number of distinct cached schema messages so a peer that
        // sends many distinct schemaId values cannot grow the cache without
        // limit (memory-exhaustion DoS). When full an arbitrary existing entry
        // is evicted; a full IPC stream re-caches its schema on the next
        // occurrence, so eviction is self-healing.
        internal const int MaxCachedSchemaMessages = 256;
        private static readonly byte[] s_streamEnd = [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00];
        // Concurrent so a decoder instance shared across receive threads cannot corrupt the cache
        // (the schema message is written from CacheSchema/CacheSchemaMessage and read from decode).
        private readonly ConcurrentDictionary<string, byte[]> m_schemaMessages = new(StringComparer.Ordinal);

        /// <inheritdoc/>
        public string TransportProfileUri
        {
            get { return ArrowNetworkMessage.PubSubMqttArrowTransport; }
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
        /// Gets the number of cached bare-batch schema messages (test/diagnostic accessor).
        /// </summary>
        internal int CachedSchemaMessageCount => m_schemaMessages.Count;

        /// <summary>
        /// Ingests an Arrow schema announcement into the decoder cache.
        /// </summary>
        /// <param name="announcement">The schema announcement to ingest.</param>
        public void Ingest(ArrowSchemaAnnouncement announcement)
        {
            SchemaCache.Add(announcement);
        }

        /// <summary>
        /// Caches a serialized Arrow Schema IPC message under a SchemaId so that subsequent bare
        /// RecordBatch payloads referencing that SchemaId can be decoded. This is the out-of-band
        /// counterpart to embedding the Schema message in every stream.
        /// </summary>
        /// <param name="schemaId">The SchemaId that identifies the schema.</param>
        /// <param name="schemaMessage">The serialized Arrow Schema IPC message bytes.</param>
        public void CacheSchema(string schemaId, ReadOnlyMemory<byte> schemaMessage)
        {
            if (string.IsNullOrEmpty(schemaId) || schemaMessage.IsEmpty)
            {
                return;
            }
            StoreSchemaMessage(schemaId, schemaMessage.ToArray());
        }

        /// <summary>
        /// Decodes a bare Arrow RecordBatch payload (a <see cref="ArrowIpcFraming.Batch"/> message)
        /// whose schema was previously cached for the referenced SchemaId, either by decoding a
        /// full IPC stream for the same SchemaId or via <see cref="CacheSchema"/>.
        /// </summary>
        /// <param name="recordBatch">The bare Arrow RecordBatch message bytes.</param>
        /// <param name="schemaId">The SchemaId that selects the cached schema.</param>
        /// <param name="context">The PubSub decoding context.</param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The decoded network message, or null when the schema is not available.</returns>
        public ValueTask<PubSubNetworkMessage?> TryDecodeBatchAsync(
            ReadOnlyMemory<byte> recordBatch,
            string schemaId,
            PubSubNetworkMessageContext context,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (schemaId is null || !m_schemaMessages.TryGetValue(schemaId, out byte[]? schemaMessage))
            {
                context.Diagnostics.Increment(PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                context.Diagnostics.RecordError(
                    (StatusCode)StatusCodes.BadDecodingError,
                    $"Arrow schema '{schemaId}' is not cached for bare-batch decoding.");
                return new ValueTask<PubSubNetworkMessage?>((PubSubNetworkMessage?)null);
            }

            byte[] reconstructed = new byte[schemaMessage.Length + recordBatch.Length + s_streamEnd.Length];
            schemaMessage.CopyTo(reconstructed.AsSpan(0));
            recordBatch.Span.CopyTo(reconstructed.AsSpan(schemaMessage.Length));
            s_streamEnd.CopyTo(reconstructed.AsSpan(schemaMessage.Length + recordBatch.Length));
            return new ValueTask<PubSubNetworkMessage?>(DecodeCore(reconstructed, context));
        }

        /// <inheritdoc/>
        public async ValueTask<PubSubNetworkMessage?> TryDecodeAsync(
            ReadOnlyMemory<byte> frame,
            PubSubNetworkMessageContext context,
            CancellationToken cancellationToken = default)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            return DecodeCore(frame, context);
        }

        private PubSubNetworkMessage? DecodeCore(
            ReadOnlyMemory<byte> frame,
            PubSubNetworkMessageContext context)
        {
            try
            {
                using MemoryStream stream = new(frame.ToArray(), writable: false);
                using ArrowStreamReader reader = new(stream, leaveOpen: true);
                using RecordBatch? batch = reader.ReadNextRecordBatch();
                if (batch is null)
                {
                    context.Diagnostics.Increment(PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                    return null;
                }

                IReadOnlyDictionary<string, string> metadata = batch.Schema.Metadata;
                if (!string.Equals(ReadMeta(metadata, "magic"), Magic, StringComparison.Ordinal)
                    || !string.Equals(ReadMeta(metadata, "version"), Version, StringComparison.Ordinal))
                {
                    context.Diagnostics.Increment(PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                    return null;
                }

                PublisherId publisherId = string.IsNullOrEmpty(ReadMeta(metadata, "publisherId"))
                    ? PublisherId.Null
                    : PublisherId.FromString(ReadMeta(metadata, "publisherId"));
                ushort writerGroupId = ParseUInt16(ReadMeta(metadata, "writerGroupId"));
                Uuid dataSetClassId = ParseUuid(ReadMeta(metadata, "dataSetClassId"));
                string schemaId = ReadMeta(metadata, "schemaId");
                CacheSchemaMessage(frame, schemaId);
                if (SchemaCache.TryParseKey(schemaId, out ByteString schemaIdBytes)
                    && !SchemaCache.TryGetOrResolve(schemaIdBytes, SchemaResolver, out _))
                {
                    throw new FormatException($"Arrow schema '{schemaId}' is not available.");
                }
                uint majorVersion = ParseUInt32(ReadMeta(metadata, "majorVersion"));
                uint minorVersion = ParseUInt32(ReadMeta(metadata, "minorVersion"));

                ArrowNetworkMessage envelope = new()
                {
                    PublisherId = publisherId,
                    WriterGroupId = writerGroupId == 0 ? null : writerGroupId,
                    DataSetClassId = dataSetClassId,
                    SchemaId = schemaId
                };

                int rowCount = checked((int)batch.Length);
                var messages = new List<PubSubDataSetMessage>(rowCount);
                for (int row = 0; row < rowCount; row++)
                {
                    ArrowDataSetMessage message = ReadDataSetMessage(
                        batch,
                        envelope,
                        context,
                        row,
                        majorVersion,
                        minorVersion);
                    messages.Add(message);
                }

                context.Diagnostics.Increment(PubSubDiagnosticsCounterKind.ReceivedNetworkMessages);
                context.Diagnostics.Increment(PubSubDiagnosticsCounterKind.ReceivedDataSetMessages, messages.Count);
                return envelope with { DataSetMessages = messages };
            }
            catch (Exception ex) when (ex is FormatException
                or InvalidCastException
                or EndOfStreamException
                or OverflowException
                or NotSupportedException
                or ArgumentException
                or ServiceResultException)
            {
                context.Diagnostics.Increment(PubSubDiagnosticsCounterKind.ReceivedInvalidNetworkMessages);
                context.Diagnostics.RecordError((StatusCode)StatusCodes.BadDecodingError, ex.Message);
                return null;
            }
        }
        private static ArrowDataSetMessage ReadDataSetMessage(
            RecordBatch batch,
            ArrowNetworkMessage envelope,
            PubSubNetworkMessageContext context,
            int row,
            uint majorVersion,
            uint minorVersion)
        {
            if (batch.ColumnCount < HeaderColumnCount)
            {
                throw new FormatException(
                    $"Arrow DataSetMessage record batch has {batch.ColumnCount} columns; expected at least {HeaderColumnCount}.");
            }

            ushort writerId = ((UInt16Array)batch.Column(0)).GetValue(row) ?? 0;
            uint sequenceNumber = ((UInt32Array)batch.Column(1)).GetValue(row) ?? 0;
            StatusCode status = new(((UInt32Array)batch.Column(2)).GetValue(row) ?? 0);
            DateTimeUtc timestamp = new(((Int64Array)batch.Column(3)).GetValue(row) ?? 0);
            PubSubDataSetMessageType messageType =
                (PubSubDataSetMessageType)(((Int32Array)batch.Column(4)).GetValue(row) ?? 0);

            var message = new ArrowDataSetMessage
            {
                DataSetWriterId = writerId,
                SequenceNumber = sequenceNumber,
                Status = status,
                Timestamp = timestamp,
                MessageType = messageType,
                MetaDataVersion = new ConfigurationVersionDataType
                {
                    MajorVersion = majorVersion,
                    MinorVersion = minorVersion
                },
                FieldContentMask = DataSetFieldContentMask.RawData
            };

            DataSetMetaDataType? metaData = PubSubMessageEncoding.ResolveMetaData(
                envelope,
                message,
                context,
                envelope.DataSetClassId);
            var fields = new List<DataSetField>(Math.Max(0, batch.ColumnCount - HeaderColumnCount));
            for (int col = HeaderColumnCount; col < batch.ColumnCount; col++)
            {
                Field arrowField = batch.Schema.GetFieldByIndex(col);
                int fieldIndex = col - HeaderColumnCount;
                TypeInfo typeInfo = PubSubMessageEncoding.ResolveFieldType(metaData, arrowField.Name, fieldIndex)
                    ?? InferTypeInfo(arrowField.Name, arrowField.DataType);
                fields.Add(new DataSetField
                {
                    Name = arrowField.Name,
                    FieldIndex = fieldIndex,
                    Encoding = PubSubFieldEncoding.RawData,
                    Value = ReadVariant(batch.Column(col), row, arrowField.Name, typeInfo)
                });
            }

            return message with { Fields = fields };
        }

        private static Variant ReadVariant(IArrowArray array, int row, string fieldName, TypeInfo typeInfo)
        {
            if (typeInfo.ValueRank >= 0)
            {
                return ReadListVariant((ListArray)array, row, fieldName, typeInfo.BuiltInType);
            }
            return typeInfo.BuiltInType switch
            {
                BuiltInType.Boolean => ((BooleanArray)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(((BooleanArray)array).GetValue(row) ?? false),
                BuiltInType.SByte => ((Int8Array)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(((Int8Array)array).GetValue(row) ?? 0),
                BuiltInType.Byte => ((UInt8Array)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(((UInt8Array)array).GetValue(row) ?? 0),
                BuiltInType.Int16 => ((Int16Array)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(((Int16Array)array).GetValue(row) ?? 0),
                BuiltInType.UInt16 => ((UInt16Array)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(((UInt16Array)array).GetValue(row) ?? 0),
                BuiltInType.Int32 => ((Int32Array)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(((Int32Array)array).GetValue(row) ?? 0),
                BuiltInType.UInt32 => ((UInt32Array)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(((UInt32Array)array).GetValue(row) ?? 0),
                BuiltInType.Int64 => ((Int64Array)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(((Int64Array)array).GetValue(row) ?? 0),
                BuiltInType.UInt64 => ((UInt64Array)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(((UInt64Array)array).GetValue(row) ?? 0),
                BuiltInType.Float => ((FloatArray)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(((FloatArray)array).GetValue(row) ?? 0),
                BuiltInType.Double => ((DoubleArray)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(((DoubleArray)array).GetValue(row) ?? 0),
                BuiltInType.String => ((StringArray)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(((StringArray)array).GetString(row)),
                BuiltInType.DateTime => ((Int64Array)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(new DateTimeUtc(((Int64Array)array).GetValue(row) ?? 0)),
                BuiltInType.Guid => ((FixedSizeBinaryArray)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(new Uuid(((FixedSizeBinaryArray)array).GetBytes(row).ToArray())),
                BuiltInType.ByteString => ((BinaryArray)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(ByteString.From(((BinaryArray)array).GetBytes(row).ToArray())),
                BuiltInType.StatusCode => ((UInt32Array)array).IsNull(row)
                    ? Variant.Null
                    : new Variant(new StatusCode(((UInt32Array)array).GetValue(row) ?? 0)),
                _ => throw Unsupported(fieldName, typeInfo)
            };
        }

        private static Variant ReadListVariant(ListArray list, int row, string fieldName, BuiltInType type)
        {
            if (list.IsNull(row))
            {
                return Variant.Null;
            }
            int start = list.ValueOffsets[row];
            int length = list.ValueOffsets[row + 1] - start;
            IArrowArray values = list.Values;
            if (start < 0 || length < 0 || (long)start + length > values.Length)
            {
                throw new FormatException(
                    $"Arrow list offsets for field '{fieldName}' are out of range " +
                    $"(start={start}, length={length}, values={values.Length}).");
            }
            return type switch
            {
                BuiltInType.Boolean => new Variant(new ArrayOf<bool>(
                    Enumerable.Range(start, length).Select(i => ((BooleanArray)values).GetValue(i) ?? false).ToArray())),
                BuiltInType.SByte => new Variant(new ArrayOf<sbyte>(
                    Enumerable.Range(start, length).Select(i => ((Int8Array)values).GetValue(i) ?? 0).ToArray())),
                BuiltInType.Byte => new Variant(new ArrayOf<byte>(
                    Enumerable.Range(start, length).Select(i => ((UInt8Array)values).GetValue(i) ?? 0).ToArray())),
                BuiltInType.Int16 => new Variant(new ArrayOf<short>(
                    Enumerable.Range(start, length).Select(i => ((Int16Array)values).GetValue(i) ?? 0).ToArray())),
                BuiltInType.UInt16 => new Variant(new ArrayOf<ushort>(
                    Enumerable.Range(start, length).Select(i => ((UInt16Array)values).GetValue(i) ?? 0).ToArray())),
                BuiltInType.Int32 => new Variant(new ArrayOf<int>(
                    Enumerable.Range(start, length).Select(i => ((Int32Array)values).GetValue(i) ?? 0).ToArray())),
                BuiltInType.UInt32 => new Variant(new ArrayOf<uint>(
                    Enumerable.Range(start, length).Select(i => ((UInt32Array)values).GetValue(i) ?? 0).ToArray())),
                BuiltInType.Int64 => new Variant(new ArrayOf<long>(
                    Enumerable.Range(start, length).Select(i => ((Int64Array)values).GetValue(i) ?? 0).ToArray())),
                BuiltInType.UInt64 => new Variant(new ArrayOf<ulong>(
                    Enumerable.Range(start, length).Select(i => ((UInt64Array)values).GetValue(i) ?? 0).ToArray())),
                BuiltInType.Float => new Variant(new ArrayOf<float>(
                    Enumerable.Range(start, length).Select(i => ((FloatArray)values).GetValue(i) ?? 0).ToArray())),
                BuiltInType.Double => new Variant(new ArrayOf<double>(
                    Enumerable.Range(start, length).Select(i => ((DoubleArray)values).GetValue(i) ?? 0).ToArray())),
                BuiltInType.String => new Variant(new ArrayOf<string>(
                    Enumerable.Range(start, length)
                        .Select(i => ((StringArray)values).IsNull(i) ? null : ((StringArray)values).GetString(i))
                        .ToArray()!)),
                BuiltInType.DateTime => new Variant(new ArrayOf<DateTimeUtc>(
                    Enumerable.Range(start, length)
                        .Select(i => ((Int64Array)values).IsNull(i)
                            ? default
                            : new DateTimeUtc(((Int64Array)values).GetValue(i) ?? 0))
                        .ToArray())),
                BuiltInType.Guid => new Variant(new ArrayOf<Uuid>(
                    Enumerable.Range(start, length)
                        .Select(i => new Uuid(((FixedSizeBinaryArray)values).GetBytes(i).ToArray()))
                        .ToArray())),
                BuiltInType.ByteString => new Variant(new ArrayOf<ByteString>(
                    Enumerable.Range(start, length)
                        .Select(i => ((BinaryArray)values).IsNull(i)
                            ? default
                            : ByteString.From(((BinaryArray)values).GetBytes(i).ToArray()))
                        .ToArray())),
                BuiltInType.StatusCode => new Variant(new ArrayOf<StatusCode>(
                    Enumerable.Range(start, length)
                        .Select(i => new StatusCode(((UInt32Array)values).GetValue(i) ?? 0))
                        .ToArray())),
                _ => throw new NotSupportedException(
                    $"Arrow list field '{fieldName}' with element type {type} is not supported.")
            };
        }

        private static TypeInfo InferTypeInfo(string fieldName, IArrowType type)
        {
            if (type is ListType listType)
            {
                TypeInfo scalar = InferTypeInfo(fieldName, listType.ValueField.DataType);
                return TypeInfo.Create(scalar.BuiltInType, ValueRanks.OneDimension);
            }
            BuiltInType builtIn = type.TypeId switch
            {
                ArrowTypeId.Boolean => BuiltInType.Boolean,
                ArrowTypeId.Int8 => BuiltInType.SByte,
                ArrowTypeId.UInt8 => BuiltInType.Byte,
                ArrowTypeId.Int16 => BuiltInType.Int16,
                ArrowTypeId.UInt16 => BuiltInType.UInt16,
                ArrowTypeId.Int32 => BuiltInType.Int32,
                ArrowTypeId.UInt32 => BuiltInType.UInt32,
                ArrowTypeId.Int64 => BuiltInType.Int64,
                ArrowTypeId.UInt64 => BuiltInType.UInt64,
                ArrowTypeId.Float => BuiltInType.Float,
                ArrowTypeId.Double => BuiltInType.Double,
                ArrowTypeId.String => BuiltInType.String,
                ArrowTypeId.FixedSizedBinary => BuiltInType.Guid,
                ArrowTypeId.Binary => BuiltInType.ByteString,
                _ => throw new NotSupportedException(
                    $"Arrow field '{fieldName}' with Arrow type {type.Name} is not supported.")
            };
            return TypeInfo.Create(builtIn, ValueRanks.Scalar);
        }

        private static string ReadMeta(IReadOnlyDictionary<string, string> metadata, string key)
        {
            return metadata.TryGetValue(key, out string? value) ? value : string.Empty;
        }

        private static ushort ParseUInt16(string value)
        {
            return ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort parsed)
                ? parsed
                : (ushort)0;
        }

        private static uint ParseUInt32(string value)
        {
            return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed) ? parsed : 0;
        }

        private static Uuid ParseUuid(string value)
        {
            return Guid.TryParse(value, out Guid guid) ? new Uuid(guid) : Uuid.Empty;
        }

        private static NotSupportedException Unsupported(string fieldName, TypeInfo typeInfo)
        {
            return new NotSupportedException(
                $"Arrow RawData field '{fieldName}' with type {typeInfo} is not supported by this adapter.");
        }

        private void CacheSchemaMessage(ReadOnlyMemory<byte> frame, string schemaId)
        {
            if (string.IsNullOrEmpty(schemaId))
            {
                return;
            }
            int length = SchemaMessageLength(frame.Span);
            if (length > 0 && length <= frame.Length)
            {
                StoreSchemaMessage(schemaId, frame.Slice(0, length).ToArray());
            }
        }

        private void StoreSchemaMessage(string schemaId, byte[] schemaMessage)
        {
            if (m_schemaMessages.Count >= MaxCachedSchemaMessages
                && !m_schemaMessages.ContainsKey(schemaId))
            {
                foreach (string existing in m_schemaMessages.Keys)
                {
                    if (m_schemaMessages.TryRemove(existing, out _))
                    {
                        break;
                    }
                }
            }
            m_schemaMessages[schemaId] = schemaMessage;
        }

        private static int SchemaMessageLength(ReadOnlySpan<byte> frame)
        {
            // Arrow encapsulated IPC message: [0xFFFFFFFF continuation][int32 metadata size][metadata + padding].
            // Encapsulated messages are aligned to 8-byte boundaries. For spec-compliant
            // streams the metadata size already includes that padding, but round the total
            // up to the next 8-byte boundary defensively so a Schema message (which carries
            // no body buffers) is never truncated when the metadata size is not aligned.
            if (frame.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(frame) != 0xFFFFFFFFu)
            {
                return 0;
            }
            int metadataSize = BinaryPrimitives.ReadInt32LittleEndian(frame.Slice(4));
            if (metadataSize < 0)
            {
                return 0;
            }
            long aligned = (8L + metadataSize + 7L) & ~7L;
            if (aligned > frame.Length)
            {
                return 0;
            }
            return (int)aligned;
        }
    }
}
#endif
