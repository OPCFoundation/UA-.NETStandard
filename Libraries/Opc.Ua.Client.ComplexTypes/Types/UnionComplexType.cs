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
using System.Linq;
using System.Text;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Implements a union complex type.
    /// </summary>
    public class UnionComplexType : BaseComplexType
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public UnionComplexType() : base()
        {
            m_switchField = 0;
        }

        /// <summary>
        /// Initializes the object with a <paramref name="typeId"/>.
        /// </summary>
        /// <param name="typeId">The type to copy and create an instance from</param>
        public UnionComplexType(ExpandedNodeId typeId) : base(typeId)
        {
            m_switchField = 0;
        }
        #endregion Constructors

        #region ICloneable
        /// <inheritdoc/>
        public override object Clone()
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
            UnionComplexType clone = (UnionComplexType)base.MemberwiseClone();
            clone.m_switchField = m_switchField;
            return clone;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The union selector determines which property is valid.
        /// A value of 0 means all properties are invalid, x=1..n means the
        /// xth property is valid.
        /// </summary>
        public UInt32 SwitchField => m_switchField;

        /// <inheritdoc/>
        public override StructureType StructureType => StructureType.Union;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(XmlNamespace);

            string fieldName = null;
            if (encoder.UseReversibleEncoding)
            {
                encoder.WriteUInt32("SwitchField", m_switchField);
                fieldName = "Value";
            }

            if (m_switchField != 0)
            {
                int unionSelector = 1;
                ComplexTypePropertyInfo unionProperty = null;
                foreach (var property in GetPropertyEnumerator())
                {
                    if (unionSelector == m_switchField)
                    {
                        unionProperty = property;
                        break;
                    }
                    unionSelector++;
                }
                EncodeProperty(encoder, fieldName, unionProperty);
            }
            else if (!encoder.UseReversibleEncoding)
            {
                encoder.WriteString(null, "null");
            }

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(XmlNamespace);

            m_switchField = decoder.ReadUInt32("SwitchField");

            UInt32 unionSelector = m_switchField;
            if (unionSelector > 0)
            {
                foreach (var property in GetPropertyEnumerator())
                {
                    if (--unionSelector == 0)
                    {
                        DecodeProperty(decoder, "Value", property);
                        break;
                    }
                }
            }
            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable equalValue)
        {
            if (Object.ReferenceEquals(this, equalValue))
            {
                return true;
            }

            if (!(equalValue is UnionComplexType valueBaseType))
            {
                return false;
            }

            if (SwitchField != valueBaseType.SwitchField)
            {
                return false;
            }

            var valueType = valueBaseType.GetType();
            if (this.GetType() != valueType)
            {
                return false;
            }

            if (m_switchField != 0)
            {
                UInt32 unionSelector = m_switchField;
                foreach (var property in GetPropertyEnumerator())
                {
                    if (--unionSelector == 0)
                    {
                        if (!Utils.IsEqual(property.GetValue(this), property.GetValue(valueBaseType)))
                        {
                            return false;
                        }
                        break;
                    }
                }
            }
            return true;
        }
        #endregion Public Properties

        #region IFormattable Members
        /// <inheritdoc/>
        public override string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                StringBuilder body = new StringBuilder();
                if (m_switchField != 0)
                {
                    UInt32 unionSelector = m_switchField;
                    foreach (var property in GetPropertyEnumerator())
                    {
                        if (--unionSelector == 0)
                        {
                            object unionProperty = property.GetValue(this);
                            AppendPropertyValue(formatProvider, body, unionProperty, property.ValueRank);
                            break;
                        }
                    }
                }

                if (body.Length > 0)
                {
                    body.Append('}');
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
        #endregion IFormattable Members

        #region IComplexTypeProperties Members
        /// <summary>
        /// Access property values by index.
        /// </summary>
        /// <remarks>
        /// The value of a Union is determined by the union selector.
        /// Calling get on an unselected property returns null,
        ///     otherwise the selected object.
        /// Calling get with an invalid index (e.g.-1) returns the selected object.
        /// Calling set with a valid object on a selected property sets the value and the
        /// union selector.
        /// Calling set with a null object or an invalid index unselects the union.
        /// </remarks>
        public override object this[int index]
        {
            get
            {
                if (index + 1 == (int)m_switchField)
                {
                    return m_propertyList.ElementAt(index).GetValue(this);
                }
                if (index < 0 &&
                    m_switchField > 0)
                {
                    return m_propertyList.ElementAt((int)m_switchField - 1).GetValue(this);
                }
                return null;
            }
            set
            {
                if (index >= 0)
                {
                    m_propertyList.ElementAt(index).SetValue(this, value);
                    // note: selector is updated in SetValue by emitted code for union
                    // m_unionSelector = (uint)(index + 1);
                    if (value != null)
                    {
                        return;
                    }
                    // reset union selector if value is a null
                }
                m_switchField = 0;
            }
        }

        /// <summary>
        /// Access property values by name.
        /// </summary>
        /// <remarks>
        /// The value of a Union is determined by the union selector.
        /// Calling get on an unselected property returns null,
        /// otherwise the selected object.
        /// Calling get with an invalid name returns the selected object.
        /// Calling set with a valid object on a selected property sets the value and the
        /// union selector.
        /// Calling set with a null object or an invalid name unselects the union.
        /// </remarks>
        public override object this[string name]
        {
            get
            {
                if (SwitchField > 0)
                {
                    ComplexTypePropertyInfo property;
                    if (m_propertyDict.TryGetValue(name, out property))
                    {
                        if ((int)m_switchField == property.Order)
                        {
                            return property.GetValue(this);
                        }
                    }
                    else
                    {
                        return m_propertyList.ElementAt((int)SwitchField - 1).GetValue(this);
                    }
                }
                return null;
            }
            set
            {
                ComplexTypePropertyInfo property;
                if (m_propertyDict.TryGetValue(name, out property))
                {
                    property.SetValue(this, value);
                    // note: selector is updated in SetValue by emitted code for union
                    // m_unionSelector = (uint)(property.Order);
                    if (value != null)
                    {
                        return;
                    }
                    // reset union selector if value is a null
                }
                m_switchField = 0;
            }
        }

        /// <summary>
        /// Simple accessor for Union to access current Value.
        /// </summary>
        public object Value => (m_switchField == 0) ? null : m_propertyList.ElementAt((int)m_switchField - 1).GetValue(this);
        #endregion IComplexTypeProperties Members

        #region Private Fields
        /// <summary>
        /// The selector for the value of the Union.
        /// </summary>
        protected UInt32 m_switchField;
        #endregion Private Fields
    }
}//namespace
