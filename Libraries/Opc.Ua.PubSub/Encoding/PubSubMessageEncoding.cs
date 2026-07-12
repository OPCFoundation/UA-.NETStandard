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
using Opc.Ua.PubSub.MetaData;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Resolves PubSub metadata and converts field payloads shared by the Avro and Arrow encoders.
    /// </summary>
    internal static class PubSubMessageEncoding
    {
        /// <summary>
        /// Finds the metadata that describes a DataSetMessage in the message envelope or registry.
        /// </summary>
        /// <param name="envelope">The network message that carries the DataSetMessage.</param>
        /// <param name="dataSetMessage">The DataSetMessage whose writer id and version select metadata.</param>
        /// <param name="context">The encode or decode context containing the metadata registry.</param>
        /// <param name="dataSetClassId">The DataSetClassId advertised by the experimental envelope.</param>
        /// <returns>The matching metadata, or <see langword="null"/> when no compatible entry exists.</returns>
        public static DataSetMetaDataType? ResolveMetaData(
            PubSubNetworkMessage envelope,
            PubSubDataSetMessage dataSetMessage,
            PubSubNetworkMessageContext context,
            Uuid dataSetClassId)
        {
            if (envelope.MetaData is not null)
            {
                return envelope.MetaData;
            }

            DataSetMetaDataKey key = new(
                envelope.PublisherId,
                envelope.WriterGroupId ?? 0,
                dataSetMessage.DataSetWriterId,
                dataSetClassId,
                dataSetMessage.MetaDataVersion.MajorVersion);
            MetaDataMatchResult match = context.MetaDataRegistry.TryGet(
                in key,
                out DataSetMetaDataType? metaData);
            return match is MetaDataMatchResult.Match or MetaDataMatchResult.MinorVersionMismatch
                ? metaData
                : null;
        }

        /// <summary>
        /// Chooses the on-wire field name from the field instance, metadata, or a deterministic fallback.
        /// </summary>
        /// <param name="field">The field being encoded.</param>
        /// <param name="metaData">Optional DataSet metadata that can provide a field name.</param>
        /// <param name="index">The zero-based field position used by the fallback name.</param>
        /// <returns>The name that should be written for the field.</returns>
        public static string ResolveFieldName(
            DataSetField field,
            DataSetMetaDataType? metaData,
            int index)
        {
            if (!string.IsNullOrEmpty(field.Name))
            {
                return field.Name;
            }
            if (metaData is not null
                && metaData.Fields.Count > index
                && metaData.Fields[index].Name is { Length: > 0 } name)
            {
                return name;
            }
            return FormattableString.Invariant($"Field{index}");
        }

        /// <summary>
        /// Locates metadata for a field by exact name and then by ordinal position.
        /// </summary>
        /// <param name="metaData">The metadata containing DataSet field declarations.</param>
        /// <param name="name">The field name read from or written to the experimental payload.</param>
        /// <param name="index">The zero-based field position used as a fallback lookup.</param>
        /// <returns>The matching field metadata, or <see langword="null"/> when no entry matches.</returns>
        public static FieldMetaData? ResolveFieldMetaData(
            DataSetMetaDataType? metaData,
            string name,
            int index)
        {
            if (metaData is null || metaData.Fields.Count == 0)
            {
                return null;
            }
            for (int i = 0; i < metaData.Fields.Count; i++)
            {
                FieldMetaData candidate = metaData.Fields[i];
                if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            return index >= 0 && index < metaData.Fields.Count ? metaData.Fields[index] : null;
        }

        /// <summary>
        /// Creates an OPC UA type descriptor for a field from DataSet metadata.
        /// </summary>
        /// <param name="metaData">The metadata containing DataSet field declarations.</param>
        /// <param name="name">The field name read from or written to the experimental payload.</param>
        /// <param name="index">The zero-based field position used as a fallback lookup.</param>
        /// <returns>The field type descriptor, or <see langword="null"/> when metadata is unavailable.</returns>
        public static TypeInfo? ResolveFieldType(
            DataSetMetaDataType? metaData,
            string name,
            int index)
        {
            FieldMetaData? field = ResolveFieldMetaData(metaData, name, index);
            return field is null
                ? null
                : TypeInfo.Create((BuiltInType)field.BuiltInType, field.ValueRank);
        }

        /// <summary>
        /// Projects a PubSub field into a DataValue while honoring the selected field-content mask.
        /// </summary>
        /// <param name="field">The PubSub field containing the value and optional DataValue members.</param>
        /// <param name="mask">The field-content bits that select which DataValue members are encoded.</param>
        /// <returns>The DataValue representation written to the experimental payload.</returns>
        public static DataValue BuildDataValue(DataSetField field, DataSetFieldContentMask mask)
        {
            if (mask == DataSetFieldContentMask.None)
            {
                return new DataValue(
                    field.Value,
                    field.StatusCode,
                    field.SourceTimestamp,
                    field.ServerTimestamp,
                    field.SourcePicoSeconds,
                    field.ServerPicoSeconds);
            }

            StatusCode statusCode = (mask & DataSetFieldContentMask.StatusCode) != 0
                ? field.StatusCode : default;
            DateTimeUtc sourceTimestamp = (mask & DataSetFieldContentMask.SourceTimestamp) != 0
                ? field.SourceTimestamp : default;
            ushort sourcePicoSeconds = (mask & DataSetFieldContentMask.SourcePicoSeconds) != 0
                ? field.SourcePicoSeconds : (ushort)0;
            DateTimeUtc serverTimestamp = (mask & DataSetFieldContentMask.ServerTimestamp) != 0
                ? field.ServerTimestamp : default;
            ushort serverPicoSeconds = (mask & DataSetFieldContentMask.ServerPicoSeconds) != 0
                ? field.ServerPicoSeconds : (ushort)0;
            return new DataValue(
                field.Value,
                statusCode,
                sourceTimestamp,
                serverTimestamp,
                sourcePicoSeconds,
                serverPicoSeconds);
        }

        /// <summary>
        /// Reconstructs a PubSub field from a decoded DataValue and the field-content mask.
        /// </summary>
        /// <param name="name">The field name decoded from the payload.</param>
        /// <param name="index">The field index decoded from the payload.</param>
        /// <param name="value">The decoded DataValue.</param>
        /// <param name="mask">The field-content bits that identify which DataValue members were encoded.</param>
        /// <returns>A DataSetField using DataValue field encoding semantics.</returns>
        public static DataSetField FromDataValue(
            string name,
            int index,
            DataValue value,
            DataSetFieldContentMask mask)
        {
            return new DataSetField
            {
                Name = name,
                FieldIndex = index,
                Value = value.WrappedValue,
                StatusCode = mask == DataSetFieldContentMask.None
                    || (mask & DataSetFieldContentMask.StatusCode) != 0
                        ? value.StatusCode
                        : (StatusCode)StatusCodes.Good,
                SourceTimestamp = mask == DataSetFieldContentMask.None
                    || (mask & DataSetFieldContentMask.SourceTimestamp) != 0
                        ? value.SourceTimestamp
                        : default,
                SourcePicoSeconds = mask == DataSetFieldContentMask.None
                    || (mask & DataSetFieldContentMask.SourcePicoSeconds) != 0
                        ? value.SourcePicoseconds
                        : (ushort)0,
                ServerTimestamp = mask == DataSetFieldContentMask.None
                    || (mask & DataSetFieldContentMask.ServerTimestamp) != 0
                        ? value.ServerTimestamp
                        : default,
                ServerPicoSeconds = mask == DataSetFieldContentMask.None
                    || (mask & DataSetFieldContentMask.ServerPicoSeconds) != 0
                        ? value.ServerPicoseconds
                        : (ushort)0,
                Encoding = PubSubFieldEncoding.DataValue
            };
        }
    }
}
