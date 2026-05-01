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
    /// A complex type with optional fields.
    /// </summary>
    public class StructureWithOptionalFields : Structure
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public StructureWithOptionalFields(
            XmlQualifiedName xmlName,
            ExpandedNodeId typeId,
            ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId,
            StructureDefinition structureDefinition,
            Dictionary<string, BuiltInType> fieldTypes)
            : base(
                  xmlName,
                  typeId,
                  binaryEncodingId,
                  xmlEncodingId,
                  structureDefinition,
                  fieldTypes)
        {
            EncodingMask = 0;

            // build optional field mask attribute
            uint optionalFieldMask = 1;
            foreach (Field property in PropertyList)
            {
                property.OptionalFieldMask = 0;
                if (property.IsOptional)
                {
                    property.OptionalFieldMask = optionalFieldMask;
                    optionalFieldMask <<= 1;
                }
            }
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        protected StructureWithOptionalFields(StructureWithOptionalFields source)
            : base(source)
        {
            EncodingMask = source.EncodingMask;
        }

        /// <inheritdoc/>
        public override StructureType StructureType => StructureType.StructureWithOptionalFields;

        /// <summary>
        /// The encoding mask for the optional fields.
        /// </summary>
        public uint EncodingMask { get; private set; }

        /// <inheritdoc/>
        public override object Clone()
        {
            return new StructureWithOptionalFields(this);
        }

        /// <inheritdoc/>
        public override IEncodeable CreateInstance()
        {
            return new StructureWithOptionalFields(
                XmlName,
                TypeId,
                BinaryEncodingId,
                XmlEncodingId,
                Definition,
                FieldTypes);
        }

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(XmlNamespace);

            encoder.WriteEncodingMask(EncodingMask);

            foreach (Field property in PropertyList)
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

            EncodingMask = decoder.ReadEncodingMask(null!);

            // try again if the mask is implicitly defined by the JSON keys
            if (EncodingMask == 0 && decoder is JsonDecoder)
            {
                var masks = new List<string>();
                foreach (Field property in PropertyList)
                {
                    if (property.IsOptional)
                    {
                        masks.Add(property.Name);
                    }
                }

                EncodingMask = decoder.ReadEncodingMask(masks);
            }

            foreach (Field property in PropertyList)
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
        public override bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not StructureWithOptionalFields valueBaseType)
            {
                return false;
            }

            if (EncodingMask != valueBaseType.EncodingMask)
            {
                return false;
            }

            if (valueBaseType.PropertyList.Count != PropertyList.Count)
            {
                return false;
            }

            for (int ii = 0; ii < PropertyList.Count; ii++)
            {
                if (PropertyList[ii].IsOptional &&
                    (PropertyList[ii].OptionalFieldMask & EncodingMask) == 0)
                {
                    continue;
                }

                if (PropertyList[ii].Value != valueBaseType.PropertyList[ii].Value)
                {
                    return false;
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
                foreach (Field property in PropertyList)
                {
                    if (property.IsOptional && (property.OptionalFieldMask & EncodingMask) == 0)
                    {
                        continue;
                    }

                    AppendPropertyValue(
                        body,
                        property.Value);
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

        /// <inheritdoc/>
        public override Variant this[int index]
        {
            get
            {
                Field property = PropertyList[index];
                if (property.IsOptional && (property.OptionalFieldMask & EncodingMask) == 0)
                {
                    return default;
                }
                return property.Value;
            }
            set
            {
                Field property = PropertyList[index];
                property.Value = value;
                if (property.IsOptional)
                {
                    if (value.IsNull)
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
        public override Variant this[string name]
        {
            get
            {
                if (PropertyDict.TryGetValue(name, out Field? property))
                {
                    if (property.IsOptional && (property.OptionalFieldMask & EncodingMask) == 0)
                    {
                        return default;
                    }
                    return property.Value;
                }
                throw new KeyNotFoundException();
            }
            set
            {
                if (PropertyDict.TryGetValue(name, out Field? property))
                {
                    property.Value = value;
                    if (property.IsOptional)
                    {
                        if (value.IsNull)
                        {
                            EncodingMask &= ~property.OptionalFieldMask;
                        }
                        else
                        {
                            EncodingMask |= property.OptionalFieldMask;
                        }
                    }
                }
                else
                {
                    throw new KeyNotFoundException();
                }
            }
        }
    }
}
