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
    /// Internal helpers that translate a single <see cref="JsonElement"/>
    /// (the value of one field in the <c>Payload</c> object of a JSON
    /// DataSetMessage) into a <see cref="Variant"/> or
    /// <see cref="DataValue"/> by delegating to the Stack
    /// <see cref="Ua.JsonDecoder"/>.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.4">
    /// Part 14 §7.2.5.4</see>. The Stack <see cref="Ua.JsonDecoder"/>
    /// expects the top of its element stack to be a JSON object, so the
    /// helper copies the UTF-8 bytes of the supplied element into a
    /// pooled synthetic <c>{ "v": &lt;element&gt; }</c> envelope before
    /// reading. The copy is written straight from the parsed document,
    /// so no intermediate UTF-16 text is materialised.
    /// </remarks>
    internal static class JsonVariantDecoder
    {
        /// <summary>
        /// Property name of the synthetic splice envelope.
        /// </summary>
        internal const string SpliceFieldName = "v";

        /// <summary>
        /// Decodes a single Variant payload from the supplied element.
        /// </summary>
        /// <param name="element">JSON element holding the value.</param>
        /// <param name="mode">
        /// Detected encoding mode. <see cref="JsonEncodingMode.Verbose"/>
        /// expects the Part 6 §5.4.2.17 <c>{ "UaType", "Value" }</c>
        /// envelope, or the bare value of a field whose FieldMetaData
        /// supplies a concrete type (Part 14 §7.2.5.4.2);
        /// <see cref="JsonEncodingMode.Compact"/> and
        /// <see cref="JsonEncodingMode.RawData"/> expect bare values.
        /// </param>
        /// <param name="typeInfo">
        /// Required for Compact / RawData decoding and for collapsed
        /// Verbose fields when the metadata declares the field's type.
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
            if (JsonVariantEncoder.WrapsInVariantEnvelope(mode) &&
                (typeInfo is not { } concrete ||
                    !JsonVariantEncoder.IsCollapsedField(concrete.BuiltInType, concrete.ValueRank) ||
                    IsVariantEnvelope(element)))
            {
                return DecodeSpliced(
                    element,
                    context,
                    static decoder => decoder.ReadVariant(SpliceFieldName));
            }
            if (typeInfo is null)
            {
                return Variant.Null;
            }
            TypeInfo resolved = typeInfo.Value;
            return DecodeSpliced(
                element,
                context,
                decoder => decoder.ReadVariantValue(SpliceFieldName, resolved));
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
            return DecodeSpliced(
                element,
                context,
                static decoder => decoder.ReadDataValue(SpliceFieldName));
        }

        /// <summary>
        /// Copies <paramref name="element"/> into the synthetic
        /// <c>{ "v": &lt;element&gt; }</c> envelope and runs
        /// <paramref name="read"/> against a Stack
        /// <see cref="Ua.JsonDecoder"/> positioned on it.
        /// </summary>
        /// <typeparam name="T">Decoded value type.</typeparam>
        /// <param name="element">Source element.</param>
        /// <param name="context">Stack message context.</param>
        /// <param name="read">Reads the <see cref="SpliceFieldName"/>
        /// property.</param>
        /// <returns>The decoded value.</returns>
        internal static T DecodeSpliced<T>(
            JsonElement element,
            IServiceMessageContext context,
            Func<Ua.JsonDecoder, T> read)
        {
            using JsonBufferWriter buffer = new(256);
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
            {
                SkipValidation = true,
                Indented = false
            }))
            {
                writer.WriteStartObject();
                writer.WritePropertyName(SpliceFieldName);
                element.WriteTo(writer);
                writer.WriteEndObject();
            }
            using Ua.JsonDecoder decoder = new(
                new ReadOnlySequence<byte>(buffer.WrittenMemory),
                context);
            return read(decoder);
        }

        /// <summary>
        /// Whether <paramref name="element"/> is a Part 6 §5.4.2.17
        /// Variant object (it carries <c>UaType</c>).
        /// </summary>
        /// <param name="element">Candidate element.</param>
        /// <returns><see langword="true"/> for a Variant envelope.</returns>
        internal static bool IsVariantEnvelope(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty("UaType", out _);
        }
    }
}
