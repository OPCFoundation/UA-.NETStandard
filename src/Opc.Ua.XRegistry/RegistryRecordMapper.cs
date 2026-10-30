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
using System.Text;

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Maps between a generic registry document and its generated named native record, reflection-free.
    /// </summary>
    /// <remarks>
    /// Every known source member becomes exactly one named field; PresentFields lists the authored fields
    /// in declaration order and AdditionalFields keeps only unknown members. Absent fields carry canonical
    /// unused values (empty String, false, empty array, null ExtensionObject); defaults are never inserted.
    /// A sibling selector chooses the subtype of a selector family; an absent or undeclared selector keeps
    /// the content exactly in the family fallback. Records are constructed through their generated Decode
    /// and read through their generated Encode, so no reflection or JSON is involved. Restoration re-projects
    /// the restored document and requires the identical native record, so a contradictory record, a wrong
    /// family subtype or a non-canonical absent value is rejected instead of silently normalized.
    /// </remarks>
    public sealed class RegistryRecordMapper
    {
        /// <summary>
        /// Creates a mapper without value adapters. Adapter-owned DataTypes, such as typed schema content,
        /// are then rejected explicitly.
        /// </summary>
        public RegistryRecordMapper(RegistryNativeCatalog catalog, IServiceMessageContext context)
            : this(catalog, context, [])
        {
        }

        /// <summary>
        /// Creates a mapper that delegates adapter-owned DataTypes to explicit value adapters.
        /// </summary>
        /// <exception cref="ArgumentException">An adapter claims an undeclared or already owned DataType.</exception>
        public RegistryRecordMapper(
            RegistryNativeCatalog catalog,
            IServiceMessageContext context,
            ArrayOf<IRegistryNativeValueAdapter> adapters)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Context = context ?? throw new ArgumentNullException(nameof(context));
            foreach (IRegistryNativeValueAdapter adapter in adapters)
            {
                if (adapter is null)
                {
                    throw new ArgumentException("A native value adapter is null.", nameof(adapters));
                }
                foreach (string dataType in adapter.DataTypes)
                {
                    if (dataType is null ||
                        !catalog.TryGetType(dataType, out RegistryNativeTypeDescriptor? type) ||
                        type.IsAbstract || catalog.TryGetMap(dataType, out _) ||
                        catalog.IsSubtype(dataType, RegistryNativeCatalog.ValueType) ||
                        !m_adapters.TryAdd(dataType, adapter))
                    {
                        throw new ArgumentException(
                            "An adapter must own a distinct concrete non-value catalog DataType: " + dataType,
                            nameof(adapters));
                    }
                }
            }
        }

        /// <summary>
        /// Gets the published native catalog.
        /// </summary>
        public RegistryNativeCatalog Catalog { get; }

        /// <summary>
        /// Gets the message context used to traverse and construct generated encodeables.
        /// </summary>
        public IServiceMessageContext Context { get; }

        /// <summary>
        /// Projects a generic document to the named native record with DataType NodeId
        /// <paramref name="dataTypeId"/>.
        /// </summary>
        /// <exception cref="RegistryRecordMappingException">The document cannot be represented exactly.</exception>
        public RegistryRecordDataType Project(RegistryValueDataType document, ExpandedNodeId dataTypeId)
        {
            if (!Catalog.TryGetType(dataTypeId, out RegistryNativeTypeDescriptor? type) &&
                (!string.IsNullOrEmpty(dataTypeId.NamespaceUri) ||
                    Context.NamespaceUris.GetString(dataTypeId.NamespaceIndex) is not string namespaceUri ||
                    !Catalog.TryGetType(dataTypeId.WithNamespaceUri(namespaceUri), out type)))
            {
                throw new RegistryRecordMappingException(
                    StatusCodes.BadNotSupported,
                    "The native record DataType is not published: " + dataTypeId,
                    []);
            }
            return Project(document, type.Name);
        }

        /// <summary>
        /// Projects a generic document to the named native record DataType <paramref name="dataType"/>.
        /// </summary>
        /// <exception cref="RegistryRecordMappingException">The document cannot be represented exactly.</exception>
        public RegistryRecordDataType Project(RegistryValueDataType document, string dataType)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            var operation = new Operation();
            if (dataType is null || !Catalog.TryGetType(dataType, out RegistryNativeTypeDescriptor? type) ||
                !Catalog.IsSubtype(dataType, RegistryNativeCatalog.RecordType))
            {
                throw operation.Fail(StatusCodes.BadNotSupported, "A published native record DataType is required: "
                    + dataType);
            }
            return (RegistryRecordDataType)ProjectRecord(document, type, 0, operation);
        }

        /// <summary>
        /// Restores the exact generic document of a native record. The record must be the unique projection
        /// of that document.
        /// </summary>
        /// <exception cref="RegistryRecordMappingException">The record is contradictory or not canonical.</exception>
        public RegistryValueDataType Restore(RegistryRecordDataType record)
        {
            if (record is null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            var operation = new Operation();
            RegistryNativeTypeDescriptor type = Resolve(record, operation);
            if (!Catalog.IsSubtype(type.Name, RegistryNativeCatalog.RecordType))
            {
                throw operation.Fail(StatusCodes.BadNotSupported, "A native record is required: " + type.Name);
            }
            RegistryValueDataType document = RestoreRecord(record, type, 0, operation);
            IEncodeable expected = ProjectRecord(document, type, 0, new Operation());
            if (!NativeEquals(expected, record))
            {
                throw operation.Invalid("The native record and its selected field subtypes or values disagree.");
            }
            return document;
        }

        /// <summary>
        /// Returns the canonical form of a record built by application code: fields listed in
        /// PresentFields keep their values, every unlisted field receives its canonical unused value
        /// (generated constructors pre-initialize Structure fields), and field subtypes are selected
        /// from the authored selectors. Presence is never inferred from a value.
        /// </summary>
        /// <exception cref="RegistryRecordMappingException">A present value is invalid.</exception>
        public RegistryRecordDataType Canonicalize(RegistryRecordDataType record)
        {
            if (record is null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            var operation = new Operation { IgnoreAbsentValues = true };
            RegistryNativeTypeDescriptor type = Resolve(record, operation);
            if (!Catalog.IsSubtype(type.Name, RegistryNativeCatalog.RecordType))
            {
                throw operation.Fail(StatusCodes.BadNotSupported, "A native record is required: " + type.Name);
            }
            RegistryValueDataType document = RestoreRecord(record, type, 0, operation);
            return (RegistryRecordDataType)ProjectRecord(document, type, 0, new Operation());
        }

        private IEncodeable ProjectRecord(
            RegistryValueDataType value,
            RegistryNativeTypeDescriptor type,
            int depth,
            Operation operation)
        {
            if (Math.Max(depth, operation.Records) > MaxDepth)
            {
                throw operation.Limit();
            }
            if (!Catalog.IsSubtype(type.Name, RegistryNativeCatalog.RecordType))
            {
                throw operation.Fail(StatusCodes.BadNotSupported, "A native record DataType is required: " + type.Name);
            }
            if (IsFallback(type))
            {
                return ProjectFallback(value, type, operation);
            }
            if (value is not RegistryObjectValueDataType document || !IsExact(value, 5))
            {
                throw operation.Invalid("A native record requires an Object: " + type.Name);
            }
            if (type.IsAbstract)
            {
                throw operation.Invalid("An abstract record DataType cannot be instantiated: " + type.Name);
            }
            ArrayOf<RegistryNativeFieldDescriptor> fields = Catalog.GetFields(type.Name);
            Dictionary<string, RegistryValueDataType> members = Members(document, operation);
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (RegistryNativeFieldDescriptor field in fields)
            {
                if (field.Source.Length > 0)
                {
                    known.Add(field.Source);
                }
            }
            operation.Take();
            operation.Records++;
            try
            {
                var values = new ProjectedField[fields.Count];
                var present = new List<string?>();
                for (int index = 0; index < fields.Count; index++)
                {
                    RegistryNativeFieldDescriptor field = fields[index];
                    if (field.Source.Length == 0 ||
                        !members.TryGetValue(field.Source, out RegistryValueDataType? member))
                    {
                        values[index] = Unused(field, operation);
                        continue;
                    }
                    present.Add(field.Name);
                    string choice = field.AllowSubtypes ? Select(field.DataType, members, operation) : field.DataType;
                    operation.Path.Add(field.Source);
                    values[index] = ProjectField(member, choice, field.IsArray, depth + 1, field.Shape, operation);
                    operation.Path.RemoveAt(operation.Path.Count - 1);
                }
                var additional = new List<IEncodeable>();
                foreach (RegistryMemberDataType member in document.Members)
                {
                    if (!known.Contains(member.Name!))
                    {
                        operation.Path.Add(member.Name!);
                        additional.Add(new RegistryMemberDataType
                        {
                            Name = member.Name,
                            Value = CopyValue(member.Value, 0, operation)
                        });
                        operation.Path.RemoveAt(operation.Path.Count - 1);
                    }
                }
                values[IndexOf(fields, RegistryNativeCatalog.PresentFieldsField)] =
                    ProjectedField.FromStrings(present.ToArray());
                values[IndexOf(fields, RegistryNativeCatalog.AdditionalFieldsField)] =
                    ProjectedField.FromStructures([.. additional]);
                return Create(type, fields, values, operation);
            }
            finally
            {
                operation.Records--;
            }
        }

        private IEncodeable ProjectFallback(
            RegistryValueDataType value,
            RegistryNativeTypeDescriptor type,
            Operation operation)
        {
            ArrayOf<RegistryNativeFieldDescriptor> fields = Catalog.GetFields(type.Name);
            operation.Take();
            var values = new ProjectedField[fields.Count];
            for (int index = 0; index < fields.Count; index++)
            {
                values[index] = Unused(fields[index], operation);
            }
            int holder = IndexOf(fields, RegistryNativeCatalog.ExtensionValueField, required: false);
            if (value is RegistryObjectValueDataType document && IsExact(value, 5))
            {
                Members(document, operation);
                var members = new IEncodeable[document.Members.Count];
                for (int index = 0; index < members.Length; index++)
                {
                    RegistryMemberDataType member = document.Members[index];
                    operation.Path.Add(member.Name!);
                    members[index] = new RegistryMemberDataType
                    {
                        Name = member.Name,
                        Value = CopyValue(member.Value, 0, operation)
                    };
                    operation.Path.RemoveAt(operation.Path.Count - 1);
                }
                values[IndexOf(fields, RegistryNativeCatalog.AdditionalFieldsField)] =
                    ProjectedField.FromStructures(members);
            }
            else if (holder >= 0)
            {
                values[holder] = ProjectedField.FromStructure(CopyValue(value, 0, operation));
            }
            else
            {
                throw operation.Invalid("Unknown selector content must be an Object for " + type.Name);
            }
            return Create(type, fields, values, operation);
        }

        private ProjectedField ProjectField(
            RegistryValueDataType value,
            string dataType,
            bool isArray,
            int depth,
            RegistrySourceShape? shape,
            Operation operation)
        {
            if (depth > MaxDepth)
            {
                throw operation.Limit();
            }
            if (!isArray)
            {
                return dataType switch
                {
                    "String" => ProjectedField.FromString(Text(value, operation)),
                    "Boolean" => ProjectedField.FromBoolean(Flag(value, operation)),
                    _ => ProjectedField.FromStructure(ProjectStructure(value, dataType, depth, shape, operation))
                };
            }
            if (value is not RegistryArrayValueDataType array || !IsExact(value, 4) || array.Items.IsNull)
            {
                throw operation.Invalid("An array value is required.");
            }
            int count = array.Items.Count;
            if (dataType is "String" or "Boolean")
            {
                operation.Take(count);
            }
            var texts = new string?[dataType == "String" ? count : 0];
            var flags = new bool[dataType == "Boolean" ? count : 0];
            var structures = new IEncodeable[dataType is "String" or "Boolean" ? 0 : count];
            for (int index = 0; index < count; index++)
            {
                operation.Path.Add(index.ToString(CultureInfo.InvariantCulture));
                RegistryValueDataType item = array.Items[index];
                if (dataType == "String")
                {
                    texts[index] = Text(item, operation);
                }
                else if (dataType == "Boolean")
                {
                    flags[index] = Flag(item, operation);
                }
                else
                {
                    structures[index] = ProjectStructure(item, dataType, depth + 1, shape?.Item, operation);
                }
                operation.Path.RemoveAt(operation.Path.Count - 1);
            }
            return dataType switch
            {
                "String" => ProjectedField.FromStrings(texts),
                "Boolean" => ProjectedField.FromBooleans(flags),
                _ => ProjectedField.FromStructures(structures)
            };
        }

        private IEncodeable ProjectStructure(
            RegistryValueDataType value,
            string dataType,
            int depth,
            RegistrySourceShape? shape,
            Operation operation)
        {
            if (Catalog.IsSubtype(dataType, RegistryNativeCatalog.ValueType))
            {
                RegistryValueDataType copy = CopyValue(value, 0, operation);
                CheckShape(copy, shape, 0, operation);
                return Catalog.IsSubtype(ValueTypeName(copy), dataType)
                    ? copy
                    : throw operation.Invalid("The generic value has the wrong native subtype: " + dataType);
            }
            if (Catalog.TryGetMap(dataType, out RegistryNativeMapDescriptor? map))
            {
                return ProjectMap(value, map, depth, operation);
            }
            if (m_adapters.TryGetValue(dataType, out IRegistryNativeValueAdapter? adapter))
            {
                operation.Take();
                IEncodeable? projected;
                try
                {
                    projected = adapter.Project(value, dataType, this);
                }
                catch (Exception error) when (error is not RegistryRecordMappingException)
                {
                    throw operation.Wrap(error, dataType);
                }
                if (projected is null ||
                    !Catalog.TryGetType(projected.TypeId, out RegistryNativeTypeDescriptor? actual) ||
                    !string.Equals(actual.Name, dataType, StringComparison.Ordinal))
                {
                    throw operation.Invalid("The native value adapter returned another DataType than " + dataType);
                }
                return projected;
            }
            if (!Catalog.TryGetType(dataType, out RegistryNativeTypeDescriptor? type) ||
                !Catalog.IsSubtype(dataType, RegistryNativeCatalog.RecordType))
            {
                throw operation.Fail(StatusCodes.BadNotSupported,
                    "No native record, map or value adapter is available for " + dataType);
            }
            return ProjectRecord(value, type, depth, operation);
        }

        private IEncodeable ProjectMap(
            RegistryValueDataType value,
            RegistryNativeMapDescriptor map,
            int depth,
            Operation operation)
        {
            if (value is not RegistryObjectValueDataType document || !IsExact(value, 5))
            {
                throw operation.Invalid("A map value requires an Object: " + map.MapType);
            }
            Members(document, operation);
            operation.Take(1 + document.Members.Count);
            RegistryNativeTypeDescriptor entryType = Descriptor(map.EntryType);
            ArrayOf<RegistryNativeFieldDescriptor> entryFields = Catalog.GetFields(map.EntryType);
            var entries = new IEncodeable[document.Members.Count];
            for (int index = 0; index < entries.Length; index++)
            {
                RegistryMemberDataType member = document.Members[index];
                operation.Path.Add(member.Name!);
                ProjectedField entryValue = ProjectField(
                    member.Value, map.ValueType, false, depth + 1, map.ValueShape, operation);
                entries[index] = Create(
                    entryType,
                    entryFields,
                    [ProjectedField.FromString(member.Name!), entryValue],
                    operation);
                operation.Path.RemoveAt(operation.Path.Count - 1);
            }
            RegistryNativeTypeDescriptor mapType = Descriptor(map.MapType);
            return Create(mapType, Catalog.GetFields(map.MapType), [ProjectedField.FromStructures(entries)],
                operation);
        }

        private RegistryValueDataType RestoreRecord(
            IEncodeable record,
            RegistryNativeTypeDescriptor type,
            int depth,
            Operation operation)
        {
            if (Math.Max(depth, operation.Records) > MaxDepth)
            {
                throw operation.Limit();
            }
            if (IsFallback(type))
            {
                return RestoreFallback(record, type, operation);
            }
            if (type.IsAbstract)
            {
                throw operation.Invalid("An abstract record DataType has no value: " + type.Name);
            }
            ArrayOf<RegistryNativeFieldDescriptor> fields = Catalog.GetFields(type.Name);
            Variant[] native = ReadFields(record, type, fields, operation);
            var present = new HashSet<string>(StringComparer.Ordinal);
            var sources = new HashSet<string>(StringComparer.Ordinal);
            var sourced = new HashSet<string>(StringComparer.Ordinal);
            foreach (RegistryNativeFieldDescriptor field in fields)
            {
                if (field.Source.Length > 0)
                {
                    sources.Add(field.Source);
                    sourced.Add(field.Name);
                }
            }
            foreach (string? name in Strings(native[IndexOf(fields, RegistryNativeCatalog.PresentFieldsField)],
                operation))
            {
                if (name is null || !sourced.Contains(name) || !present.Add(name))
                {
                    throw operation.Invalid("Invalid record field presence: " + name);
                }
            }
            operation.Take();
            operation.Records++;
            try
            {
                var members = new List<RegistryMemberDataType>();
                for (int index = 0; index < fields.Count; index++)
                {
                    RegistryNativeFieldDescriptor field = fields[index];
                    if (field.Source.Length == 0)
                    {
                        continue;
                    }
                    if (!present.Contains(field.Name))
                    {
                        if (!operation.IgnoreAbsentValues && !IsUnused(native[index], field))
                        {
                            throw operation.Invalid("An absent field carries a noncanonical value: " + field.Name);
                        }
                        continue;
                    }
                    operation.Path.Add(field.Source);
                    members.Add(new RegistryMemberDataType
                    {
                        Name = field.Source,
                        Value = RestoreField(native[index], field.DataType, field.IsArray, depth + 1, operation)
                    });
                    operation.Path.RemoveAt(operation.Path.Count - 1);
                }
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (IEncodeable? item in Structures(
                    native[IndexOf(fields, RegistryNativeCatalog.AdditionalFieldsField)], operation))
                {
                    if (item is not RegistryMemberDataType member ||
                        item.GetType() != typeof(RegistryMemberDataType) ||
                        member.Name is null ||
                        sources.Contains(member.Name) ||
                        !names.Add(Unicode(member.Name, operation)))
                    {
                        throw operation.Invalid(
                            "An extension member shadows a known field, repeats a member or is malformed.");
                    }
                    operation.Path.Add(member.Name);
                    members.Add(new RegistryMemberDataType
                    {
                        Name = member.Name,
                        Value = CopyValue(member.Value, 0, operation)
                    });
                    operation.Path.RemoveAt(operation.Path.Count - 1);
                }
                return new RegistryObjectValueDataType { Kind = 5, Members = members.ToArray() };
            }
            finally
            {
                operation.Records--;
            }
        }

        private RegistryValueDataType RestoreFallback(
            IEncodeable record,
            RegistryNativeTypeDescriptor type,
            Operation operation)
        {
            ArrayOf<RegistryNativeFieldDescriptor> fields = Catalog.GetFields(type.Name);
            Variant[] native = ReadFields(record, type, fields, operation);
            int present = IndexOf(fields, RegistryNativeCatalog.PresentFieldsField);
            int additional = IndexOf(fields, RegistryNativeCatalog.AdditionalFieldsField);
            int holder = IndexOf(fields, RegistryNativeCatalog.ExtensionValueField, required: false);
            if (!IsUnused(native[present], fields[present]))
            {
                throw operation.Invalid("Unknown selector content has no present typed fields.");
            }
            for (int index = 0; index < fields.Count; index++)
            {
                if (index != present && index != additional && index != holder &&
                    !operation.IgnoreAbsentValues && !IsUnused(native[index], fields[index]))
                {
                    throw operation.Invalid("An absent field carries a noncanonical value: " + fields[index].Name);
                }
            }
            operation.Take();
            var members = new List<RegistryMemberDataType>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (IEncodeable? item in Structures(native[additional], operation))
            {
                if (item is not RegistryMemberDataType member || item.GetType() != typeof(RegistryMemberDataType) ||
                    member.Name is null || !names.Add(Unicode(member.Name, operation)))
                {
                    throw operation.Invalid("Unknown selector content repeats a member or is malformed.");
                }
                operation.Path.Add(member.Name);
                members.Add(new RegistryMemberDataType
                {
                    Name = member.Name,
                    Value = CopyValue(member.Value, 0, operation)
                });
                operation.Path.RemoveAt(operation.Path.Count - 1);
            }
            IEncodeable? value = holder >= 0 ? Structure(native[holder], operation) : null;
            if (value is null)
            {
                return new RegistryObjectValueDataType { Kind = 5, Members = members.ToArray() };
            }
            if (members.Count > 0)
            {
                throw operation.Invalid("Unknown selector content is either Object members or one value.");
            }
            if (value is not RegistryValueDataType generic)
            {
                throw operation.Invalid("The unknown selector value is not a generic registry value.");
            }
            RegistryValueDataType copy = CopyValue(generic, 0, operation);
            return copy is RegistryObjectValueDataType
                ? throw operation.Invalid("Object content under an unknown selector uses AdditionalFields.")
                : copy;
        }

        private RegistryValueDataType RestoreField(
            in Variant value,
            string dataType,
            bool isArray,
            int depth,
            Operation operation)
        {
            if (depth > MaxDepth)
            {
                throw operation.Limit();
            }
            if (!isArray)
            {
                return dataType switch
                {
                    "String" => new RegistryStringValueDataType { Kind = 2, Value = Text(value, operation) },
                    "Boolean" => new RegistryBooleanValueDataType
                    {
                        Kind = 1,
                        Value = value.TryGetValue(out bool flag)
                            ? flag
                            : throw operation.Invalid("A Boolean is required.")
                    },
                    _ => RestoreStructure(
                        Structure(value, operation) ??
                            throw operation.Invalid("A present native field is null: " + dataType),
                        dataType,
                        depth,
                        operation)
                };
            }
            var items = new List<RegistryValueDataType>();
            if (dataType == "String")
            {
                ArrayOf<string> texts = Strings(value, operation);
                operation.Take(texts.Count);
                foreach (string? text in texts)
                {
                    items.Add(new RegistryStringValueDataType
                    {
                        Kind = 2,
                        Value = Unicode(text ?? throw operation.Invalid("A String array item is null."), operation)
                    });
                }
            }
            else if (dataType == "Boolean")
            {
                if (!value.TryGetValue(out ArrayOf<bool> flags) || flags.IsNull)
                {
                    throw operation.Invalid("A Boolean array is required.");
                }
                operation.Take(flags.Count);
                foreach (bool flag in flags)
                {
                    items.Add(new RegistryBooleanValueDataType { Kind = 1, Value = flag });
                }
            }
            else
            {
                IEncodeable?[] structures = Structures(value, operation);
                for (int index = 0; index < structures.Length; index++)
                {
                    operation.Path.Add(index.ToString(CultureInfo.InvariantCulture));
                    items.Add(RestoreStructure(
                        structures[index] ?? throw operation.Invalid("A native array item is null: " + dataType),
                        dataType,
                        depth + 1,
                        operation));
                    operation.Path.RemoveAt(operation.Path.Count - 1);
                }
            }
            return new RegistryArrayValueDataType { Kind = 4, Items = items.ToArray() };
        }

        private RegistryValueDataType RestoreStructure(
            IEncodeable value,
            string dataType,
            int depth,
            Operation operation)
        {
            RegistryNativeTypeDescriptor actual = Resolve(value, operation);
            if (!Catalog.IsSubtype(actual.Name, dataType))
            {
                throw operation.Invalid("The native field has the wrong subtype: " + dataType);
            }
            if (Catalog.IsSubtype(dataType, RegistryNativeCatalog.ValueType))
            {
                return value is RegistryValueDataType generic
                    ? CopyValue(generic, 0, operation)
                    : throw operation.Invalid("A generic registry value is required: " + dataType);
            }
            if (Catalog.TryGetMap(dataType, out RegistryNativeMapDescriptor? map))
            {
                return RestoreMap(value, map, actual, depth, operation);
            }
            if (m_adapters.TryGetValue(actual.Name, out IRegistryNativeValueAdapter? adapter))
            {
                operation.Take();
                RegistryValueDataType? restored;
                try
                {
                    restored = adapter.Restore(value, actual.Name, this);
                }
                catch (Exception error) when (error is not RegistryRecordMappingException)
                {
                    throw operation.Wrap(error, actual.Name);
                }
                return restored is null
                    ? throw operation.Invalid("The native value adapter returned no value for " + actual.Name)
                    : CopyValue(restored, 0, operation);
            }
            if (!Catalog.IsSubtype(actual.Name, RegistryNativeCatalog.RecordType))
            {
                throw operation.Fail(StatusCodes.BadNotSupported,
                    "No native record or value adapter is available for " + actual.Name);
            }
            return RestoreRecord(value, actual, depth, operation);
        }

        private RegistryObjectValueDataType RestoreMap(
            IEncodeable value,
            RegistryNativeMapDescriptor map,
            RegistryNativeTypeDescriptor actual,
            int depth,
            Operation operation)
        {
            if (!string.Equals(actual.Name, map.MapType, StringComparison.Ordinal))
            {
                throw operation.Invalid("A map value must have the declared map DataType: " + map.MapType);
            }
            IEncodeable?[] entries = Structures(
                ReadFields(value, actual, Catalog.GetFields(map.MapType), operation)[0],
                operation);
            operation.Take(1 + entries.Length);
            RegistryNativeTypeDescriptor entryType = Descriptor(map.EntryType);
            ArrayOf<RegistryNativeFieldDescriptor> entryFields = Catalog.GetFields(map.EntryType);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var members = new RegistryMemberDataType[entries.Length];
            for (int index = 0; index < entries.Length; index++)
            {
                IEncodeable entry = entries[index] ?? throw operation.Invalid("A map entry is null: " + map.MapType);
                if (!string.Equals(Resolve(entry, operation).Name, map.EntryType, StringComparison.Ordinal))
                {
                    throw operation.Invalid("A map entry must have the declared entry DataType: " + map.EntryType);
                }
                Variant[] native = ReadFields(entry, entryType, entryFields, operation);
                string name = Text(native[0], operation);
                if (!names.Add(name))
                {
                    throw operation.Invalid("A map repeats the entry name " + name);
                }
                operation.Path.Add(name);
                members[index] = new RegistryMemberDataType
                {
                    Name = name,
                    Value = RestoreField(native[1], map.ValueType, false, depth + 1, operation)
                };
                operation.Path.RemoveAt(operation.Path.Count - 1);
            }
            return new RegistryObjectValueDataType { Kind = 5, Members = members };
        }

        private IEncodeable Create(
            RegistryNativeTypeDescriptor type,
            ArrayOf<RegistryNativeFieldDescriptor> fields,
            ProjectedField[] values,
            Operation operation)
        {
            IEncodeable instance = type.Factory?.Invoke() ??
                throw operation.Fail(StatusCodes.BadNotSupported, "No constructor is published for " + type.Name);
            if (instance.TypeId != type.DataTypeId)
            {
                throw operation.Fail(StatusCodes.BadInvalidState,
                    "The constructor of " + type.Name + " creates another DataType.");
            }
            using var decoder = new RegistryRecordFieldDecoder(Context, type.Name, fields, values);
            try
            {
                instance.Decode(decoder);
                decoder.Complete();
            }
            catch (Exception error) when (error is not RegistryRecordMappingException)
            {
                throw operation.Native(error, type.Name);
            }
            return instance;
        }

        private string Select(
            string dataType,
            Dictionary<string, RegistryValueDataType> members,
            Operation operation)
        {
            if (!Catalog.TryGetChoiceFamily(dataType, out RegistryNativeChoiceFamily? family))
            {
                return dataType;
            }
            if (!members.TryGetValue(family.Selector, out RegistryValueDataType? selector))
            {
                return family.Fallback;
            }
            if (selector is not RegistryStringValueDataType text || !IsExact(selector, 2))
            {
                throw operation.Invalid(family.Selector + " requires a String.");
            }
            return Catalog.SelectType(dataType, Unicode(text.Value, operation));
        }

        private ProjectedField Unused(RegistryNativeFieldDescriptor field, Operation operation)
        {
            if (field.IsArray)
            {
                return field.DataType switch
                {
                    "String" => ProjectedField.FromStrings(ArrayOf<string?>.Empty),
                    "Boolean" => ProjectedField.FromBooleans(ArrayOf<bool>.Empty),
                    _ => ProjectedField.FromStructures([])
                };
            }
            return field.DataType switch
            {
                "String" => ProjectedField.FromString(string.Empty),
                "Boolean" => ProjectedField.FromBoolean(false),
                _ when RegistryNativeCatalog.IsCoreType(field.DataType) =>
                    throw operation.Fail(StatusCodes.BadNotSupported,
                        "Native records support String and Boolean core fields only: " + field.Name),
                _ => ProjectedField.FromStructure(null)
            };
        }

        private static bool IsUnused(in Variant value, RegistryNativeFieldDescriptor field)
        {
            if (field.IsArray)
            {
                return field.DataType switch
                {
                    "String" => value.TryGetValue(out ArrayOf<string> texts) && !texts.IsNull && texts.Count == 0,
                    "Boolean" => value.TryGetValue(out ArrayOf<bool> flags) && !flags.IsNull && flags.Count == 0,
                    _ => value.TryGetValue(out ArrayOf<ExtensionObject> items) && !items.IsNull && items.Count == 0
                };
            }
            return field.DataType switch
            {
                "String" => value.TryGetValue(out string text) && text is not null && text.Length == 0,
                "Boolean" => value.TryGetValue(out bool flag) && !flag,
                _ => value.TryGetValue(out ExtensionObject extension) && extension.IsNull
            };
        }

        private Variant[] ReadFields(
            IEncodeable value,
            RegistryNativeTypeDescriptor type,
            ArrayOf<RegistryNativeFieldDescriptor> fields,
            Operation operation)
        {
            ArrayOf<RegistryNativeField> native;
            try
            {
                native = RegistryNativeFields.Read(value, Context);
            }
            catch (Exception error) when (error is not RegistryRecordMappingException)
            {
                throw operation.Native(error, type.Name);
            }
            if (native.Count != fields.Count)
            {
                throw operation.Invalid("The native fields disagree with the published DataType: " + type.Name);
            }
            var values = new Variant[fields.Count];
            for (int index = 0; index < values.Length; index++)
            {
                if (!string.Equals(native[index].Name, fields[index].Name, StringComparison.Ordinal))
                {
                    throw operation.Invalid("The native fields disagree with the published DataType: " + type.Name);
                }
                values[index] = native[index].Value;
            }
            return values;
        }

        private RegistryNativeTypeDescriptor Resolve(IEncodeable value, Operation operation)
        {
            return Catalog.TryGetType(value.TypeId, out RegistryNativeTypeDescriptor? type)
                ? type
                : throw operation.Fail(StatusCodes.BadNotSupported,
                    "The native DataType is not published by the catalog: " + value.TypeId);
        }

        private RegistryNativeTypeDescriptor Descriptor(string name)
        {
            return Catalog.TryGetType(name, out RegistryNativeTypeDescriptor? type)
                ? type
                : throw new ServiceResultException(StatusCodes.BadInvalidState, "Undeclared native DataType " + name);
        }

        private bool IsFallback(RegistryNativeTypeDescriptor type)
        {
            return Catalog.IsFallback(type.Name) && !m_adapters.ContainsKey(type.Name);
        }

        private static int IndexOf(ArrayOf<RegistryNativeFieldDescriptor> fields, string name, bool required = true)
        {
            for (int index = 0; index < fields.Count; index++)
            {
                if (string.Equals(fields[index].Name, name, StringComparison.Ordinal))
                {
                    return index;
                }
            }
            return required
                ? throw new ServiceResultException(StatusCodes.BadInvalidState, "A native record lacks " + name)
                : -1;
        }

        private static Dictionary<string, RegistryValueDataType> Members(
            RegistryObjectValueDataType document,
            Operation operation)
        {
            var members = new Dictionary<string, RegistryValueDataType>(StringComparer.Ordinal);
            foreach (RegistryMemberDataType member in document.Members)
            {
                if (member is null || member.GetType() != typeof(RegistryMemberDataType) || member.Name is null ||
                    member.Value is null || members.ContainsKey(Unicode(member.Name, operation)))
                {
                    throw operation.Invalid("An Object repeats a member name or has a malformed member.");
                }
                members.Add(member.Name, member.Value);
            }
            return members;
        }

        private RegistryValueDataType CopyValue(RegistryValueDataType? value, int depth, Operation operation)
        {
            operation.Take();
            if (depth > MaxDepth)
            {
                throw operation.Limit();
            }
            switch (value)
            {
                case RegistryNullValueDataType when IsExact(value, 0):
                    return new RegistryNullValueDataType { Kind = 0 };
                case RegistryBooleanValueDataType flag when IsExact(value, 1):
                    return new RegistryBooleanValueDataType { Kind = 1, Value = flag.Value };
                case RegistryStringValueDataType text when IsExact(value, 2):
                    return new RegistryStringValueDataType { Kind = 2, Value = Unicode(text.Value, operation) };
                case RegistryNumberValueDataType number when IsExact(value, 3):
                    Coefficient(number, operation);
                    return new RegistryNumberValueDataType
                    {
                        Kind = 3,
                        Coefficient = ByteString.From(number.Coefficient.ToArray()),
                        Exponent = number.Exponent,
                        IsInteger = number.IsInteger,
                        NegativeZero = number.NegativeZero
                    };
                case RegistryArrayValueDataType array when IsExact(value, 4):
                    if (array.Items.IsNull)
                    {
                        throw operation.Invalid("An Array requires non-null items.");
                    }
                    var items = new RegistryValueDataType[array.Items.Count];
                    for (int index = 0; index < items.Length; index++)
                    {
                        operation.Path.Add(index.ToString(CultureInfo.InvariantCulture));
                        items[index] = CopyValue(array.Items[index], depth + 1, operation);
                        operation.Path.RemoveAt(operation.Path.Count - 1);
                    }
                    return new RegistryArrayValueDataType { Kind = 4, Items = items };
                case RegistryObjectValueDataType document when IsExact(value, 5):
                    Members(document, operation);
                    var members = new RegistryMemberDataType[document.Members.Count];
                    for (int index = 0; index < members.Length; index++)
                    {
                        RegistryMemberDataType member = document.Members[index];
                        operation.Path.Add(member.Name!);
                        members[index] = new RegistryMemberDataType
                        {
                            Name = member.Name,
                            Value = CopyValue(member.Value, depth + 1, operation)
                        };
                        operation.Path.RemoveAt(operation.Path.Count - 1);
                    }
                    return new RegistryObjectValueDataType { Kind = 5, Members = members };
                default:
                    throw operation.Invalid("The generic value subtype and its discriminator disagree.");
            }
        }

        private void CheckShape(RegistryValueDataType value, RegistrySourceShape? shape, int depth, Operation operation)
        {
            if (shape is null || shape.Kind == "any")
            {
                return;
            }
            if (depth > MaxDepth)
            {
                throw operation.Limit();
            }
            switch (shape.Kind)
            {
                case "string":
                case "uri":
                case "url":
                case "xid":
                case "uritemplate":
                case "timestamp":
                    Require(value is RegistryStringValueDataType, "A String is required: " + shape.Kind, operation);
                    break;
                case "boolean":
                    Require(value is RegistryBooleanValueDataType, "A Boolean is required.", operation);
                    break;
                case "integer":
                case "uinteger":
                case "number":
                case "decimal":
                    if (value is not RegistryNumberValueDataType number)
                    {
                        throw operation.Invalid("A number is required: " + shape.Kind);
                    }
                    BigInteger coefficient = Coefficient(number, operation);
                    if (shape.Kind is "integer" or "uinteger")
                    {
                        Require(IsIntegral(number, coefficient), "An integer has a fractional part.", operation);
                    }
                    if (shape.Kind == "uinteger")
                    {
                        Require(coefficient.Sign >= 0, "An unsigned integer is negative.", operation);
                    }
                    break;
                case "array":
                    if (value is not RegistryArrayValueDataType array)
                    {
                        throw operation.Invalid("An array is required.");
                    }
                    foreach (RegistryValueDataType item in array.Items)
                    {
                        CheckShape(item, shape.Item, depth + 1, operation);
                    }
                    break;
                case "map":
                    if (value is not RegistryObjectValueDataType map)
                    {
                        throw operation.Invalid("A map is required.");
                    }
                    foreach (RegistryMemberDataType member in map.Members)
                    {
                        CheckShape(member.Value, shape.Item, depth + 1, operation);
                    }
                    break;
                case "object":
                    if (value is not RegistryObjectValueDataType document)
                    {
                        throw operation.Invalid("An Object is required.");
                    }
                    foreach (RegistrySourceShapeMember attribute in shape.Attributes)
                    {
                        foreach (RegistryMemberDataType member in document.Members)
                        {
                            if (string.Equals(member.Name, attribute.Name, StringComparison.Ordinal))
                            {
                                CheckShape(member.Value, attribute.Shape, depth + 1, operation);
                            }
                        }
                    }
                    break;
                default:
                    throw operation.Fail(StatusCodes.BadNotSupported, "Unknown source shape " + shape.Kind);
            }
        }

        private static bool IsIntegral(RegistryNumberValueDataType number, BigInteger coefficient)
        {
            if (number.IsInteger || number.Exponent >= 0 || coefficient.IsZero)
            {
                return true;
            }
            BigInteger remaining = BigInteger.Abs(coefficient);
            long zeros = 0;
            while (!remaining.IsZero && remaining % 10 == 0)
            {
                remaining /= 10;
                zeros++;
            }
            return number.Exponent != long.MinValue && zeros >= -number.Exponent;
        }

        private static BigInteger Coefficient(RegistryNumberValueDataType number, Operation operation)
        {
            ReadOnlySpan<byte> bytes = number.Coefficient.Span;
            if (bytes.IsEmpty ||
                (bytes.Length > 1 && ((bytes[0] == 0 && bytes[1] < 128) || (bytes[0] == 255 && bytes[1] >= 128))))
            {
                throw operation.Invalid("An exact number requires a minimal signed coefficient.");
            }
            byte[] reversed = bytes.ToArray();
            Array.Reverse(reversed);
            var coefficient = new BigInteger(reversed);
            if ((number.IsInteger && (number.Exponent != 0 || number.NegativeZero)) ||
                (number.NegativeZero && !coefficient.IsZero))
            {
                throw operation.Invalid("An exact number has inconsistent integral or negative-zero fields.");
            }
            return coefficient;
        }

        private static string Text(RegistryValueDataType value, Operation operation)
        {
            return value is RegistryStringValueDataType text && IsExact(value, 2)
                ? Unicode(text.Value, operation)
                : throw operation.Invalid("A String value is required.");
        }

        private static string Text(in Variant value, Operation operation)
        {
            return value.TryGetValue(out string text) && text is not null
                ? Unicode(text, operation)
                : throw operation.Invalid("A String is required.");
        }

        private static bool Flag(RegistryValueDataType value, Operation operation)
        {
            return value is RegistryBooleanValueDataType flag && IsExact(value, 1)
                ? flag.Value
                : throw operation.Invalid("A Boolean value is required.");
        }

        private static ArrayOf<string> Strings(in Variant value, Operation operation)
        {
            return value.TryGetValue(out ArrayOf<string> texts) && !texts.IsNull
                ? texts
                : throw operation.Invalid("A String array is required.");
        }

        private IEncodeable? Structure(in Variant value, Operation operation)
        {
            if (!value.TryGetValue(out ExtensionObject extension))
            {
                throw operation.Invalid("A native Structure is required.");
            }
            if (extension.IsNull)
            {
                return null;
            }
            return extension.TryGetValue(out IEncodeable? body, Context)
                ? body
                : throw operation.Invalid("A native Structure cannot be decoded.");
        }

        private IEncodeable?[] Structures(in Variant value, Operation operation)
        {
            if (!value.TryGetValue(out ArrayOf<ExtensionObject> extensions) || extensions.IsNull)
            {
                throw operation.Invalid("A native Structure array is required.");
            }
            var result = new IEncodeable?[extensions.Count];
            for (int index = 0; index < result.Length; index++)
            {
                ExtensionObject extension = extensions[index];
                if (extension.IsNull)
                {
                    continue;
                }
                result[index] = extension.TryGetValue(out IEncodeable? body, Context)
                    ? body
                    : throw operation.Invalid("A native Structure cannot be decoded.");
            }
            return result;
        }

        private bool NativeEquals(IEncodeable first, IEncodeable second)
        {
            if (first.GetType() != second.GetType() || first.TypeId != second.TypeId)
            {
                return false;
            }
            ArrayOf<RegistryNativeField> left = RegistryNativeFields.Read(first, Context);
            ArrayOf<RegistryNativeField> right = RegistryNativeFields.Read(second, Context);
            if (left.Count != right.Count)
            {
                return false;
            }
            for (int index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index].Name, right[index].Name, StringComparison.Ordinal) ||
                    !VariantEquals(left[index].Value, right[index].Value))
                {
                    return false;
                }
            }
            return true;
        }

        private bool VariantEquals(in Variant first, in Variant second)
        {
            if (first.IsNull || second.IsNull)
            {
                return first.IsNull && second.IsNull;
            }
            TypeInfo left = first.TypeInfo;
            TypeInfo right = second.TypeInfo;
            if (left.BuiltInType != right.BuiltInType || left.ValueRank != right.ValueRank)
            {
                return false;
            }
            if (left.BuiltInType == BuiltInType.ExtensionObject)
            {
                if (left.ValueRank == ValueRanks.Scalar)
                {
                    return first.TryGetValue(out ExtensionObject a) && second.TryGetValue(out ExtensionObject b) &&
                        ExtensionEquals(a, b);
                }
                if (!first.TryGetValue(out ArrayOf<ExtensionObject> items) ||
                    !second.TryGetValue(out ArrayOf<ExtensionObject> others) ||
                    items.IsNull != others.IsNull || items.Count != others.Count)
                {
                    return false;
                }
                for (int index = 0; index < items.Count; index++)
                {
                    if (!ExtensionEquals(items[index], others[index]))
                    {
                        return false;
                    }
                }
                return true;
            }
            if (left.BuiltInType == BuiltInType.String)
            {
                if (left.ValueRank == ValueRanks.Scalar)
                {
                    return first.TryGetValue(out string a) && second.TryGetValue(out string b) &&
                        string.Equals(a, b, StringComparison.Ordinal);
                }
                if (!first.TryGetValue(out ArrayOf<string> texts) || !second.TryGetValue(out ArrayOf<string> others) ||
                    texts.IsNull != others.IsNull || texts.Count != others.Count)
                {
                    return false;
                }
                for (int index = 0; index < texts.Count; index++)
                {
                    if (!string.Equals(texts[index], others[index], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
                return true;
            }
            return first.Equals(second);
        }

        private bool ExtensionEquals(ExtensionObject first, ExtensionObject second)
        {
            if (first.IsNull || second.IsNull)
            {
                return first.IsNull && second.IsNull;
            }
            return first.TryGetValue(out IEncodeable? a, Context) && second.TryGetValue(out IEncodeable? b, Context) &&
                NativeEquals(a, b);
        }

        private static string ValueTypeName(RegistryValueDataType value)
        {
            return value switch
            {
                RegistryNullValueDataType => nameof(RegistryNullValueDataType),
                RegistryBooleanValueDataType => nameof(RegistryBooleanValueDataType),
                RegistryStringValueDataType => nameof(RegistryStringValueDataType),
                RegistryNumberValueDataType => nameof(RegistryNumberValueDataType),
                RegistryArrayValueDataType => nameof(RegistryArrayValueDataType),
                RegistryObjectValueDataType => nameof(RegistryObjectValueDataType),
                _ => RegistryNativeCatalog.ValueType
            };
        }

        private static bool IsExact(RegistryValueDataType value, uint kind)
        {
            Type expected = kind switch
            {
                0 => typeof(RegistryNullValueDataType),
                1 => typeof(RegistryBooleanValueDataType),
                2 => typeof(RegistryStringValueDataType),
                3 => typeof(RegistryNumberValueDataType),
                4 => typeof(RegistryArrayValueDataType),
                _ => typeof(RegistryObjectValueDataType)
            };
            return value.GetType() == expected && value.Kind == kind;
        }

        private static void Require(bool condition, string message, Operation operation)
        {
            if (!condition)
            {
                throw operation.Invalid(message);
            }
        }

        private static string Unicode(string? value, Operation operation)
        {
            if (value is null)
            {
                throw operation.Invalid("A registry String or member name cannot be null.");
            }
            try
            {
                _ = s_utf8.GetByteCount(value);
            }
            catch (ArgumentException)
            {
                throw operation.Invalid("A registry String or member name is not valid Unicode.");
            }
            return value;
        }

        /// <summary>
        /// The element budget, record nesting and document path of one top-level mapping call.
        /// </summary>
        private sealed class Operation
        {
            public int Records { get; set; }

            /// <summary>
            /// Ignores the values of absent fields instead of requiring canonical unused values.
            /// </summary>
            public bool IgnoreAbsentValues { get; init; }

            public List<string> Path { get; } = [];

            public void Take(int count = 1)
            {
                m_remaining -= count;
                if (m_remaining < 0)
                {
                    throw Limit();
                }
            }

            public RegistryRecordMappingException Limit()
            {
                return Fail(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "The native value or record nesting limit is exceeded.");
            }

            public RegistryRecordMappingException Invalid(string message)
            {
                return Fail(StatusCodes.BadInvalidArgument, message);
            }

            public RegistryRecordMappingException Fail(StatusCode statusCode, string message)
            {
                return new RegistryRecordMappingException(statusCode, message, Path.ToArray());
            }

            public RegistryRecordMappingException Wrap(Exception error, string dataType)
            {
                StatusCode statusCode = error is ServiceResultException service
                    ? service.StatusCode
                    : StatusCodes.BadInvalidArgument;
                return new RegistryRecordMappingException(
                    statusCode,
                    "The " + dataType + " value adapter rejected the value: " + error.Message,
                    Path.ToArray(),
                    error);
            }

            public RegistryRecordMappingException Native(Exception error, string dataType)
            {
                StatusCode statusCode = error is ServiceResultException service
                    ? service.StatusCode
                    : StatusCodes.BadInvalidState;
                return new RegistryRecordMappingException(
                    statusCode,
                    "The generated encoding of " + dataType + " failed: " + error.Message,
                    Path.ToArray(),
                    error);
            }

            private int m_remaining = MaxElements;
        }

        /// <summary>
        /// The maximum record nesting and generic value depth of one mapping call.
        /// </summary>
        public const int MaxDepth = 128;

        /// <summary>
        /// The maximum number of native elements that one mapping call produces or restores.
        /// </summary>
        public const int MaxElements = 100000;

        private static readonly UTF8Encoding s_utf8 = new(false, true);
        private readonly Dictionary<string, IRegistryNativeValueAdapter> m_adapters = new(StringComparer.Ordinal);
    }
}
