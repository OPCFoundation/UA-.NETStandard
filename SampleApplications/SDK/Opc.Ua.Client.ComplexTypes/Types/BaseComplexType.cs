/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

    public class BaseComplexType :
        IEncodeable, IFormattable, IComplexTypeInstance, IComplexTypeProperties
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
        #endregion

        #region Public Properties
        /// <summary cref="IEncodeable.TypeId" />
        public ExpandedNodeId TypeId { get; set; }

        /// <summary cref="IEncodeable.BinaryEncodingId" />
        public ExpandedNodeId BinaryEncodingId { get; set; }

        /// <summary cref="IEncodeable.XmlEncodingId" />
        public ExpandedNodeId XmlEncodingId { get; set; }

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public new virtual object MemberwiseClone()
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

        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(TypeId.NamespaceUri);

            foreach (var property in GetPropertyEnumerator())
            {
                EncodeProperty(encoder, property.PropertyInfo, property.ValueRank);
            }

            encoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(TypeId.NamespaceUri);

            foreach (var property in GetPropertyEnumerator())
            {
                DecodeProperty(decoder, property.PropertyInfo, property.ValueRank);
            }

            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public virtual bool IsEqual(IEncodeable equalValue)
        {
            if (Object.ReferenceEquals(this, equalValue))
            {
                return true;
            }

            if (!(equalValue is BaseComplexType valueBaseType))
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

        /// <summary cref="object.ToString()" />
        public override string ToString()
        {
            return ToString(null, null);
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the complex type.
        /// </summary>
        /// <param name="format">(Unused). Leave this as null</param>
        /// <param name="formatProvider">The provider of a mechanism for retrieving an object to control formatting.</param>
        /// <returns>
        /// A <see cref="T:System.String"/> containing the value of the current embeded instance in the specified format.
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
                    body.Append("}");
                    return body.ToString();
                }

                if (!NodeId.IsNull(this.TypeId))
                {
                    return String.Format(formatProvider, "{{{0}}}", this.TypeId);
                }

                return "(null)";
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region IComplexTypeProperties
        /// <summary cref="IComplexTypeProperties.GetPropertyCount()" />
        public virtual int GetPropertyCount()
        {
            return m_propertyList.Count;
        }

        /// <summary cref="IComplexTypeProperties.GetPropertyNames()" />
        public virtual IList<string> GetPropertyNames()
        {
            return m_propertyList.Select(p => p.Name).ToList();
        }

        /// <summary cref="IComplexTypeProperties.GetPropertyTypes()" />
        public virtual IList<Type> GetPropertyTypes()
        {
            return m_propertyList.Select(p => p.PropertyType).ToList();
        }

        /// <summary>
        /// Access property values by index.
        /// </summary>
        public virtual object this[int index] {
            get => m_propertyList.ElementAt(index).GetValue(this);
            set => m_propertyList.ElementAt(index).SetValue(this, value);
        }

        /// <summary>
        /// Access property values by name.
        /// </summary>
        public virtual object this[string name] {
            get => m_propertyDict[name].GetValue(this);
            set => m_propertyDict[name].SetValue(this, value);
        }

        /// <summary>
        /// Ordered enumerator for properties.      
        /// </summary>
        public virtual IEnumerable<ComplexTypePropertyAttribute> GetPropertyEnumerator()
        {
            return m_propertyList;
        }
        #endregion

        #region Private Members
        /// <summary>
        /// Formatting helper.
        /// </summary>
        private void AddSeparator(StringBuilder body)
        {
            if (body.Length == 0)
            {
                body.Append("{");
            }
            else
            {
                body.Append(" | ");
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
            if (valueRank >= 0 &&
                value is IEnumerable)
            {
                bool first = true;
                var enumerable = value as IEnumerable;
                body.Append("[");
                foreach (var item in enumerable)
                {
                    if (!first)
                    {
                        body.Append(",");
                    }
                    AppendPropertyValue(formatProvider, body, item);
                    first = false;
                }
                body.Append("]");
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
            if (value is byte[])
            {
                body.AppendFormat(formatProvider, "Byte[{0}]", ((byte[])value).Length);
                return;
            }

            if (value is XmlElement)
            {
                body.AppendFormat(formatProvider, "<{0}>", ((XmlElement)value).Name);
                return;
            }

            body.AppendFormat("{0}", value);
        }

        /// <summary>
        /// Encode a property based on the property type and value rank.
        /// </summary>
        protected void EncodeProperty(IEncoder encoder, PropertyInfo property, int valueRank)
        {
            if (valueRank < 0)
            {
                EncodeProperty(encoder, property);
            }
            else
            {
                EncodePropertyArray(encoder, property);
            }
        }

        /// <summary>
        /// Encode a scalar property based on the property type.
        /// </summary>
        private void EncodeProperty(IEncoder encoder, PropertyInfo property)
        {
            var propertyType = property.PropertyType;
            if (propertyType == typeof(Boolean))
            {
                encoder.WriteBoolean(property.Name, (Boolean)property.GetValue(this));
            }
            else if (propertyType == typeof(SByte))
            {
                encoder.WriteSByte(property.Name, (SByte)property.GetValue(this));
            }
            else if (propertyType == typeof(Byte))
            {
                encoder.WriteByte(property.Name, (Byte)property.GetValue(this));
            }
            else if (propertyType == typeof(Int16))
            {
                encoder.WriteInt16(property.Name, (Int16)property.GetValue(this));
            }
            else if (propertyType == typeof(UInt16))
            {
                encoder.WriteUInt16(property.Name, (UInt16)property.GetValue(this));
            }
            else if (propertyType.IsEnum)
            {
                encoder.WriteEnumerated(property.Name, (Enum)property.GetValue(this));
            }
            else if (propertyType == typeof(Int32))
            {
                encoder.WriteInt32(property.Name, (Int32)property.GetValue(this));
            }
            else if (propertyType == typeof(UInt32))
            {
                encoder.WriteUInt32(property.Name, (UInt32)property.GetValue(this));
            }
            else if (propertyType == typeof(Int64))
            {
                encoder.WriteInt64(property.Name, (Int64)property.GetValue(this));
            }
            else if (propertyType == typeof(UInt64))
            {
                encoder.WriteUInt64(property.Name, (UInt64)property.GetValue(this));
            }
            else if (propertyType == typeof(Single))
            {
                encoder.WriteFloat(property.Name, (Single)property.GetValue(this));
            }
            else if (propertyType == typeof(Double))
            {
                encoder.WriteDouble(property.Name, (Double)property.GetValue(this));
            }
            else if (propertyType == typeof(String))
            {
                encoder.WriteString(property.Name, (String)property.GetValue(this));
            }
            else if (propertyType == typeof(DateTime))
            {
                encoder.WriteDateTime(property.Name, (DateTime)property.GetValue(this));
            }
            else if (propertyType == typeof(Uuid))
            {
                encoder.WriteGuid(property.Name, (Uuid)property.GetValue(this));
            }
            else if (propertyType == typeof(Byte[]))
            {
                encoder.WriteByteArray(property.Name, (Byte[])property.GetValue(this));
            }
            else if (propertyType == typeof(XmlElement))
            {
                encoder.WriteXmlElement(property.Name, (XmlElement)property.GetValue(this));
            }
            else if (propertyType == typeof(NodeId))
            {
                encoder.WriteNodeId(property.Name, (NodeId)property.GetValue(this));
            }
            else if (propertyType == typeof(ExpandedNodeId))
            {
                encoder.WriteExpandedNodeId(property.Name, (ExpandedNodeId)property.GetValue(this));
            }
            else if (propertyType == typeof(StatusCode))
            {
                encoder.WriteStatusCode(property.Name, (StatusCode)property.GetValue(this));
            }
            else if (propertyType == typeof(DiagnosticInfo))
            {
                encoder.WriteDiagnosticInfo(property.Name, (DiagnosticInfo)property.GetValue(this));
            }
            else if (propertyType == typeof(QualifiedName))
            {
                encoder.WriteQualifiedName(property.Name, (QualifiedName)property.GetValue(this));
            }
            else if (propertyType == typeof(LocalizedText))
            {
                encoder.WriteLocalizedText(property.Name, (LocalizedText)property.GetValue(this));
            }
            else if (propertyType == typeof(DataValue))
            {
                encoder.WriteDataValue(property.Name, (DataValue)property.GetValue(this));
            }
            else if (propertyType == typeof(Variant))
            {
                encoder.WriteVariant(property.Name, (Variant)property.GetValue(this));
            }
            else if (propertyType == typeof(ExtensionObject))
            {
                encoder.WriteExtensionObject(property.Name, (ExtensionObject)property.GetValue(this));
            }
            else if (typeof(IEncodeable).IsAssignableFrom(propertyType))
            {
                encoder.WriteEncodeable(property.Name, (IEncodeable)property.GetValue(this), propertyType);
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    $"Unknown type {propertyType} to encode.");
            }
        }

        /// <summary>
        /// Encode an array property based on the base property type.
        /// </summary>
        private void EncodePropertyArray(IEncoder encoder, PropertyInfo property)
        {
            var elementType = property.PropertyType.GetElementType();
            if (elementType == null)
            {
                elementType = property.PropertyType.GetItemType();
            }
            if (elementType == typeof(Boolean))
            {
                encoder.WriteBooleanArray(property.Name, (BooleanCollection)property.GetValue(this));
            }
            else if (elementType == typeof(SByte))
            {
                encoder.WriteSByteArray(property.Name, (SByteCollection)property.GetValue(this));
            }
            else if (elementType == typeof(Byte))
            {
                encoder.WriteByteArray(property.Name, (ByteCollection)property.GetValue(this));
            }
            else if (elementType == typeof(Int16))
            {
                encoder.WriteInt16Array(property.Name, (Int16Collection)property.GetValue(this));
            }
            else if (elementType == typeof(UInt16))
            {
                encoder.WriteUInt16Array(property.Name, (UInt16Collection)property.GetValue(this));
            }
            else if (elementType == typeof(Int32))
            {
                encoder.WriteInt32Array(property.Name, (Int32Collection)property.GetValue(this));
            }
            else if (elementType.IsEnum)
            {
                encoder.WriteEnumeratedArray(property.Name, (Array)property.GetValue(this), elementType);
            }
            else if (elementType == typeof(UInt32))
            {
                encoder.WriteUInt32Array(property.Name, (UInt32Collection)property.GetValue(this));
            }
            else if (elementType == typeof(Int64))
            {
                encoder.WriteInt64Array(property.Name, (Int64Collection)property.GetValue(this));
            }
            else if (elementType == typeof(UInt64))
            {
                encoder.WriteUInt64Array(property.Name, (UInt64Collection)property.GetValue(this));
            }
            else if (elementType == typeof(Single))
            {
                encoder.WriteFloatArray(property.Name, (FloatCollection)property.GetValue(this));
            }
            else if (elementType == typeof(Double))
            {
                encoder.WriteDoubleArray(property.Name, (DoubleCollection)property.GetValue(this));
            }
            else if (elementType == typeof(String))
            {
                encoder.WriteStringArray(property.Name, (StringCollection)property.GetValue(this));
            }
            else if (elementType == typeof(DateTime))
            {
                encoder.WriteDateTimeArray(property.Name, (DateTimeCollection)property.GetValue(this));
            }
            else if (elementType == typeof(Uuid))
            {
                encoder.WriteGuidArray(property.Name, (UuidCollection)property.GetValue(this));
            }
            else if (elementType == typeof(Byte[]))
            {
                encoder.WriteByteStringArray(property.Name, (ByteStringCollection)property.GetValue(this));
            }
            else if (elementType == typeof(XmlElement))
            {
                encoder.WriteXmlElementArray(property.Name, (XmlElementCollection)property.GetValue(this));
            }
            else if (elementType == typeof(NodeId))
            {
                encoder.WriteNodeIdArray(property.Name, (NodeIdCollection)property.GetValue(this));
            }
            else if (elementType == typeof(ExpandedNodeId))
            {
                encoder.WriteExpandedNodeIdArray(property.Name, (ExpandedNodeIdCollection)property.GetValue(this));
            }
            else if (elementType == typeof(StatusCode))
            {
                encoder.WriteStatusCodeArray(property.Name, (StatusCodeCollection)property.GetValue(this));
            }
            else if (elementType == typeof(DiagnosticInfo))
            {
                encoder.WriteDiagnosticInfoArray(property.Name, (DiagnosticInfoCollection)property.GetValue(this));
            }
            else if (elementType == typeof(QualifiedName))
            {
                encoder.WriteQualifiedNameArray(property.Name, (QualifiedNameCollection)property.GetValue(this));
            }
            else if (elementType == typeof(LocalizedText))
            {
                encoder.WriteLocalizedTextArray(property.Name, (LocalizedTextCollection)property.GetValue(this));
            }
            else if (elementType == typeof(DataValue))
            {
                encoder.WriteDataValueArray(property.Name, (DataValueCollection)property.GetValue(this));
            }
            else if (elementType == typeof(Variant))
            {
                encoder.WriteVariantArray(property.Name, (VariantCollection)property.GetValue(this));
            }
            else if (elementType == typeof(ExtensionObject))
            {
                encoder.WriteExtensionObjectArray(property.Name, (ExtensionObjectCollection)property.GetValue(this));
            }
            else if (typeof(IEncodeable).IsAssignableFrom(elementType))
            {
                var value = property.GetValue(this);
                if (!(value is IEncodeableCollection encodable))
                {
                    encodable = IEncodeableCollection.ToIEncodeableCollection(value as IEncodeable[]);
                }
                encoder.WriteEncodeableArray(property.Name, encodable.ToArray(), property.PropertyType);
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    $"Unknown type {elementType} to encode.");
            }
        }

        /// <summary>
        /// Decode a property based on the property type and value rank.
        /// </summary>
        protected void DecodeProperty(IDecoder decoder, PropertyInfo property, int valueRank)
        {
            if (valueRank < 0)
            {
                DecodeProperty(decoder, property);
            }
            else
            {
                DecodePropertyArray(decoder, property);
            }
        }

        /// <summary>
        /// Decode a scalar property based on the property type.
        /// </summary>
        private void DecodeProperty(IDecoder decoder, PropertyInfo property)
        {
            var propertyType = property.PropertyType;
            if (propertyType == typeof(Boolean))
            {
                property.SetValue(this, decoder.ReadBoolean(property.Name));
            }
            else if (propertyType == typeof(SByte))
            {
                property.SetValue(this, decoder.ReadSByte(property.Name));
            }
            else if (propertyType == typeof(Byte))
            {
                property.SetValue(this, decoder.ReadByte(property.Name));
            }
            else if (propertyType == typeof(Int16))
            {
                property.SetValue(this, decoder.ReadInt16(property.Name));
            }
            else if (propertyType == typeof(UInt16))
            {
                property.SetValue(this, decoder.ReadUInt16(property.Name));
            }
            else if (propertyType.IsEnum)
            {
                property.SetValue(this, decoder.ReadEnumerated(property.Name, propertyType));
            }
            else if (propertyType == typeof(Int32))
            {
                property.SetValue(this, decoder.ReadInt32(property.Name));
            }
            else if (propertyType == typeof(UInt32))
            {
                property.SetValue(this, decoder.ReadUInt32(property.Name));
            }
            else if (propertyType == typeof(Int64))
            {
                property.SetValue(this, decoder.ReadInt64(property.Name));
            }
            else if (propertyType == typeof(UInt64))
            {
                property.SetValue(this, decoder.ReadUInt64(property.Name));
            }
            else if (propertyType == typeof(Single))
            {
                property.SetValue(this, decoder.ReadFloat(property.Name));
            }
            else if (propertyType == typeof(Double))
            {
                property.SetValue(this, decoder.ReadDouble(property.Name));
            }
            else if (propertyType == typeof(String))
            {
                property.SetValue(this, decoder.ReadString(property.Name));
            }
            else if (propertyType == typeof(DateTime))
            {
                property.SetValue(this, decoder.ReadDateTime(property.Name));
            }
            else if (propertyType == typeof(Uuid))
            {
                property.SetValue(this, decoder.ReadGuid(property.Name));
            }
            else if (propertyType == typeof(Byte[]))
            {
                property.SetValue(this, decoder.ReadByteString(property.Name));
            }
            else if (propertyType == typeof(XmlElement))
            {
                property.SetValue(this, decoder.ReadXmlElement(property.Name));
            }
            else if (propertyType == typeof(NodeId))
            {
                property.SetValue(this, decoder.ReadNodeId(property.Name));
            }
            else if (propertyType == typeof(ExpandedNodeId))
            {
                property.SetValue(this, decoder.ReadExpandedNodeId(property.Name));
            }
            else if (propertyType == typeof(StatusCode))
            {
                property.SetValue(this, decoder.ReadStatusCode(property.Name));
            }
            else if (propertyType == typeof(DiagnosticInfo))
            {
                property.SetValue(this, decoder.ReadDiagnosticInfo(property.Name));
            }
            else if (propertyType == typeof(QualifiedName))
            {
                property.SetValue(this, decoder.ReadQualifiedName(property.Name));
            }
            else if (propertyType == typeof(LocalizedText))
            {
                property.SetValue(this, decoder.ReadLocalizedText(property.Name));
            }
            else if (propertyType == typeof(DataValue))
            {
                property.SetValue(this, decoder.ReadDataValue(property.Name));
            }
            else if (propertyType == typeof(Variant))
            {
                property.SetValue(this, decoder.ReadVariant(property.Name));
            }
            else if (propertyType == typeof(ExtensionObject))
            {
                property.SetValue(this, decoder.ReadExtensionObject(property.Name));
            }
            else if (typeof(IEncodeable).IsAssignableFrom(propertyType))
            {
                property.SetValue(this, decoder.ReadEncodeable(property.Name, propertyType));
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    $"Unknown type {propertyType} to decode.");
            }
        }

        /// <summary>
        /// Decode an array property based on the base property type.
        /// </summary>
        private void DecodePropertyArray(IDecoder decoder, PropertyInfo property)
        {
            var elementType = property.PropertyType.GetElementType();
            if (elementType == null)
            {
                elementType = property.PropertyType.GetItemType();
            }
            if (elementType == typeof(Boolean))
            {
                property.SetValue(this, decoder.ReadBooleanArray(property.Name));
            }
            else if (elementType == typeof(SByte))
            {
                property.SetValue(this, decoder.ReadSByteArray(property.Name));
            }
            else if (elementType == typeof(Byte))
            {
                property.SetValue(this, decoder.ReadByteArray(property.Name));
            }
            else if (elementType == typeof(Int16))
            {
                property.SetValue(this, decoder.ReadInt16Array(property.Name));
            }
            else if (elementType == typeof(UInt16))
            {
                property.SetValue(this, decoder.ReadUInt16Array(property.Name));
            }
            else if (elementType.IsEnum)
            {
                property.SetValue(this, decoder.ReadEnumeratedArray(property.Name, elementType));
            }
            else if (elementType == typeof(Int32))
            {
                property.SetValue(this, decoder.ReadInt32Array(property.Name));
            }
            else if (elementType == typeof(UInt32))
            {
                property.SetValue(this, decoder.ReadUInt32Array(property.Name));
            }
            else if (elementType == typeof(Int64))
            {
                property.SetValue(this, decoder.ReadInt64Array(property.Name));
            }
            else if (elementType == typeof(UInt64))
            {
                property.SetValue(this, decoder.ReadUInt64Array(property.Name));
            }
            else if (elementType == typeof(Single))
            {
                property.SetValue(this, decoder.ReadFloatArray(property.Name));
            }
            else if (elementType == typeof(Double))
            {
                property.SetValue(this, decoder.ReadDoubleArray(property.Name));
            }
            else if (elementType == typeof(String))
            {
                property.SetValue(this, decoder.ReadStringArray(property.Name));
            }
            else if (elementType == typeof(DateTime))
            {
                property.SetValue(this, decoder.ReadDateTimeArray(property.Name));
            }
            else if (elementType == typeof(Uuid))
            {
                property.SetValue(this, decoder.ReadGuidArray(property.Name));
            }
            else if (elementType == typeof(Byte[]))
            {
                property.SetValue(this, decoder.ReadByteStringArray(property.Name));
            }
            else if (elementType == typeof(XmlElement))
            {
                property.SetValue(this, decoder.ReadXmlElementArray(property.Name));
            }
            else if (elementType == typeof(NodeId))
            {
                property.SetValue(this, decoder.ReadNodeIdArray(property.Name));
            }
            else if (elementType == typeof(ExpandedNodeId))
            {
                property.SetValue(this, decoder.ReadExpandedNodeIdArray(property.Name));
            }
            else if (elementType == typeof(StatusCode))
            {
                property.SetValue(this, decoder.ReadStatusCodeArray(property.Name));
            }
            else if (elementType == typeof(DiagnosticInfo))
            {
                property.SetValue(this, decoder.ReadDiagnosticInfoArray(property.Name));
            }
            else if (elementType == typeof(QualifiedName))
            {
                property.SetValue(this, decoder.ReadQualifiedNameArray(property.Name));
            }
            else if (elementType == typeof(LocalizedText))
            {
                property.SetValue(this, decoder.ReadLocalizedTextArray(property.Name));
            }
            else if (elementType == typeof(DataValue))
            {
                property.SetValue(this, decoder.ReadDataValueArray(property.Name));
            }
            else if (elementType == typeof(Variant))
            {
                property.SetValue(this, decoder.ReadVariantArray(property.Name));
            }
            else if (elementType == typeof(ExtensionObject))
            {
                property.SetValue(this, decoder.ReadExtensionObjectArray(property.Name));
            }
            else if (typeof(IEncodeable).IsAssignableFrom(elementType))
            {
                property.SetValue(this, decoder.ReadEncodeableArray(property.Name, elementType));
            }
            else
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    $"Unknown type {elementType} to decode.");
            }
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

            m_propertyList = new List<ComplexTypePropertyAttribute>();
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

                var newProperty = new ComplexTypePropertyAttribute(property, fieldAttribute, dataAttribute);

                m_propertyList.Add(newProperty);
            }
            m_propertyList = m_propertyList.OrderBy(p => p.Order).ToList();
            m_propertyDict = m_propertyList.ToDictionary(p => p.Name, p => p);
        }
        #endregion

        #region Private Fields
        private ServiceMessageContext m_context;
        private StructureBaseDataType m_structureBaseType;
        protected IList<ComplexTypePropertyAttribute> m_propertyList;
        protected Dictionary<string, ComplexTypePropertyAttribute> m_propertyDict;
        #endregion
    }


}//namespace
