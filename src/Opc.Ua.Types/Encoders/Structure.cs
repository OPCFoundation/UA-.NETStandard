/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;

namespace Opc.Ua.Encoders
{
    /// <summary>
    /// The base class for all complex types.
    /// </summary>
    public class Structure :
        IEncodeable,
        IFormattable,
        IStructure,
        IStructureTypeInfo,
        IEncodeableType,
        IDataTypeDefinitionSource
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public Structure(
            XmlQualifiedName xmlName,
            ExpandedNodeId typeId,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId,
            StructureDefinition structureDefinition,
            Dictionary<string, BuiltInType> fieldTypes)
        {
            Definition = structureDefinition;
            XmlName = xmlName;
            TypeId = typeId;
            XmlEncodingId = xmlEncodingId;
            BinaryEncodingId = binaryEncodingId;
            FieldTypes = fieldTypes;
            m_layout = new SharedLayout();

            // Built now, so that a definition this type cannot represent
            // (a field without a name, two fields of one name) is reported
            // where the type is created and not where a value is decoded.
            _ = PropertyDict;
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        protected Structure(Structure structure)
        {
            Definition = structure.Definition;
            XmlName = structure.XmlName;
            TypeId = structure.TypeId;
            XmlEncodingId = structure.XmlEncodingId;
            BinaryEncodingId = structure.BinaryEncodingId;
            FieldTypes = structure.FieldTypes;
            m_layout = structure.m_layout;
            if (structure.m_propertyList != null)
            {
                m_propertyList = structure.m_propertyList.ConvertAll(p => (Field)p.Clone());
            }
            else
            {
                m_single = (Field?)structure.m_single?.Clone();
            }
        }

        /// <summary>
        /// Creates a new value of the type of <paramref name="type"/>.
        /// </summary>
        /// <remarks>
        /// The fields are created when they are first used. The definition, and
        /// with it the number of fields, comes from the peer, while a value can
        /// take a few bytes on the wire (a Union without a selected field takes
        /// four), so creating a value must not cost one object per field.
        /// </remarks>
        private protected Structure(Structure type, TypeInstance instance)
        {
            _ = instance;
            Definition = type.Definition;
            XmlName = type.XmlName;
            TypeId = type.TypeId;
            XmlEncodingId = type.XmlEncodingId;
            BinaryEncodingId = type.BinaryEncodingId;
            FieldTypes = type.FieldTypes;
            m_layout = type.m_layout;
        }

        /// <inheritdoc/>
        public Type Type => typeof(Structure);

        /// <inheritdoc/>
        public XmlQualifiedName XmlName { get; }

        /// <inheritdoc/>
        public ExpandedNodeId TypeId { get; }

        /// <inheritdoc/>
        public ExpandedNodeId BinaryEncodingId { get; }

        /// <inheritdoc/>
        public ExpandedNodeId XmlEncodingId { get; }

        /// <inheritdoc/>
        public virtual StructureType StructureType => StructureType.Structure;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(XmlNamespace);

            foreach (Field property in PropertyList)
            {
                EncodeProperty(encoder, property);
            }

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(XmlNamespace);

            foreach (Field property in PropertyList)
            {
                DecodeProperty(decoder, property);
            }

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not Structure valueBaseType)
            {
                return false;
            }

            if (valueBaseType.PropertyList.Count != PropertyList.Count)
            {
                return false;
            }

            for (int i = 0; i < PropertyList.Count; i++)
            {
                if (PropertyList[i].Value != valueBaseType.PropertyList[i].Value)
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <inheritdoc/>
        public virtual string ToString(
            string? format,
            IFormatProvider? formatProvider)
        {
            if (format == null)
            {
                var body = new StringBuilder();

                foreach (Field property in PropertyList)
                {
                    AppendPropertyValue(
                        body,
                        property.Value);
                }

                if (body.Length > 0)
                {
                    return body.Append('}').ToString();
                }

                if (!TypeId.IsNull)
                {
                    return string.Format(formatProvider, "{{{0}}}", TypeId);
                }

                return "(null)";
            }

            throw new FormatException(
                CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <inheritdoc/>
        public virtual IReadOnlyList<IStructureField> GetFields()
        {
            return PropertyList;
        }

        /// <inheritdoc/>
        public virtual Variant this[int index]
        {
            get => PropertyList[index].Value;
            set => PropertyList[index].Value = value;
        }

        /// <inheritdoc/>
        public virtual Variant this[string name]
        {
            get => PropertyDict[name].Value;
            set => PropertyDict[name].Value = value;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return new Structure(this);
        }

        /// <summary>
        /// Formatting helper.
        /// </summary>
        private static StringBuilder AddSeparator(StringBuilder body)
        {
            if (body.Length == 0)
            {
                return body.Append('{');
            }
            return body.Append('|');
        }

        /// <summary>
        /// Append a property to the value string.
        /// Handle arrays and enumerations.
        /// </summary>
        protected void AppendPropertyValue(StringBuilder body, Variant value)
        {
            AddSeparator(body).Append(value);
        }

        /// <summary>
        /// Encode a property based on the property type and value rank.
        /// </summary>
        internal static void EncodeProperty(IEncoder encoder, Field property)
        {
            EncodeProperty(encoder, property.Name!, property);
        }

        /// <summary>
        /// Decode a property based on the property type and value rank.
        /// </summary>
        internal static void DecodeProperty(IDecoder decoder, Field property)
        {
            DecodeProperty(decoder, property.Name!, property);
        }

        /// <summary>
        /// Encode a property based on the property type and value rank.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static void EncodeProperty(
            IEncoder encoder,
            string name,
            Field property)
        {
            Variant variant = property.Value;
            switch (property.TypeInfo.BuiltInType)
            {
                // IEncodeable types are handled by type property as BuiltInType.Null
                // vs ExtensionObject which allow optional and subtyped fields in structures
                case BuiltInType.Null:
                    var dataTypeId = NodeId.ToExpandedNodeId(
                        property.Definition.DataType,
                        encoder.Context.NamespaceUris);
                    if (property.TypeInfo.IsScalar)
                    {
                        encoder.WriteEncodeable(
                            name,
                            variant.GetStructure<IEncodeable>(),
                            dataTypeId);
                    }
                    else if (property.TypeInfo.IsArray)
                    {
                        encoder.WriteEncodeableArray(
                            name,
                            variant.GetStructureArray<IEncodeable>(),
                            dataTypeId);
                    }
                    else
                    {
                        encoder.WriteEncodeableMatrix(
                            name,
                            variant.GetStructureMatrix<IEncodeable>(),
                            dataTypeId);
                    }
                    break;
                case BuiltInType.Variant or
                    BuiltInType.Number or
                    BuiltInType.Integer or
                    BuiltInType.UInteger when property.TypeInfo.IsScalar:
                    encoder.WriteVariant(name, variant);
                    break;
                case BuiltInType.DiagnosticInfo when property.TypeInfo.IsScalar:
                    // A Variant cannot hold a DiagnosticInfo, so the field is
                    // null. All fields are written also when null (OPC
                    // 10000-6 5.2.1): the null DiagnosticInfo is the single
                    // encoding mask byte 0x00 in binary (5.2.2.12).
                    encoder.WriteDiagnosticInfo(name, null);
                    break;
                default:
                    // A raw binary value is written without type
                    // information, so a null Variant must still carry the
                    // field's type and shape: the default scalar, a null
                    // array (length -1) or a null inline matrix (OPC 10000-6
                    // 5.2.5), never nothing at all.
                    // XML writes a field like the typed writer of its type
                    // (OPC 10000-6 5.3.5), which needs the type of a null
                    // value too. (Json writes a null Variant as a null field.)
                    if (property.TypeInfo.BuiltInType is BuiltInType.Variant or
                        BuiltInType.Number or
                        BuiltInType.Integer or
                        BuiltInType.UInteger)
                    {
                        variant = ToVariantElements(variant);
                    }
                    if (variant.IsNull &&
                        (encoder.EncodingType == EncodingType.Binary ||
                        (encoder.EncodingType == EncodingType.Xml && !property.TypeInfo.IsMatrix)))
                    {
                        variant = Variant.CreateDefault(property.TypeInfo);
                    }
                    if (property.TypeInfo.IsMatrix)
                    {
                        encoder.WriteInlineMatrixValue(name, variant);
                    }
                    else
                    {
                        encoder.WriteVariantValue(name, variant);
                    }
                    break;
            }
        }

        /// <summary>
        /// OPC 10000-6 5.1.6: the elements of a BaseDataType, Number, Integer
        /// or UInteger array or matrix field are Variants, which is how every
        /// decoder reads them. A value of a concrete type (e.g. Int32[]) is
        /// lifted into Variants so it is not written as a raw typed array.
        /// </summary>
        private static Variant ToVariantElements(in Variant value)
        {
            if (value.IsNull ||
                value.TypeInfo.IsScalar ||
                value.TypeInfo.BuiltInType is BuiltInType.Variant or
                    BuiltInType.Number or
                    BuiltInType.Integer or
                    BuiltInType.UInteger)
            {
                return value;
            }
            ArrayOf<Variant> elements = value.Expand();
            if (value.TypeInfo.IsMatrix && value.Raw is IMatrixOf matrix)
            {
                return Variant.From(elements.ToMatrix(matrix.Dimensions));
            }
            return Variant.From(elements);
        }

        /// <summary>
        /// Decode a property based on the property type and value rank.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static void DecodeProperty(
            IDecoder decoder,
            string name,
            Field property)
        {
            Variant variant;
            switch (property.TypeInfo.BuiltInType)
            {
                // IEncodeable types are handled by type property as BuiltInType.Null
                case BuiltInType.Null:
                    var dataTypeId = NodeId.ToExpandedNodeId(
                        property.Definition.DataType,
                        decoder.Context.NamespaceUris);
                    if (property.TypeInfo.IsScalar)
                    {
                        IEncodeable encodeable =
                            decoder.ReadEncodeable<IEncodeable>(name, dataTypeId);
                        variant = Variant.FromStructure(encodeable);
                    }
                    else if (property.TypeInfo.IsArray)
                    {
                        ArrayOf<IEncodeable> encodeables =
                            decoder.ReadEncodeableArray<IEncodeable>(name, dataTypeId);
                        variant = Variant.FromStructure(encodeables);
                    }
                    else
                    {
                        MatrixOf<IEncodeable> encodeables =
                            decoder.ReadEncodeableMatrix<IEncodeable>(name, dataTypeId);
                        variant = Variant.FromStructure(encodeables);
                    }
                    break;
                case BuiltInType.Variant or
                    BuiltInType.Number or
                    BuiltInType.Integer or
                    BuiltInType.UInteger when property.TypeInfo.IsScalar:
                    variant = decoder.ReadVariant(name);
                    break;
                case BuiltInType.DiagnosticInfo when property.TypeInfo.IsScalar:
                    // Consume the DiagnosticInfo (OPC 10000-6 5.2.2.12); a
                    // Variant cannot hold it, so the field value stays null.
                    _ = decoder.ReadDiagnosticInfo(name);
                    variant = Variant.Null;
                    break;
                default:
                    variant = decoder.ReadVariantValue(name, property.TypeInfo);
                    break;
            }
            if (!variant.IsNull &&
                property.TypeInfo.BuiltInType == BuiltInType.Enumeration &&
                decoder.Context.Factory.TryGetEnumeratedType(
                    NodeId.ToExpandedNodeId(property.Definition.DataType, decoder.Context.NamespaceUris),
                    out IEnumeratedType? enumeratedType))
            {
                if (property.TypeInfo.IsScalar)
                {
                    variant = Variant.From(new EnumValue(variant.GetEnumeration().Value, enumeratedType));
                }
                else if (property.TypeInfo.IsArray && variant.TypeInfo.BuiltInType == BuiltInType.Int32)
                {
                    ArrayOf<int> integerValues = variant.GetInt32Array();
                    if (!integerValues.IsNull)
                    {
                        variant = Variant.From(integerValues.ConvertAll(value => new EnumValue(value, enumeratedType)));
                    }
                }
                else if (property.TypeInfo.IsArray)
                {
                    ArrayOf<EnumValue> values = variant.GetEnumerationArray();
                    if (!values.IsNull)
                    {
                        variant = Variant.From(values.ConvertAll(value => new EnumValue(value.Value, enumeratedType)));
                    }
                }
                else if (variant.TypeInfo.BuiltInType == BuiltInType.Int32)
                {
                    // The XML encoding carries enumerations as Int32.
                    variant = Variant.From(variant.GetInt32Matrix()
                        .ConvertAll(value => new EnumValue(value, enumeratedType)));
                }
                else
                {
                    variant = Variant.From(variant.GetEnumerationMatrix()
                        .ConvertAll(value => new EnumValue(value.Value, enumeratedType)));
                }
            }
            property.Value = variant;
        }

        /// <inheritdoc/>
        public virtual IEncodeable CreateInstance()
        {
            return new Structure(this, default(TypeInstance));
        }

        /// <inheritdoc/>
        public DataTypeDefinition GetDataTypeDefinition(NamespaceTable namespaceUris)
        {
            return Definition;
        }

        /// <summary>
        /// Provide XmlNamespace based on systemType
        /// </summary>
        protected string XmlNamespace =>
            XmlName != null ? XmlName.Namespace : string.Empty;

        /// <summary>
        /// The list of properties as dictionary.
        /// </summary>
        internal Dictionary<string, Field> PropertyDict
            => m_propertyDict ??= PropertyList.ToDictionary(p => p.Name!, p => p);

        /// <summary>
        /// The list of properties of this complex type, in the order of the
        /// definition.
        /// </summary>
        internal List<Field> PropertyList => m_propertyList ?? CreateFields();

        /// <summary>
        /// The number of fields of the definition.
        /// </summary>
        internal int FieldCount => Definition.Fields.Count;

        /// <summary>
        /// The names of the fields in the order of the definition, shared by
        /// all values of the type.
        /// </summary>
        internal IList<string> FieldNames
        {
            get
            {
                string[]? names = m_layout.FieldNames;
                if (names == null)
                {
                    names = new string[FieldCount];
                    for (int i = 0; i < names.Length; i++)
                    {
                        names[i] = Definition.Fields[i].Name!;
                    }
                    m_layout.FieldNames = names;
                }
                return names;
            }
        }

        /// <summary>
        /// The field at <paramref name="index"/>. Does not create the other
        /// fields while this is the only field used.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal Field GetField(int index)
        {
            List<Field>? fields = m_propertyList;
            if (fields != null)
            {
                return fields[index];
            }
            if ((uint)index >= (uint)FieldCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            Field? single = m_single;
            if (single == null)
            {
                // Readers of one value may run concurrently: one field wins.
                Field created = CreateField(index);
                single = Interlocked.CompareExchange(ref m_single, created, null) ?? created;
            }
            if (single.Order == index + 1)
            {
                return single;
            }
            return CreateFields()[index];
        }

        /// <summary>
        /// Creates the field at <paramref name="index"/> with its default value.
        /// </summary>
        private Field CreateField(int index)
        {
            StructureField field = Definition.Fields[index];

            // The field itself does not specify its built in type
            // we need to look it up in the provided lookup table.
            if (!FieldTypes.TryGetValue(field.Name!, out BuiltInType fieldType))
            {
                fieldType = BuiltInType.Null;
            }
            return new Field(index + 1, field, fieldType);
        }

        /// <summary>
        /// Creates all fields, keeping the one that is in use already.
        /// </summary>
        private List<Field> CreateFields()
        {
            int count = FieldCount;
            var fields = new List<Field>(count);
            Field? single = m_single;
            for (int i = 0; i < count; i++)
            {
                fields.Add(single != null && single.Order == i + 1 ? single : CreateField(i));
            }

            // Reading a value creates its fields, and readers of one value may
            // run concurrently. The single field stays set, so every list
            // built here holds it, and the first list published is the one
            // all readers use.
            return Interlocked.CompareExchange(ref m_propertyList, fields, null) ?? fields;
        }

        /// <summary>
        /// Selects the constructor of a new value of an existing type.
        /// </summary>
        private protected readonly struct TypeInstance
        {
        }

        /// <summary>
        /// What all values of one type share.
        /// </summary>
        private sealed class SharedLayout
        {
            public string[]? FieldNames;
        }

        private readonly SharedLayout m_layout;
        private List<Field>? m_propertyList;
        private Dictionary<string, Field>? m_propertyDict;

        // The only field in use while m_propertyList is not created. It is
        // kept afterwards and is then the field of the list at its position.
        private Field? m_single;

        /// <summary>
        /// Definition
        /// </summary>
        protected StructureDefinition Definition { get; }

        /// <summary>
        /// Field types flow cloning
        /// </summary>
        protected Dictionary<string, BuiltInType> FieldTypes { get; }
    }
}
