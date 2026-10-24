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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Opc.Ua.XRegistry;

namespace Opc.Ua.SchemaRegistry.Formats
{
    /// <summary>
    /// Avro schema-only grammar and Parsing Canonical Form.
    /// The exact native document retains annotations, defaults, custom properties and authored names.
    /// </summary>
    public sealed class AvroSchemaFormatProvider : ISchemaFormatProvider
    {
        /// <inheritdoc/>
        public string Format => "Avro/1.11";

        /// <inheritdoc/>
        public string ContentType => "application/vnd.apache.avro+json";

        /// <inheritdoc/>
        public string SchemaIdAlgorithm => "CRC-64-AVRO";

        /// <inheritdoc/>
        public SchemaContentDataType Parse(ReadOnlySpan<byte> document)
        {
            AvroSchemaNodeDataType root = Project(RegistryValues.Parse(document), 0);
            Canonical(root, out _);
            return new AvroSchemaContentDataType { Format = Format, Root = root };
        }

        /// <inheritdoc/>
        public ByteString Serialize(SchemaContentDataType content)
        {
            AvroSchemaContentDataType schema = Require(content);
            RegistryValueDataType value = Restore(schema.Root, 0);
            if (!schema.Root.IsEqual(Project(value, 0)))
            {
                throw new ArgumentException("Native Avro presence fields would discard non-default data.");
            }
            Canonical(schema.Root, out _);
            return RegistryValues.ToJson(value);
        }

        /// <inheritdoc/>
        public ByteString ComputeSchemaId(ReadOnlySpan<byte> document)
        {
            var schema = (AvroSchemaContentDataType)Parse(document);
            byte[] canonical = Canonical(schema.Root, out _);
            ulong fingerprint = kEmpty;
            foreach (byte value in canonical)
            {
                fingerprint = (fingerprint >> 8) ^ s_fingerprintTable[(int)((fingerprint ^ value) & 255)];
            }
            var bytes = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, fingerprint);
            return ByteString.From(bytes);
        }

        /// <inheritdoc/>
        public IEncodeable Select(SchemaContentDataType content, string selector)
        {
            if (selector is null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
            AvroSchemaContentDataType schema = Require(content);
            Canonical(schema.Root, out Dictionary<string, AvroDeclarationDataType> names);
            if (selector.Length == 0)
            {
                return (IEncodeable)schema.Root.Clone();
            }
            return names.TryGetValue(selector, out AvroDeclarationDataType? selected)
                ? new AvroObjectDataType { Kind = 2, Declaration = (AvroDeclarationDataType)selected.Clone() }
                : throw new ArgumentException("The selected Avro full name is not declared.", nameof(selector));
        }

        private static AvroSchemaContentDataType Require(SchemaContentDataType content)
        {
            if (content is not AvroSchemaContentDataType schema ||
                content.GetType() != typeof(AvroSchemaContentDataType) ||
                !string.Equals(schema.Format, "Avro/1.11", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Avro 1.11 native content is required.", nameof(content));
            }
            return schema;
        }

        private static AvroSchemaNodeDataType Project(RegistryValueDataType? value, int depth)
        {
            Depth(depth);
            if (value is RegistryStringValueDataType text && text.Value is not null)
            {
                return new AvroNameDataType { Kind = 0, Name = text.Value };
            }
            if (value is RegistryArrayValueDataType array)
            {
                var branches = new List<AvroSchemaNodeDataType>();
                foreach (RegistryValueDataType branch in array.Items)
                {
                    branches.Add(Project(branch, depth + 1));
                }
                return new AvroUnionDataType { Kind = 1, Branches = branches.ToArray() };
            }
            if (value is not RegistryObjectValueDataType map)
            {
                throw new ArgumentException("An Avro schema is a name, union array or declaration object.");
            }
            var declaration = new AvroDeclarationDataType
            {
                Type = null!,
                Name = string.Empty,
                Namespace = string.Empty,
                Doc = string.Empty,
                LogicalType = string.Empty,
                Aliases = [],
                Fields = [],
                Symbols = [],
                Items = null!,
                Values = null!,
                Size = null!,
                Precision = null!,
                Scale = null!,
                Default = null!
            };
            var present = new List<string>();
            var additional = new List<RegistryMemberDataType>();
            foreach (RegistryMemberDataType member in map.Members)
            {
                switch (member.Name)
                {
                    case "type": declaration.Type = Project(member.Value, depth + 1); present.Add("Type"); break;
                    case "name": declaration.Name = Text(member.Value); present.Add("Name"); break;
                    case "namespace": declaration.Namespace = Text(member.Value); present.Add("Namespace"); break;
                    case "doc": declaration.Doc = Text(member.Value); present.Add("Doc"); break;
                    case "logicalType": declaration.LogicalType = Text(member.Value); present.Add("LogicalType"); break;
                    case "aliases": declaration.Aliases = Strings(member.Value); present.Add("Aliases"); break;
                    case "symbols": declaration.Symbols = Strings(member.Value); present.Add("Symbols"); break;
                    case "size": declaration.Size = member.Value; present.Add("Size"); break;
                    case "precision": declaration.Precision = member.Value; present.Add("Precision"); break;
                    case "scale": declaration.Scale = member.Value; present.Add("Scale"); break;
                    case "default": declaration.Default = member.Value; present.Add("Default"); break;
                    case "items": declaration.Items = Project(member.Value, depth + 1); present.Add("Items"); break;
                    case "values": declaration.Values = Project(member.Value, depth + 1); present.Add("Values"); break;
                    case "fields":
                        if (member.Value is not RegistryArrayValueDataType fields)
                        {
                            throw new ArgumentException("Avro fields must be an array.");
                        }
                        var nativeFields = new List<AvroFieldDataType>();
                        foreach (RegistryValueDataType field in fields.Items)
                        {
                            nativeFields.Add(ProjectField(field, depth + 1));
                        }
                        declaration.Fields = nativeFields.ToArray();
                        present.Add("Fields");
                        break;
                    default: additional.Add(member); break;
                }
            }
            declaration.PresentFields = present.ToArray();
            declaration.AdditionalFields = additional.ToArray();
            if (!Has(declaration, "Type"))
            {
                throw new ArgumentException("An Avro declaration requires type.");
            }
            return new AvroObjectDataType { Kind = 2, Declaration = declaration };
        }

        private static AvroFieldDataType ProjectField(RegistryValueDataType value, int depth)
        {
            if (value is not RegistryObjectValueDataType map)
            {
                throw new ArgumentException("An Avro field must be an object.");
            }
            var field = new AvroFieldDataType
            {
                Name = string.Empty,
                Type = null!,
                Doc = string.Empty,
                Default = null!,
                Order = string.Empty,
                Aliases = []
            };
            var present = new List<string>();
            var additional = new List<RegistryMemberDataType>();
            foreach (RegistryMemberDataType member in map.Members)
            {
                switch (member.Name)
                {
                    case "name": field.Name = Text(member.Value); present.Add("Name"); break;
                    case "type": field.Type = Project(member.Value, depth + 1); present.Add("Type"); break;
                    case "doc": field.Doc = Text(member.Value); present.Add("Doc"); break;
                    case "order":
                        field.Order = Text(member.Value);
                        if (field.Order is not ("ascending" or "descending" or "ignore"))
                        {
                            throw new ArgumentException("Invalid Avro field order.");
                        }
                        present.Add("Order");
                        break;
                    case "aliases": field.Aliases = Strings(member.Value); present.Add("Aliases"); break;
                    case "default": field.Default = member.Value; present.Add("Default"); break;
                    default: additional.Add(member); break;
                }
            }
            field.PresentFields = present.ToArray();
            field.AdditionalFields = additional.ToArray();
            if (!Has(field, "Name") || !Has(field, "Type"))
            {
                throw new ArgumentException("An Avro field requires name and type.");
            }
            Name(field.Name, qualified: false);
            foreach (string alias in field.Aliases)
            {
                Name(alias, qualified: false);
            }
            return field;
        }

        private static RegistryValueDataType Restore(AvroSchemaNodeDataType? node, int depth)
        {
            Depth(depth);
            if (node is AvroNameDataType name && node.GetType() == typeof(AvroNameDataType) && name.Kind == 0)
            {
                return String(name.Name);
            }
            if (node is AvroUnionDataType union && node.GetType() == typeof(AvroUnionDataType) && union.Kind == 1)
            {
                var branches = new List<RegistryValueDataType>();
                foreach (AvroSchemaNodeDataType branch in union.Branches)
                {
                    branches.Add(Restore(branch, depth + 1));
                }
                return new RegistryArrayValueDataType { Kind = 4, Items = branches.ToArray() };
            }
            if (node is not AvroObjectDataType obj || node.GetType() != typeof(AvroObjectDataType) ||
                obj.Kind != 2 || obj.Declaration is null ||
                obj.Declaration.GetType() != typeof(AvroDeclarationDataType))
            {
                throw new ArgumentException("Invalid or unsupported native Avro node.");
            }
            AvroDeclarationDataType declaration = obj.Declaration;
            var members = new List<RegistryMemberDataType>();
            foreach (string field in Presence(declaration))
            {
                RegistryValueDataType value = field switch
                {
                    "Type" => Restore(declaration.Type, depth + 1),
                    "Name" => String(declaration.Name),
                    "Namespace" => String(declaration.Namespace),
                    "Doc" => String(declaration.Doc),
                    "LogicalType" => String(declaration.LogicalType),
                    "Aliases" => StringArray(declaration.Aliases),
                    "Symbols" => StringArray(declaration.Symbols),
                    "Items" => Restore(declaration.Items, depth + 1),
                    "Values" => Restore(declaration.Values, depth + 1),
                    "Size" => Required(declaration.Size),
                    "Precision" => Required(declaration.Precision),
                    "Scale" => Required(declaration.Scale),
                    "Default" => Required(declaration.Default),
                    "Fields" => RestoreFields(declaration.Fields, depth + 1),
                    _ => throw new ArgumentException("Unknown Avro presence field: " + field)
                };
                members.Add(Member(SourceName(field), value));
            }
            AddAdditional(members, declaration.AdditionalFields, s_declarationFields);
            return new RegistryObjectValueDataType { Kind = 5, Members = members.ToArray() };
        }

        private static RegistryArrayValueDataType RestoreFields(ArrayOf<AvroFieldDataType> fields, int depth)
        {
            var result = new List<RegistryValueDataType>();
            foreach (AvroFieldDataType field in fields)
            {
                if (field is null || field.GetType() != typeof(AvroFieldDataType))
                {
                    throw new ArgumentException("Unsupported native Avro field.");
                }
                var members = new List<RegistryMemberDataType>();
                foreach (string present in Presence(field))
                {
                    RegistryValueDataType value = present switch
                    {
                        "Name" => String(field.Name),
                        "Type" => Restore(field.Type, depth + 1),
                        "Doc" => String(field.Doc),
                        "Order" => String(field.Order),
                        "Aliases" => StringArray(field.Aliases),
                        "Default" => Required(field.Default),
                        _ => throw new ArgumentException("Unknown Avro field presence: " + present)
                    };
                    members.Add(Member(SourceName(present), value));
                }
                AddAdditional(members, field.AdditionalFields, s_fieldFields);
                result.Add(new RegistryObjectValueDataType { Kind = 5, Members = members.ToArray() });
            }
            return new RegistryArrayValueDataType { Kind = 4, Items = result.ToArray() };
        }

        private static byte[] Canonical(
            AvroSchemaNodeDataType? node,
            out Dictionary<string, AvroDeclarationDataType> declarations)
        {
            declarations = new Dictionary<string, AvroDeclarationDataType>(StringComparer.Ordinal);
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output))
            {
                WriteCanonical(writer, node, string.Empty, declarations, 0);
            }
            foreach (KeyValuePair<string, AvroDeclarationDataType> named in declarations)
            {
                string scope = NamespaceOf(named.Key);
                foreach (AvroFieldDataType field in named.Value.Fields)
                {
                    if (Has(field, "Default") &&
                        !DefaultMatches(field.Default, field.Type, scope, declarations, 0))
                    {
                        throw new ArgumentException("The default of Avro field " + field.Name +
                            " does not match its schema; a union default matches the first branch.");
                    }
                }
            }
            return output.ToArray();
        }

        private static string WriteCanonical(
            Utf8JsonWriter writer,
            AvroSchemaNodeDataType? node,
            string scope,
            Dictionary<string, AvroDeclarationDataType> names,
            int depth)
        {
            Depth(depth);
            if (node is AvroNameDataType reference)
            {
                string name = reference.Name ?? throw new ArgumentException("An Avro type name is missing.");
                if (!s_primitives.Contains(name))
                {
                    name = FullName(name, scope);
                    if (!names.ContainsKey(name))
                    {
                        throw new ArgumentException("An Avro named reference is not declared: " + name);
                    }
                }
                writer.WriteStringValue(name);
                return name;
            }
            if (node is AvroUnionDataType union)
            {
                var branches = new HashSet<string>(StringComparer.Ordinal);
                writer.WriteStartArray();
                foreach (AvroSchemaNodeDataType branch in union.Branches)
                {
                    if (branch is AvroUnionDataType)
                    {
                        throw new ArgumentException("An Avro union cannot directly contain another union.");
                    }
                    if (!branches.Add(WriteCanonical(writer, branch, scope, names, depth + 1)))
                    {
                        throw new ArgumentException("An Avro union repeats a branch type.");
                    }
                }
                writer.WriteEndArray();
                return "union";
            }
            if (node is not AvroObjectDataType obj || obj.Declaration is null)
            {
                throw new ArgumentException("An Avro schema node is missing.");
            }
            AvroDeclarationDataType declaration = obj.Declaration;
            if (declaration.Type is not AvroNameDataType typeName || typeName.Name is null)
            {
                throw new ArgumentException("An Avro declaration requires a String type.");
            }
            string type = typeName.Name;
            if (s_primitives.Contains(type))
            {
                writer.WriteStringValue(type);
                return type;
            }
            if (type is not ("record" or "error" or "enum" or "fixed" or "array" or "map"))
            {
                return WriteCanonical(writer, typeName, scope, names, depth + 1);
            }
            string identity = type;
            writer.WriteStartObject();
            if (type is "record" or "error" or "enum" or "fixed")
            {
                if (!Has(declaration, "Name"))
                {
                    throw new ArgumentException("An Avro named type requires name.");
                }
                string ns = Has(declaration, "Namespace") ? declaration.Namespace! : scope;
                if (ns.Length > 0)
                {
                    Name(ns, qualified: true);
                }
                identity = FullName(declaration.Name!, ns);
                int separator = identity.AsSpan().LastIndexOf('.');
                if (s_primitives.Contains(identity.Substring(separator + 1)))
                {
                    throw new ArgumentException("A named Avro type cannot use a primitive type name.");
                }
                foreach (string alias in declaration.Aliases)
                {
                    Name(alias, qualified: true);
                }
                if (names.ContainsKey(identity))
                {
                    throw new ArgumentException("An Avro full name is declared more than once: " + identity);
                }
                names.Add(identity, declaration);
                scope = NamespaceOf(identity);
                writer.WriteString("name", identity);
            }
            writer.WriteString("type", type == "error" ? "record" : type);
            switch (type)
            {
                case "record":
                case "error":
                    if (!Has(declaration, "Fields"))
                    {
                        throw new ArgumentException("An Avro record requires fields.");
                    }
                    writer.WriteStartArray("fields");
                    var fields = new HashSet<string>(StringComparer.Ordinal);
                    foreach (AvroFieldDataType field in declaration.Fields)
                    {
                        Name(field.Name, qualified: false);
                        if (!fields.Add(field.Name!))
                        {
                            throw new ArgumentException("An Avro record repeats a field name.");
                        }
                        writer.WriteStartObject();
                        writer.WriteString("name", field.Name);
                        writer.WritePropertyName("type");
                        WriteCanonical(writer, field.Type, scope, names, depth + 1);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    break;
                case "enum":
                    if (!Has(declaration, "Symbols"))
                    {
                        throw new ArgumentException("An Avro enum requires symbols.");
                    }
                    writer.WriteStartArray("symbols");
                    var symbols = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string symbol in declaration.Symbols)
                    {
                        Name(symbol, qualified: false);
                        if (!symbols.Add(symbol))
                        {
                            throw new ArgumentException("An Avro enum repeats a symbol.");
                        }
                        writer.WriteStringValue(symbol);
                    }
                    writer.WriteEndArray();
                    if (Has(declaration, "Default") &&
                        (declaration.Default is not RegistryStringValueDataType defaultSymbol ||
                            !symbols.Contains(defaultSymbol.Value!)))
                    {
                        throw new ArgumentException("An enum default must be one of its symbols.");
                    }
                    break;
                case "fixed":
                    writer.WritePropertyName("size");
                    writer.WriteRawValue(Size(declaration.Size).ToString(CultureInfo.InvariantCulture),
                        skipInputValidation: false);
                    break;
                case "array":
                    writer.WritePropertyName("items");
                    WriteCanonical(writer, declaration.Items, scope, names, depth + 1);
                    break;
                case "map":
                    writer.WritePropertyName("values");
                    WriteCanonical(writer, declaration.Values, scope, names, depth + 1);
                    break;
            }
            writer.WriteEndObject();
            return identity;
        }

        private static BigInteger Size(RegistryValueDataType? value)
        {
            if (value is not RegistryNumberValueDataType number || !number.IsInteger || number.Exponent != 0)
            {
                throw new ArgumentException("Avro size must use an integral JSON source form.");
            }
            byte[] bytes = number.Coefficient.ToArray();
            Array.Reverse(bytes);
            var coefficient = new BigInteger(bytes);
            if (coefficient.Sign < 0)
            {
                throw new ArgumentException("Avro size must be non-negative.");
            }
            return coefficient;
        }

        private static string NamespaceOf(string fullName)
        {
            int separator = fullName.AsSpan().LastIndexOf('.');
            return separator < 0 ? string.Empty : fullName.Substring(0, separator);
        }

        private static bool DefaultMatches(
            RegistryValueDataType? value,
            AvroSchemaNodeDataType? schema,
            string scope,
            Dictionary<string, AvroDeclarationDataType> names,
            int depth)
        {
            Depth(depth);
            if (schema is AvroUnionDataType union)
            {
                return union.Branches.Count > 0 &&
                    DefaultMatches(value, union.Branches[0], scope, names, depth + 1);
            }
            if (schema is AvroObjectDataType wrapped && wrapped.Declaration?.Type is AvroNameDataType namedType &&
                namedType.Name is not ("record" or "error" or "enum" or "fixed" or "array" or "map"))
            {
                schema = namedType;
            }
            if (schema is AvroNameDataType name)
            {
                switch (name.Name)
                {
                    case "null": return value is RegistryNullValueDataType;
                    case "boolean": return value is RegistryBooleanValueDataType;
                    case "string": return value is RegistryStringValueDataType;
                    case "bytes": return value is RegistryStringValueDataType binary && IsBytes(binary.Value);
                    case "float":
                    case "double": return value is RegistryNumberValueDataType;
                    case "int":
                    case "long":
                        if (value is not RegistryNumberValueDataType integer || !integer.IsInteger)
                        {
                            return false;
                        }
                        byte[] bytes = integer.Coefficient.ToArray();
                        Array.Reverse(bytes);
                        var number = new BigInteger(bytes);
                        return name.Name == "int"
                            ? number >= int.MinValue && number <= int.MaxValue
                            : number >= long.MinValue && number <= long.MaxValue;
                }
                string fullName = FullName(name.Name!, scope);
                if (!names.TryGetValue(fullName, out AvroDeclarationDataType? declaration))
                {
                    return false;
                }
                schema = new AvroObjectDataType { Kind = 2, Declaration = declaration };
                scope = NamespaceOf(fullName);
            }
            if (schema is not AvroObjectDataType obj || obj.Declaration?.Type is not AvroNameDataType kind)
            {
                return false;
            }
            AvroDeclarationDataType record = obj.Declaration;
            switch (kind.Name)
            {
                case "array":
                    if (value is not RegistryArrayValueDataType array)
                    {
                        return false;
                    }
                    foreach (RegistryValueDataType item in array.Items)
                    {
                        if (!DefaultMatches(item, record.Items, scope, names, depth + 1))
                        {
                            return false;
                        }
                    }
                    return true;
                case "map":
                    if (value is not RegistryObjectValueDataType map)
                    {
                        return false;
                    }
                    foreach (RegistryMemberDataType member in map.Members)
                    {
                        if (!DefaultMatches(member.Value, record.Values, scope, names, depth + 1))
                        {
                            return false;
                        }
                    }
                    return true;
                case "enum":
                    if (value is not RegistryStringValueDataType symbol)
                    {
                        return false;
                    }
                    foreach (string declared in record.Symbols)
                    {
                        if (declared == symbol.Value)
                        {
                            return true;
                        }
                    }
                    return false;
                case "fixed":
                    return value is RegistryStringValueDataType fixedBytes && IsBytes(fixedBytes.Value) &&
                        fixedBytes.Value!.Length == Size(record.Size);
                case "record":
                case "error":
                    if (value is not RegistryObjectValueDataType fields)
                    {
                        return false;
                    }
                    string inner = NamespaceOf(FullName(record.Name!,
                        Has(record, "Namespace") ? record.Namespace! : scope));
                    foreach (AvroFieldDataType field in record.Fields)
                    {
                        RegistryValueDataType? memberValue = null;
                        foreach (RegistryMemberDataType member in fields.Members)
                        {
                            if (member.Name == field.Name)
                            {
                                memberValue = member.Value;
                                break;
                            }
                        }
                        if (memberValue is null ? !Has(field, "Default") :
                            !DefaultMatches(memberValue, field.Type, inner, names, depth + 1))
                        {
                            return false;
                        }
                    }
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsBytes(string? value)
        {
            if (value is null)
            {
                return false;
            }
            foreach (char c in value)
            {
                if (c > 255)
                {
                    return false;
                }
            }
            return true;
        }

        private static string FullName(string name, string scope)
        {
            Name(name, qualified: true);
            return name.AsSpan().IndexOf('.') >= 0 || scope.Length == 0 ? name : scope + "." + name;
        }

        private static void Name(string? name, bool qualified)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("An Avro name cannot be empty.");
            }
            foreach (string part in qualified ? name.Split('.') : new[] { name })
            {
                bool Letter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';
                if (part.Length == 0 || !Letter(part[0]))
                {
                    throw new ArgumentException("Invalid Avro name: " + name);
                }
                foreach (char c in part)
                {
                    if (!Letter(c) && c is not (>= '0' and <= '9'))
                    {
                        throw new ArgumentException("Invalid Avro name: " + name);
                    }
                }
            }
        }

        private static RegistryValueDataType Required(RegistryValueDataType? value)
        {
            return value ?? throw new ArgumentException("A present Avro value cannot be a null ExtensionObject.");
        }

        private static RegistryStringValueDataType String(string? value)
        {
            return new RegistryStringValueDataType
            {
                Kind = 2,
                Value = value ?? throw new ArgumentException("A present Avro String cannot be null.")
            };
        }

        private static string Text(RegistryValueDataType? value)
        {
            return value is RegistryStringValueDataType text && text.Value is not null
                ? text.Value
                : throw new ArgumentException("An Avro textual attribute requires a String.");
        }

        private static ArrayOf<string> Strings(RegistryValueDataType? value)
        {
            if (value is not RegistryArrayValueDataType array)
            {
                throw new ArgumentException("An Avro string list must be an array.");
            }
            var result = new List<string>();
            foreach (RegistryValueDataType item in array.Items)
            {
                result.Add(Text(item));
            }
            return result.ToArray();
        }

        private static RegistryArrayValueDataType StringArray(ArrayOf<string> values)
        {
            var items = new List<RegistryValueDataType>();
            foreach (string value in values)
            {
                items.Add(String(value));
            }
            return new RegistryArrayValueDataType { Kind = 4, Items = items.ToArray() };
        }

        private static RegistryMemberDataType Member(string name, RegistryValueDataType value)
        {
            return new RegistryMemberDataType { Name = name, Value = value };
        }

        private static ArrayOf<string> Presence(RegistryRecordDataType record)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string field in record.PresentFields)
            {
                if (field is null || !names.Add(field))
                {
                    throw new ArgumentException("Invalid or duplicate native presence field.");
                }
            }
            return record.PresentFields;
        }

        private static bool Has(RegistryRecordDataType record, string field)
        {
            foreach (string present in record.PresentFields)
            {
                if (present == field)
                {
                    return true;
                }
            }
            return false;
        }

        private static string SourceName(string native)
        {
            return char.ToLowerInvariant(native[0]) + native.Substring(1);
        }

        private static void AddAdditional(
            List<RegistryMemberDataType> members,
            ArrayOf<RegistryMemberDataType> additional,
            HashSet<string> known)
        {
            foreach (RegistryMemberDataType member in additional)
            {
                if (member.Name is null || known.Contains(member.Name))
                {
                    throw new ArgumentException("An Avro extension cannot shadow a known field.");
                }
                members.Add(member);
            }
        }

        private static void Depth(int depth)
        {
            if (depth >= 128)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
        }

        private static ulong[] FingerprintTable()
        {
            var table = new ulong[256];
            for (int index = 0; index < table.Length; index++)
            {
                ulong value = (uint)index;
                for (int bit = 0; bit < 8; bit++)
                {
                    value = (value >> 1) ^ ((value & 1) == 0 ? 0 : kEmpty);
                }
                table[index] = value;
            }
            return table;
        }

        private const ulong kEmpty = 0xc15d213aa4d7a795;
        private static readonly ulong[] s_fingerprintTable = FingerprintTable();
        private static readonly HashSet<string> s_primitives = new(
            ["null", "boolean", "int", "long", "float", "double", "bytes", "string"], StringComparer.Ordinal);
        private static readonly HashSet<string> s_declarationFields = new(
            ["type", "name", "namespace", "doc", "logicalType", "aliases", "fields", "symbols", "items", "values",
                "size", "precision", "scale", "default"], StringComparer.Ordinal);
        private static readonly HashSet<string> s_fieldFields = new(
            ["name", "type", "doc", "default", "order", "aliases"], StringComparer.Ordinal);
    }
}
