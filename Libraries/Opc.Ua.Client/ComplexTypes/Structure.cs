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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Xml;

namespace Opc.Ua.Client.ComplexTypes
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
            StructureDefinition structureDefinition)
        {
            m_definition = structureDefinition;
            m_xmlName = xmlName;
            TypeId = typeId;
            XmlEncodingId = xmlEncodingId;
            BinaryEncodingId = binaryEncodingId;
            for (int ii = 0; ii < m_definition.Fields.Count; ii++)
            {
                var newProperty = new Field(ii + 1, m_definition.Fields[ii]);
                m_propertyList.Add(newProperty);
            }
            m_propertyList.Sort((a, b) => a.Order.CompareTo(b.Order));
            m_propertyDict = m_propertyList.ToDictionary(p => p.Name, p => p);
        }

        /// <inheritdoc/>
        public Type Type => typeof(Structure);

        /// <inheritdoc/>
        public XmlQualifiedName XmlName => m_xmlName;

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

            foreach (Field property in GetPropertyEnumerator())
            {
                EncodeProperty(encoder, property);
            }

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(XmlNamespace);

            foreach (Field property in GetPropertyEnumerator())
            {
                DecodeProperty(decoder, property);
            }

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not Structure valueBaseType)
            {
                return false;
            }

            if (valueBaseType.m_propertyList.Count != m_propertyList.Count)
            {
                return false;
            }

            for (int ii = 0; ii < m_propertyList.Count; ii++)
            {
                if (m_propertyList[ii].Value != valueBaseType.m_propertyList[ii].Value)
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

        /// <summary>
        /// Returns the string representation of the complex type.
        /// </summary>
        /// <param name="format">(Unused). Leave this as null</param>
        /// <param name="formatProvider">The provider of a mechanism for
        /// retrieving an object to control formatting.</param>
        /// <returns>
        /// A <see cref="string"/> containing the value of the current
        /// embedded instance in the specified format.
        /// </returns>
        /// <exception cref="FormatException">Thrown if the <i>format</i>
        /// parameter is not null</exception>
        public virtual string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (format == null)
            {
                var body = new StringBuilder();

                foreach (Field property in GetPropertyEnumerator())
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

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <inheritdoc/>
        public virtual int GetPropertyCount()
        {
            return m_propertyList.Count;
        }

        /// <inheritdoc/>
        public virtual IReadOnlyList<string> GetPropertyNames()
        {
            return [.. m_propertyList.Select(p => p.Name)];
        }

        /// <inheritdoc/>
        public virtual Variant this[int index]
        {
            get => m_propertyList[index].Value;
            set => m_propertyList[index].Value = value;
        }

        /// <inheritdoc/>
        public virtual Variant this[string name]
        {
            get => m_propertyDict[name].Value;
            set => m_propertyDict[name].Value = value;
        }

        /// <inheritdoc/>
        public virtual IEnumerable<Field> GetPropertyEnumerator()
        {
            return m_propertyList;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return CreateInstance();
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
        protected void EncodeProperty(IEncoder encoder, Field property)
        {
            EncodeProperty(encoder, property.Name, property);
        }

        /// <summary>
        /// Decode a property based on the property type and value rank.
        /// </summary>
        protected void DecodeProperty(IDecoder decoder, Field property)
        {
            DecodeProperty(decoder, property.Name, property);
        }

        /// <summary>
        /// Encode a property based on the property type and value rank.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected void EncodeProperty(
            IEncoder encoder,
            string name,
            Field property)
        {
            Variant variant = property.Value;
            switch (property.TypeInfo.BuiltInType)
            {
                // IEncodeable types are handled by type property as BuiltInType.Null
                case BuiltInType.Null:
                    ExpandedNodeId dataTypeId = TypeInfo.GetDataTypeId(
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
                case BuiltInType.Variant when property.TypeInfo.IsScalar:
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
        protected void DecodeProperty(
            IDecoder decoder,
            string name,
            Field property)
        {
            Variant variant;
            switch (property.TypeInfo.BuiltInType)
            {
                // IEncodeable types are handled by type property as BuiltInType.Null
                case BuiltInType.Null:
                    ExpandedNodeId dataTypeId = TypeInfo.GetDataTypeId(
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
                case BuiltInType.Variant when property.TypeInfo.IsScalar:
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
            return new Structure(m_xmlName, TypeId, BinaryEncodingId, XmlEncodingId, m_definition);
        }

        /// <summary>
        /// Provide XmlNamespace based on systemType
        /// </summary>
        protected string XmlNamespace => m_xmlName != null ? m_xmlName.Namespace : string.Empty;

        /// <summary>
        /// The list of properties as dictionary.
        /// </summary>
        protected Dictionary<string, Field> m_propertyDict;

        /// <summary>
        /// The list of properties of this complex type.
        /// </summary>
        protected List<Field> m_propertyList = [];

        /// <summary>
        /// Xml name
        /// </summary>
        protected XmlQualifiedName m_xmlName;

        /// <summary>
        /// Definition
        /// </summary>
        protected readonly StructureDefinition m_definition;
    }
}
