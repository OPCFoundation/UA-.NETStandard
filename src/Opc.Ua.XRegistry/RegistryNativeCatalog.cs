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
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Comparison rule between a selector value and the declared keys of a choice family.
    /// </summary>
    public enum RegistrySelectorComparison
    {
        /// <summary>
        /// Keys compare ordinally.
        /// </summary>
        Ordinal = 0,

        /// <summary>
        /// Keys compare after Unicode upper-casing, as protocol and envelope selectors do.
        /// </summary>
        UpperCase = 1,

        /// <summary>
        /// Keys compare after Unicode case folding, as schema format selectors do.
        /// </summary>
        CaseFold = 2
    }

    /// <summary>
    /// A declared selector key and the concrete DataType that it selects.
    /// </summary>
    public readonly record struct RegistryNativeChoice(string Key, string DataType);

    /// <summary>
    /// A shorthand selector value and the declared key that it stands for, for example MQTT for MQTT/5.0.
    /// </summary>
    public readonly record struct RegistrySelectorAlias(string Alias, string Key);

    /// <summary>
    /// A named attribute of an Object source shape.
    /// </summary>
    public readonly record struct RegistrySourceShapeMember(string Name, RegistrySourceShape Shape);

    /// <summary>
    /// The source-model shape that a generic native value field must satisfy, for example an unsigned
    /// integer. Kind is a source model type name such as <c>uinteger</c>, <c>array</c> or <c>any</c>.
    /// </summary>
    public sealed record RegistrySourceShape(string Kind)
    {
        /// <summary>
        /// Gets the shape of array items or map values.
        /// </summary>
        public RegistrySourceShape? Item { get; init; }

        /// <summary>
        /// Gets the shapes of declared Object attributes.
        /// </summary>
        public ArrayOf<RegistrySourceShapeMember> Attributes { get; init; }
    }

    /// <summary>
    /// One declared field of a native DataType. Source is the authored member name; a field without a
    /// source (PresentFields, AdditionalFields, ExtensionValue) is never read from a document member.
    /// </summary>
    public sealed record RegistryNativeFieldDescriptor(string Name, string DataType)
    {
        /// <summary>
        /// Gets the authored source member name, or empty when the field has no source member.
        /// </summary>
        public string Source { get; init; } = string.Empty;

        /// <summary>
        /// Gets whether the field is a one-dimensional array.
        /// </summary>
        public bool IsArray { get; init; }

        /// <summary>
        /// Gets whether the field is encoded as an ExtensionObject and allows subtypes.
        /// </summary>
        public bool AllowSubtypes { get; init; }

        /// <summary>
        /// Gets the source shape checked for a generic value field.
        /// </summary>
        public RegistrySourceShape? Shape { get; init; }
    }

    /// <summary>
    /// Published metadata of one native DataType: its name, NodeId, base type, declared fields and the
    /// generated constructor used instead of reflection.
    /// </summary>
    public sealed record RegistryNativeTypeDescriptor(string Name, ExpandedNodeId DataTypeId, string BaseType)
    {
        /// <summary>
        /// Gets whether the DataType is abstract.
        /// </summary>
        public bool IsAbstract { get; init; }

        /// <summary>
        /// Gets the fields declared by this DataType, excluding inherited fields.
        /// </summary>
        public ArrayOf<RegistryNativeFieldDescriptor> Fields { get; init; } = [];

        /// <summary>
        /// Gets the constructor of a concrete DataType.
        /// </summary>
        public Func<IEncodeable>? Factory { get; init; }
    }

    /// <summary>
    /// An ordered map DataType whose Entries carry a Name and a Value of ValueType.
    /// </summary>
    public sealed record RegistryNativeMapDescriptor(string MapType, string EntryType, string ValueType)
    {
        /// <summary>
        /// Gets the source shape checked for generic map values.
        /// </summary>
        public RegistrySourceShape? ValueShape { get; init; }
    }

    /// <summary>
    /// A selector family: the sibling Selector member of a record chooses the concrete subtype of Root.
    /// An absent or undeclared selector value selects the concrete Fallback, which keeps its content exactly.
    /// </summary>
    public sealed record RegistryNativeChoiceFamily(string Root, string Selector, string Fallback)
    {
        /// <summary>
        /// Gets the selector comparison rule.
        /// </summary>
        public RegistrySelectorComparison Comparison { get; init; }

        /// <summary>
        /// Gets shorthand selector values.
        /// </summary>
        public ArrayOf<RegistrySelectorAlias> Aliases { get; init; } = [];

        /// <summary>
        /// Gets the declared keys and their subtypes.
        /// </summary>
        public ArrayOf<RegistryNativeChoice> Choices { get; init; } = [];
    }

    /// <summary>
    /// Immutable published metadata of native registry records, maps, selector families and adapter-owned
    /// DataTypes. DataType names are unique. The generic value family and RegistryRecordDataType are
    /// built in; domain and vendor DataTypes are added through a <see cref="Builder"/>.
    /// </summary>
    public sealed class RegistryNativeCatalog
    {
        private RegistryNativeCatalog(
            Dictionary<string, RegistryNativeTypeDescriptor> types,
            Dictionary<string, RegistryNativeMapDescriptor> maps,
            Dictionary<string, RegistryNativeChoiceFamily> families,
            List<string> order)
        {
            m_types = types;
            m_maps = maps;
            m_families = families;
            m_order = order;
            foreach (RegistryNativeTypeDescriptor type in types.Values)
            {
                m_byId[type.DataTypeId] = type;
            }
            foreach (RegistryNativeChoiceFamily family in families.Values)
            {
                var index = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (RegistryNativeChoice choice in family.Choices)
                {
                    index[Canonical(family, choice.Key)] = choice.DataType;
                }
                m_choices[family.Root] = index;
                m_fallbacks.Add(family.Fallback);
            }
            foreach (string name in order)
            {
                m_fields[name] = CollectFields(name);
            }
        }

        /// <summary>
        /// Gets every DataType in declaration order, including the built-in value family.
        /// </summary>
        public ArrayOf<RegistryNativeTypeDescriptor> Types
        {
            get
            {
                var result = new RegistryNativeTypeDescriptor[m_order.Count];
                for (int index = 0; index < result.Length; index++)
                {
                    result[index] = m_types[m_order[index]];
                }
                return result;
            }
        }

        /// <summary>
        /// Gets every map DataType.
        /// </summary>
        public ArrayOf<RegistryNativeMapDescriptor> Maps => new List<RegistryNativeMapDescriptor>(m_maps.Values);

        /// <summary>
        /// Gets every selector family.
        /// </summary>
        public ArrayOf<RegistryNativeChoiceFamily> ChoiceFamilies =>
            new List<RegistryNativeChoiceFamily>(m_families.Values);

        /// <summary>
        /// Creates a builder that contains only the built-in value family and record base.
        /// </summary>
        public static Builder CreateBuilder()
        {
            var builder = new Builder();
            foreach (RegistryNativeTypeDescriptor type in BuiltInTypes())
            {
                builder.AddType(type);
            }
            return builder;
        }

        /// <summary>
        /// Creates a builder that starts from this catalog, for example to register vendor DataTypes.
        /// </summary>
        public Builder ToBuilder()
        {
            var builder = new Builder();
            foreach (string name in m_order)
            {
                builder.AddType(m_types[name]);
            }
            foreach (RegistryNativeMapDescriptor map in m_maps.Values)
            {
                builder.AddMap(map);
            }
            foreach (RegistryNativeChoiceFamily family in m_families.Values)
            {
                builder.AddChoiceFamily(family);
            }
            return builder;
        }

        /// <summary>
        /// Finds a DataType by name.
        /// </summary>
        public bool TryGetType(string name, [NotNullWhen(true)] out RegistryNativeTypeDescriptor? type)
        {
            return m_types.TryGetValue(name ?? throw new ArgumentNullException(nameof(name)), out type);
        }

        /// <summary>
        /// Finds a DataType by its DataType NodeId.
        /// </summary>
        public bool TryGetType(ExpandedNodeId dataTypeId, [NotNullWhen(true)] out RegistryNativeTypeDescriptor? type)
        {
            return m_byId.TryGetValue(dataTypeId, out type);
        }

        /// <summary>
        /// Finds the entry and value DataTypes of a map DataType.
        /// </summary>
        public bool TryGetMap(string mapType, [NotNullWhen(true)] out RegistryNativeMapDescriptor? map)
        {
            return m_maps.TryGetValue(mapType ?? throw new ArgumentNullException(nameof(mapType)), out map);
        }

        /// <summary>
        /// Finds the selector family rooted at a DataType.
        /// </summary>
        public bool TryGetChoiceFamily(string root, [NotNullWhen(true)] out RegistryNativeChoiceFamily? family)
        {
            return m_families.TryGetValue(root ?? throw new ArgumentNullException(nameof(root)), out family);
        }

        /// <summary>
        /// Gets the inherited and declared fields of a DataType in encoding order.
        /// </summary>
        /// <exception cref="ArgumentException">The DataType is not declared.</exception>
        public ArrayOf<RegistryNativeFieldDescriptor> GetFields(string name)
        {
            return m_fields.TryGetValue(name ?? throw new ArgumentNullException(nameof(name)),
                out ArrayOf<RegistryNativeFieldDescriptor> fields)
                ? fields
                : throw new ArgumentException("The native DataType is not declared: " + name, nameof(name));
        }

        /// <summary>
        /// Checks whether a DataType is the base type or one of its declared subtypes.
        /// </summary>
        public bool IsSubtype(string name, string baseType)
        {
            string? current = name;
            for (int depth = 0; current is not null && depth <= m_types.Count; depth++)
            {
                if (string.Equals(current, baseType, StringComparison.Ordinal))
                {
                    return true;
                }
                current = m_types.TryGetValue(current, out RegistryNativeTypeDescriptor? type) ? type.BaseType : null;
            }
            return false;
        }

        /// <summary>
        /// Checks whether a DataType is the fallback of a selector family.
        /// </summary>
        public bool IsFallback(string name)
        {
            return m_fallbacks.Contains(name);
        }

        /// <summary>
        /// Selects the concrete DataType of a field declared with DataType <paramref name="dataType"/>.
        /// A DataType without a selector family is returned unchanged; an absent (null) or undeclared
        /// selector value selects the family fallback.
        /// </summary>
        public string SelectType(string dataType, string? selectorValue)
        {
            if (!m_families.TryGetValue(dataType ?? throw new ArgumentNullException(nameof(dataType)),
                out RegistryNativeChoiceFamily? family))
            {
                return dataType;
            }
            if (selectorValue is null)
            {
                return family.Fallback;
            }
            return m_choices[dataType].TryGetValue(Canonical(family, selectorValue), out string? selected)
                ? selected
                : family.Fallback;
        }

        internal static string Canonical(RegistryNativeChoiceFamily family, string value)
        {
            switch (family.Comparison)
            {
                case RegistrySelectorComparison.UpperCase:
                    string upper = Map(value, upper: true);
                    foreach (RegistrySelectorAlias alias in family.Aliases)
                    {
                        if (string.Equals(upper, Map(alias.Alias, upper: true), StringComparison.Ordinal))
                        {
                            return Map(alias.Key, upper: true);
                        }
                    }
                    return upper;
                case RegistrySelectorComparison.CaseFold:
                    return Map(value, upper: false);
                default:
                    return value;
            }
        }

        /// <summary>
        /// Applies the Unicode upper-case or case-fold mapping to the characters that can match an
        /// ASCII key: ASCII letters and the few non-ASCII characters whose full mapping is ASCII.
        /// Every other character is kept, so it can never match a declared (ASCII) key.
        /// </summary>
        private static string Map(string value, bool upper)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char ch in value)
            {
                if (ch < 0x80)
                {
                    builder.Append(upper ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
                    continue;
                }
                string? mapped = upper ? UpperToAscii(ch) : FoldToAscii(ch);
                if (mapped is null)
                {
                    builder.Append(ch);
                }
                else
                {
                    builder.Append(mapped);
                }
            }
            return builder.ToString();
        }

        private static string? UpperToAscii(char ch)
        {
            return ch switch
            {
                '\u00DF' => "SS",
                '\u0131' => "I",
                '\u017F' => "S",
                _ => Ligature(ch, upper: true)
            };
        }

        private static string? FoldToAscii(char ch)
        {
            return ch switch
            {
                '\u00DF' => "ss",
                '\u017F' => "s",
                '\u1E9E' => "ss",
                '\u212A' => "k",
                _ => Ligature(ch, upper: false)
            };
        }

        private static string? Ligature(char ch, bool upper)
        {
            string? ligature = ch switch
            {
                '\uFB00' => "ff",
                '\uFB01' => "fi",
                '\uFB02' => "fl",
                '\uFB03' => "ffi",
                '\uFB04' => "ffl",
                '\uFB05' => "st",
                '\uFB06' => "st",
                _ => null
            };
            return upper ? ligature?.ToUpperInvariant() : ligature;
        }

        private ArrayOf<RegistryNativeFieldDescriptor> CollectFields(string name)
        {
            var chain = new List<RegistryNativeTypeDescriptor>();
            string? current = name;
            while (current is not null && m_types.TryGetValue(current, out RegistryNativeTypeDescriptor? type))
            {
                chain.Insert(0, type);
                current = type.BaseType;
            }
            var fields = new List<RegistryNativeFieldDescriptor>();
            foreach (RegistryNativeTypeDescriptor type in chain)
            {
                foreach (RegistryNativeFieldDescriptor field in type.Fields)
                {
                    fields.Add(field);
                }
            }
            return fields;
        }

        private static IEnumerable<RegistryNativeTypeDescriptor> BuiltInTypes()
        {
            yield return new RegistryNativeTypeDescriptor(ValueType, DataTypeIds.RegistryValueDataType, StructureType)
            {
                IsAbstract = true,
                Fields = [new RegistryNativeFieldDescriptor("Kind", "UInt32")]
            };
            yield return Value(nameof(RegistryNullValueDataType), DataTypeIds.RegistryNullValueDataType,
                [], static () => new RegistryNullValueDataType());
            yield return Value(nameof(RegistryBooleanValueDataType), DataTypeIds.RegistryBooleanValueDataType,
                [new RegistryNativeFieldDescriptor("Value", "Boolean")],
                static () => new RegistryBooleanValueDataType());
            yield return Value(nameof(RegistryStringValueDataType), DataTypeIds.RegistryStringValueDataType,
                [new RegistryNativeFieldDescriptor("Value", "String")],
                static () => new RegistryStringValueDataType());
            yield return Value(nameof(RegistryNumberValueDataType), DataTypeIds.RegistryNumberValueDataType,
                [
                    new RegistryNativeFieldDescriptor("Coefficient", "ByteString"),
                    new RegistryNativeFieldDescriptor("Exponent", "Int64"),
                    new RegistryNativeFieldDescriptor("IsInteger", "Boolean"),
                    new RegistryNativeFieldDescriptor("NegativeZero", "Boolean")
                ],
                static () => new RegistryNumberValueDataType());
            yield return Value(nameof(RegistryArrayValueDataType), DataTypeIds.RegistryArrayValueDataType,
                [new RegistryNativeFieldDescriptor("Items", ValueType) { IsArray = true, AllowSubtypes = true }],
                static () => new RegistryArrayValueDataType());
            yield return new RegistryNativeTypeDescriptor(
                nameof(RegistryMemberDataType), DataTypeIds.RegistryMemberDataType, StructureType)
            {
                Fields =
                [
                    new RegistryNativeFieldDescriptor("Name", "String"),
                    new RegistryNativeFieldDescriptor("Value", ValueType) { AllowSubtypes = true }
                ],
                Factory = static () => new RegistryMemberDataType()
            };
            yield return Value(nameof(RegistryObjectValueDataType), DataTypeIds.RegistryObjectValueDataType,
                [new RegistryNativeFieldDescriptor("Members", nameof(RegistryMemberDataType)) { IsArray = true }],
                static () => new RegistryObjectValueDataType());
            yield return new RegistryNativeTypeDescriptor(RecordType, DataTypeIds.RegistryRecordDataType, StructureType)
            {
                IsAbstract = true,
                Fields =
                [
                    new RegistryNativeFieldDescriptor(PresentFieldsField, "String") { IsArray = true },
                    new RegistryNativeFieldDescriptor(AdditionalFieldsField, nameof(RegistryMemberDataType))
                    {
                        IsArray = true
                    }
                ]
            };
        }

        private static RegistryNativeTypeDescriptor Value(
            string name,
            ExpandedNodeId dataTypeId,
            ArrayOf<RegistryNativeFieldDescriptor> fields,
            Func<IEncodeable> factory)
        {
            return new RegistryNativeTypeDescriptor(name, dataTypeId, ValueType) { Fields = fields, Factory = factory };
        }

        /// <summary>
        /// Collects and validates native DataType metadata before a catalog is published.
        /// </summary>
        public sealed class Builder
        {
            internal Builder()
            {
            }

            /// <summary>
            /// Adds a DataType. Names and DataType NodeIds are unique.
            /// </summary>
            /// <exception cref="ArgumentException">The name or NodeId is already declared.</exception>
            public Builder AddType(RegistryNativeTypeDescriptor type)
            {
                if (type is null)
                {
                    throw new ArgumentNullException(nameof(type));
                }
                if (string.IsNullOrEmpty(type.Name) || string.IsNullOrEmpty(type.BaseType) || type.DataTypeId.IsNull)
                {
                    throw new ArgumentException("A native DataType needs a name, a base type and a NodeId.",
                        nameof(type));
                }
                if (m_types.ContainsKey(type.Name) || m_ids.Contains(type.DataTypeId))
                {
                    throw new ArgumentException("The native DataType is already declared: " + type.Name, nameof(type));
                }
                m_types.Add(type.Name, type);
                m_ids.Add(type.DataTypeId);
                m_order.Add(type.Name);
                return this;
            }

            /// <summary>
            /// Declares a map DataType, its entry DataType and its value DataType.
            /// </summary>
            /// <exception cref="ArgumentException">The map is already declared.</exception>
            public Builder AddMap(RegistryNativeMapDescriptor map)
            {
                if (map is null)
                {
                    throw new ArgumentNullException(nameof(map));
                }
                if (string.IsNullOrEmpty(map.MapType) || m_maps.ContainsKey(map.MapType))
                {
                    throw new ArgumentException("The map DataType is missing or already declared.", nameof(map));
                }
                m_maps.Add(map.MapType, map);
                return this;
            }

            /// <summary>
            /// Declares a selector family.
            /// </summary>
            /// <exception cref="ArgumentException">The family root already has a selector family.</exception>
            public Builder AddChoiceFamily(RegistryNativeChoiceFamily family)
            {
                if (family is null)
                {
                    throw new ArgumentNullException(nameof(family));
                }
                if (string.IsNullOrEmpty(family.Root) || m_families.ContainsKey(family.Root))
                {
                    throw new ArgumentException("The selector family root is missing or already declared.",
                        nameof(family));
                }
                m_families.Add(family.Root, family);
                return this;
            }

            /// <summary>
            /// Selects a (vendor) subtype for an additional selector value of an existing family.
            /// Ancestry, key collisions and the fallback are validated when the catalog is built; registering
            /// the same selector value for the same subtype again is idempotent.
            /// </summary>
            /// <exception cref="ArgumentException">No selector family is declared for the root.</exception>
            public Builder RegisterChoice(string root, string key, string dataType)
            {
                if (root is null || !m_families.TryGetValue(root, out RegistryNativeChoiceFamily? family))
                {
                    throw new ArgumentException("No selector family is declared for " + root, nameof(root));
                }
                var choices = new List<RegistryNativeChoice>(family.Choices.ToArray() ?? [])
                {
                    new(key ?? throw new ArgumentNullException(nameof(key)),
                        dataType ?? throw new ArgumentNullException(nameof(dataType)))
                };
                m_families[root] = family with { Choices = choices };
                return this;
            }

            /// <summary>
            /// Validates inheritance, fields, maps and selector families and publishes the catalog.
            /// </summary>
            /// <exception cref="InvalidOperationException">The metadata is inconsistent.</exception>
            public RegistryNativeCatalog Build()
            {
                var types = new Dictionary<string, RegistryNativeTypeDescriptor>(m_types, StringComparer.Ordinal);
                foreach (RegistryNativeTypeDescriptor type in types.Values)
                {
                    ValidateType(type, types);
                }
                foreach (RegistryNativeMapDescriptor map in m_maps.Values)
                {
                    ValidateMap(map, types);
                }
                var fallbacks = new HashSet<string>(StringComparer.Ordinal);
                foreach (RegistryNativeChoiceFamily family in m_families.Values)
                {
                    ValidateFamily(family, types, fallbacks);
                }
                return new RegistryNativeCatalog(
                    types,
                    new Dictionary<string, RegistryNativeMapDescriptor>(m_maps, StringComparer.Ordinal),
                    new Dictionary<string, RegistryNativeChoiceFamily>(m_families, StringComparer.Ordinal),
                    [.. m_order]);
            }

            private static void ValidateType(
                RegistryNativeTypeDescriptor type,
                Dictionary<string, RegistryNativeTypeDescriptor> types)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                var sources = new HashSet<string>(StringComparer.Ordinal);
                var chain = new HashSet<string>(StringComparer.Ordinal);
                RegistryNativeTypeDescriptor? current = type;
                while (current is not null)
                {
                    if (!chain.Add(current.Name))
                    {
                        throw Inconsistent("Cyclic native DataType inheritance: " + type.Name);
                    }
                    foreach (RegistryNativeFieldDescriptor field in current.Fields)
                    {
                        if (field is null || string.IsNullOrEmpty(field.Name) || string.IsNullOrEmpty(field.DataType) ||
                            !names.Add(field.Name) || (field.Source.Length > 0 && !sources.Add(field.Source)))
                        {
                            throw Inconsistent("Missing, redeclared or colliding native field in " + type.Name);
                        }
                        if (!types.ContainsKey(field.DataType) && !IsCoreType(field.DataType))
                        {
                            throw Inconsistent(
                                $"{type.Name}.{field.Name} uses the undeclared DataType {field.DataType}");
                        }
                    }
                    if (string.Equals(current.BaseType, StructureType, StringComparison.Ordinal))
                    {
                        break;
                    }
                    if (!types.TryGetValue(current.BaseType, out current))
                    {
                        throw Inconsistent("The base DataType of " + type.Name + " is not declared.");
                    }
                }
                if (!type.IsAbstract && type.Factory is null)
                {
                    throw Inconsistent("A concrete native DataType needs a constructor: " + type.Name);
                }
            }

            private static void ValidateMap(
                RegistryNativeMapDescriptor map,
                Dictionary<string, RegistryNativeTypeDescriptor> types)
            {
                if (!types.TryGetValue(map.MapType, out RegistryNativeTypeDescriptor? mapType) ||
                    !types.TryGetValue(map.EntryType ?? string.Empty, out RegistryNativeTypeDescriptor? entryType) ||
                    mapType.IsAbstract || entryType.IsAbstract ||
                    (!types.ContainsKey(map.ValueType ?? string.Empty) && !IsCoreType(map.ValueType ?? string.Empty)) ||
                    mapType.Fields.Count != 1 ||
                    !string.Equals(mapType.Fields[0].Name, "Entries", StringComparison.Ordinal) ||
                    !string.Equals(mapType.Fields[0].DataType, map.EntryType, StringComparison.Ordinal) ||
                    !mapType.Fields[0].IsArray ||
                    entryType.Fields.Count != 2 ||
                    !string.Equals(entryType.Fields[0].Name, "Name", StringComparison.Ordinal) ||
                    !string.Equals(entryType.Fields[0].DataType, "String", StringComparison.Ordinal) ||
                    !string.Equals(entryType.Fields[1].Name, "Value", StringComparison.Ordinal) ||
                    !string.Equals(entryType.Fields[1].DataType, map.ValueType, StringComparison.Ordinal))
                {
                    throw Inconsistent("The map DataType layout is not Entries[Name, Value]: " + map.MapType);
                }
            }

            private void ValidateFamily(
                RegistryNativeChoiceFamily family,
                Dictionary<string, RegistryNativeTypeDescriptor> types,
                HashSet<string> fallbacks)
            {
                if (string.IsNullOrEmpty(family.Selector) || !types.ContainsKey(family.Root) ||
                    !IsConcreteSubtype(family.Fallback, family.Root, types) || !fallbacks.Add(family.Fallback))
                {
                    throw Inconsistent("Invalid selector family or shared fallback: " + family.Root);
                }
                var keys = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (RegistrySelectorAlias alias in family.Aliases)
                {
                    if (!IsAscii(alias.Alias) || !IsAscii(alias.Key))
                    {
                        throw Inconsistent("Selector aliases are ASCII: " + family.Root);
                    }
                }
                foreach (RegistryNativeChoice choice in family.Choices)
                {
                    string key = IsAscii(choice.Key) ? Canonical(family, choice.Key) : string.Empty;
                    if (key.Length == 0 ||
                        string.Equals(choice.DataType, family.Fallback, StringComparison.Ordinal) ||
                        !IsConcreteSubtype(choice.DataType, family.Root, types) ||
                        (keys.TryGetValue(key, out string? existing) &&
                            !string.Equals(existing, choice.DataType, StringComparison.Ordinal)))
                    {
                        throw Inconsistent(
                            $"Selector value '{choice.Key}' of {family.Root} collides, is not ASCII or does not " +
                            "select a concrete family subtype.");
                    }
                    keys[key] = choice.DataType;
                }
                if (m_maps.ContainsKey(family.Root))
                {
                    throw Inconsistent("A map DataType cannot root a selector family: " + family.Root);
                }
            }

            private static bool IsConcreteSubtype(
                string? name,
                string root,
                Dictionary<string, RegistryNativeTypeDescriptor> types)
            {
                if (name is null || !types.TryGetValue(name, out RegistryNativeTypeDescriptor? type) || type.IsAbstract)
                {
                    return false;
                }
                for (int depth = 0; depth <= types.Count; depth++)
                {
                    if (string.Equals(type.Name, root, StringComparison.Ordinal))
                    {
                        return true;
                    }
                    if (!types.TryGetValue(type.BaseType, out type))
                    {
                        return false;
                    }
                }
                return false;
            }

            private static bool IsAscii(string? value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return false;
                }
                foreach (char ch in value!)
                {
                    if (ch >= 0x80)
                    {
                        return false;
                    }
                }
                return true;
            }

            private static InvalidOperationException Inconsistent(string message)
            {
                return new InvalidOperationException(message);
            }

            private readonly Dictionary<string, RegistryNativeTypeDescriptor> m_types = new(StringComparer.Ordinal);
            private readonly HashSet<ExpandedNodeId> m_ids = [];
            private readonly List<string> m_order = [];
            private readonly Dictionary<string, RegistryNativeMapDescriptor> m_maps = new(StringComparer.Ordinal);
            private readonly Dictionary<string, RegistryNativeChoiceFamily> m_families = new(StringComparer.Ordinal);
        }

        internal static bool IsCoreType(string name)
        {
            return name is "String" or "Boolean" or "UInt32" or "Int64" or "ByteString";
        }

        /// <summary>
        /// The name of the abstract base of every extensible named record.
        /// </summary>
        public const string RecordType = "RegistryRecordDataType";

        /// <summary>
        /// The name of the abstract base of the generic value family.
        /// </summary>
        public const string ValueType = "RegistryValueDataType";

        /// <summary>
        /// The name of the own field of every selector fallback that keeps a non-Object sibling value.
        /// </summary>
        public const string ExtensionValueField = "ExtensionValue";

        internal const string StructureType = "Structure";
        internal const string PresentFieldsField = "PresentFields";
        internal const string AdditionalFieldsField = "AdditionalFields";
        private readonly Dictionary<string, RegistryNativeTypeDescriptor> m_types;
        private readonly Dictionary<ExpandedNodeId, RegistryNativeTypeDescriptor> m_byId = [];
        private readonly Dictionary<string, RegistryNativeMapDescriptor> m_maps;
        private readonly Dictionary<string, RegistryNativeChoiceFamily> m_families;
        private readonly Dictionary<string, Dictionary<string, string>> m_choices = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_fallbacks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ArrayOf<RegistryNativeFieldDescriptor>> m_fields =
            new(StringComparer.Ordinal);
        private readonly List<string> m_order;
    }
}
