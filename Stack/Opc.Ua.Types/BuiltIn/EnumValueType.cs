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
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Enum value
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class EnumValueType : IEncodeable, IJsonEncodeable
    {
        /// <summary>
        /// Create enum value
        /// </summary>
        public EnumValueType()
        {
            Initialize();
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        private void Initialize()
        {
            Value = 0;
            DisplayName = null;
            Description = null;
        }

        /// <summary>
        /// Enum value
        /// </summary>
        [DataMember(Name = "Value", IsRequired = false, Order = 1)]
        public long Value { get; set; }

        /// <summary>
        /// Display name
        /// </summary>
        [DataMember(Name = "DisplayName", IsRequired = false, Order = 2)]
        public LocalizedText DisplayName { get; set; }

        /// <summary>
        /// Description
        /// </summary>
        [DataMember(Name = "Description", IsRequired = false, Order = 3)]
        public LocalizedText Description { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.EnumValueType;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.EnumValueType_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.EnumValueType_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.EnumValueType_Encoding_DefaultJson;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteInt64("Value", Value);
            encoder.WriteLocalizedText("DisplayName", DisplayName);
            encoder.WriteLocalizedText("Description", Description);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Value = decoder.ReadInt64("Value");
            DisplayName = decoder.ReadLocalizedText("DisplayName");
            Description = decoder.ReadLocalizedText("Description");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not EnumValueType value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Value, value.Value))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(DisplayName, value.DisplayName))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Description, value.Description))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (EnumValueType)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (EnumValueType)base.MemberwiseClone();

            clone.Value = (long)CoreUtils.Clone(Value);
            clone.DisplayName = CoreUtils.Clone(DisplayName);
            clone.Description = CoreUtils.Clone(Description);

            return clone;
        }
    }

    /// <summary>
    /// List of enum value types
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfEnumValueType",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "EnumValueType")]
    public class EnumValueTypeCollection : List<EnumValueType>, ICloneable
    {
        /// <inheritdoc/>
        public EnumValueTypeCollection()
        {
        }

        /// <inheritdoc/>
        public EnumValueTypeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public EnumValueTypeCollection(IEnumerable<EnumValueType> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator EnumValueTypeCollection(EnumValueType[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator EnumValueType[](EnumValueTypeCollection values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return null;
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return (EnumValueTypeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new EnumValueTypeCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
