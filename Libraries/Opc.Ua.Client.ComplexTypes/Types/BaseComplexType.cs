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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// The base class for all complex types.
    /// </summary>
    public class BaseComplexType :
        IEncodeable,
        IFormattable,
        IComplexTypeProperties,
        IStructureTypeInfo
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public BaseComplexType()
        {
            TypeId = ExpandedNodeId.Null;
            BinaryEncodingId = ExpandedNodeId.Null;
            XmlEncodingId = ExpandedNodeId.Null;
            InitializePropertyAttributes();
        }

        /// <summary>
        /// Initializes the object with a <paramref name="typeId"/>.
        /// </summary>
        /// <param name="typeId">The type to copy and create an instance from</param>
        public BaseComplexType(ExpandedNodeId typeId)
        {
            TypeId = typeId;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            TypeId = ExpandedNodeId.Null;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            Type thisType = GetType();
            var clone = Activator.CreateInstance(thisType) as BaseComplexType;

            clone.TypeId = TypeId;
            clone.BinaryEncodingId = BinaryEncodingId;
            clone.XmlEncodingId = XmlEncodingId;

            // clone all properties of derived class
            foreach (ComplexTypePropertyInfo property in GetPropertyEnumerator())
            {
                property.SetValue(clone, property.GetValue(this));
            }

            return clone;
        }

        /// <inheritdoc/>
        public ExpandedNodeId TypeId { get; set; }

        /// <inheritdoc/>
        public ExpandedNodeId BinaryEncodingId { get; set; }

        /// <inheritdoc/>
        public ExpandedNodeId XmlEncodingId { get; set; }

        /// <inheritdoc/>
        public virtual StructureType StructureType => StructureType.Structure;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(XmlNamespace);

            foreach (ComplexTypePropertyInfo property in GetPropertyEnumerator())
            {
                EncodeProperty(encoder, property);
            }

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(XmlNamespace);

            foreach (ComplexTypePropertyInfo property in GetPropertyEnumerator())
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

            if (encodeable is not BaseComplexType valueBaseType)
            {
                return false;
            }

            Type valueType = valueBaseType.GetType();
            if (GetType() != valueType)
            {
                return false;
            }

            foreach (ComplexTypePropertyInfo property in GetPropertyEnumerator())
            {
                if (property.GetValue(this) != property.GetValue(valueBaseType))
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
        /// <param name="formatProvider">The provider of a mechanism for retrieving an object to control formatting.</param>
        /// <returns>
        /// A <see cref="string"/> containing the value of the current embedded instance in the specified format.
        /// </returns>
        /// <exception cref="FormatException">Thrown if the <i>format</i> parameter is not null</exception>
        public virtual string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                var body = new StringBuilder();

                foreach (ComplexTypePropertyInfo property in GetPropertyEnumerator())
                {
                    AppendPropertyValue(
                        body,
                        property.GetValue(this));
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
        public virtual IList<string> GetPropertyNames()
        {
            return [.. m_propertyList.Select(p => p.Name)];
        }

        /// <inheritdoc/>
        public virtual IList<Type> GetPropertyTypes()
        {
            return [.. m_propertyList.Select(p => p.PropertyType)];
        }

        /// <inheritdoc/>
        public virtual Variant this[int index]
        {
            get => m_propertyList[index].GetValue(this);
            set => m_propertyList[index].SetValue(this, value);
        }

        /// <inheritdoc/>
        public virtual Variant this[string name]
        {
            get => m_propertyDict[name].GetValue(this);
            set => m_propertyDict[name].SetValue(this, value);
        }

        /// <inheritdoc/>
        public virtual IEnumerable<ComplexTypePropertyInfo> GetPropertyEnumerator()
        {
            return m_propertyList;
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
        /// <exception cref="ServiceResultException"></exception>
        protected void EncodeProperty(
            IEncoder encoder,
            string name,
            ComplexTypePropertyInfo property)
        {
            encoder.WriteVariantValue(name, property.GetValue(this));
        }

        /// <summary>
        /// Encode a property based on the property type and value rank.
        /// </summary>
        protected void EncodeProperty(IEncoder encoder, ComplexTypePropertyInfo property)
        {
            EncodeProperty(encoder, property.Name, property);
        }

        /// <summary>
        /// Decode a property based on the property type and value rank.
        /// </summary>
        protected void DecodeProperty(IDecoder decoder, ComplexTypePropertyInfo property)
        {
            DecodeProperty(decoder, property.Name, property);
        }

        /// <summary>
        /// Decode a property based on the property type and value rank.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        protected void DecodeProperty(
            IDecoder decoder,
            string name,
            ComplexTypePropertyInfo property)
        {
            decoder.ReadVariantValue(name, property.TypeInfo);
        }

        /// <summary>
        /// Initialize the helpers for property enumerator and dictionary.
        /// </summary>
        protected virtual void InitializePropertyAttributes()
        {
            StructureDefinitionAttribute definitionAttribute = GetType()
                .GetCustomAttribute<StructureDefinitionAttribute>();

            StructureTypeIdAttribute typeAttribute = GetType()
                .GetCustomAttribute<StructureTypeIdAttribute>();
            if (typeAttribute != null)
            {
                TypeId = ExpandedNodeId.Parse(typeAttribute.ComplexTypeId);
                BinaryEncodingId = ExpandedNodeId.Parse(typeAttribute.BinaryEncodingId);
                XmlEncodingId = ExpandedNodeId.Parse(typeAttribute.XmlEncodingId);
            }

            m_propertyList = [];
            foreach (PropertyInfo property in GetType().GetProperties())
            {
                StructureFieldAttribute fieldAttribute = property
                    .GetCustomAttribute<StructureFieldAttribute>();

                if (fieldAttribute == null)
                {
                    continue;
                }

                DataMemberAttribute dataAttribute = property
                    .GetCustomAttribute<DataMemberAttribute>();

                var newProperty = new ComplexTypePropertyInfo(
                    property,
                    fieldAttribute,
                    dataAttribute);

                m_propertyList.Add(newProperty);
            }
            m_propertyList = [.. m_propertyList.OrderBy(p => p.Order)];
            m_propertyDict = m_propertyList.ToDictionary(p => p.Name, p => p);
        }

        /// <summary>
        /// Provide XmlNamespace based on systemType
        /// </summary>
        protected string XmlNamespace
        {
            get
            {
                if (m_xmlName == null)
                {
                    m_xmlName = TypeInfo.GetXmlName(GetType());
                }

                return m_xmlName != null ? m_xmlName.Namespace : string.Empty;
            }
        }

        /// <summary>
        /// The list of properties of this complex type.
        /// </summary>
        protected IList<ComplexTypePropertyInfo> m_propertyList;

        /// <summary>
        /// The list of properties as dictionary.
        /// </summary>
        protected Dictionary<string, ComplexTypePropertyInfo> m_propertyDict;

        private XmlQualifiedName m_xmlName;
    }
}
