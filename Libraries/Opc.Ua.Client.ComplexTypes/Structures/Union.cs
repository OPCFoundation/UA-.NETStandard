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
using System.Text;
using System.Xml;

namespace Opc.Ua.Client.ComplexTypes.Structures
{
    /// <summary>
    /// Implements a union complex type.
    /// </summary>
    public class Union : Structure
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public Union(
            XmlQualifiedName xmlName,
            ExpandedNodeId typeId,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId,
            StructureDefinition structureDefinition,
            uint switchField = 0)
            : base(
                  xmlName,
                  typeId,
                  binaryEncodingId,
                  xmlEncodingId,
                  structureDefinition)
        {
            m_switchField = switchField;
        }

        /// <summary>
        /// The union selector determines which property is valid.
        /// A value of 0 means all properties are invalid, x=1..n means the
        /// xth property is valid.
        /// </summary>
        public uint SwitchField => m_switchField;

        /// <inheritdoc/>
        public override StructureType StructureType => StructureType.Union;

        /// <inheritdoc/>
        public override object Clone()
        {
            return CreateInstance();
        }

        /// <inheritdoc/>
        public override IEncodeable CreateInstance()
        {
            return new Union(
                XmlName,
                TypeId,
                BinaryEncodingId,
                XmlEncodingId,
                m_definition,
                m_switchField);
        }

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(XmlNamespace);

            // the encoder may return an override for the field name
            // e.g. to support reversible JSON encoding
            encoder.WriteSwitchField(m_switchField, out string fieldName);

            if (m_switchField != 0)
            {
                int unionSelector = 1;
                Field unionProperty = null;
                foreach (Field property in GetPropertyEnumerator())
                {
                    if (unionSelector == m_switchField)
                    {
                        unionProperty = property;
                        break;
                    }
                    unionSelector++;
                }

                fieldName ??= unionProperty.Name;

                EncodeProperty(encoder, fieldName, unionProperty);
            }

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(XmlNamespace);

            uint unionSelector = decoder.ReadSwitchField(null, out _);

            bool isJsonDecoder = decoder.EncodingType == EncodingType.Json;
            if (unionSelector == 0 && isJsonDecoder)
            {
                var fields = new List<string>();
                foreach (Field property in GetPropertyEnumerator())
                {
                    if (property.IsOptional)
                    {
                        fields.Add(property.Name);
                    }
                }

                unionSelector = decoder.ReadSwitchField(fields, out _);
            }

            m_switchField = unionSelector;
            if (unionSelector > 0)
            {
                foreach (Field property in GetPropertyEnumerator())
                {
                    if (--unionSelector == 0)
                    {
                        DecodeProperty(decoder, property.Name, property);
                        break;
                    }
                }
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

            if (encodeable is not Union valueBaseType)
            {
                return false;
            }

            if (SwitchField != valueBaseType.SwitchField)
            {
                return false;
            }

            if (valueBaseType.m_propertyList.Count != m_propertyList.Count)
            {
                return false;
            }

            if (m_switchField != 0)
            {
                uint unionSelector = m_switchField;
                for (int ii = 0; ii < m_propertyList.Count; ii++)
                {
                    if (--unionSelector == 0)
                    {
                        if (m_propertyList[ii].Value != valueBaseType.m_propertyList[ii].Value)
                        {
                            return false;
                        }
                        break;
                    }
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
                if (m_switchField != 0)
                {
                    uint unionSelector = m_switchField;
                    foreach (Field property in GetPropertyEnumerator())
                    {
                        if (--unionSelector == 0)
                        {
                            Variant unionProperty = property.Value;
                            AppendPropertyValue(
                                body,
                                unionProperty);
                            break;
                        }
                    }
                }

                if (body.Length > 0)
                {
                    _ = body.Append('}');
                    return body.ToString();
                }

                if (!TypeId.IsNull)
                {
                    return string.Format(formatProvider, "{{{0}}}", TypeId);
                }

                return "(null)";
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

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
        public override Variant this[int index]
        {
            get
            {
                if (index + 1 == (int)m_switchField)
                {
                    return m_propertyList[index].Value;
                }
                if (index < 0 && m_switchField > 0)
                {
                    return m_propertyList[(int)m_switchField - 1].Value;
                }
                return default;
            }
            set
            {
                if (index >= 0)
                {
                    m_propertyList[index].Value = value;
                    m_switchField = (uint)m_propertyList[index].Order;
                    if (!value.IsNull)
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
        public override Variant this[string name]
        {
            get
            {
                if (SwitchField > 0)
                {
                    if (m_propertyDict.TryGetValue(name, out Field property))
                    {
                        if ((int)m_switchField == property.Order)
                        {
                            return property.Value;
                        }
                    }
                    else
                    {
                        return m_propertyList[(int)SwitchField - 1].Value;
                    }
                }
                return default;
            }
            set
            {
                if (m_propertyDict.TryGetValue(name, out Field property))
                {
                    property.Value = value;
                    m_switchField = (uint)property.Order;
                    if (!value.IsNull)
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
        public Variant Value
            => m_switchField == 0 ? default : m_propertyList[(int)m_switchField - 1].Value;

        /// <summary>
        /// The selector for the value of the Union.
        /// </summary>
        protected uint m_switchField;
    }
}
