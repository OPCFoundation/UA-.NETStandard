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
    /// Internal helpers that translate a single <see cref="JsonElement"/>
    /// (the value of one field in the <c>Payload</c> object of a JSON
    /// DataSetMessage) into a <see cref="Variant"/> or
    /// <see cref="DataValue"/> by delegating to the Stack
    /// <see cref="Opc.Ua.JsonDecoder"/>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.4">
    /// Part 14 §7.2.5.4</see>. The Stack <see cref="Opc.Ua.JsonDecoder"/>
    /// expects the top of its element stack to be a JSON object, so the
    /// helper wraps the supplied element in a synthetic
    /// <c>{ "v": &lt;element&gt; }</c> envelope before reading.
    /// </remarks>
    internal static class JsonVariantDecoder
    {
        private const string SpliceFieldName = "v";

        /// <summary>
        /// Decodes a single Variant payload from the supplied element.
        /// </summary>
        /// <param name="element">JSON element holding the value.</param>
        /// <param name="mode">
        /// Detected encoding mode. <see cref="JsonEncodingMode.Verbose"/>
        /// expects the Part 6 §5.4.1 <c>{ "Type", "Body" }</c> envelope;
        /// <see cref="JsonEncodingMode.Compact"/> and
        /// <see cref="JsonEncodingMode.RawData"/> expect bare values.
        /// </param>
        /// <param name="typeInfo">
        /// Required for Compact / RawData decoding when the metadata
        /// declares the field's type.
        /// </param>
        /// <param name="context">Stack message context.</param>
        /// <returns>Decoded variant.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static Variant DecodeVariant(
            JsonElement element,
            JsonEncodingMode mode,
            TypeInfo? typeInfo,
            IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return Variant.Null;
            }
            bool wrapsEnvelope = JsonVariantEncoder.WrapsInVariantEnvelope(mode);
            string wrapped = wrapsEnvelope
                ? WrapAndRenameVariant(element)
                : WrapAsObject(element);
            using Opc.Ua.JsonDecoder decoder = new(wrapped, context);
            if (wrapsEnvelope)
            {
                return decoder.ReadVariant(SpliceFieldName);
            }
            if (typeInfo is null)
            {
                return Variant.Null;
            }
            return decoder.ReadVariantValue(SpliceFieldName, typeInfo.Value);
        }

        /// <summary>
        /// Decodes a single DataValue payload from the supplied element.
        /// </summary>
        /// <param name="element">JSON element holding the value.</param>
        /// <param name="context">Stack message context.</param>
        /// <returns>Decoded DataValue (never null; may be
        /// <see cref="DataValue.IsNull"/>).</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static DataValue DecodeDataValue(
            JsonElement element,
            IServiceMessageContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return DataValue.Null;
            }
            string wrapped = WrapAsObject(element);
            using Opc.Ua.JsonDecoder decoder = new(wrapped, context);
            return decoder.ReadDataValue(SpliceFieldName);
        }

        /// <summary>
        /// Wraps the supplied element in the synthetic
        /// <c>{ "v": &lt;raw&gt; }</c> envelope required by the Stack
        /// <see cref="Opc.Ua.JsonDecoder"/>.
        /// </summary>
        /// <param name="element">Source element.</param>
        /// <returns>JSON text suitable for a string-based decoder
        /// constructor.</returns>
        private static string WrapAsObject(JsonElement element)
        {
            string raw = element.GetRawText();
            return string.Concat("{\"", SpliceFieldName, "\":", raw, "}");
        }

        /// <summary>
        /// Wraps the supplied Verbose Variant element in the
        /// synthetic <c>{ "v": &lt;raw&gt; }</c> envelope while
        /// re-mapping the Part 14 §7.2.5 wire key names
        /// (<c>Type</c>/<c>Body</c>) back to the Stack JSON encoder's
        /// Variant key names (<c>UaType</c>/<c>Value</c>) so the
        /// Stack <see cref="Opc.Ua.JsonDecoder"/> can rehydrate it.
        /// </summary>
        /// <param name="element">Source variant element.</param>
        /// <returns>JSON text suitable for the Stack decoder.</returns>
        private static string WrapAndRenameVariant(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return WrapAsObject(element);
            }
            using var buffer = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
            {
                SkipValidation = true,
                Indented = false
            }))
            {
                writer.WriteStartObject();
                writer.WritePropertyName(SpliceFieldName);
                writer.WriteStartObject();
                foreach (JsonProperty member in element.EnumerateObject())
                {
                    string mapped = member.Name switch
                    {
                        "Type" => "UaType",
                        "Body" => "Value",
                        _ => member.Name
                    };
                    writer.WritePropertyName(mapped);
                    writer.WriteRawValue(
                        member.Value.GetRawText(),
                        skipInputValidation: true);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
    }
}
