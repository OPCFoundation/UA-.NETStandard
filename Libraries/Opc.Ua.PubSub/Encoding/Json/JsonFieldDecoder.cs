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
        public static IReadOnlyList<DataSetField> DecodeFields(
            JsonElement payload,
            DataSetMetaDataType? metaData,
            JsonEncodingMode detectedMode,
            IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (payload.ValueKind is not JsonValueKind.Object)
            {
                return [];
            }
            var fields = new List<DataSetField>(payload.GetArrayLengthSafe());
            int index = 0;
            foreach (JsonProperty property in payload.EnumerateObject())
            {
                FieldMetaData? fmd = ResolveMetaData(metaData, property.Name, index);
                DataSetField field = DecodeOne(property, fmd, detectedMode, context);
                fields.Add(field);
                index++;
            }
            return fields;
        }

        /// <summary>
        /// Decodes a single JSON property into a
        /// <see cref="DataSetField"/>.
        /// </summary>
        /// <param name="property">Source JSON property.</param>
        /// <param name="metaData">Optional matching field metadata.</param>
        /// <param name="detectedMode">Detected encoding mode.</param>
        /// <param name="context">Stack message context.</param>
        /// <returns>Decoded field.</returns>
        private static DataSetField DecodeOne(
            JsonProperty property,
            FieldMetaData? metaData,
            JsonEncodingMode detectedMode,
            IServiceMessageContext context)
        {
            JsonElement value = property.Value;
            if (LooksLikeDataValue(value))
            {
                DataValue dv = JsonVariantDecoder.DecodeDataValue(value, context);
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
            Variant variant = JsonVariantDecoder.DecodeVariant(
                value,
                detectedMode,
                typeInfo,
                context);
            return new DataSetField
            {
                Name = property.Name,
                Value = variant,
                Encoding = encoding
            };
        }

        /// <summary>
        /// Locates the metadata entry that matches the supplied field
        /// name or, failing that, the entry at the same ordinal.
        /// </summary>
        /// <param name="metaData">Optional metadata.</param>
        /// <param name="name">Field name from the payload.</param>
        /// <param name="index">Ordinal in the payload.</param>
        /// <returns>Matching field metadata, or
        /// <see langword="null"/>.</returns>
        private static FieldMetaData? ResolveMetaData(
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
                FieldMetaData fmd = metaData.Fields[i];
                if (string.Equals(fmd.Name, name, StringComparison.Ordinal))
                {
                    return fmd;
                }
            }
            if (index < metaData.Fields.Count)
            {
                return metaData.Fields[index];
            }
            return null;
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
            foreach (JsonProperty member in value.EnumerateObject())
            {
                switch (member.Name)
                {
                    case "Value":
                    case "Status":
                    case "StatusCode":
                    case "SourceTimestamp":
                    case "SourcePicoseconds":
                    case "ServerTimestamp":
                    case "ServerPicoseconds":
                        continue;
                    default:
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Safe variant of <see cref="JsonElement.GetArrayLength"/>
        /// that returns a default capacity for objects (which do not
        /// have an array length).
        /// </summary>
        /// <param name="element">Element being measured.</param>
        /// <returns>Suggested list pre-allocation capacity.</returns>
        private static int GetArrayLengthSafe(this JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                int count = 0;
                foreach (JsonProperty _ in element.EnumerateObject())
                {
                    count++;
                }
                return count;
            }
            if (element.ValueKind == JsonValueKind.Array)
            {
                return element.GetArrayLength();
            }
            return 0;
        }
    }
}
