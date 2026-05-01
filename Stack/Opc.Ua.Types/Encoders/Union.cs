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

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Opc.Ua.Encoders
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
            Dictionary<string, BuiltInType> fieldTypes,
            uint switchField = 0)
            : base(
                  xmlName,
                  typeId,
                  binaryEncodingId,
                  xmlEncodingId,
                  structureDefinition,
                  fieldTypes)
        {
            SwitchField = switchField;
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        protected Union(Union source)
            : base(source)
        {
            SwitchField = source.SwitchField;
        }

        /// <summary>
        /// The union selector determines which property is valid.
        /// A value of 0 means all properties are invalid, x=1..n means the
        /// xth property is valid.
        /// </summary>
        public uint SwitchField { get; protected set; }

        /// <inheritdoc/>
        public override StructureType StructureType => StructureType.Union;

        /// <inheritdoc/>
        public override object Clone()
        {
            return new Union(this);
        }

        /// <inheritdoc/>
        public override IEncodeable CreateInstance()
        {
            return new Union(
                XmlName,
                TypeId,
                BinaryEncodingId,
                XmlEncodingId,
                Definition,
                FieldTypes,
                SwitchField);
        }

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(XmlNamespace);

            // the encoder may return an override for the field name
            // e.g. to support reversible JSON encoding
            encoder.WriteSwitchField(SwitchField, out string? fieldName);

            if (SwitchField != 0)
            {
                int unionSelector = 1;
                Field? unionProperty = null;
                foreach (Field property in PropertyList)
                {
                    if (unionSelector == SwitchField)
                    {
                        unionProperty = property;
                        break;
                    }
                    unionSelector++;
                }

                if (unionProperty != null)
                {
                    fieldName ??= unionProperty.Name;
                    EncodeProperty(encoder, fieldName, unionProperty);
                }
            }

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(XmlNamespace);

            uint unionSelector = decoder.ReadSwitchField(null!, out _);

            bool isJsonDecoder = decoder.EncodingType == EncodingType.Json;
            if (unionSelector == 0 && isJsonDecoder)
            {
                var fields = new List<string>();
                foreach (Field property in PropertyList)
                {
                    if (property.IsOptional)
                    {
                        fields.Add(property.Name);
                    }
                }

                unionSelector = decoder.ReadSwitchField(fields, out _);
            }

            SwitchField = unionSelector;
            if (unionSelector > 0)
            {
                foreach (Field property in PropertyList)
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

            if (valueBaseType.PropertyList.Count != PropertyList.Count)
            {
                return false;
            }

            if (SwitchField != 0)
            {
                uint unionSelector = SwitchField;
                for (int ii = 0; ii < PropertyList.Count; ii++)
                {
                    if (--unionSelector == 0)
                    {
                        if (PropertyList[ii].Value != valueBaseType.PropertyList[ii].Value)
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
        public override string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (format == null)
            {
                var body = new StringBuilder();
                if (SwitchField != 0)
                {
                    uint unionSelector = SwitchField;
                    foreach (Field property in PropertyList)
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

            throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
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
                if (index + 1 == (int)SwitchField)
                {
                    return PropertyList[index].Value;
                }
                if (index < 0 && SwitchField > 0)
                {
                    return PropertyList[(int)SwitchField - 1].Value;
                }
                return default;
            }
            set
            {
                if (index >= 0)
                {
                    PropertyList[index].Value = value;
                    SwitchField = (uint)PropertyList[index].Order;
                    if (!value.IsNull)
                    {
                        return;
                    }
                    // reset union selector if value is a null
                }
                SwitchField = 0;
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
                    if (PropertyDict.TryGetValue(name, out Field? property))
                    {
                        if ((int)SwitchField == property.Order)
                        {
                            return property.Value;
                        }
                    }
                    else
                    {
                        return PropertyList[(int)SwitchField - 1].Value;
                    }
                }
                return default;
            }
            set
            {
                if (PropertyDict.TryGetValue(name, out Field? property))
                {
                    property.Value = value;
                    SwitchField = (uint)property.Order;
                    if (!value.IsNull)
                    {
                        return;
                    }
                    // reset union selector if value is a null
                }
                SwitchField = 0;
            }
        }

        /// <summary>
        /// Simple accessor for Union to access current Value.
        /// </summary>
        public Variant Value
            => SwitchField == 0 ? default : PropertyList[(int)SwitchField - 1].Value;
    }
}
