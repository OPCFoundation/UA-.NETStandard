/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

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
