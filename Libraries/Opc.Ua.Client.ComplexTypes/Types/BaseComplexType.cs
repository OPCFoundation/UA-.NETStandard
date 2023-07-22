/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// The base class for all complex types.
    /// </summary>
    public class BaseComplexType :
        IEncodeable, IFormattable, ICloneable,
        IComplexTypeProperties,
        IStructureTypeInfo
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public BaseComplexType()
        {
            TypeId = ExpandedNodeId.Null;
            BinaryEncodingId = ExpandedNodeId.Null;
            XmlEncodingId = ExpandedNodeId.Null;
            m_context = MessageContextExtension.CurrentContext;
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

        [OnSerializing()]
        private void UpdateContext(StreamingContext context)
        {
            m_context = MessageContextExtension.CurrentContext;
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        private void Initialize(StreamingContext context)
        {
            TypeId = ExpandedNodeId.Null;
            m_context = MessageContextExtension.CurrentContext;
        }
        #endregion Constructors

        #region ICloneable
        /// <inheritdoc/>
        public virtual object Clone()
        {
            return this.MemberwiseClone();
        }

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new object MemberwiseClone()
        {
            Type thisType = this.GetType();
            BaseComplexType clone = Activator.CreateInstance(thisType) as BaseComplexType;

            clone.TypeId = TypeId;
            clone.BinaryEncodingId = BinaryEncodingId;
            clone.XmlEncodingId = XmlEncodingId;

            // clone all properties of derived class
            foreach (var property in GetPropertyEnumerator())
            {
                property.SetValue(clone, Utils.Clone(property.GetValue(this)));
            }

            return clone;
        }
        #endregion

        #region Public Properties
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
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (!(encodeable is BaseComplexType valueBaseType))
            {
                return false;
            }

            var valueType = valueBaseType.GetType();
            if (this.GetType() != valueType)
            {
                return false;
            }

            foreach (var property in GetPropertyEnumerator())
            {
                if (!Utils.IsEqual(property.GetValue(this), property.GetValue(valueBaseType)))
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
        #endregion Public Properties

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the complex type.
        /// </summary>
        /// <param name="format">(Unused). Leave this as null</param>
        /// <param name="formatProvider">The provider of a mechanism for retrieving an object to control formatting.</param>
        /// <returns>
        /// A <see cref="System.String"/> containing the value of the current embeded instance in the specified format.
        /// </returns>
        /// <exception cref="FormatException">Thrown if the <i>format</i> parameter is not null</exception>
        public virtual string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                StringBuilder body = new StringBuilder();

                foreach (var property in GetPropertyEnumerator())
                {
                    AppendPropertyValue(formatProvider, body, property.GetValue(this), property.ValueRank);
                }

                if (body.Length > 0)
                {
                    return body.Append('}').ToString();
                }

                if (!NodeId.IsNull(this.TypeId))
                {
                    return String.Format(formatProvider, "{{{0}}}", this.TypeId);
                }

                return "(null)";
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion IFormattable Members

        #region IComplexTypeProperties
        /// <inheritdoc/>
        public virtual int GetPropertyCount()
        {
            return m_propertyList.Count;
        }

        /// <inheritdoc/>
        public virtual IList<string> GetPropertyNames()
        {
            return m_propertyList.Select(p => p.Name).ToList();
        }

        /// <inheritdoc/>
        public virtual IList<Type> GetPropertyTypes()
        {
            return m_propertyList.Select(p => p.PropertyType).ToList();
        }

        /// <inheritdoc/>
        public virtual object this[int index]
        {
            get => m_propertyList.ElementAt(index).GetValue(this);
            set => m_propertyList.ElementAt(index).SetValue(this, value);
        }

        /// <inheritdoc/>
        public virtual object this[string name]
        {
            get => m_propertyDict[name].GetValue(this);
            set => m_propertyDict[name].SetValue(this, value);
        }

        /// <inheritdoc/>
        public virtual IEnumerable<ComplexTypePropertyInfo> GetPropertyEnumerator()
        {
            return m_propertyList;
        }
        #endregion IComplexTypeProperties

        #region Private Members
        /// <summary>
        /// Formatting helper.
        /// </summary>
        private void AddSeparator(StringBuilder body)
        {
            if (body.Length == 0)
            {
                body.Append('{');
            }
            else
            {
                body.Append('|');
            }
        }

        /// <summary>
        /// Append a property to the value string.
        /// Handle arrays and enumerations.
        /// </summary>
        protected void AppendPropertyValue(
            IFormatProvider formatProvider,
            StringBuilder body,
            object value,
            int valueRank)
        {
            AddSeparator(body);
            if (valueRank >= 0 && value is Array array)
            {
                var rank = array.Rank;
                var dimensions = new int[rank];
                var mods = new int[rank];
                for (int ii = 0; ii < rank; ii++)
                {
                    dimensions[ii] = array.GetLength(ii);
                }

                for (int ii = rank - 1; ii >= 0; ii--)
                {
                    mods[ii] = dimensions[ii];
                    if (ii < rank - 1)
                    {
                        mods[ii] *= mods[ii + 1];
                    }
                }

                int count = 0;
                foreach (var item in array)
                {
                    bool needSeparator = true;
                    for (int dc = 0; dc < rank; dc++)
                    {
                        if ((count % mods[dc]) == 0)
                        {
                            body.Append('[');
                            needSeparator = false;
                        }
                    }
                    if (needSeparator)
                    {
                        body.Append(',');
                    }
                    AppendPropertyValue(formatProvider, body, item);
                    count++;
                    needSeparator = false;
                    for (int dc = 0; dc < rank; dc++)
                    {
                        if ((count % mods[dc]) == 0)
                        {
                            body.Append(']');
                            needSeparator = true;
                        }
                    }
                    if (needSeparator && count < array.Length)
                    {
                        body.Append(',');
                    }
                }
            }
            else if (valueRank >= 0 && value is IEnumerable enumerable)
            {
                bool first = true;
                body.Append('[');
                foreach (var item in enumerable)
                {
                    if (!first)
                    {
                        body.Append(',');
                    }
                    AppendPropertyValue(formatProvider, body, item);
                    first = false;
                }
                body.Append(']');
            }
            else
            {
                AppendPropertyValue(formatProvider, body, value);
            }
        }

        /// <summary>
        /// Append a property to the value string.
        /// </summary>
        private void AppendPropertyValue(
            IFormatProvider formatProvider,
            StringBuilder body,
            object value)
        {
            if (value is byte[] x)
            {
                body.AppendFormat(formatProvider, "Byte[{0}]", x.Length);
                return;
            }

            if (value is XmlElement xmlElements)
            {
                body.AppendFormat(formatProvider, "<{0}>", xmlElements.Name);
                return;
            }

            body.AppendFormat(formatProvider, "{0}", value);
        }

        /// <summary>
        /// Encode a property based on the property type and value rank.
        /// </summary>
        protected void EncodeProperty(
            IEncoder encoder,
            string name,
            ComplexTypePropertyInfo property
            )
        {
            int valueRank = property.ValueRank;
            BuiltInType builtInType = property.BuiltInType;
            if (valueRank == ValueRanks.Scalar)
            {
                EncodeProperty(encoder, name, property.PropertyInfo, builtInType);
            }
            else if (valueRank >= ValueRanks.OneDimension)
            {
                EncodePropertyArray(encoder, name, property.PropertyInfo, builtInType, valueRank);
            }
            else
            {
                throw ServiceResultException.Create(StatusCodes.BadEncodingError,
                    "Cannot encode a property with unsupported ValueRank {0}.", valueRank);
            }
        }

        /// <summary>
        /// Encode a property based on the property type and value rank.
        /// </summary>
        protected void EncodeProperty(
            IEncoder encoder,
            ComplexTypePropertyInfo property)
        {
            EncodeProperty(encoder, property.Name, property);
        }

        /// <summary>
        /// Encode a scalar property based on the property type.
        /// </summary>
        private void EncodeProperty(IEncoder encoder, string name, PropertyInfo property, BuiltInType builtInType)
        {
            var propertyType = property.PropertyType;
            if (propertyType.IsEnum)
            {
                builtInType = BuiltInType.Enumeration;
            }
            switch (builtInType)
            {
                case BuiltInType.Boolean: encoder.WriteBoolean(name, (Boolean)property.GetValue(this)); break;
                case BuiltInType.SByte: encoder.WriteSByte(name, (SByte)property.GetValue(this)); break;
                case BuiltInType.Byte: encoder.WriteByte(name, (Byte)property.GetValue(this)); break;
                case BuiltInType.Int16: encoder.WriteInt16(name, (Int16)property.GetValue(this)); break;
                case BuiltInType.UInt16: encoder.WriteUInt16(name, (UInt16)property.GetValue(this)); break;
                case BuiltInType.Int32: encoder.WriteInt32(name, (Int32)property.GetValue(this)); break;
                case BuiltInType.UInt32: encoder.WriteUInt32(name, (UInt32)property.GetValue(this)); break;
                case BuiltInType.Int64: encoder.WriteInt64(name, (Int64)property.GetValue(this)); break;
                case BuiltInType.UInt64: encoder.WriteUInt64(name, (UInt64)property.GetValue(this)); break;
                case BuiltInType.Float: encoder.WriteFloat(name, (Single)property.GetValue(this)); break;
                case BuiltInType.Double: encoder.WriteDouble(name, (Double)property.GetValue(this)); break;
                case BuiltInType.String: encoder.WriteString(name, (String)property.GetValue(this)); break;
                case BuiltInType.DateTime: encoder.WriteDateTime(name, (DateTime)property.GetValue(this)); break;
                case BuiltInType.Guid: encoder.WriteGuid(name, (Uuid)property.GetValue(this)); break;
                case BuiltInType.ByteString: encoder.WriteByteString(name, (Byte[])property.GetValue(this)); break;
                case BuiltInType.XmlElement: encoder.WriteXmlElement(name, (XmlElement)property.GetValue(this)); break;
                case BuiltInType.NodeId: encoder.WriteNodeId(name, (NodeId)property.GetValue(this)); break;
                case BuiltInType.ExpandedNodeId: encoder.WriteExpandedNodeId(name, (ExpandedNodeId)property.GetValue(this)); break;
                case BuiltInType.StatusCode: encoder.WriteStatusCode(name, (StatusCode)property.GetValue(this)); break;
                case BuiltInType.DiagnosticInfo: encoder.WriteDiagnosticInfo(name, (DiagnosticInfo)property.GetValue(this)); break;
                case BuiltInType.QualifiedName: encoder.WriteQualifiedName(name, (QualifiedName)property.GetValue(this)); break;
                case BuiltInType.LocalizedText: encoder.WriteLocalizedText(name, (LocalizedText)property.GetValue(this)); break;
                case BuiltInType.DataValue: encoder.WriteDataValue(name, (DataValue)property.GetValue(this)); break;
                case BuiltInType.Variant: encoder.WriteVariant(name, (Variant)property.GetValue(this)); break;
                case BuiltInType.ExtensionObject: encoder.WriteExtensionObject(name, (ExtensionObject)property.GetValue(this)); break;
                case BuiltInType.Enumeration:
                    if (propertyType.IsEnum)
                    {
                        encoder.WriteEnumerated(name, (Enum)property.GetValue(this));
                        break;
                    }
                    goto case BuiltInType.Int32;
                default:
                    if (typeof(IEncodeable).IsAssignableFrom(propertyType))
                    {
                        encoder.WriteEncodeable(name, (IEncodeable)property.GetValue(this), propertyType);
                        break;
                    }
                    throw ServiceResultException.Create(StatusCodes.BadEncodingError,
                        "Cannot encode unknown type {0}.", propertyType.Name);
            }
        }

        /// <summary>
        /// Encode an array property based on the base property type.
        /// </summary>
        private void EncodePropertyArray(IEncoder encoder, string name, PropertyInfo property, BuiltInType builtInType, int valueRank)
        {
            Type elementType = property.PropertyType.GetElementType() ?? property.PropertyType.GetItemType();
            if (elementType.IsEnum)
            {
                builtInType = BuiltInType.Enumeration;
            }
            encoder.WriteArray(name, property.GetValue(this), valueRank, builtInType);
        }

        /// <summary>
        /// Decode a property based on the property type and value rank.
        /// </summary>
        protected void DecodeProperty(
            IDecoder decoder,
            ComplexTypePropertyInfo property)
        {
            DecodeProperty(decoder, property.Name, property);
        }

        /// <summary>
        /// Decode a property based on the property type and value rank.
        /// </summary>
        protected void DecodeProperty(
            IDecoder decoder,
            string name,
            ComplexTypePropertyInfo property)
        {
            int valueRank = property.ValueRank;
            if (valueRank == ValueRanks.Scalar)
            {
                DecodeProperty(decoder, name, property.PropertyInfo, property.BuiltInType);
            }
            else if (valueRank >= ValueRanks.OneDimension)
            {
                DecodePropertyArray(decoder, name, property.PropertyInfo, property.BuiltInType, valueRank);
            }
            else
            {
                throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                    "Cannot decode a property with unsupported ValueRank {0}.", valueRank);
            }
        }

        /// <summary>
        /// Decode a scalar property based on the property type.
        /// </summary>
        private void DecodeProperty(IDecoder decoder, string name, PropertyInfo property, BuiltInType builtInType)
        {
            var propertyType = property.PropertyType;
            if (propertyType.IsEnum)
            {
                builtInType = BuiltInType.Enumeration;
            }
            switch (builtInType)
            {
                case BuiltInType.Boolean: property.SetValue(this, decoder.ReadBoolean(name)); break;
                case BuiltInType.SByte: property.SetValue(this, decoder.ReadSByte(name)); break;
                case BuiltInType.Byte: property.SetValue(this, decoder.ReadByte(name)); break;
                case BuiltInType.Int16: property.SetValue(this, decoder.ReadInt16(name)); break;
                case BuiltInType.UInt16: property.SetValue(this, decoder.ReadUInt16(name)); break;
                case BuiltInType.Int32: property.SetValue(this, decoder.ReadInt32(name)); break;
                case BuiltInType.UInt32: property.SetValue(this, decoder.ReadUInt32(name)); break;
                case BuiltInType.Int64: property.SetValue(this, decoder.ReadInt64(name)); break;
                case BuiltInType.UInt64: property.SetValue(this, decoder.ReadUInt64(name)); break;
                case BuiltInType.Float: property.SetValue(this, decoder.ReadFloat(name)); break;
                case BuiltInType.Double: property.SetValue(this, decoder.ReadDouble(name)); break;
                case BuiltInType.String: property.SetValue(this, decoder.ReadString(name)); break;
                case BuiltInType.DateTime: property.SetValue(this, decoder.ReadDateTime(name)); break;
                case BuiltInType.Guid: property.SetValue(this, decoder.ReadGuid(name)); break;
                case BuiltInType.ByteString: property.SetValue(this, decoder.ReadByteString(name)); break;
                case BuiltInType.XmlElement: property.SetValue(this, decoder.ReadXmlElement(name)); break;
                case BuiltInType.NodeId: property.SetValue(this, decoder.ReadNodeId(name)); break;
                case BuiltInType.ExpandedNodeId: property.SetValue(this, decoder.ReadExpandedNodeId(name)); break;
                case BuiltInType.StatusCode: property.SetValue(this, decoder.ReadStatusCode(name)); break;
                case BuiltInType.QualifiedName: property.SetValue(this, decoder.ReadQualifiedName(name)); break;
                case BuiltInType.LocalizedText: property.SetValue(this, decoder.ReadLocalizedText(name)); break;
                case BuiltInType.DataValue: property.SetValue(this, decoder.ReadDataValue(name)); break;
                case BuiltInType.Variant: property.SetValue(this, decoder.ReadVariant(name)); break;
                case BuiltInType.DiagnosticInfo: property.SetValue(this, decoder.ReadDiagnosticInfo(name)); break;
                case BuiltInType.ExtensionObject:
                    if (typeof(IEncodeable).IsAssignableFrom(propertyType))
                    {
                        property.SetValue(this, decoder.ReadEncodeable(name, propertyType));
                        break;
                    }
                    property.SetValue(this, decoder.ReadExtensionObject(name));
                    break;
                case BuiltInType.Enumeration:
                    if (propertyType.IsEnum)
                    {
                        property.SetValue(this, decoder.ReadEnumerated(name, propertyType)); break;
                    }
                    goto case BuiltInType.Int32;
                default:
                    if (typeof(IEncodeable).IsAssignableFrom(propertyType))
                    {
                        property.SetValue(this, decoder.ReadEncodeable(name, propertyType));
                        break;
                    }
                    throw ServiceResultException.Create(StatusCodes.BadDecodingError,
                        "Cannot decode unknown type {0}.", propertyType.Name);
            }
        }

        /// <summary>
        /// Decode an array property based on the base property type.
        /// </summary>
        private void DecodePropertyArray(IDecoder decoder, string name, PropertyInfo property, BuiltInType builtInType, int valueRank)
        {
            Type elementType = property.PropertyType.GetElementType() ?? property.PropertyType.GetItemType();
            if (elementType.IsEnum)
            {
                builtInType = BuiltInType.Enumeration;
            }
            Array decodedArray = decoder.ReadArray(name, valueRank, builtInType, elementType);
            property.SetValue(this, decodedArray);
        }

        /// <summary>
        /// Initialize the helpers for property enumerator and dictionary.
        /// </summary>
        protected virtual void InitializePropertyAttributes()
        {
            var definitionAttribute = (StructureDefinitionAttribute)
                GetType().GetCustomAttribute(typeof(StructureDefinitionAttribute));
            if (definitionAttribute != null)
            {
                m_structureBaseType = definitionAttribute.BaseDataType;
            }

            var typeAttribute = (StructureTypeIdAttribute)
                GetType().GetCustomAttribute(typeof(StructureTypeIdAttribute));
            if (typeAttribute != null)
            {
                TypeId = ExpandedNodeId.Parse(typeAttribute.ComplexTypeId);
                BinaryEncodingId = ExpandedNodeId.Parse(typeAttribute.BinaryEncodingId);
                XmlEncodingId = ExpandedNodeId.Parse(typeAttribute.XmlEncodingId);
            }

            m_propertyList = new List<ComplexTypePropertyInfo>();
            var properties = GetType().GetProperties();
            foreach (var property in properties)
            {
                StructureFieldAttribute fieldAttribute = (StructureFieldAttribute)
                    property.GetCustomAttribute(typeof(StructureFieldAttribute));

                if (fieldAttribute == null)
                {
                    continue;
                }

                DataMemberAttribute dataAttribute = (DataMemberAttribute)
                    property.GetCustomAttribute(typeof(DataMemberAttribute));

                var newProperty = new ComplexTypePropertyInfo(property, fieldAttribute, dataAttribute);

                m_propertyList.Add(newProperty);
            }
            m_propertyList = m_propertyList.OrderBy(p => p.Order).ToList();
            m_propertyDict = m_propertyList.ToDictionary(p => p.Name, p => p);
        }
        #endregion Private Members

        #region Protected Properties
        /// <summary>
        /// Provide XmlNamespace based on systemType
        /// </summary>
        protected string XmlNamespace
        {
            get
            {
                if (m_xmlName == null)
                {
                    m_xmlName = EncodeableFactory.GetXmlName(GetType());
                }

                return m_xmlName != null ? m_xmlName.Namespace : string.Empty;
            }
        }
        #endregion

        #region Protected Fields
        /// <summary>
        /// The list of properties of this complex type.
        /// </summary>
        protected IList<ComplexTypePropertyInfo> m_propertyList;

        /// <summary>
        /// The list of properties as dictionary.
        /// </summary>
        protected Dictionary<string, ComplexTypePropertyInfo> m_propertyDict;
        #endregion Protected Fields

        #region Private Fields
        private IServiceMessageContext m_context;
        private StructureBaseDataType m_structureBaseType;
        private XmlQualifiedName m_xmlName;
        #endregion Private Fields
    }
}//namespace
