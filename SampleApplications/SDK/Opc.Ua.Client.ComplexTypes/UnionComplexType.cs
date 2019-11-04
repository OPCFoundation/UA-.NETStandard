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
using System.Linq;
using System.Reflection;
using System.Text;

namespace Opc.Ua.Client.ComplexTypes
{
    public class UnionComplexType : BaseComplexType
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public UnionComplexType() : base()
        {
            m_unionSelector = 0;
        }

        /// <summary>
        /// Initializes the object with a <paramref name="typeId"/>.
        /// </summary>
        /// <param name="typeId">The type to copy and create an instance from</param>
        public UnionComplexType(ExpandedNodeId typeId) : base(typeId)
        {
            m_unionSelector = 0;
        }
        #endregion

        #region Public Properties

        UInt32 UnionSelector => m_unionSelector;

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public override object MemberwiseClone()
        {
            UnionComplexType clone = (UnionComplexType)base.MemberwiseClone();
            clone.m_unionSelector = m_unionSelector;
            return clone;
        }

        /// <summary cref="IEncodeable.Encode(IEncoder)" />
        public override void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(TypeId.NamespaceUri);

            var attribute = (StructureDefinitionAttribute)
                GetType().GetCustomAttribute(typeof(StructureDefinitionAttribute));

            var properties = GetType().GetProperties();

            int unionSelector = 1;
            int valueRank = -1;
            PropertyInfo unionProperty = null;
            foreach (var property in properties)
            {
                var fieldAttribute = (StructureFieldAttribute)
                    property.GetCustomAttribute(typeof(StructureFieldAttribute));

                if (fieldAttribute == null)
                {
                    continue;
                }

                if (unionSelector == m_unionSelector)
                {
                    valueRank = fieldAttribute.ValueRank;
                    unionProperty = property;
                    break;
                }

                unionSelector++;
            }

            encoder.WriteUInt32("SwitchField", m_unionSelector);
            EncodeProperty(encoder, unionProperty, valueRank);

            encoder.PopNamespace();
        }

        /// <summary cref="IEncodeable.Decode(IDecoder)" />
        public override void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(TypeId.NamespaceUri);

            var attribute = (StructureDefinitionAttribute)
                GetType().GetCustomAttribute(typeof(StructureDefinitionAttribute));

            var properties = GetType().GetProperties();
            m_unionSelector = decoder.ReadUInt32("SwitchField");
            UInt32 unionSelector = m_unionSelector;
            if (unionSelector > 0)
            {
                foreach (var property in properties)
                {
                    var fieldAttribute = (StructureFieldAttribute)
                        property.GetCustomAttribute(typeof(StructureFieldAttribute));

                    if (fieldAttribute == null)
                    {
                        continue;
                    }

                    if (--unionSelector == 0)
                    {
                        DecodeProperty(decoder, property, fieldAttribute.ValueRank);
                        break;
                    }
                }
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

            var valueBaseType = equalValue as UnionComplexType;
            if (valueBaseType == null)
            {
                return false;
            }

            if (UnionSelector != valueBaseType.UnionSelector)
            {
                return false;
            }

            var valueType = valueBaseType.GetType();
            if (this.GetType() != valueType)
            {
                return false;
            }

            var properties = this.GetType().GetProperties();
            UInt32 unionSelector = m_unionSelector;
            foreach (var property in properties)
            {
                if (property.CustomAttributes.Count() == 0)
                {
                    continue;
                }

                if (--unionSelector == 0)
                {
                    if (!Utils.IsEqual(property.GetValue(this), property.GetValue(valueBaseType)))
                    {
                        return false;
                    }
                    break;
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
                UInt32 unionSelector = m_unionSelector;
                foreach (var property in properties)
                {
                    var fieldAttribute = (StructureFieldAttribute)
                        property.GetCustomAttribute(typeof(StructureFieldAttribute));

                    if (fieldAttribute == null)
                    {
                        continue;
                    }

                    if (--unionSelector == 0)
                    {
                        object unionProperty = property.GetValue(this);
                        AppendPropertyValue(formatProvider, body, unionProperty, fieldAttribute.ValueRank);
                        break;
                    }
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
        private UInt32 m_unionSelector;
        #endregion
    }


}//namespace
