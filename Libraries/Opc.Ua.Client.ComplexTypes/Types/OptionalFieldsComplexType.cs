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
using System.Text;

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// A complex type with optional fields.
    /// </summary>
    public class OptionalFieldsComplexType : BaseComplexType
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public OptionalFieldsComplexType()
        {
            EncodingMask = 0;
        }

        /// <summary>
        /// Initializes the object with a <paramref name="typeId"/>.
        /// </summary>
        /// <param name="typeId">The type to copy and create an instance from</param>
        public OptionalFieldsComplexType(ExpandedNodeId typeId)
            : base(typeId)
        {
            EncodingMask = 0;
        }

        /// <inheritdoc/>
        public override object Clone()
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
            var clone = (OptionalFieldsComplexType)base.MemberwiseClone();
            clone.EncodingMask = EncodingMask;
            return clone;
        }

        /// <inheritdoc/>
        public override StructureType StructureType => StructureType.StructureWithOptionalFields;

        /// <summary>
        /// The encoding mask for the optional fields.
        /// </summary>
        public uint EncodingMask { get; private set; }

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(XmlNamespace);

            encoder.WriteEncodingMask(EncodingMask);

            foreach (ComplexTypePropertyInfo property in GetPropertyEnumerator())
            {
                if (property.IsOptional && (property.OptionalFieldMask & EncodingMask) == 0)
                {
                    continue;
                }

                EncodeProperty(encoder, property);
            }
            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(XmlNamespace);

            EncodingMask = decoder.ReadEncodingMask(null);

            // try again if the mask is implicitly defined by the JSON keys
            if (EncodingMask == 0 && decoder is IJsonDecoder)
            {
                var masks = new StringCollection();
                foreach (ComplexTypePropertyInfo property in GetPropertyEnumerator())
                {
                    if (property.IsOptional)
                    {
                        masks.Add(property.Name);
                    }
                }

                EncodingMask = decoder.ReadEncodingMask(masks);
            }

            foreach (ComplexTypePropertyInfo property in GetPropertyEnumerator())
            {
                if (property.IsOptional && (property.OptionalFieldMask & EncodingMask) == 0)
                {
                    continue;
                }

                DecodeProperty(decoder, property);
            }

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not OptionalFieldsComplexType valueBaseType)
            {
                return false;
            }

            if (EncodingMask != valueBaseType.EncodingMask)
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
                if (property.IsOptional && (property.OptionalFieldMask & EncodingMask) == 0)
                {
                    continue;
                }

                if (!Utils.IsEqual(property.GetValue(this), property.GetValue(valueBaseType)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public override string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                var body = new StringBuilder();
                foreach (ComplexTypePropertyInfo property in GetPropertyEnumerator())
                {
                    if (property.IsOptional && (property.OptionalFieldMask & EncodingMask) == 0)
                    {
                        continue;
                    }

                    AppendPropertyValue(formatProvider, body, property.GetValue(this), property.ValueRank);
                }

                if (body.Length > 0)
                {
                    _ = body.Append('}');
                    return body.ToString();
                }

                if (!NodeId.IsNull(TypeId))
                {
                    return string.Format(formatProvider, "{{{0}}}", TypeId);
                }

                return "(null)";
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <inheritdoc/>
        public override object this[int index]
        {
            get
            {
                ComplexTypePropertyInfo property = m_propertyList[index];
                if (property.IsOptional && (property.OptionalFieldMask & EncodingMask) == 0)
                {
                    return null;
                }
                return property.GetValue(this);
            }
            set
            {
                ComplexTypePropertyInfo property = m_propertyList[index];
                property.SetValue(this, value);
                if (property.IsOptional)
                {
                    if (value == null)
                    {
                        EncodingMask &= ~property.OptionalFieldMask;
                    }
                    else
                    {
                        EncodingMask |= property.OptionalFieldMask;
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
                    if (property.IsOptional && (property.OptionalFieldMask & EncodingMask) == 0)
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
                        EncodingMask &= ~property.OptionalFieldMask;
                    }
                    else
                    {
                        EncodingMask |= property.OptionalFieldMask;
                    }
                }
                else
                {
                    throw new KeyNotFoundException();
                }
            }
        }

        /// <inheritdoc/>
        protected override void InitializePropertyAttributes()
        {
            base.InitializePropertyAttributes();

            // build optional field mask attribute
            uint optionalFieldMask = 1;
            foreach (ComplexTypePropertyInfo property in GetPropertyEnumerator())
            {
                property.OptionalFieldMask = 0;
                if (property.IsOptional)
                {
                    property.OptionalFieldMask = optionalFieldMask;
                    optionalFieldMask <<= 1;
                }
            }
        }
    }
} //namespace
