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
using System.Text.Json;

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// Internal helpers translating a <see cref="JsonEncodingMode"/>
    /// into the Stack-level <see cref="JsonEncoderOptions"/> that the
    /// Stack <see cref="Ua.JsonEncoder"/> consumes, plus a tiny
    /// utility that takes an <see cref="Ua.JsonEncoder"/>
    /// invocation that wrote one named property and splices the
    /// property's value (verbatim JSON) into a destination
    /// <see cref="Utf8JsonWriter"/>.
    /// </summary>
    /// <remarks>
    /// Implements the Part 6 §5.4.1 mode selector mapped through
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5</see>. The splice helper is required because the
    /// Stack <see cref="Ua.JsonEncoder"/> always wraps its output
    /// in an outer object; embedding a Variant or DataValue inside the
    /// PubSub envelope therefore requires an intermediate buffer.
    /// </remarks>
    internal static class JsonVariantEncoder
    {
        private const string SpliceFieldName = "v";
        private const string CollapsedValueFieldName = "c";

        /// <summary>
        /// Translates a PubSub-level <see cref="JsonEncodingMode"/> to
        /// the matching Stack <see cref="JsonEncoderOptions"/> profile.
        /// </summary>
        /// <param name="mode">Caller-selected encoding mode.</param>
        /// <returns>
        /// One of the static profiles on
        /// <see cref="JsonEncoderOptions"/>.
        /// </returns>
        public static JsonEncoderOptions ToEncoderOptions(JsonEncodingMode mode)
        {
            return mode switch
            {
                JsonEncodingMode.Verbose => JsonEncoderOptions.Verbose,
                JsonEncodingMode.Compact => JsonEncoderOptions.Compact,
                JsonEncodingMode.RawData => JsonEncoderOptions.RawData,
                _ => JsonEncoderOptions.Verbose
            };
        }

        /// <summary>
        /// <see langword="true"/> when the mode wraps every Variant in
        /// the Part 6 §5.4.2.17 <c>{ "UaType", "Value" }</c> envelope.
        /// </summary>
        /// <param name="mode">Selected mode.</param>
        /// <returns>True for Verbose, false for Compact / RawData.</returns>
        public static bool WrapsInVariantEnvelope(JsonEncodingMode mode)
        {
            return mode is JsonEncodingMode.Verbose;
        }

        /// <summary>
        /// <see langword="true"/> when a top-level VerboseEncoding field
        /// described by <paramref name="builtInType"/> and
        /// <paramref name="valueRank"/> has a concrete DataType, so its
        /// Variant is collapsed to the bare value (Part 14 §7.2.5.4.2).
        /// Abstract, structured and multi-dimensional fields keep the
        /// Variant envelope because the bare value could not be decoded
        /// from the FieldMetaData alone.
        /// </summary>
        /// <param name="builtInType">FieldMetaData BuiltInType.</param>
        /// <param name="valueRank">FieldMetaData ValueRank.</param>
        /// <returns>Whether the field is collapsed.</returns>
        public static bool IsCollapsedField(BuiltInType builtInType, int valueRank)
        {
            if (valueRank is not (ValueRanks.Scalar or ValueRanks.OneDimension))
            {
                return false;
            }
            return builtInType is > BuiltInType.Null and < BuiltInType.ExtensionObject;
        }

        /// <summary>
        /// <see langword="true"/> when the field has an Enumeration
        /// DataType: its BuiltInType is Int32 but its DataType is not
        /// Int32. A top-level VerboseEncoding Enumeration field is written
        /// as the verbose Enumeration <c>&lt;name&gt;_&lt;value&gt;</c>
        /// (Part 14 §7.2.5.4.2, Part 6 §5.4.4.2).
        /// </summary>
        /// <param name="metaData">FieldMetaData of the field.</param>
        /// <returns>Whether the field is a scalar or one-dimensional
        /// Enumeration.</returns>
        public static bool IsEnumerationField(FieldMetaData? metaData)
        {
            return metaData is not null &&
                metaData.BuiltInType == (byte)BuiltInType.Int32 &&
                metaData.ValueRank is ValueRanks.Scalar or ValueRanks.OneDimension &&
                !metaData.DataType.IsNull &&
                metaData.DataType != DataTypeIds.Int32;
        }

        /// <summary>
        /// Encodes a single <see cref="Variant"/> as a named property
        /// of the destination writer. <see cref="JsonEncodingMode.Verbose"/>
        /// emits the Part 6 §5.4.2.17 <c>{ "UaType", "Value" }</c>
        /// envelope unless <paramref name="collapsed"/> is set;
        /// <see cref="JsonEncodingMode.Compact"/> and
        /// <see cref="JsonEncodingMode.RawData"/> emit the bare value.
        /// </summary>
        /// <param name="destination">Target writer (must currently be
        /// inside an object scope).</param>
        /// <param name="propertyName">Property name to emit.</param>
        /// <param name="value">Variant payload.</param>
        /// <param name="mode">Selected encoding mode.</param>
        /// <param name="context">Stack message context for encoders.</param>
        /// <param name="collapsed">When <see langword="true"/> the
        /// FieldMetaData supplies the concrete type of the value, so the
        /// Variant envelope is omitted (Part 14 §7.2.5.4.2).</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void WriteVariantProperty(
            Utf8JsonWriter destination,
            string propertyName,
            Variant value,
            JsonEncodingMode mode,
            IServiceMessageContext context,
            bool collapsed = false)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (propertyName is null)
            {
                throw new ArgumentNullException(nameof(propertyName));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (value.IsNull)
            {
                destination.WriteNull(propertyName);
                return;
            }
            JsonEncoderOptions options = ToEncoderOptions(mode);
            using JsonBufferWriter buffer = new(256);
            using (Ua.JsonEncoder encoder = new(buffer, context, options))
            {
                if (WrapsInVariantEnvelope(mode) && !collapsed)
                {
                    encoder.WriteVariant(SpliceFieldName, value);
                }
                else
                {
                    encoder.WriteVariantValue(SpliceFieldName, value);
                }
            }
            SplicePropertyValue(destination, propertyName, buffer.WrittenMemory);
        }

        /// <summary>
        /// Encodes a single <see cref="DataValue"/> as a named property
        /// of the destination writer. DataValue is always emitted using
        /// the Stack DataValue encoder; the network-wide mode selects
        /// the embedded Variant envelope (Verbose wraps; Compact /
        /// RawData emit bare bodies).
        /// </summary>
        /// <param name="destination">Target writer.</param>
        /// <param name="propertyName">Property name to emit.</param>
        /// <param name="value">DataValue payload.</param>
        /// <param name="mode">Selected encoding mode.</param>
        /// <param name="context">Stack message context for encoders.</param>
        /// <param name="collapsedValue">When set, the FieldMetaData
        /// supplies the concrete type of the value, so this bare value is
        /// written as the <c>Value</c> without <c>UaType</c>
        /// (Part 14 §7.2.5.4.3 Table 187).</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void WriteDataValueProperty(
            Utf8JsonWriter destination,
            string propertyName,
            DataValue value,
            JsonEncodingMode mode,
            IServiceMessageContext context,
            Variant? collapsedValue = null)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (propertyName is null)
            {
                throw new ArgumentNullException(nameof(propertyName));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (value.IsNull)
            {
                destination.WriteNull(propertyName);
                return;
            }
            JsonEncoderOptions options = ToEncoderOptions(mode);
            using JsonBufferWriter buffer = new(384);
            using (Ua.JsonEncoder encoder = new(buffer, context, options))
            {
                if (collapsedValue is { } raw)
                {
                    encoder.WriteVariantValue(CollapsedValueFieldName, raw);
                }
                encoder.WriteDataValue(SpliceFieldName, value);
            }
            if (collapsedValue is null)
            {
                SplicePropertyValue(destination, propertyName, buffer.WrittenMemory);
                return;
            }
            using var document = JsonDocument.Parse(buffer.WrittenMemory);
            JsonElement root = document.RootElement;
            destination.WritePropertyName(propertyName);
            destination.WriteStartObject();
            if (root.TryGetProperty(CollapsedValueFieldName, out JsonElement rawValue))
            {
                destination.WritePropertyName("Value");
                rawValue.WriteTo(destination);
            }
            if (root.TryGetProperty(SpliceFieldName, out JsonElement dataValue) &&
                dataValue.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty member in dataValue.EnumerateObject())
                {
                    if (!member.NameEquals("UaType") &&
                        !member.NameEquals("Value") &&
                        !member.NameEquals("Dimensions"))
                    {
                        member.WriteTo(destination);
                    }
                }
            }
            destination.WriteEndObject();
        }

        /// <summary>
        /// Parses the single-property object encoded into
        /// <paramref name="encoded"/> by the Stack
        /// <see cref="Ua.JsonEncoder"/> and writes the value of the
        /// (only) property to <paramref name="destination"/> under
        /// <paramref name="propertyName"/>. The intermediate buffer is
        /// always of the form
        /// <c>{ "v": &lt;value&gt; }</c>; this helper reads
        /// <c>v</c> and splices its raw JSON text.
        /// </summary>
        /// <param name="destination">Destination writer.</param>
        /// <param name="propertyName">Output property name.</param>
        /// <param name="encoded">Encoded single-property object bytes.</param>
        private static void SplicePropertyValue(
            Utf8JsonWriter destination,
            string propertyName,
            ReadOnlyMemory<byte> encoded)
        {
            using var document = JsonDocument.Parse(encoded);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                destination.WriteNull(propertyName);
                return;
            }
            if (!root.TryGetProperty(SpliceFieldName, out JsonElement valueElement))
            {
                destination.WriteNull(propertyName);
                return;
            }
            destination.WritePropertyName(propertyName);
            if (valueElement.ValueKind is JsonValueKind.Null or
                JsonValueKind.Undefined)
            {
                destination.WriteNullValue();
                return;
            }
            valueElement.WriteTo(destination);
        }
    }
}
