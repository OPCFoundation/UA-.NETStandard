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
using System.Reflection;
using System.Text;

namespace Opc.Ua.Client.ComplexTypes
{
    public class OptionalFieldsComplexType : BaseComplexType
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public OptionalFieldsComplexType()
        {
            m_optionalFields = 0;
        }

        /// <summary>
        /// Initializes the object with a <paramref name="typeId"/>.
        /// </summary>
        /// <param name="typeId">The type to copy and create an instance from</param>
        public OptionalFieldsComplexType(ExpandedNodeId typeId) : base(typeId)
        {
            m_optionalFields = 0;
        }
        #endregion

        #region Public Properties

        UInt32 OptionalFields => m_optionalFields;

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public override object MemberwiseClone()
        {
            OptionalFieldsComplexType clone = (OptionalFieldsComplexType)base.MemberwiseClone();
            clone.m_optionalFields = m_optionalFields;
            return clone;
        }

        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public override void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(TypeId.NamespaceUri);

            var attribute = (StructureDefinitionAttribute)
                GetType().GetCustomAttribute(typeof(StructureDefinitionAttribute));

            var properties = GetType().GetProperties();
            encoder.WriteUInt32("OptionalField", m_optionalFields);

            UInt32 bitSelector = 1;
            foreach (var property in properties)
            {
                var fieldAttribute = (StructureFieldAttribute)
                    property.GetCustomAttribute(typeof(StructureFieldAttribute));

                if (fieldAttribute == null)
                {
                    continue;
                }

                if (fieldAttribute.IsOptional)
                {
                    UInt32 testselector = bitSelector;
                    bitSelector <<= 1;
                    if ((m_optionalFields & testselector) == 0)
                    {
                        continue;
                    }
                }

                EncodeProperty(encoder, property, fieldAttribute.ValueRank);
            }
            encoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public override void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(TypeId.NamespaceUri);

            var attribute = (StructureDefinitionAttribute)
                GetType().GetCustomAttribute(typeof(StructureDefinitionAttribute));

            m_optionalFields = decoder.ReadUInt32("OptionalField");

            var properties = GetType().GetProperties();
            UInt32 bitSelector = 1;
            foreach (var property in properties)
            {
                var fieldAttribute = (StructureFieldAttribute)
                    property.GetCustomAttribute(typeof(StructureFieldAttribute));

                if (fieldAttribute == null)
                {
                    continue;
                }

                if (fieldAttribute.IsOptional)
                {
                    var testSelector = bitSelector;
                    bitSelector <<= 1;
                    if ((testSelector & m_optionalFields) == 0)
                    {
                        continue;
                    }
                }

                DecodeProperty(decoder, property, fieldAttribute.ValueRank);
            }
            decoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.IsEqual(IEncodeable)" />
        public override bool IsEqual(IEncodeable equalValue)
        {
            if (Object.ReferenceEquals(this, equalValue))
            {
                return true;
            }

            var valueBaseType = equalValue as OptionalFieldsComplexType;
            if (valueBaseType == null)
            {
                return false;
            }

            if (m_optionalFields != valueBaseType.OptionalFields)
            {
                return false;
            }

            var valueType = valueBaseType.GetType();
            if (this.GetType() != valueType)
            {
                return false;
            }

            var properties = this.GetType().GetProperties();
            UInt32 unionSelector = m_optionalFields;
            UInt32 bitSelector = 1;
            foreach (var property in properties)
            {
                var fieldAttribute = (StructureFieldAttribute)
                    property.GetCustomAttribute(typeof(StructureFieldAttribute));

                if (fieldAttribute == null)
                {
                    continue;
                }

                if (fieldAttribute.IsOptional)
                {
                    UInt32 testSelector = bitSelector;
                    bitSelector <<= 1;
                    if ((testSelector & m_optionalFields) == 0)
                    {
                        continue;
                    }
                }

                if (!Utils.IsEqual(property.GetValue(this), property.GetValue(valueBaseType)))
                {
                    return false;
                }
            }

            return true;
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
        public override string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                StringBuilder body = new StringBuilder();
                var attribute = (StructureDefinitionAttribute)
                    GetType().GetCustomAttribute(typeof(StructureDefinitionAttribute));

                var properties = GetType().GetProperties();
                UInt32 bitSelector = 1;
                foreach (var property in properties)
                {
                    var fieldAttribute = (StructureFieldAttribute)
                        property.GetCustomAttribute(typeof(StructureFieldAttribute));

                    if (fieldAttribute == null)
                    {
                        continue;
                    }

                    if (fieldAttribute.IsOptional)
                    {
                        UInt32 testSelector = bitSelector;
                        bitSelector <<= 1;
                        if ((testSelector & m_optionalFields) == 0)
                        {
                            continue;
                        }
                    }

                    AppendPropertyValue(formatProvider, body, property.GetValue(this), fieldAttribute.ValueRank);
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

        #region Private Members
        #endregion

        #region Private Fields
        private UInt32 m_optionalFields;
        #endregion
    }


}//namespace
