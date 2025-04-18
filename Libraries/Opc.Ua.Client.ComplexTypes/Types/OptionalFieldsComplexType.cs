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
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// A complex type with optional fields.
    /// </summary>
    public class OptionalFieldsComplexType : BaseComplexType
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public OptionalFieldsComplexType()
        {
            m_encodingMask = 0;
        }

        /// <summary>
        /// Initializes the object with a <paramref name="typeId"/>.
        /// </summary>
        /// <param name="typeId">The type to copy and create an instance from</param>
        public OptionalFieldsComplexType(ExpandedNodeId typeId) : base(typeId)
        {
            m_encodingMask = 0;
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
            OptionalFieldsComplexType clone = (OptionalFieldsComplexType)base.MemberwiseClone();
            clone.m_encodingMask = m_encodingMask;
            return clone;
        }
        #endregion

        #region Public Properties
        /// <inheritdoc/>
        public override StructureType StructureType => StructureType.StructureWithOptionalFields;

        /// <summary>
        /// The encoding mask for the optional fields.
        /// </summary>
        public UInt32 EncodingMask => m_encodingMask;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(XmlNamespace);

            encoder.WriteEncodingMask(m_encodingMask);

            foreach (var property in GetPropertyEnumerator())
            {
                if (property.IsOptional)
                {
                    if ((property.OptionalFieldMask & m_encodingMask) == 0)
                    {
                        continue;
                    }
                }

                EncodeProperty(encoder, property);
            }
            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(XmlNamespace);

            m_encodingMask = decoder.ReadEncodingMask(null);

            // try again if the mask is implicitly defined by the JSON keys
            if (m_encodingMask == 0 && decoder is IJsonDecoder)
            {
                var masks = new StringCollection();
                foreach (var property in GetPropertyEnumerator())
                {
                    if (property.IsOptional)
                    {
                        masks.Add(property.Name);
                    }
                }

                m_encodingMask = decoder.ReadEncodingMask(masks);
            }

            foreach (var property in GetPropertyEnumerator())
            {
                if (property.IsOptional)
                {
                    if ((property.OptionalFieldMask & m_encodingMask) == 0)
                    {
                        continue;
                    }
                }

                DecodeProperty(decoder, property);
            }

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (!(encodeable is OptionalFieldsComplexType valueBaseType))
            {
                return false;
            }

            if (m_encodingMask != valueBaseType.EncodingMask)
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
                if (property.IsOptional)
                {
                    if ((property.OptionalFieldMask & m_encodingMask) == 0)
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
        #endregion Public Properties

        #region IFormattable Members
        /// <inheritdoc/>
        public override string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                StringBuilder body = new StringBuilder();
                foreach (var property in GetPropertyEnumerator())
                {
                    if (property.IsOptional)
                    {
                        if ((property.OptionalFieldMask & m_encodingMask) == 0)
                        {
                            continue;
                        }
                    }

                    AppendPropertyValue(formatProvider, body, property.GetValue(this), property.ValueRank);
                }

                if (body.Length > 0)
                {
                    body.Append('}');
                    return body.ToString();
                }

                if (!NodeId.IsNull(this.TypeId))
                {
                    return string.Format(formatProvider, "{{{0}}}", this.TypeId);
                }

                return "(null)";
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion IFormattable Members

        #region IComplexTypeProperties Members
        /// <inheritdoc/>
        public override object this[int index]
        {
            get
            {
                var property = m_propertyList.ElementAt(index);
                if (property.IsOptional &&
                    (property.OptionalFieldMask & m_encodingMask) == 0)
                {
                    return null;
                }
                return property.GetValue(this);
            }
            set
            {
                var property = m_propertyList.ElementAt(index);
                property.SetValue(this, value);
                if (property.IsOptional)
                {
                    if (value == null)
                    {
                        m_encodingMask &= ~property.OptionalFieldMask;
                    }
                    else
                    {
                        m_encodingMask |= property.OptionalFieldMask;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override object this[string name]
        {
            get
            {
                ComplexTypePropertyInfo property;
                if (m_propertyDict.TryGetValue(name, out property))
                {
                    if (property.IsOptional &&
                        (property.OptionalFieldMask & m_encodingMask) == 0)
                    {
                        return null;
                    }
                    return property.GetValue(this);
                }
                throw new KeyNotFoundException();
            }
            set
            {
                ComplexTypePropertyInfo property;
                if (m_propertyDict.TryGetValue(name, out property))
                {
                    property.SetValue(this, value);
                    if (value == null)
                    {
                        m_encodingMask &= ~property.OptionalFieldMask;
                    }
                    else
                    {
                        m_encodingMask |= property.OptionalFieldMask;
                    }
                }
                else
                {
                    throw new KeyNotFoundException();
                }
            }
        }
        #endregion IComplexTypeProperties Members

        #region Private Members
        /// <inheritdoc/>
        protected override void InitializePropertyAttributes()
        {
            base.InitializePropertyAttributes();

            // build optional field mask attribute
            UInt32 optionalFieldMask = 1;
            foreach (var property in GetPropertyEnumerator())
            {
                property.OptionalFieldMask = 0;
                if (property.IsOptional)
                {
                    property.OptionalFieldMask = optionalFieldMask;
                    optionalFieldMask <<= 1;
                }
            }
        }
        #endregion Private Members

        #region Private Fields
        private UInt32 m_encodingMask;
        #endregion Private Fields
    }
}//namespace
