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
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Opc.Ua.XRegistry;

namespace Opc.Ua.SchemaRegistry.Formats
{
    /// <summary>
    /// Lossless native projection of the JSON Schema 2020-12 document vocabulary.
    /// References remain references; this provider does not fetch schemas or validate payloads.
    /// </summary>
    public sealed class JsonSchemaFormatProvider : ISchemaFormatProvider
    {
        /// <inheritdoc/>
        public string Format => "JsonSchema/2020-12";

        /// <inheritdoc/>
        public string ContentType => "application/schema+json";

        /// <inheritdoc/>
        public string SchemaIdAlgorithm => "SHA-256/JCS";

        /// <summary>
        /// Projects retained JSON bytes to native schema content.
        /// </summary>
        public SchemaContentDataType Parse(ReadOnlySpan<byte> document)
        {
            RegistryValueDataType value = RegistryValues.Parse(document);
            return new JsonSchemaContentDataType
            {
                Format = "JsonSchema/2020-12",
                Root = Project(value, 0)
            };
        }

        /// <summary>
        /// Serializes a supported complete native document without normalizing exact defaults.
        /// </summary>
        public ByteString Serialize(SchemaContentDataType content)
        {
            if (content is not JsonSchemaContentDataType schema ||
                content.GetType() != typeof(JsonSchemaContentDataType) ||
                !string.Equals(schema.Format, "JsonSchema/2020-12", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("JSON Schema 2020-12 native content is required.", nameof(content));
            }
            RegistryValueDataType value = Restore(schema.Root, 0);
            if (!schema.Root.IsEqual(Project(value, 0)))
            {
                throw new ArgumentException(
                    "Unlisted fields must have their canonical unused values; serialization would discard data.",
                    nameof(content));
            }
            return RegistryValues.ToJson(value);
        }

        /// <summary>
        /// Computes the first eight SHA-256 bytes of the RFC 8785 canonical schema.
        /// A number outside the interoperable JCS domain is rejected, never rounded.
        /// </summary>
        public ByteString ComputeSchemaId(ReadOnlySpan<byte> document)
        {
            Parse(document);
            using JsonDocument parsed = JsonDocument.Parse(document.ToArray(),
                new JsonDocumentOptions { MaxDepth = 128 });
            if (!JsonCanonicalizer.TryGetUtf8(parsed.RootElement, out ByteString canonical, out string error))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, error);
            }
#if NETFRAMEWORK || NETSTANDARD2_1
            using SHA256 hash = SHA256.Create();
            byte[] digest = hash.ComputeHash(canonical.ToArray());
#else
            byte[] digest = SHA256.HashData(canonical.Span);
#endif
            return ByteString.From(digest.AsSpan(0, 8).ToArray());
        }

        /// <summary>
        /// Selects a schema node with a plain RFC 6901 JSON Pointer.
        /// References and identifiers remain authored values; no network resolution occurs.
        /// </summary>
        public JsonSchemaNodeDataType Select(SchemaContentDataType content, string selector)
        {
            if (selector is null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
            if (content is not JsonSchemaContentDataType schema ||
                content.GetType() != typeof(JsonSchemaContentDataType))
            {
                throw new ArgumentException("JSON Schema native content is required.", nameof(content));
            }
            RegistryValueDataType selected = Restore(schema.Root, 0);
            if (selector.Length == 0)
            {
                return Project(selected, 0);
            }
            if (selector[0] != '/')
            {
                throw new ArgumentException("A plain JSON Pointer begins with '/'.", nameof(selector));
            }
            var tokens = new List<string>();
            foreach (string part in selector.Substring(1).Split('/'))
            {
                var text = new StringBuilder();
                for (int index = 0; index < part.Length; index++)
                {
                    if (part[index] != '~')
                    {
                        text.Append(part[index]);
                    }
                    else
                    {
                        if (++index == part.Length || (part[index] != '0' && part[index] != '1'))
                        {
                            throw new ArgumentException("Invalid JSON Pointer escape.", nameof(selector));
                        }
                        text.Append(part[index] == '0' ? '~' : '/');
                    }
                }
                tokens.Add(text.ToString());
            }
            for (int index = 0; index < tokens.Count; index++)
            {
                string name = tokens[index];
                if (!s_bySource.TryGetValue(name, out Keyword? keyword) ||
                    keyword.Selection == Selection.None)
                {
                    throw new ArgumentException("The selected keyword has no supported subschema semantics.",
                        nameof(selector));
                }
                selected = ObjectMember(selected, name);
                if (keyword.Selection == Selection.Node)
                {
                    continue;
                }
                if (++index == tokens.Count)
                {
                    throw new ArgumentException("A schema map or array is not itself a subschema.", nameof(selector));
                }
                name = tokens[index];
                if (keyword.Selection == Selection.Map)
                {
                    selected = ObjectMember(selected, name);
                }
                else if (selected is RegistryArrayValueDataType array &&
                    name.Length > 0 && (name.Length == 1 || name[0] != '0') &&
                    int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int offset) &&
                    offset < array.Items.Count)
                {
                    selected = array.Items[offset];
                }
                else
                {
                    throw new ArgumentException("Schema array selection was not found.", nameof(selector));
                }
            }
            return Project(selected, 0);
        }

        private static RegistryValueDataType ObjectMember(RegistryValueDataType value, string name)
        {
            if (value is RegistryObjectValueDataType map)
            {
                foreach (RegistryMemberDataType member in map.Members)
                {
                    if (string.Equals(member.Name, name, StringComparison.Ordinal))
                    {
                        return member.Value;
                    }
                }
            }
            throw new ArgumentException("Schema object selection was not found.");
        }

        IEncodeable ISchemaFormatProvider.Select(SchemaContentDataType content, string selector)
        {
            return Select(content, selector);
        }

        private static JsonSchemaNodeDataType Project(RegistryValueDataType? value, int depth)
        {
            CheckDepth(depth);
            if (value is RegistryBooleanValueDataType boolean)
            {
                return new JsonSchemaBooleanDataType
                {
                    PresentFields = ["Value"],
                    AdditionalFields = [],
                    Value = boolean.Value
                };
            }
            if (value is not RegistryObjectValueDataType map)
            {
                throw new ArgumentException("A schema must be an object or a Boolean.");
            }
            var node = new JsonSchemaObjectDataType();
            foreach (Keyword keyword in s_keywords)
            {
                keyword.Clear(node);
            }
            var present = new List<string>();
            var additional = new List<RegistryMemberDataType>();
            foreach (RegistryMemberDataType member in map.Members)
            {
                if (member.Name is not null && s_bySource.TryGetValue(member.Name, out Keyword? keyword))
                {
                    keyword.Read(node, member.Value, depth);
                    present.Add(keyword.Native);
                }
                else
                {
                    additional.Add(member);
                }
            }
            node.PresentFields = present.ToArray();
            node.AdditionalFields = additional.ToArray();
            return node;
        }

        private static RegistryValueDataType Restore(JsonSchemaNodeDataType? node, int depth)
        {
            CheckDepth(depth);
            if (node is JsonSchemaBooleanDataType boolean && node.GetType() == typeof(JsonSchemaBooleanDataType))
            {
                if (boolean.AdditionalFields.Count != 0 || boolean.PresentFields.Count != 1 ||
                    boolean.PresentFields[0] != "Value")
                {
                    throw new ArgumentException("A Boolean schema cannot contain object keywords.");
                }
                return new RegistryBooleanValueDataType { Kind = 1, Value = boolean.Value };
            }
            if (node is not JsonSchemaObjectDataType schema || node.GetType() != typeof(JsonSchemaObjectDataType))
            {
                throw new ArgumentException("Unsupported native JSON Schema node.", nameof(node));
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            var members = new List<RegistryMemberDataType>();
            foreach (string field in schema.PresentFields)
            {
                if (!names.Add(field))
                {
                    throw new ArgumentException("A schema repeats a presence field.", nameof(node));
                }
                if (!s_byNative.TryGetValue(field, out Keyword? keyword))
                {
                    throw new ArgumentException("Unknown native schema presence field: " + field);
                }
                members.Add(Member(keyword.Source, keyword.Write(schema, depth)));
            }
            foreach (RegistryMemberDataType member in schema.AdditionalFields)
            {
                if (member.Name is null || s_bySource.ContainsKey(member.Name))
                {
                    throw new ArgumentException("AdditionalFields cannot shadow a standard schema keyword.");
                }
                members.Add(member);
            }
            var result = new RegistryObjectValueDataType { Kind = 5, Members = members.ToArray() };
            RegistryValues.Validate(result);
            return result;
        }

        private static RegistryMemberDataType Member(string? name, RegistryValueDataType? value)
        {
            if (name is null || value is null)
            {
                throw new ArgumentException("A present schema member needs a name and a native value.");
            }
            RegistryValues.Validate(value);
            return new RegistryMemberDataType { Name = name, Value = value };
        }

        private static ArrayOf<string> Strings(RegistryValueDataType? value)
        {
            if (value is not RegistryArrayValueDataType array)
            {
                throw new ArgumentException("A unique array of Strings is required.");
            }
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var names = new List<string>();
            foreach (RegistryValueDataType item in array.Items)
            {
                if (item is not RegistryStringValueDataType text || text.Value is null || !unique.Add(text.Value))
                {
                    throw new ArgumentException("A unique array of Strings is required.");
                }
                names.Add(text.Value);
            }
            return names.ToArray();
        }

        private static void ValidateType(RegistryValueDataType? value)
        {
            if (value is RegistryStringValueDataType text && text.Value is not null && s_types.Contains(text.Value))
            {
                return;
            }
            ArrayOf<string> types = Strings(value);
            if (types.Count == 0)
            {
                throw new ArgumentException("type must contain at least one type name.");
            }
            foreach (string type in types)
            {
                if (!s_types.Contains(type))
                {
                    throw new ArgumentException("Unknown JSON Schema type name: " + type);
                }
            }
        }

        private static void CheckDepth(int depth)
        {
            if (depth >= 128)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
        }

        private static Keyword Text(
            string source,
            string native,
            Action<JsonSchemaObjectDataType, string> set,
            Func<JsonSchemaObjectDataType, string?> get)
        {
            return new Keyword(source, native,
                (node, value, _) => set(node, ValidateText(source, StringValue(value))),
                (node, _) => new RegistryStringValueDataType
                {
                    Kind = 2,
                    Value = ValidateText(source,
                        get(node) ?? throw new ArgumentException("A present String cannot be null: " + source))
                },
                node => set(node, string.Empty));
        }

        private static string ValidateText(string source, string text)
        {
            if (source == "$id")
            {
                int fragment = text.AsSpan().IndexOf('#');
                if (fragment >= 0 && fragment != text.Length - 1)
                {
                    throw new ArgumentException("$id cannot have a non-empty fragment.");
                }
            }
            if (source == "$schema" && !Uri.TryCreate(text, UriKind.Absolute, out _))
            {
                throw new ArgumentException("$schema requires an absolute URI.");
            }
            if (source is "$anchor" or "$dynamicAnchor")
            {
                bool Letter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';
                if (text.Length == 0 || !Letter(text[0]))
                {
                    throw new ArgumentException("Invalid JSON Schema anchor.");
                }
                foreach (char c in text)
                {
                    if (!Letter(c) && c is not (>= '0' and <= '9' or '-' or '.'))
                    {
                        throw new ArgumentException("Invalid JSON Schema anchor.");
                    }
                }
            }
            return text;
        }

        private static string StringValue(RegistryValueDataType? value)
        {
            return value is RegistryStringValueDataType text && text.Value is not null
                ? text.Value
                : throw new ArgumentException("A String schema keyword requires a String.");
        }

        private static Keyword Flag(
            string source,
            string native,
            Action<JsonSchemaObjectDataType, bool> set,
            Func<JsonSchemaObjectDataType, bool> get)
        {
            return new Keyword(source, native,
                (node, value, _) => set(node, value is RegistryBooleanValueDataType flag
                    ? flag.Value
                    : throw new ArgumentException("A Boolean schema keyword requires a Boolean: " + source)),
                (node, _) => new RegistryBooleanValueDataType { Kind = 1, Value = get(node) },
                node => set(node, false));
        }

        private static Keyword Value(
            string source,
            string native,
            Action<JsonSchemaObjectDataType, RegistryValueDataType> set,
            Func<JsonSchemaObjectDataType, RegistryValueDataType?> get,
            Action<RegistryValueDataType?>? validate = null)
        {
            RegistryValueDataType Checked(RegistryValueDataType? value)
            {
                if (value is null)
                {
                    throw new ArgumentException("A present native keyword cannot be null: " + source);
                }
                RegistryValues.Validate(value);
                validate?.Invoke(value);
                return value;
            }
            return new Keyword(source, native,
                (node, value, _) => set(node, Checked(value)),
                (node, _) => Checked(get(node)),
                node => set(node, null!));
        }

        private static Keyword Nodes(
            string source,
            string native,
            Action<JsonSchemaObjectDataType, JsonSchemaNodeMapDataType> set,
            Func<JsonSchemaObjectDataType, JsonSchemaNodeMapDataType?> get)
        {
            return new Keyword(source, native, (node, value, depth) =>
            {
                if (value is not RegistryObjectValueDataType map)
                {
                    throw new ArgumentException(source + " requires an object of schemas.");
                }
                var entries = new List<JsonSchemaNodeMapEntryDataType>();
                foreach (RegistryMemberDataType member in map.Members)
                {
                    entries.Add(new JsonSchemaNodeMapEntryDataType
                    {
                        Name = member.Name,
                        Value = Project(member.Value, depth + 1)
                    });
                }
                set(node, new JsonSchemaNodeMapDataType { Entries = entries.ToArray() });
            }, (node, depth) =>
            {
                JsonSchemaNodeMapDataType map = get(node) ??
                    throw new ArgumentException("A present schema map is null: " + source);
                var members = new List<RegistryMemberDataType>();
                foreach (JsonSchemaNodeMapEntryDataType entry in map.Entries)
                {
                    members.Add(Member(entry.Name, Restore(entry.Value, depth + 1)));
                }
                return new RegistryObjectValueDataType { Kind = 5, Members = members.ToArray() };
            }, node => set(node, null!), Selection.Map);
        }

        private static Keyword Node(
            string source,
            string native,
            Action<JsonSchemaObjectDataType, JsonSchemaNodeDataType> set,
            Func<JsonSchemaObjectDataType, JsonSchemaNodeDataType?> get)
        {
            return new Keyword(source, native,
                (node, value, depth) => set(node, Project(value, depth + 1)),
                (node, depth) => Restore(get(node), depth + 1),
                node => set(node, null!), Selection.Node);
        }

        private static Keyword NodeArray(
            string source,
            string native,
            Action<JsonSchemaObjectDataType, ArrayOf<JsonSchemaNodeDataType>> set,
            Func<JsonSchemaObjectDataType, ArrayOf<JsonSchemaNodeDataType>> get)
        {
            return new Keyword(source, native, (node, value, depth) =>
            {
                if (value is not RegistryArrayValueDataType array || array.Items.Count == 0)
                {
                    throw new ArgumentException(source + " requires a non-empty array of schemas.");
                }
                var items = new List<JsonSchemaNodeDataType>();
                foreach (RegistryValueDataType child in array.Items)
                {
                    items.Add(Project(child, depth + 1));
                }
                set(node, items.ToArray());
            }, (node, depth) =>
            {
                ArrayOf<JsonSchemaNodeDataType> array = get(node);
                if (array.Count == 0)
                {
                    throw new ArgumentException(source + " requires a non-empty array of schemas.");
                }
                var items = new List<RegistryValueDataType>();
                foreach (JsonSchemaNodeDataType child in array)
                {
                    items.Add(Restore(child, depth + 1));
                }
                return new RegistryArrayValueDataType { Kind = 4, Items = items.ToArray() };
            }, node => set(node, []), Selection.Array);
        }

        private static Keyword Values(
            string source,
            string native,
            Action<JsonSchemaObjectDataType, ArrayOf<RegistryValueDataType>> set,
            Func<JsonSchemaObjectDataType, ArrayOf<RegistryValueDataType>> get)
        {
            return new Keyword(source, native,
                (node, value, _) => set(node, value is RegistryArrayValueDataType array
                    ? array.Items
                    : throw new ArgumentException(source + " requires an array.")),
                (node, _) => new RegistryArrayValueDataType { Kind = 4, Items = get(node) },
                node => set(node, []));
        }

        private static void Number(RegistryValueDataType? value)
        {
            if (value is not RegistryNumberValueDataType)
            {
                throw new ArgumentException("A numeric schema keyword requires a number.");
            }
        }

        private static BigInteger Coefficient(RegistryNumberValueDataType number)
        {
            byte[] bytes = number.Coefficient.ToArray();
            Array.Reverse(bytes);
            return new BigInteger(bytes);
        }

        private static void NonNegativeInteger(RegistryValueDataType? value)
        {
            Number(value);
            var number = (RegistryNumberValueDataType)value!;
            BigInteger coefficient = Coefficient(number);
            if (coefficient.Sign < 0)
            {
                throw new ArgumentException("A schema count cannot be negative.");
            }
            if (coefficient.IsZero || number.Exponent >= 0)
            {
                return;
            }
            string digits = coefficient.ToString(CultureInfo.InvariantCulture);
            int zeros = 0;
            for (int index = digits.Length - 1; index >= 0 && digits[index] == '0'; index--)
            {
                zeros++;
            }
            if (number.Exponent < -zeros)
            {
                throw new ArgumentException("A schema count must be integral.");
            }
        }

        private static void Positive(RegistryValueDataType? value)
        {
            Number(value);
            if (Coefficient((RegistryNumberValueDataType)value!).Sign <= 0)
            {
                throw new ArgumentException("multipleOf must be positive.");
            }
        }

        private static RegistryObjectValueDataType Vocabulary(RegistryValueDataType? value)
        {
            if (value is not RegistryObjectValueDataType map)
            {
                throw new ArgumentException("$vocabulary requires an object.");
            }
            foreach (RegistryMemberDataType member in map.Members)
            {
                if (!Uri.TryCreate(member.Name, UriKind.Absolute, out _) ||
                    member.Value is not RegistryBooleanValueDataType required)
                {
                    throw new ArgumentException("Vocabulary entries require absolute URIs and Boolean values.");
                }
                if (required.Value && !s_vocabularies.Contains(member.Name!))
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "Required schema vocabulary is not supported: " + member.Name);
                }
            }
            return map;
        }

        private static RegistryArrayValueDataType StringValues(ArrayOf<string> values)
        {
            var items = new List<RegistryValueDataType>();
            foreach (string value in values)
            {
                items.Add(new RegistryStringValueDataType { Kind = 2, Value = value });
            }
            var result = new RegistryArrayValueDataType { Kind = 4, Items = items.ToArray() };
            Strings(result);
            return result;
        }

        private enum Selection
        {
            None,
            Node,
            Map,
            Array
        }

        /// <summary>
        /// A standard keyword; Clear assigns the canonical unused value of its native field
        /// (empty String, false, empty array or a null structure), never a generated default instance.
        /// </summary>
        private sealed record Keyword(
            string Source,
            string Native,
            Action<JsonSchemaObjectDataType, RegistryValueDataType?, int> Read,
            Func<JsonSchemaObjectDataType, int, RegistryValueDataType> Write,
            Action<JsonSchemaObjectDataType> Clear,
            Selection Selection = Selection.None);

        private static Dictionary<string, Keyword> Index(bool native)
        {
            var result = new Dictionary<string, Keyword>(StringComparer.Ordinal);
            foreach (Keyword keyword in s_keywords)
            {
                result.Add(native ? keyword.Native : keyword.Source, keyword);
            }
            return result;
        }

        private static readonly HashSet<string> s_types = new(
            ["null", "boolean", "object", "array", "number", "string", "integer"], StringComparer.Ordinal);
        private static readonly HashSet<string> s_vocabularies = new(
        [
            "https://json-schema.org/draft/2020-12/vocab/core",
            "https://json-schema.org/draft/2020-12/vocab/applicator",
            "https://json-schema.org/draft/2020-12/vocab/unevaluated",
            "https://json-schema.org/draft/2020-12/vocab/validation",
            "https://json-schema.org/draft/2020-12/vocab/meta-data",
            "https://json-schema.org/draft/2020-12/vocab/format-annotation",
            "https://json-schema.org/draft/2020-12/vocab/format-assertion",
            "https://json-schema.org/draft/2020-12/vocab/content"
        ], StringComparer.Ordinal);
        private static readonly Keyword[] s_keywords =
        [
            Text("$schema", "Schema", (n, v) => n.Schema = v, n => n.Schema),
            Text("$id", "Id", (n, v) => n.Id = v, n => n.Id),
            Text("$ref", "Ref", (n, v) => n.Ref = v, n => n.Ref),
            Text("$anchor", "Anchor", (n, v) => n.Anchor = v, n => n.Anchor),
            Text("$dynamicRef", "DynamicRef", (n, v) => n.DynamicRef = v, n => n.DynamicRef),
            Text("$dynamicAnchor", "DynamicAnchor", (n, v) => n.DynamicAnchor = v, n => n.DynamicAnchor),
            Text("$comment", "Comment", (n, v) => n.Comment = v, n => n.Comment),
            Text("title", "Title", (n, v) => n.Title = v, n => n.Title),
            Text("description", "Description", (n, v) => n.Description = v, n => n.Description),
            Text("format", "Format", (n, v) => n.Format = v, n => n.Format),
            Text("pattern", "Pattern", (n, v) => n.Pattern = v, n => n.Pattern),
            Text("contentEncoding", "ContentEncoding", (n, v) => n.ContentEncoding = v, n => n.ContentEncoding),
            Text("contentMediaType", "ContentMediaType", (n, v) => n.ContentMediaType = v, n => n.ContentMediaType),
            Flag("readOnly", "ReadOnly", (n, v) => n.ReadOnly = v, n => n.ReadOnly),
            Flag("writeOnly", "WriteOnly", (n, v) => n.WriteOnly = v, n => n.WriteOnly),
            Flag("deprecated", "Deprecated", (n, v) => n.Deprecated = v, n => n.Deprecated),
            Flag("uniqueItems", "UniqueItems", (n, v) => n.UniqueItems = v, n => n.UniqueItems),
            Value("type", "Type", (n, v) => n.Type = v, n => n.Type, ValidateType),
            Value("const", "Const", (n, v) => n.Const = v, n => n.Const),
            Value("default", "Default", (n, v) => n.Default = v, n => n.Default),
            Value("minimum", "Minimum", (n, v) => n.Minimum = v, n => n.Minimum, Number),
            Value("maximum", "Maximum", (n, v) => n.Maximum = v, n => n.Maximum, Number),
            Value("exclusiveMinimum", "ExclusiveMinimum", (n, v) => n.ExclusiveMinimum = v, n => n.ExclusiveMinimum, Number),
            Value("exclusiveMaximum", "ExclusiveMaximum", (n, v) => n.ExclusiveMaximum = v, n => n.ExclusiveMaximum, Number),
            Value("multipleOf", "MultipleOf", (n, v) => n.MultipleOf = v, n => n.MultipleOf, Positive),
            Value("minLength", "MinLength", (n, v) => n.MinLength = v, n => n.MinLength, NonNegativeInteger),
            Value("maxLength", "MaxLength", (n, v) => n.MaxLength = v, n => n.MaxLength, NonNegativeInteger),
            Value("minItems", "MinItems", (n, v) => n.MinItems = v, n => n.MinItems, NonNegativeInteger),
            Value("maxItems", "MaxItems", (n, v) => n.MaxItems = v, n => n.MaxItems, NonNegativeInteger),
            Value("minContains", "MinContains", (n, v) => n.MinContains = v, n => n.MinContains, NonNegativeInteger),
            Value("maxContains", "MaxContains", (n, v) => n.MaxContains = v, n => n.MaxContains, NonNegativeInteger),
            Value("minProperties", "MinProperties", (n, v) => n.MinProperties = v, n => n.MinProperties, NonNegativeInteger),
            Value("maxProperties", "MaxProperties", (n, v) => n.MaxProperties = v, n => n.MaxProperties, NonNegativeInteger),
            Values("enum", "Enum", (n, v) => n.Enum = v, n => n.Enum),
            Values("examples", "Examples", (n, v) => n.Examples = v, n => n.Examples),
            Nodes("$defs", "Defs", (n, v) => n.Defs = v, n => n.Defs),
            Nodes("properties", "Properties", (n, v) => n.Properties = v, n => n.Properties),
            Nodes("patternProperties", "PatternProperties", (n, v) => n.PatternProperties = v, n => n.PatternProperties),
            Nodes("dependentSchemas", "DependentSchemas", (n, v) => n.DependentSchemas = v, n => n.DependentSchemas),
            Node("items", "Items", (n, v) => n.Items = v, n => n.Items),
            Node("contains", "Contains", (n, v) => n.Contains = v, n => n.Contains),
            Node("additionalProperties", "AdditionalProperties", (n, v) => n.AdditionalProperties = v, n => n.AdditionalProperties),
            Node("unevaluatedItems", "UnevaluatedItems", (n, v) => n.UnevaluatedItems = v, n => n.UnevaluatedItems),
            Node("unevaluatedProperties", "UnevaluatedProperties", (n, v) => n.UnevaluatedProperties = v, n => n.UnevaluatedProperties),
            Node("propertyNames", "PropertyNames", (n, v) => n.PropertyNames = v, n => n.PropertyNames),
            Node("not", "Not", (n, v) => n.Not = v, n => n.Not),
            Node("if", "If", (n, v) => n.If = v, n => n.If),
            Node("then", "Then", (n, v) => n.Then = v, n => n.Then),
            Node("else", "Else", (n, v) => n.Else = v, n => n.Else),
            Node("contentSchema", "ContentSchema", (n, v) => n.ContentSchema = v, n => n.ContentSchema),
            NodeArray("allOf", "AllOf", (n, v) => n.AllOf = v, n => n.AllOf),
            NodeArray("anyOf", "AnyOf", (n, v) => n.AnyOf = v, n => n.AnyOf),
            NodeArray("oneOf", "OneOf", (n, v) => n.OneOf = v, n => n.OneOf),
            NodeArray("prefixItems", "PrefixItems", (n, v) => n.PrefixItems = v, n => n.PrefixItems),
            new("required", "Required", (n, v, _) => n.Required = Strings(v), (n, _) => StringValues(n.Required),
                n => n.Required = []),
            new("$vocabulary", "Vocabulary", (n, v, _) => n.Vocabulary = Vocabulary(v), (n, _) => Vocabulary(n.Vocabulary),
                n => n.Vocabulary = null!),
            new("dependentRequired", "DependentRequired", (node, value, _) =>
            {
                if (value is not RegistryObjectValueDataType map)
                {
                    throw new ArgumentException("dependentRequired requires an object.");
                }
                var entries = new List<JsonSchemaStringArrayMapEntryDataType>();
                foreach (RegistryMemberDataType member in map.Members)
                {
                    Strings(member.Value);
                    entries.Add(new JsonSchemaStringArrayMapEntryDataType { Name = member.Name, Value = member.Value });
                }
                node.DependentRequired = new JsonSchemaStringArrayMapDataType { Entries = entries.ToArray() };
            }, (node, _) =>
            {
                if (node.DependentRequired is null)
                {
                    throw new ArgumentException("A present dependentRequired cannot be null.");
                }
                var members = new List<RegistryMemberDataType>();
                foreach (JsonSchemaStringArrayMapEntryDataType entry in node.DependentRequired.Entries)
                {
                    Strings(entry.Value);
                    members.Add(Member(entry.Name, entry.Value));
                }
                return new RegistryObjectValueDataType { Kind = 5, Members = members.ToArray() };
            }, node => node.DependentRequired = null!)
        ];
        private static readonly Dictionary<string, Keyword> s_bySource = Index(native: false);
        private static readonly Dictionary<string, Keyword> s_byNative = Index(native: true);
    }
}
