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
        IEncodeableType
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

            for (int i = 0; i < Definition.Fields.Count; i++)
            {
                // The field itself does not specify its built in type
                // we need to look it up in the provided lookup table.
                if (!fieldTypes.TryGetValue(
                    Definition.Fields[i].Name,
                    out BuiltInType fieldType))
                {
                    fieldType = BuiltInType.Null;
                }

                var newProperty = new Field(
                    i + 1,
                    Definition.Fields[i],
                    fieldType);
                PropertyList.Add(newProperty);
            }
            PropertyList.Sort((a, b) => a.Order.CompareTo(b.Order));
            PropertyDict = PropertyList.ToDictionary(p => p.Name, p => p);
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
            PropertyList = structure.PropertyList.ConvertAll(p => (Field)p.Clone());
            PropertyDict = PropertyList.ToDictionary(p => p.Name, p => p);
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
        internal void EncodeProperty(IEncoder encoder, Field property)
        {
            EncodeProperty(encoder, property.Name, property);
        }

        /// <summary>
        /// Decode a property based on the property type and value rank.
        /// </summary>
        internal void DecodeProperty(IDecoder decoder, Field property)
        {
            DecodeProperty(decoder, property.Name, property);
        }

        /// <summary>
        /// Encode a property based on the property type and value rank.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal void EncodeProperty(
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
                default:
                    encoder.WriteVariantValue(name, variant);
                    break;
            }
        }

        /// <summary>
        /// Decode a property based on the property type and value rank.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal void DecodeProperty(
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
                default:
                    variant = decoder.ReadVariantValue(name, property.TypeInfo);
                    break;
            }
            property.Value = variant;
        }

        /// <inheritdoc/>
        public virtual IEncodeable CreateInstance()
        {
            return new Structure(
                XmlName,
                TypeId,
                BinaryEncodingId,
                XmlEncodingId,
                Definition,
                FieldTypes);
        }

        /// <summary>
        /// Provide XmlNamespace based on systemType
        /// </summary>
        protected string XmlNamespace =>
            XmlName != null ? XmlName.Namespace : string.Empty;

        /// <summary>
        /// The list of properties as dictionary.
        /// </summary>
        internal Dictionary<string, Field> PropertyDict { get; }

        /// <summary>
        /// The list of properties of this complex type.
        /// </summary>
        internal List<Field> PropertyList { get; } = [];

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
