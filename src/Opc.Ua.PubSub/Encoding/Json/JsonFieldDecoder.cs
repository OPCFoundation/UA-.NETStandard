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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// Inverse of <see cref="JsonFieldEncoder"/>: walks the
    /// <c>Payload</c> object of a JSON DataSetMessage and yields a
    /// <see cref="DataSetField"/> sequence by resolving each member's
    /// type from the optionally supplied
    /// <see cref="DataSetMetaDataType"/>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.4">
    /// Part 14 §7.2.5.4</see>. Compact and RawData payloads (per
    /// Part 6 §5.4.1) do not carry per-value type information and
    /// therefore require metadata to round-trip; when metadata is
    /// absent the decoder yields <see cref="Variant.Null"/> entries so
    /// the caller can decide whether to reject the message or surface
    /// the structural skeleton.
    /// </remarks>
    public static class JsonFieldDecoder
    {
        private static readonly ConditionalWeakTable<DataSetMetaDataType, FieldIndex> s_fieldIndexes = new();

        /// <summary>
        /// Decodes the <c>Payload</c> object into a list of
        /// <see cref="DataSetField"/> values.
        /// </summary>
        /// <param name="payload">Payload JSON object.</param>
        /// <param name="metaData">Optional metadata used to resolve
        /// field types for Compact / RawData payloads.</param>
        /// <param name="detectedMode">Detected encoding mode.</param>
        /// <param name="context">Stack message context.</param>
        /// <returns>Ordered list of decoded fields.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static ArrayOf<DataSetField> DecodeFields(
            JsonElement payload,
            DataSetMetaDataType? metaData,
            JsonEncodingMode detectedMode,
            IServiceMessageContext context)
        {
            // The public contract still surfaces malformed payloads to the caller.
            _ = TryDecodeFields(
                payload, metaData, detectedMode, context, tolerant: false, out ArrayOf<DataSetField> fields);
            return fields;
        }

        internal static bool TryDecodeFields(
            JsonElement payload,
            DataSetMetaDataType? metaData,
            JsonEncodingMode detectedMode,
            IServiceMessageContext context,
            out ArrayOf<DataSetField> fields)
        {
            return TryDecodeFields(payload, metaData, detectedMode, context, tolerant: true, out fields);
        }

        private static bool TryDecodeFields(
            JsonElement payload,
            DataSetMetaDataType? metaData,
            JsonEncodingMode detectedMode,
            IServiceMessageContext context,
            bool tolerant,
            out ArrayOf<DataSetField> fields)
        {
            fields = [];
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (payload.ValueKind is not JsonValueKind.Object)
            {
                return true;
            }
            int memberCount;
            try
            {
                memberCount = JsonDecoder.CheckArrayLength(payload, context);
            }
            catch (ServiceResultException) when (tolerant)
            {
                return false;
            }
            // A Publisher may append fields with only a MinorVersion change
            // (Part 14 §6.2.3.2.6 Table 11), so the Payload can carry more
            // members than the metadata describes; those decode without
            // FieldMetaData. The member count is bounded by MaxArrayLength.
            FieldIndex? fieldIndex = null;
            if (metaData is not null && metaData.Fields.Count > 0)
            {
                fieldIndex = GetFieldIndex(metaData);
            }
            var decodedFields = new List<DataSetField>(memberCount);
            int index = 0;
            foreach (JsonProperty property in payload.EnumerateObject())
            {
                FieldMetaData? fmd = fieldIndex?.Resolve(property.Name, index);
                DataSetField? field = DecodeOne(property, fmd, detectedMode, context, tolerant);
                if (field is null)
                {
                    return false;
                }
                decodedFields.Add(field);
                index++;
            }
            fields = decodedFields;
            return true;
        }

        /// <summary>
        /// Decodes a single JSON property into a
        /// <see cref="DataSetField"/>.
        /// </summary>
        /// <param name="property">Source JSON property.</param>
        /// <param name="metaData">Optional matching field metadata.</param>
        /// <param name="detectedMode">Detected encoding mode.</param>
        /// <param name="context">Stack message context.</param>
        /// <param name="tolerant">Whether malformed values reject instead of throwing.</param>
        /// <returns>Decoded field.</returns>
        private static DataSetField? DecodeOne(
            JsonProperty property,
            FieldMetaData? metaData,
            JsonEncodingMode detectedMode,
            IServiceMessageContext context,
            bool tolerant)
        {
            JsonElement value = property.Value;
            if (LooksLikeDataValue(value))
            {
                if (!TryDecodeDataValue(value, context, tolerant, out DataValue dv))
                {
                    return null;
                }
                return new DataSetField
                {
                    Name = property.Name,
                    Value = dv.WrappedValue,
                    StatusCode = dv.StatusCode,
                    SourceTimestamp = dv.SourceTimestamp,
                    SourcePicoSeconds = dv.SourcePicoseconds,
                    ServerTimestamp = dv.ServerTimestamp,
                    ServerPicoSeconds = dv.ServerPicoseconds,
                    Encoding = PubSubFieldEncoding.DataValue
                };
            }
            TypeInfo? typeInfo = metaData is null
                ? null
                : TypeInfo.Create(
                    (BuiltInType)metaData.BuiltInType,
                    metaData.ValueRank);
            PubSubFieldEncoding encoding = JsonVariantEncoder.WrapsInVariantEnvelope(detectedMode)
                ? PubSubFieldEncoding.Variant
                : PubSubFieldEncoding.RawData;
            Variant variant;
            if (!TryDecodeVariant(value, detectedMode, typeInfo, context, tolerant, out variant))
            {
                return null;
            }
            return new DataSetField
            {
                Name = property.Name,
                Value = variant,
                Encoding = encoding
            };
        }

        private static bool TryDecodeVariant(
            JsonElement value,
            JsonEncodingMode detectedMode,
            TypeInfo? typeInfo,
            IServiceMessageContext context,
            bool tolerant,
            out Variant variant)
        {
            try
            {
                variant = JsonVariantDecoder.DecodeVariant(
                    value,
                    detectedMode,
                    typeInfo,
                    context);
                return true;
            }
            catch (ServiceResultException) when (tolerant)
            {
                variant = Variant.Null;
                return false;
            }
            catch (JsonException) when (tolerant)
            {
                variant = Variant.Null;
                return false;
            }
        }

        private static bool TryDecodeDataValue(
            JsonElement value,
            IServiceMessageContext context,
            bool tolerant,
            out DataValue dataValue)
        {
            try
            {
                dataValue = JsonVariantDecoder.DecodeDataValue(value, context);
                return true;
            }
            catch (ServiceResultException) when (tolerant)
            {
                dataValue = DataValue.Null;
                return false;
            }
            catch (JsonException) when (tolerant)
            {
                dataValue = DataValue.Null;
                return false;
            }
        }

        /// <summary>
        /// Returns the cached name index of the metadata fields, building
        /// it once per <see cref="DataSetMetaDataType"/> (and again only
        /// when its <see cref="DataSetMetaDataType.Fields"/> array is
        /// replaced) so each payload member resolves in O(1).
        /// </summary>
        /// <param name="metaData">Metadata with at least one field.</param>
        /// <returns>The field index.</returns>
        private static FieldIndex GetFieldIndex(DataSetMetaDataType metaData)
        {
            ReadOnlyMemory<FieldMetaData> fields = metaData.Fields.Memory;
            if (s_fieldIndexes.TryGetValue(metaData, out FieldIndex? cached) &&
                cached.Fields.Equals(fields))
            {
                return cached;
            }
            var index = new FieldIndex(fields);
            lock (s_fieldIndexes)
            {
                s_fieldIndexes.Remove(metaData);
                s_fieldIndexes.Add(metaData, index);
            }
            return index;
        }

        /// <summary>
        /// Name to <see cref="FieldMetaData"/> lookup for one metadata
        /// field array.
        /// </summary>
        private sealed class FieldIndex
        {
            public FieldIndex(ReadOnlyMemory<FieldMetaData> fields)
            {
                Fields = fields;
                m_byName = new Dictionary<string, FieldMetaData>(
                    fields.Length,
                    StringComparer.Ordinal);
                ReadOnlySpan<FieldMetaData> span = fields.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    // The first field wins for duplicate names.
                    string? name = span[i]?.Name;
                    if (name is not null && !m_byName.ContainsKey(name))
                    {
                        m_byName.Add(name, span[i]);
                    }
                }
            }

            /// <summary>
            /// The field array the index was built from.
            /// </summary>
            public ReadOnlyMemory<FieldMetaData> Fields { get; }

            /// <summary>
            /// Locates the metadata entry that matches the supplied
            /// field name or, failing that, the entry at the same
            /// ordinal.
            /// </summary>
            /// <param name="name">Field name from the payload.</param>
            /// <param name="index">Ordinal in the payload.</param>
            /// <returns>Matching field metadata, or
            /// <see langword="null"/>.</returns>
            public FieldMetaData? Resolve(string name, int index)
            {
                if (m_byName.TryGetValue(name, out FieldMetaData? fmd))
                {
                    return fmd;
                }
                return index < Fields.Length ? Fields.Span[index] : null;
            }

            private readonly Dictionary<string, FieldMetaData> m_byName;
        }

        /// <summary>
        /// Heuristic detection of the Part 6 JSON
        /// <c>DataValue</c> envelope shape
        /// (object containing a <c>Value</c> property plus optional
        /// <c>StatusCode</c> / <c>SourceTimestamp</c> /
        /// <c>ServerTimestamp</c> properties).
        /// </summary>
        /// <param name="value">Candidate element.</param>
        /// <returns><see langword="true"/> when the value looks like a
        /// DataValue envelope.</returns>
        private static bool LooksLikeDataValue(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            if (!value.TryGetProperty("Value", out _))
            {
                return false;
            }
            bool hasTypeEnvelope = false;
            bool hasDataValueMetadata = false;
            foreach (JsonProperty member in value.EnumerateObject())
            {
                switch (member.Name)
                {
                    case "Value":
                        continue;
                    case "UaType":
                    case "Dimensions":
                        hasTypeEnvelope = true;
                        continue;
                    case "Status":
                    case "StatusCode":
                    case "SourceTimestamp":
                    case "SourcePicoseconds":
                    case "ServerTimestamp":
                    case "ServerPicoseconds":
                        hasDataValueMetadata = true;
                        continue;
                    default:
                        return false;
                }
            }
            // Part 6 flattens typed DataValues; a plain typed Variant has no quality/timestamp members.
            return hasDataValueMetadata || !hasTypeEnvelope;
        }
    }
}
