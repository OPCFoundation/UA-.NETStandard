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
 *
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

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The kind of WoT document, derived from its <c>@type</c>.
    /// </summary>
    public enum WotDocumentKind
    {
        /// <summary>The document kind could not be determined.</summary>
        Unknown,

        /// <summary>A class-level Thing Model (an OPC UA type projection).</summary>
        ThingModel,

        /// <summary>An instance-level Thing Description (an OPC UA Object projection).</summary>
        ThingDescription
    }

    /// <summary>
    /// A losslessly retained Web of Things JSON document with a lexical
    /// access surface over the W3C Thing Description / Thing Model model.
    /// </summary>
    /// <remarks>
    /// The original UTF-8 representation is retained so unknown JSON-LD terms
    /// can be written back byte-for-byte without a lossy object-model
    /// projection. Typed access is exposed over the parsed
    /// <see cref="System.Text.Json.JsonElement"/> tree rather than one POCO per
    /// W3C class, so members the binding does not model are still reachable and
    /// preserved. A separate deterministic canonical writer is provided.
    /// </remarks>
    public sealed class WotDocument : IDisposable
    {
        /// <summary>The JSON-LD prefix bound to the OPC UA WoT Binding namespace.</summary>
        public const string UavPrefix = "uav:";

        private WotDocument(byte[] utf8Json, JsonDocument document)
        {
            m_utf8Json = utf8Json;
            m_document = document;
        }

        /// <summary>
        /// Gets the parsed document root.
        /// </summary>
        public JsonElement RootElement => m_document.RootElement;

        /// <summary>
        /// Gets the original UTF-8 document bytes.
        /// </summary>
        public ReadOnlyMemory<byte> Utf8Json => m_utf8Json;

        /// <summary>
        /// Gets the document kind derived from <c>@type</c>.
        /// </summary>
        public WotDocumentKind Kind
        {
            get
            {
                foreach (string token in TypeTokens)
                {
                    if (string.Equals(token, "tm:ThingModel", StringComparison.Ordinal) ||
                        string.Equals(token, UavPrefix + "objectType", StringComparison.Ordinal) ||
                        string.Equals(token, UavPrefix + "variableType", StringComparison.Ordinal))
                    {
                        return WotDocumentKind.ThingModel;
                    }
                }
                foreach (string token in TypeTokens)
                {
                    if (string.Equals(token, UavPrefix + "object", StringComparison.Ordinal) ||
                        string.Equals(token, UavPrefix + "variable", StringComparison.Ordinal) ||
                        string.Equals(token, UavPrefix + "method", StringComparison.Ordinal))
                    {
                        return WotDocumentKind.ThingDescription;
                    }
                }
                return WotDocumentKind.Unknown;
            }
        }

        /// <summary>
        /// Gets the <c>@type</c> tokens of the document.
        /// </summary>
        public IReadOnlyList<string> TypeTokens
        {
            get
            {
                m_typeTokens ??= ReadStringTokens("@type");
                return m_typeTokens;
            }
        }

        /// <summary>Gets the document <c>title</c>, if present.</summary>
        public string? Title => GetRootString("title");

        /// <summary>Gets the document <c>id</c>, if present.</summary>
        public string? Id => GetRootString("id");

        /// <summary>
        /// Attempts to get the <c>@context</c> element.
        /// </summary>
        /// <param name="context">The context element on success.</param>
        /// <returns><c>true</c> when a <c>@context</c> member is present.</returns>
        public bool TryGetContext(out JsonElement context)
        {
            return TryGetRootProperty("@context", out context);
        }

        /// <summary>Gets the <c>properties</c> affordance map (name to schema).</summary>
        public IReadOnlyDictionary<string, JsonElement> Properties
        {
            get
            {
                m_properties ??= ReadObjectMap("properties");
                return m_properties;
            }
        }

        /// <summary>Gets the <c>actions</c> affordance map (name to affordance).</summary>
        public IReadOnlyDictionary<string, JsonElement> Actions
        {
            get
            {
                m_actions ??= ReadObjectMap("actions");
                return m_actions;
            }
        }

        /// <summary>Gets the <c>events</c> affordance map (name to affordance).</summary>
        public IReadOnlyDictionary<string, JsonElement> Events
        {
            get
            {
                m_events ??= ReadObjectMap("events");
                return m_events;
            }
        }

        /// <summary>Gets the <c>securityDefinitions</c> map, if present.</summary>
        public IReadOnlyDictionary<string, JsonElement> SecurityDefinitions
        {
            get
            {
                m_securityDefinitions ??= ReadObjectMap("securityDefinitions");
                return m_securityDefinitions;
            }
        }

        /// <summary>Gets the <c>schemaDefinitions</c> map, if present.</summary>
        public IReadOnlyDictionary<string, JsonElement> SchemaDefinitions
        {
            get
            {
                m_schemaDefinitions ??= ReadObjectMap("schemaDefinitions");
                return m_schemaDefinitions;
            }
        }

        /// <summary>Gets the top-level <c>links</c> array elements.</summary>
        public IReadOnlyList<JsonElement> Links
        {
            get
            {
                m_links ??= ReadArray("links");
                return m_links;
            }
        }

        /// <summary>Gets the top-level <c>forms</c> array elements.</summary>
        public IReadOnlyList<JsonElement> Forms
        {
            get
            {
                m_forms ??= ReadArray("forms");
                return m_forms;
            }
        }

        /// <summary>
        /// Attempts to get the <c>uav:nodeSet</c> preservation envelope.
        /// </summary>
        /// <param name="envelope">The envelope element on success.</param>
        /// <returns><c>true</c> when the envelope is present as an object.</returns>
        public bool TryGetEnvelope(out JsonElement envelope)
        {
            return TryGetUav("nodeSet", out envelope) &&
                envelope.ValueKind == JsonValueKind.Object;
        }

        /// <summary>
        /// Attempts to get the native <c>uav:nodes</c> projection.
        /// </summary>
        /// <param name="projection">The projection element on success.</param>
        /// <returns><c>true</c> when the projection is present as an object.</returns>
        public bool TryGetNativeProjection(out JsonElement projection)
        {
            return TryGetUav("nodes", out projection) &&
                projection.ValueKind == JsonValueKind.Object;
        }

        /// <summary>
        /// Attempts to get a <c>uav:</c>-prefixed member of the document root.
        /// </summary>
        /// <param name="localName">The local term name without the prefix.</param>
        /// <param name="value">The member value on success.</param>
        /// <returns><c>true</c> when the member is present.</returns>
        public bool TryGetUav(string localName, out JsonElement value)
        {
            if (localName is null)
            {
                throw new ArgumentNullException(nameof(localName));
            }
            return TryGetRootProperty(UavPrefix + localName, out value);
        }

        /// <summary>
        /// Evaluates an RFC 6901 JSON Pointer against the document root.
        /// </summary>
        /// <param name="pointer">The JSON Pointer (empty string addresses the root).</param>
        /// <param name="value">The addressed element on success.</param>
        /// <returns><c>true</c> when the pointer resolves.</returns>
        public bool TryEvaluatePointer(string pointer, out JsonElement value)
        {
            if (pointer is null)
            {
                throw new ArgumentNullException(nameof(pointer));
            }
            return TryEvaluatePointer(RootElement, pointer, out value);
        }

        /// <summary>
        /// Evaluates an RFC 6901 JSON Pointer against a given element.
        /// </summary>
        /// <param name="root">The element to evaluate the pointer against.</param>
        /// <param name="pointer">The JSON Pointer (empty string addresses <paramref name="root"/>).</param>
        /// <param name="value">The addressed element on success.</param>
        /// <returns><c>true</c> when the pointer resolves.</returns>
        public static bool TryEvaluatePointer(JsonElement root, string pointer, out JsonElement value)
        {
            if (pointer is null)
            {
                throw new ArgumentNullException(nameof(pointer));
            }

            value = root;
            if (pointer.Length == 0)
            {
                return true;
            }
            if (pointer[0] != '/')
            {
                value = default;
                return false;
            }

            JsonElement current = root;
            int index = 1;
            while (index <= pointer.Length)
            {
                int next = pointer.IndexOf('/', index);
                if (next < 0)
                {
                    next = pointer.Length;
                }
                string token = UnescapePointerToken(pointer.Substring(index, next - index));
                index = next + 1;

                switch (current.ValueKind)
                {
                    case JsonValueKind.Object:
                        if (!current.TryGetProperty(token, out current))
                        {
                            value = default;
                            return false;
                        }
                        break;
                    case JsonValueKind.Array:
                        if (!TryGetArrayElement(current, token, out current))
                        {
                            value = default;
                            return false;
                        }
                        break;
                    default:
                        value = default;
                        return false;
                }
            }

            value = current;
            return true;
        }

        /// <summary>
        /// Parses a UTF-8 WoT document while preserving its original bytes.
        /// </summary>
        /// <param name="utf8Json">The UTF-8 encoded document.</param>
        /// <param name="options">Resource limits; defaults are used when omitted.</param>
        /// <returns>The parsed, byte-preserving document.</returns>
        /// <exception cref="FormatException">Thrown when the document exceeds the configured byte limit.</exception>
        public static WotDocument Parse(
            ReadOnlyMemory<byte> utf8Json,
            WotNodeSetConverterOptions? options = null)
        {
            options ??= new WotNodeSetConverterOptions();
            options.Validate();
            if (utf8Json.Length > options.MaxJsonDocumentSize)
            {
                throw new FormatException(
                    $"WoT document exceeds the configured {options.MaxJsonDocumentSize} byte limit.");
            }

            byte[] copy = utf8Json.ToArray();
            JsonDocument document = JsonDocument.Parse(
                copy,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = options.MaxJsonDepth
                });
            return new WotDocument(copy, document);
        }

        /// <summary>
        /// Writes the original UTF-8 document bytes to <paramref name="stream"/>.
        /// </summary>
        /// <param name="stream">The destination stream.</param>
        public void Write(Stream stream)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            stream.Write(m_utf8Json, 0, m_utf8Json.Length);
        }

        /// <summary>
        /// Writes a deterministic canonical serialization of the document to
        /// <paramref name="stream"/>. Object members are ordered by name and
        /// insignificant whitespace is removed so equivalent documents produce
        /// byte-identical output. Unlike <see cref="Write(Stream)"/> this does
        /// not preserve the original byte layout.
        /// </summary>
        /// <param name="stream">The destination stream.</param>
        public void WriteCanonical(Stream stream)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            using var writer = new Utf8JsonWriter(
                stream,
                new JsonWriterOptions { Indented = false, SkipValidation = false });
            WriteCanonical(writer, RootElement);
            writer.Flush();
        }

        /// <summary>
        /// Returns the deterministic canonical serialization of the document.
        /// </summary>
        /// <returns>The canonical UTF-8 bytes.</returns>
        public byte[] ToCanonicalUtf8()
        {
            using var stream = new MemoryStream();
            WriteCanonical(stream);
            return stream.ToArray();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_document.Dispose();
        }

        internal static WotDocument FromOwnedBytes(byte[] utf8Json)
        {
            JsonDocument document = JsonDocument.Parse(utf8Json);
            return new WotDocument(utf8Json, document);
        }

        private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    var members = new List<KeyValuePair<string, JsonElement>>();
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        members.Add(new KeyValuePair<string, JsonElement>(property.Name, property.Value));
                    }
                    members.Sort(static (left, right) =>
                        string.CompareOrdinal(left.Key, right.Key));
                    foreach (KeyValuePair<string, JsonElement> member in members)
                    {
                        writer.WritePropertyName(member.Key);
                        WriteCanonical(writer, member.Value);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        WriteCanonical(writer, item);
                    }
                    writer.WriteEndArray();
                    break;
                case JsonValueKind.String:
                    writer.WriteStringValue(element.GetString());
                    break;
                case JsonValueKind.Number:
                    writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    writer.WriteBooleanValue(element.GetBoolean());
                    break;
                case JsonValueKind.Null:
                    writer.WriteNullValue();
                    break;
                default:
                    writer.WriteNullValue();
                    break;
            }
        }

        private static bool TryGetArrayElement(JsonElement array, string token, out JsonElement value)
        {
            value = default;
            if (token.Length == 0 ||
                (token.Length > 1 && token[0] == '0'))
            {
                return false;
            }
            if (!int.TryParse(
                token,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int arrayIndex))
            {
                return false;
            }
            if (arrayIndex < 0 || arrayIndex >= array.GetArrayLength())
            {
                return false;
            }
            value = array[arrayIndex];
            return true;
        }

        private static string UnescapePointerToken(string token)
        {
            int tilde = -1;
            for (int ii = 0; ii < token.Length; ii++)
            {
                if (token[ii] == '~')
                {
                    tilde = ii;
                    break;
                }
            }
            if (tilde < 0)
            {
                return token;
            }

            var builder = new StringBuilder(token.Length);
            builder.Append(token, 0, tilde);
            for (int ii = tilde; ii < token.Length; ii++)
            {
                char current = token[ii];
                if (current == '~' && ii + 1 < token.Length)
                {
                    char next = token[ii + 1];
                    if (next == '1')
                    {
                        builder.Append('/');
                        ii++;
                        continue;
                    }
                    if (next == '0')
                    {
                        builder.Append('~');
                        ii++;
                        continue;
                    }
                }
                builder.Append(current);
            }
            return builder.ToString();
        }

        private bool TryGetRootProperty(string name, out JsonElement value)
        {
            JsonElement root = RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(name, out value))
            {
                return true;
            }
            value = default;
            return false;
        }

        private string? GetRootString(string name)
        {
            return TryGetRootProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private List<string> ReadStringTokens(string name)
        {
            var tokens = new List<string>();
            if (TryGetRootProperty(name, out JsonElement value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    string? token = value.GetString();
                    if (token is not null)
                    {
                        tokens.Add(token);
                    }
                }
                else if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            string? token = item.GetString();
                            if (token is not null)
                            {
                                tokens.Add(token);
                            }
                        }
                    }
                }
            }
            return tokens;
        }

        private Dictionary<string, JsonElement> ReadObjectMap(string name)
        {
            var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (TryGetRootProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    map[property.Name] = property.Value;
                }
            }
            return map;
        }

        private List<JsonElement> ReadArray(string name)
        {
            var items = new List<JsonElement>();
            if (TryGetRootProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    items.Add(item);
                }
            }
            return items;
        }

        private readonly byte[] m_utf8Json;
        private readonly JsonDocument m_document;
        private IReadOnlyList<string>? m_typeTokens;
        private IReadOnlyDictionary<string, JsonElement>? m_properties;
        private IReadOnlyDictionary<string, JsonElement>? m_actions;
        private IReadOnlyDictionary<string, JsonElement>? m_events;
        private IReadOnlyDictionary<string, JsonElement>? m_securityDefinitions;
        private IReadOnlyDictionary<string, JsonElement>? m_schemaDefinitions;
        private IReadOnlyList<JsonElement>? m_links;
        private IReadOnlyList<JsonElement>? m_forms;
    }
}
