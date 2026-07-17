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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Apache.Arrow;
using Apache.Arrow.Arrays;
using Apache.Arrow.Ipc;
using Apache.Arrow.Memory;
using Apache.Arrow.Types;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Encodes an <see cref="ArrowNetworkMessage"/> as a genuine Apache Arrow
    /// IPC stream: one schema for one DataSet, one RecordBatch, rows as
    /// DataSetMessage samples, leading per-sample header columns, and typed
    /// RawData field columns. Supported field types are Boolean, integer widths,
    /// Float, Double, String, DateTime, Guid, ByteString, StatusCode and
    /// one-dimensional arrays of those types; other BuiltInTypes throw.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental("UA_NETStandard_Encoders")]
    public sealed class ArrowNetworkMessageEncoder : INetworkMessageEncoder
    {
        private const string Magic = "OPC-UA-PubSub-Arrow";
        private const string Version = "1";
        private static readonly MemoryAllocator s_allocator = MemoryAllocator.Default.Value;

        /// <inheritdoc/>
        public string TransportProfileUri
        {
            get { return ArrowNetworkMessage.PubSubMqttArrowTransport; }
        }

        /// <inheritdoc/>
        public int EstimatedHeaderOverhead
        {
            get { return 256; }
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
        public ArrowSchemaAnnouncement? LastSchemaAnnouncement { get; private set; }

        /// <summary>
        /// Gets or sets the IPC framing used for encoded payloads. <see cref="ArrowIpcFraming.Batch"/>
        /// (the default) emits a bare RecordBatch whose schema is conveyed once out-of-band and resolved
        /// by SchemaId; <see cref="ArrowIpcFraming.Stream"/> embeds the Arrow Schema message in every
        /// payload so that it is self-contained.
        /// </summary>
        public ArrowIpcFraming Framing { get; set; } = ArrowIpcFraming.Batch;

        /// <summary>
        /// Gets the byte length of the Arrow Schema message produced by the most recent encode call.
        /// </summary>
        public int LastSchemaMessageLength { get; private set; }

        /// <summary>
        /// Gets the byte length of the Arrow RecordBatch message produced by the most recent encode call.
        /// </summary>
        public int LastRecordBatchMessageLength { get; private set; }

        /// <summary>
        /// Gets the serialized Arrow Schema IPC message bytes produced by the most recent encode call.
        /// A decoder can cache these under the SchemaId to decode subsequent bare RecordBatch payloads.
        /// </summary>
        public ReadOnlyMemory<byte> LastSchemaMessage { get; private set; }

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
            if (networkMessage is not ArrowNetworkMessage message)
            {
                throw new ArgumentException(
                    "Network message type is not supported by the Arrow encoder.",
                    nameof(networkMessage));
            }

            ArrowSchemaAnnouncement announcement = SchemaExchangeMessages.CreateArrowAnnouncement(message, context);
            LastSchemaAnnouncement = SchemaCache.MarkAnnounced(DestinationId, announcement.SchemaId)
                ? announcement
                : null;
            SchemaCache.Add(announcement);

            DataSetMetaDataType? metaData = ResolveBatchMetaData(message, context);
            FieldPlan[] fields = BuildFieldPlan(message, metaData);
            int rowCount = AllKeepAlive(message)
                ? 0
                : message.DataSetMessages.Count;
            ValidateHomogeneous(message, fields, metaData, rowCount);

            var schemaBuilder = new Apache.Arrow.Schema.Builder()
                .Metadata("magic", Magic)
                .Metadata("version", Version)
                .Metadata("publisherId", message.PublisherId.ToString())
                .Metadata("writerGroupId", (message.WriterGroupId ?? 0).ToString(CultureInfo.InvariantCulture))
                .Metadata("dataSetClassId", message.DataSetClassId.ToString())
                .Metadata("schemaId", message.SchemaId ?? string.Empty)
                .Metadata("majorVersion", FirstVersion(message).MajorVersion.ToString(CultureInfo.InvariantCulture))
                .Metadata("minorVersion", FirstVersion(message).MinorVersion.ToString(CultureInfo.InvariantCulture));

            var arrays = new List<IArrowArray>();
            AddHeaderColumns(schemaBuilder, arrays, message, rowCount);
            foreach (FieldPlan field in fields)
            {
                schemaBuilder.Field(new Field(field.Name, field.ArrowType, nullable: true, metadata: null));
                arrays.Add(BuildFieldArray(message, field, rowCount));
            }

            Apache.Arrow.Schema schema = schemaBuilder.Build();
            using RecordBatch batch = new(schema, arrays, rowCount);
            using MemoryStream stream = new();
            long schemaEnd;
            long batchEnd;
            using (ArrowStreamWriter writer = new(stream, schema, leaveOpen: true))
            {
                writer.WriteStart();
                schemaEnd = stream.Position;
                writer.WriteRecordBatch(batch);
                batchEnd = stream.Position;
                writer.WriteEnd();
            }
            await Task.CompletedTask.ConfigureAwait(false);

            byte[] full = stream.ToArray();
            LastSchemaMessageLength = (int)schemaEnd;
            LastRecordBatchMessageLength = (int)(batchEnd - schemaEnd);
            LastSchemaMessage = full.AsMemory(0, (int)schemaEnd);
            if (Framing == ArrowIpcFraming.Batch)
            {
                return full.AsMemory((int)schemaEnd, (int)(batchEnd - schemaEnd));
            }
            return full;
        }

        private static DataSetMetaDataType? ResolveBatchMetaData(
            ArrowNetworkMessage message,
            PubSubNetworkMessageContext context)
        {
            ArrowDataSetMessage? first = FirstArrowMessage(message);
            if (first is null)
            {
                return message.MetaData;
            }

            return PubSubMessageEncoding.ResolveMetaData(message, first, context, message.DataSetClassId);
        }

        private static ConfigurationVersionDataType FirstVersion(ArrowNetworkMessage message)
        {
            return FirstArrowMessage(message)?.MetaDataVersion
                ?? message.MetaData?.ConfigurationVersion
                ?? new ConfigurationVersionDataType();
        }

        private static ArrowDataSetMessage? FirstArrowMessage(
            ArrowNetworkMessage message,
            bool skipKeepAlive = false)
        {
            for (int i = 0; i < message.DataSetMessages.Count; i++)
            {
                if (message.DataSetMessages[i] is ArrowDataSetMessage candidate
                    && (!skipKeepAlive || candidate.MessageType != PubSubDataSetMessageType.KeepAlive))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static bool AllKeepAlive(ArrowNetworkMessage message)
        {
            if (message.DataSetMessages.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < message.DataSetMessages.Count; i++)
            {
                if (message.DataSetMessages[i].MessageType != PubSubDataSetMessageType.KeepAlive)
                {
                    return false;
                }
            }
            return true;
        }

        private static FieldPlan[] BuildFieldPlan(
            ArrowNetworkMessage message,
            DataSetMetaDataType? metaData)
        {
            if (metaData is not null && metaData.Fields.Count > 0)
            {
                var plans = new FieldPlan[metaData.Fields.Count];
                for (int i = 0; i < plans.Length; i++)
                {
                    FieldMetaData field = metaData.Fields[i];
                    string name = string.IsNullOrEmpty(field.Name)
                        ? FormattableString.Invariant($"Field{i}")
                        : field.Name;
                    TypeInfo typeInfo = TypeInfo.Create((BuiltInType)field.BuiltInType, field.ValueRank);
                    plans[i] = new FieldPlan(name, i, typeInfo, ToArrowType(name, typeInfo));
                }
                return plans;
            }

            ArrowDataSetMessage? first = FirstArrowMessage(message, skipKeepAlive: true);
            if (first is null)
            {
                return [];
            }
            var inferred = new FieldPlan[first.Fields.Count];
            for (int i = 0; i < first.Fields.Count; i++)
            {
                DataSetField field = first.Fields[i];
                if (field.Value.IsNull)
                {
                    throw new ArgumentException(
                        $"Arrow field '{field.Name}' requires DataSetMetaData when the first value is null.");
                }
                inferred[i] = new FieldPlan(
                    PubSubMessageEncoding.ResolveFieldName(field, null, i),
                    i,
                    field.Value.TypeInfo,
                    ToArrowType(field.Name, field.Value.TypeInfo));
            }
            return inferred;
        }

        private static void ValidateHomogeneous(
            ArrowNetworkMessage message,
            FieldPlan[] fields,
            DataSetMetaDataType? metaData,
            int rowCount)
        {
            if (rowCount == 0)
            {
                return;
            }
            foreach (PubSubDataSetMessage candidate in message.DataSetMessages)
            {
                if (candidate is not ArrowDataSetMessage dataSetMessage)
                {
                    throw new ArgumentException(
                        "DataSetMessage entries must be ArrowDataSetMessage instances.",
                        nameof(message));
                }
                if (dataSetMessage.MessageType is not PubSubDataSetMessageType.KeyFrame)
                {
                    throw new NotSupportedException(
                        "The first Arrow PubSub adapter supports key frames and keep-alive schema batches only.");
                }
            }
        }

        private static void AddHeaderColumns(
            Apache.Arrow.Schema.Builder schemaBuilder,
            List<IArrowArray> arrays,
            ArrowNetworkMessage message,
            int rowCount)
        {
            schemaBuilder.Field(new Field("dataSetWriterId", UInt16Type.Default, nullable: false, metadata: null));
            schemaBuilder.Field(new Field("sequenceNumber", UInt32Type.Default, nullable: false, metadata: null));
            schemaBuilder.Field(new Field("status", UInt32Type.Default, nullable: false, metadata: null));
            schemaBuilder.Field(new Field("timestamp", Int64Type.Default, nullable: false, metadata: null));
            schemaBuilder.Field(new Field("messageType", Int32Type.Default, nullable: false, metadata: null));

            var writerId = new UInt16Array.Builder();
            var sequence = new UInt32Array.Builder();
            var status = new UInt32Array.Builder();
            var timestamp = new Int64Array.Builder();
            var messageType = new Int32Array.Builder();
            if (rowCount > 0)
            {
                foreach (PubSubDataSetMessage entry in message.DataSetMessages)
                {
                    var row = (ArrowDataSetMessage)entry;
                    writerId.Append(row.DataSetWriterId);
                    sequence.Append(row.SequenceNumber);
                    status.Append(row.Status.Code);
                    timestamp.Append(row.Timestamp.Value);
                    messageType.Append((int)row.MessageType);
                }
            }
            arrays.Add(writerId.Build(s_allocator));
            arrays.Add(sequence.Build(s_allocator));
            arrays.Add(status.Build(s_allocator));
            arrays.Add(timestamp.Build(s_allocator));
            arrays.Add(messageType.Build(s_allocator));
        }

        private static IArrowArray BuildFieldArray(
            ArrowNetworkMessage message,
            FieldPlan plan,
            int rowCount)
        {
            if (plan.TypeInfo.ValueRank >= 0)
            {
                return BuildListArray(message, plan, rowCount);
            }

            return plan.TypeInfo.BuiltInType switch
            {
                BuiltInType.Boolean => BuildBoolean(message, plan, rowCount),
                BuiltInType.SByte => BuildInt8(message, plan, rowCount),
                BuiltInType.Byte => BuildUInt8(message, plan, rowCount),
                BuiltInType.Int16 => BuildInt16(message, plan, rowCount),
                BuiltInType.UInt16 => BuildUInt16(message, plan, rowCount),
                BuiltInType.Int32 => BuildInt32(message, plan, rowCount),
                BuiltInType.UInt32 => BuildUInt32(message, plan, rowCount),
                BuiltInType.Int64 => BuildInt64(message, plan, rowCount),
                BuiltInType.UInt64 => BuildUInt64(message, plan, rowCount),
                BuiltInType.Float => BuildFloat(message, plan, rowCount),
                BuiltInType.Double => BuildDouble(message, plan, rowCount),
                BuiltInType.String => BuildString(message, plan, rowCount),
                BuiltInType.DateTime => BuildDateTime(message, plan, rowCount),
                BuiltInType.Guid => BuildGuid(message, plan, rowCount),
                BuiltInType.ByteString => BuildBytes(message, plan, rowCount),
                BuiltInType.StatusCode => BuildStatus(message, plan, rowCount),
                _ => throw Unsupported(plan.Name, plan.TypeInfo)
            };
        }

        private static DataSetField? FindField(
            ArrowDataSetMessage message,
            string name,
            DataSetMetaDataType? metaData,
            int index)
        {
            // 1. Explicit DataSet field index is the sparse-safe key: a field carrying its field index
            //    is placed in that column regardless of its position in a sparse message.
            for (int i = 0; i < message.Fields.Count; i++)
            {
                if (message.Fields[i].FieldIndex == index)
                {
                    return message.Fields[i];
                }
            }

            // 2. Otherwise match by the field's own name (also sparse-safe for named fields).
            for (int i = 0; i < message.Fields.Count; i++)
            {
                DataSetField candidate = message.Fields[i];
                string candidateName = string.IsNullOrEmpty(candidate.Name)
                    ? PubSubMessageEncoding.ResolveFieldName(candidate, metaData, i)
                    : candidate.Name;
                if (string.Equals(candidateName, name, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            // 3. Positional fallback only when the message carries the full (dense) field set. For a
            //    sparse message a key that is not present is absent (null:null = missing), so a null
            //    field is returned and the column cell is written null.
            int total = metaData is not null && metaData.Fields.Count > 0
                ? metaData.Fields.Count
                : message.Fields.Count;
            if (message.Fields.Count >= total && index < message.Fields.Count)
            {
                return message.Fields[index];
            }

            return null;
        }

        private static Variant FieldValue(ArrowNetworkMessage message, FieldPlan plan, int row)
        {
            ArrowDataSetMessage dataSetMessage = (ArrowDataSetMessage)message.DataSetMessages[row];
            DataSetField? field = FindField(dataSetMessage, plan.Name, message.MetaData, plan.Index);
            return field?.Value ?? Variant.Null;
        }

        private static IArrowType ToArrowType(string fieldName, TypeInfo typeInfo)
        {
            IArrowType scalar = typeInfo.BuiltInType switch
            {
                BuiltInType.Boolean => BooleanType.Default,
                BuiltInType.SByte => Int8Type.Default,
                BuiltInType.Byte => UInt8Type.Default,
                BuiltInType.Int16 => Int16Type.Default,
                BuiltInType.UInt16 => UInt16Type.Default,
                BuiltInType.Int32 => Int32Type.Default,
                BuiltInType.UInt32 => UInt32Type.Default,
                BuiltInType.Int64 => Int64Type.Default,
                BuiltInType.UInt64 => UInt64Type.Default,
                BuiltInType.Float => FloatType.Default,
                BuiltInType.Double => DoubleType.Default,
                BuiltInType.String => StringType.Default,
                BuiltInType.DateTime => Int64Type.Default,
                BuiltInType.Guid => new FixedSizeBinaryType(16),
                BuiltInType.ByteString => BinaryType.Default,
                BuiltInType.StatusCode => UInt32Type.Default,
                _ => throw Unsupported(fieldName, typeInfo)
            };
            return typeInfo.ValueRank >= 0
                ? new ListType(new Field("item", scalar, nullable: true, metadata: null))
                : scalar;
        }

        private static NotSupportedException Unsupported(string fieldName, TypeInfo typeInfo)
        {
            return new NotSupportedException(
                $"Arrow RawData field '{fieldName}' with type {typeInfo} is not supported by this adapter.");
        }

        private static BooleanArray BuildBoolean(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new BooleanArray.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetBoolean());
                }
            });
            return builder.Build(s_allocator);
        }

        private static Int8Array BuildInt8(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new Int8Array.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetSByte());
                }
            });
            return builder.Build(s_allocator);
        }

        private static UInt8Array BuildUInt8(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new UInt8Array.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetByte());
                }
            });
            return builder.Build(s_allocator);
        }

        private static Int16Array BuildInt16(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new Int16Array.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetInt16());
                }
            });
            return builder.Build(s_allocator);
        }

        private static UInt16Array BuildUInt16(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new UInt16Array.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetUInt16());
                }
            });
            return builder.Build(s_allocator);
        }

        private static Int32Array BuildInt32(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new Int32Array.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetInt32());
                }
            });
            return builder.Build(s_allocator);
        }

        private static UInt32Array BuildUInt32(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new UInt32Array.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetUInt32());
                }
            });
            return builder.Build(s_allocator);
        }

        private static Int64Array BuildInt64(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new Int64Array.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetInt64());
                }
            });
            return builder.Build(s_allocator);
        }

        private static UInt64Array BuildUInt64(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new UInt64Array.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetUInt64());
                }
            });
            return builder.Build(s_allocator);
        }

        private static FloatArray BuildFloat(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new FloatArray.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetFloat());
                }
            });
            return builder.Build(s_allocator);
        }

        private static DoubleArray BuildDouble(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new DoubleArray.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetDouble());
                }
            });
            return builder.Build(s_allocator);
        }

        private static StringArray BuildString(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new StringArray.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetString());
                }
            });
            return builder.Build(s_allocator);
        }

        private static Int64Array BuildDateTime(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new Int64Array.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetDateTime().Value);
                }
            });
            return builder.Build(s_allocator);
        }

        private static BinaryArray BuildBytes(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new BinaryArray.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull || v.GetByteString().IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetByteString().Span);
                }
            });
            return builder.Build(s_allocator);
        }

        private static UInt32Array BuildStatus(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            var builder = new UInt32Array.Builder();
            ForRows(message, plan, rowCount, v =>
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetStatusCode().Code);
                }
            });
            return builder.Build(s_allocator);
        }

        private static FixedSizeBinaryArray BuildGuid(
            ArrowNetworkMessage message,
            FieldPlan plan,
            int rowCount)
        {
            var bytes = new List<byte>(rowCount * 16);
            var validity = new ArrowBuffer.BitmapBuilder(rowCount);
            ForRows(message, plan, rowCount, value =>
            {
                bool valid = !value.IsNull;
                validity.Append(valid);
                bytes.AddRange(valid ? value.GetGuid().ToByteArray() : new byte[16]);
            });
            return BuildFixedSizeBinaryArray(rowCount, validity, bytes.ToArray());
        }

        private static ListArray BuildListArray(ArrowNetworkMessage message, FieldPlan plan, int rowCount)
        {
            return plan.TypeInfo.BuiltInType switch
            {
                BuiltInType.Boolean => BuildList(message, plan, rowCount, BooleanType.Default, AppendBoolArray),
                BuiltInType.SByte => BuildList(message, plan, rowCount, Int8Type.Default, AppendInt8Array),
                BuiltInType.Byte => BuildList(message, plan, rowCount, UInt8Type.Default, AppendUInt8Array),
                BuiltInType.Int16 => BuildList(message, plan, rowCount, Int16Type.Default, AppendInt16Array),
                BuiltInType.UInt16 => BuildList(message, plan, rowCount, UInt16Type.Default, AppendUInt16Array),
                BuiltInType.Int32 => BuildList(message, plan, rowCount, Int32Type.Default, AppendInt32Array),
                BuiltInType.UInt32 => BuildList(message, plan, rowCount, UInt32Type.Default, AppendUInt32Array),
                BuiltInType.Int64 => BuildList(message, plan, rowCount, Int64Type.Default, AppendInt64Array),
                BuiltInType.UInt64 => BuildList(message, plan, rowCount, UInt64Type.Default, AppendUInt64Array),
                BuiltInType.Float => BuildList(message, plan, rowCount, FloatType.Default, AppendFloatArray),
                BuiltInType.Double => BuildList(message, plan, rowCount, DoubleType.Default, AppendDoubleArray),
                BuiltInType.String => BuildList(message, plan, rowCount, StringType.Default, AppendStringArray),
                BuiltInType.DateTime => BuildList(message, plan, rowCount, Int64Type.Default, AppendDateTimeArray),
                BuiltInType.Guid => BuildList(message, plan, rowCount, new FixedSizeBinaryType(16), AppendGuidArray),
                BuiltInType.ByteString => BuildList(message, plan, rowCount, BinaryType.Default, AppendByteStringArray),
                BuiltInType.StatusCode => BuildList(message, plan, rowCount, UInt32Type.Default, AppendStatusArray),
                _ => throw Unsupported(plan.Name, plan.TypeInfo)
            };
        }

        private static ListArray BuildList(
            ArrowNetworkMessage message,
            FieldPlan plan,
            int rowCount,
            IArrowType itemType,
            Func<List<Variant>, IArrowArray> buildChild)
        {
            var offsets = new int[rowCount + 1];
            var validity = new ArrowBuffer.BitmapBuilder(rowCount);
            var items = new List<Variant>();
            for (int row = 0; row < rowCount; row++)
            {
                Variant value = FieldValue(message, plan, row);
                if (value.IsNull)
                {
                    validity.Append(false);
                }
                else
                {
                    validity.Append(true);
                    AppendArrayElements(value, plan.TypeInfo.BuiltInType, items);
                }
                offsets[row + 1] = items.Count;
            }
            IArrowArray child = buildChild(items);
            var listType = new ListType(new Field("item", itemType, nullable: true, metadata: null));
            return new ListArray(
                listType,
                rowCount,
                BuildBuffer(offsets),
                child,
                validity.Build(s_allocator),
                rowCount - validity.SetBitCount,
                0);
        }

        private static void ForRows(
            ArrowNetworkMessage message,
            FieldPlan plan,
            int rowCount,
            Action<Variant> action)
        {
            for (int row = 0; row < rowCount; row++)
            {
                action(FieldValue(message, plan, row));
            }
        }

        private static ArrowBuffer BuildBuffer<T>(params T[] values) where T : struct
        {
            var builder = new ArrowBuffer.Builder<T>(values.Length);
            builder.Append(values.AsSpan());
            return builder.Build(s_allocator);
        }

        private static FixedSizeBinaryArray BuildFixedSizeBinaryArray(
            int length,
            ArrowBuffer.BitmapBuilder validity,
            byte[] bytes)
        {
    // Justification: ArrayData ownership is transferred to the returned FixedSizeBinaryArray.
    #pragma warning disable CA2000
            return new FixedSizeBinaryArray(new ArrayData(
                new FixedSizeBinaryType(16),
                length,
                length - validity.SetBitCount,
                0,
                [validity.Build(s_allocator), BuildBuffer(bytes)],
                []));
    #pragma warning restore CA2000
        }

        private static void AppendArrayElements(Variant value, BuiltInType type, List<Variant> items)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    foreach (bool v in value.GetBooleanArray().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                case BuiltInType.SByte:
                    foreach (sbyte v in value.GetSByteArray().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                case BuiltInType.Byte:
                    foreach (byte v in value.GetByteArray().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                case BuiltInType.Int16:
                    foreach (short v in value.GetInt16Array().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                case BuiltInType.UInt16:
                    foreach (ushort v in value.GetUInt16Array().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                case BuiltInType.Int32:
                    foreach (int v in value.GetInt32Array().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                case BuiltInType.UInt32:
                    foreach (uint v in value.GetUInt32Array().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                case BuiltInType.Int64:
                    foreach (long v in value.GetInt64Array().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                case BuiltInType.UInt64:
                    foreach (ulong v in value.GetUInt64Array().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                case BuiltInType.Float:
                    foreach (float v in value.GetFloatArray().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                case BuiltInType.Double:
                    foreach (double v in value.GetDoubleArray().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                case BuiltInType.String:
                    foreach (string? v in value.GetStringArray().Span)
                    {
                        items.Add(v is null ? Variant.Null : new Variant(v));
                    }
                    break;
                case BuiltInType.DateTime:
                    foreach (DateTimeUtc v in value.GetDateTimeArray().Span)
                    {
                        items.Add(v.IsNull ? Variant.Null : new Variant(v));
                    }
                    break;
                case BuiltInType.Guid:
                    foreach (Uuid v in value.GetGuidArray().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                case BuiltInType.ByteString:
                    foreach (ByteString v in value.GetByteStringArray().Span)
                    {
                        items.Add(v.IsNull ? Variant.Null : new Variant(v));
                    }
                    break;
                case BuiltInType.StatusCode:
                    foreach (StatusCode v in value.GetStatusCodeArray().Span)
                    {
                        items.Add(new Variant(v));
                    }
                    break;
                default:
                    throw new NotSupportedException($"Arrow list field element type {type} is not supported.");
            }
        }

        private static BooleanArray AppendBoolArray(List<Variant> values)
        {
            var builder = new BooleanArray.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetBoolean());
                }
            }
            return builder.Build(s_allocator);
        }

        private static Int8Array AppendInt8Array(List<Variant> values)
        {
            var builder = new Int8Array.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetSByte());
                }
            }
            return builder.Build(s_allocator);
        }

        private static UInt8Array AppendUInt8Array(List<Variant> values)
        {
            var builder = new UInt8Array.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetByte());
                }
            }
            return builder.Build(s_allocator);
        }

        private static Int16Array AppendInt16Array(List<Variant> values)
        {
            var builder = new Int16Array.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetInt16());
                }
            }
            return builder.Build(s_allocator);
        }

        private static UInt16Array AppendUInt16Array(List<Variant> values)
        {
            var builder = new UInt16Array.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetUInt16());
                }
            }
            return builder.Build(s_allocator);
        }

        private static Int32Array AppendInt32Array(List<Variant> values)
        {
            var builder = new Int32Array.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetInt32());
                }
            }
            return builder.Build(s_allocator);
        }

        private static UInt32Array AppendUInt32Array(List<Variant> values)
        {
            var builder = new UInt32Array.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetUInt32());
                }
            }
            return builder.Build(s_allocator);
        }

        private static Int64Array AppendInt64Array(List<Variant> values)
        {
            var builder = new Int64Array.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetInt64());
                }
            }
            return builder.Build(s_allocator);
        }

        private static UInt64Array AppendUInt64Array(List<Variant> values)
        {
            var builder = new UInt64Array.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetUInt64());
                }
            }
            return builder.Build(s_allocator);
        }

        private static FloatArray AppendFloatArray(List<Variant> values)
        {
            var builder = new FloatArray.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetFloat());
                }
            }
            return builder.Build(s_allocator);
        }

        private static DoubleArray AppendDoubleArray(List<Variant> values)
        {
            var builder = new DoubleArray.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetDouble());
                }
            }
            return builder.Build(s_allocator);
        }

        private static StringArray AppendStringArray(List<Variant> values)
        {
            var builder = new StringArray.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetString());
                }
            }
            return builder.Build(s_allocator);
        }

        private static Int64Array AppendDateTimeArray(List<Variant> values)
        {
            var builder = new Int64Array.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetDateTime().Value);
                }
            }
            return builder.Build(s_allocator);
        }

        private static BinaryArray AppendByteStringArray(List<Variant> values)
        {
            var builder = new BinaryArray.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull || v.GetByteString().IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetByteString().Span);
                }
            }
            return builder.Build(s_allocator);
        }

        private static UInt32Array AppendStatusArray(List<Variant> values)
        {
            var builder = new UInt32Array.Builder();
            foreach (Variant v in values)
            {
                if (v.IsNull)
                {
                    builder.AppendNull();
                }
                else
                {
                    builder.Append(v.GetStatusCode().Code);
                }
            }
            return builder.Build(s_allocator);
        }

        private static FixedSizeBinaryArray AppendGuidArray(List<Variant> values)
        {
            var bytes = new List<byte>(values.Count * 16);
            var validity = new ArrowBuffer.BitmapBuilder(values.Count);
            foreach (Variant v in values)
            {
                bool valid = !v.IsNull;
                validity.Append(valid);
                bytes.AddRange(valid ? v.GetGuid().ToByteArray() : new byte[16]);
            }
            return BuildFixedSizeBinaryArray(values.Count, validity, bytes.ToArray());
        }

        private readonly record struct FieldPlan(string Name, int Index, TypeInfo TypeInfo, IArrowType ArrowType);
    }
}
#endif
