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
using System.Text.Json;

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// Internal helpers translating a <see cref="JsonEncodingMode"/>
    /// into the Stack-level <see cref="JsonEncoderOptions"/> that the
    /// Stack <see cref="Opc.Ua.JsonEncoder"/> consumes, plus a tiny
    /// utility that takes an <see cref="Opc.Ua.JsonEncoder"/>
    /// invocation that wrote one named property and splices the
    /// property's value (verbatim JSON) into a destination
    /// <see cref="Utf8JsonWriter"/>.
    /// </summary>
    /// <remarks>
    /// Implements the Part 6 §5.4.1 mode selector mapped through
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5">
    /// Part 14 §7.2.5</see>. The splice helper is required because the
    /// Stack <see cref="Opc.Ua.JsonEncoder"/> always wraps its output
    /// in an outer object; embedding a Variant or DataValue inside the
    /// PubSub envelope therefore requires an intermediate buffer.
    /// </remarks>
    internal static class JsonVariantEncoder
    {
        private const string SpliceFieldName = "v";

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
        /// the Part 6 §5.4.1 <c>{ "Type", "Body" }</c> envelope.
        /// </summary>
        /// <param name="mode">Selected mode.</param>
        /// <returns>True for Verbose, false for Compact / RawData.</returns>
        public static bool WrapsInVariantEnvelope(JsonEncodingMode mode)
        {
            return mode is JsonEncodingMode.Verbose;
        }

        /// <summary>
        /// Encodes a single <see cref="Variant"/> as a named property
        /// of the destination writer. <see cref="JsonEncodingMode.Verbose"/>
        /// emits the Part 6 §5.4.1 <c>{ "Type", "Body" }</c> envelope;
        /// <see cref="JsonEncodingMode.Compact"/> and
        /// <see cref="JsonEncodingMode.RawData"/> emit the bare value.
        /// </summary>
        /// <param name="destination">Target writer (must currently be
        /// inside an object scope).</param>
        /// <param name="propertyName">Property name to emit.</param>
        /// <param name="value">Variant payload.</param>
        /// <param name="mode">Selected encoding mode.</param>
        /// <param name="context">Stack message context for encoders.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void WriteVariantProperty(
            Utf8JsonWriter destination,
            string propertyName,
            Variant value,
            JsonEncodingMode mode,
            IServiceMessageContext context)
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
            using (Opc.Ua.JsonEncoder encoder = new(buffer, context, options))
            {
                if (WrapsInVariantEnvelope(mode))
                {
                    encoder.WriteVariant(SpliceFieldName, value);
                }
                else
                {
                    encoder.WriteVariantValue(SpliceFieldName, value);
                }
            }
            SplicePropertyValue(destination, propertyName, buffer.WrittenSpan,
                remapVariantKeys: WrapsInVariantEnvelope(mode));
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
        /// <exception cref="ArgumentNullException"></exception>
        public static void WriteDataValueProperty(
            Utf8JsonWriter destination,
            string propertyName,
            DataValue value,
            JsonEncodingMode mode,
            IServiceMessageContext context)
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
            using (Opc.Ua.JsonEncoder encoder = new(buffer, context, options))
            {
                encoder.WriteDataValue(SpliceFieldName, value);
            }
            SplicePropertyValue(destination, propertyName, buffer.WrittenSpan,
                remapVariantKeys: false);
        }

        /// <summary>
        /// Parses the single-property object encoded into
        /// <paramref name="encoded"/> by the Stack
        /// <see cref="Opc.Ua.JsonEncoder"/> and writes the value of the
        /// (only) property to <paramref name="destination"/> under
        /// <paramref name="propertyName"/>. The intermediate buffer is
        /// always of the form
        /// <c>{ "v": &lt;value&gt; }</c>; this helper reads
        /// <c>v</c> and splices its raw JSON text.
        /// </summary>
        /// <param name="destination">Destination writer.</param>
        /// <param name="propertyName">Output property name.</param>
        /// <param name="encoded">Encoded single-property object bytes.</param>
        /// <param name="remapVariantKeys">
        /// When true, the spliced JSON object is rewritten so the
        /// Stack Verbose Variant keys (<c>UaType</c>/<c>Value</c>)
        /// become the Part 14 §7.2.5 wire keys
        /// (<c>Type</c>/<c>Body</c>).
        /// </param>
        private static void SplicePropertyValue(
            Utf8JsonWriter destination,
            string propertyName,
            ReadOnlySpan<byte> encoded,
            bool remapVariantKeys)
        {
            using var document = JsonDocument.Parse(encoded.ToArray());
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
            if (valueElement.ValueKind == JsonValueKind.Null ||
                valueElement.ValueKind == JsonValueKind.Undefined)
            {
                destination.WriteNullValue();
                return;
            }
            if (remapVariantKeys &&
                valueElement.ValueKind == JsonValueKind.Object)
            {
                WriteRemappedVariant(destination, valueElement);
                return;
            }
            destination.WriteRawValue(valueElement.GetRawText(), skipInputValidation: true);
        }

        /// <summary>
        /// Writes <paramref name="variant"/> to
        /// <paramref name="destination"/> after rewriting the Stack
        /// Verbose Variant key names so the wire matches Part 14
        /// §7.2.5 (<c>Type</c>, <c>Body</c>, <c>Dimensions</c>).
        /// </summary>
        /// <param name="destination">Destination writer.</param>
        /// <param name="variant">Source variant object.</param>
        private static void WriteRemappedVariant(
            Utf8JsonWriter destination,
            JsonElement variant)
        {
            destination.WriteStartObject();
            foreach (JsonProperty member in variant.EnumerateObject())
            {
                string mapped = member.Name switch
                {
                    "UaType" => "Type",
                    "Value" => "Body",
                    "Dimensions" => "Dimensions",
                    _ => member.Name
                };
                destination.WritePropertyName(mapped);
                destination.WriteRawValue(
                    member.Value.GetRawText(),
                    skipInputValidation: true);
            }
            destination.WriteEndObject();
        }
    }
}
