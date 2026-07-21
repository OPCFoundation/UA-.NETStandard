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
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>The result of encoding a value to a payload.</summary>
    public sealed class WotEncodeResult
    {
        private WotEncodeResult(bool success, ReadOnlyMemory<byte> data, string? error)
        {
            Success = success;
            Data = data;
            Error = error;
        }

        /// <summary>Gets whether encoding succeeded.</summary>
        public bool Success { get; }

        /// <summary>Gets the encoded bytes.</summary>
        public ReadOnlyMemory<byte> Data { get; }

        /// <summary>Gets the error message on failure.</summary>
        public string? Error { get; }

        /// <summary>Creates a successful encode result.</summary>
        public static WotEncodeResult Ok(ReadOnlyMemory<byte> data) => new WotEncodeResult(true, data, null);

        /// <summary>Creates a failed encode result.</summary>
        public static WotEncodeResult Fail(string error)
            => new WotEncodeResult(false, ReadOnlyMemory<byte>.Empty, error);
    }

    /// <summary>The result of decoding a payload to a value.</summary>
    public sealed class WotDecodeResult
    {
        private WotDecodeResult(bool success, Variant value, string? error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        /// <summary>Gets whether decoding succeeded.</summary>
        public bool Success { get; }

        /// <summary>Gets the decoded value.</summary>
        public Variant Value { get; }

        /// <summary>Gets the error message on failure.</summary>
        public string? Error { get; }

        /// <summary>Creates a successful decode result.</summary>
        public static WotDecodeResult Ok(Variant value) => new WotDecodeResult(true, value, null);

        /// <summary>Creates a failed decode result.</summary>
        public static WotDecodeResult Fail(string error) => new WotDecodeResult(false, Variant.Null, error);
    }

    /// <summary>
    /// Encodes and decodes payloads between OPC UA values and transport bytes for
    /// a content type. Codecs are reflection-free and AOT-safe.
    /// </summary>
    public interface IWotPayloadCodec
    {
        /// <summary>Gets the stable codec id (recorded on the compiled plan).</summary>
        string Id { get; }

        /// <summary>Gets whether the codec handles the supplied content type.</summary>
        bool CanHandle(string? contentType);

        /// <summary>Encodes a value to bytes for the supplied payload metadata.</summary>
        WotEncodeResult Encode(Variant value, WotPayloadDescriptor payload);

        /// <summary>Decodes bytes into a value for the supplied payload metadata.</summary>
        WotDecodeResult Decode(ReadOnlyMemory<byte> data, WotPayloadDescriptor payload);
    }

    /// <summary>Selects a payload codec for a content type.</summary>
    public interface IWotCodecRegistry
    {
        /// <summary>Attempts to select a codec for the supplied content type.</summary>
        bool TrySelect(string? contentType, out IWotPayloadCodec codec);
    }

    /// <summary>
    /// The default codec registry: it selects the first registered codec whose
    /// <see cref="IWotPayloadCodec.CanHandle(string)"/> returns <c>true</c>, and
    /// ships JSON, plain-text and octet-stream codecs. Additional codecs can be
    /// registered by protocol executors.
    /// </summary>
    public sealed class WotPayloadCodecRegistry : IWotCodecRegistry
    {
        /// <summary>Initializes a registry with the built-in codecs.</summary>
        public WotPayloadCodecRegistry()
        {
            m_codecs.Add(JsonWotPayloadCodec.Instance);
            m_codecs.Add(TextWotPayloadCodec.Instance);
            m_codecs.Add(OctetStreamWotPayloadCodec.Instance);
        }

        /// <summary>Gets the shared registry with the built-in codecs.</summary>
        public static WotPayloadCodecRegistry Default { get; } = new WotPayloadCodecRegistry();

        /// <summary>Registers a codec at the front of the selection order.</summary>
        public WotPayloadCodecRegistry Register(IWotPayloadCodec codec)
        {
            if (codec is null)
            {
                throw new ArgumentNullException(nameof(codec));
            }
            m_codecs.Insert(0, codec);
            return this;
        }

        /// <inheritdoc/>
        public bool TrySelect(string? contentType, out IWotPayloadCodec codec)
        {
            foreach (IWotPayloadCodec candidate in m_codecs)
            {
                if (candidate.CanHandle(contentType))
                {
                    codec = candidate;
                    return true;
                }
            }
            codec = JsonWotPayloadCodec.Instance;
            return false;
        }

        private readonly List<IWotPayloadCodec> m_codecs = new List<IWotPayloadCodec>();
    }

    /// <summary>A reflection-free JSON scalar payload codec (<c>application/json</c>).</summary>
    public sealed class JsonWotPayloadCodec : IWotPayloadCodec
    {
        /// <summary>Gets the shared instance.</summary>
        public static JsonWotPayloadCodec Instance { get; } = new JsonWotPayloadCodec();

        /// <inheritdoc/>
        public string Id => "json";

        /// <inheritdoc/>
        public bool CanHandle(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
            {
                return true;
            }
            string media = MediaType(contentType!);
            return media.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
                media.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public WotEncodeResult Encode(Variant value, WotPayloadDescriptor payload)
        {
            try
            {
                using var buffer = new MemoryStream();
                using (var writer = new Utf8JsonWriter(buffer))
                {
                    WriteValue(writer, value.AsBoxedObject());
                }
                return WotEncodeResult.Ok(buffer.ToArray());
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
            {
                return WotEncodeResult.Fail(ex.Message);
            }
        }

        /// <inheritdoc/>
        public WotDecodeResult Decode(ReadOnlyMemory<byte> data, WotPayloadDescriptor payload)
        {
            if (data.IsEmpty)
            {
                return WotDecodeResult.Ok(Variant.Null);
            }
            try
            {
                using JsonDocument document = JsonDocument.Parse(data);
                JsonElement root = document.RootElement;
                switch (root.ValueKind)
                {
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        return WotDecodeResult.Ok(new Variant(root.GetBoolean()));
                    case JsonValueKind.String:
                        return WotDecodeResult.Ok(new Variant(root.GetString() ?? string.Empty));
                    case JsonValueKind.Null:
                        return WotDecodeResult.Ok(Variant.Null);
                    case JsonValueKind.Number:
                        if (root.TryGetInt64(out long l))
                        {
                            return WotDecodeResult.Ok(new Variant(l));
                        }
                        if (root.TryGetDouble(out double d))
                        {
                            return WotDecodeResult.Ok(new Variant(d));
                        }
                        return WotDecodeResult.Ok(new Variant(root.GetRawText()));
                    default:
                        // Objects and arrays are preserved as their raw JSON text.
                        return WotDecodeResult.Ok(new Variant(root.GetRawText()));
                }
            }
            catch (JsonException ex)
            {
                return WotDecodeResult.Fail(ex.Message);
            }
        }

        private static void WriteValue(Utf8JsonWriter writer, object? value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case bool b:
                    writer.WriteBooleanValue(b);
                    break;
                case string s:
                    writer.WriteStringValue(s);
                    break;
                case sbyte sb:
                    writer.WriteNumberValue(sb);
                    break;
                case byte by:
                    writer.WriteNumberValue(by);
                    break;
                case short sh:
                    writer.WriteNumberValue(sh);
                    break;
                case ushort us:
                    writer.WriteNumberValue(us);
                    break;
                case int i:
                    writer.WriteNumberValue(i);
                    break;
                case uint ui:
                    writer.WriteNumberValue(ui);
                    break;
                case long lo:
                    writer.WriteNumberValue(lo);
                    break;
                case ulong ul:
                    writer.WriteNumberValue(ul);
                    break;
                case float f:
                    writer.WriteNumberValue(f);
                    break;
                case double dou:
                    writer.WriteNumberValue(dou);
                    break;
                case decimal de:
                    writer.WriteNumberValue(de);
                    break;
                default:
                    writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static string MediaType(string contentType)
        {
            int semicolon = -1;
            for (int i = 0; i < contentType.Length; i++)
            {
                if (contentType[i] == ';')
                {
                    semicolon = i;
                    break;
                }
            }
            return (semicolon >= 0 ? contentType.Substring(0, semicolon) : contentType).Trim();
        }
    }

    /// <summary>A plain-text payload codec (<c>text/plain</c>).</summary>
    public sealed class TextWotPayloadCodec : IWotPayloadCodec
    {
        /// <summary>Gets the shared instance.</summary>
        public static TextWotPayloadCodec Instance { get; } = new TextWotPayloadCodec();

        /// <inheritdoc/>
        public string Id => "text";

        /// <inheritdoc/>
        public bool CanHandle(string? contentType)
            => !string.IsNullOrEmpty(contentType) &&
               contentType!.StartsWith("text/", StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public WotEncodeResult Encode(Variant value, WotPayloadDescriptor payload)
        {
            object? boxed = value.AsBoxedObject();
            string text = boxed is null
                ? string.Empty
                : Convert.ToString(boxed, CultureInfo.InvariantCulture) ?? string.Empty;
            return WotEncodeResult.Ok(Encoding.UTF8.GetBytes(text));
        }

        /// <inheritdoc/>
        public WotDecodeResult Decode(ReadOnlyMemory<byte> data, WotPayloadDescriptor payload)
        {
            string text = Encoding.UTF8.GetString(data.ToArray());
            return WotDecodeResult.Ok(new Variant(text));
        }
    }

    /// <summary>An octet-stream payload codec (<c>application/octet-stream</c>).</summary>
    public sealed class OctetStreamWotPayloadCodec : IWotPayloadCodec
    {
        /// <summary>Gets the shared instance.</summary>
        public static OctetStreamWotPayloadCodec Instance { get; } = new OctetStreamWotPayloadCodec();

        /// <inheritdoc/>
        public string Id => "octet-stream";

        /// <inheritdoc/>
        public bool CanHandle(string? contentType)
            => !string.IsNullOrEmpty(contentType) &&
               contentType!.StartsWith("application/octet-stream", StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public WotEncodeResult Encode(Variant value, WotPayloadDescriptor payload)
        {
            if (value.TryGetValue(out ByteString byteString))
            {
                return WotEncodeResult.Ok(byteString.Memory.ToArray());
            }
            object? boxed = value.AsBoxedObject();
            if (boxed is byte[] bytes)
            {
                return WotEncodeResult.Ok(bytes);
            }
            string text = boxed is null
                ? string.Empty
                : Convert.ToString(boxed, CultureInfo.InvariantCulture) ?? string.Empty;
            return WotEncodeResult.Ok(Encoding.UTF8.GetBytes(text));
        }

        /// <inheritdoc/>
        public WotDecodeResult Decode(ReadOnlyMemory<byte> data, WotPayloadDescriptor payload)
            => WotDecodeResult.Ok(new Variant(new ByteString(data.ToArray())));
    }
}
