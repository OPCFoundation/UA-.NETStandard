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
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Enum field
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class EnumField : EnumValueType
    {
        /// <inheritdoc/>
        public EnumField()
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
            Name = null;
        }

        /// <summary>
        /// Name
        /// </summary>
        [DataMember(Name = "Name", IsRequired = false, Order = 1)]
        public string Name { get; set; }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.EnumField;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.EnumField_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.EnumField_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override ExpandedNodeId JsonEncodingId => ObjectIds.EnumField_Encoding_DefaultJson;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteString("Name", Name);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Name = decoder.ReadString("Name");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not EnumField value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Name, value.Name))
            {
                return false;
            }

            return base.IsEqual(encodeable);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return (EnumField)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (EnumField)base.MemberwiseClone();

            clone.Name = CoreUtils.Clone(Name);

            return clone;
        }
    }

    /// <summary>
    /// List of EnumField objects
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfEnumField",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "EnumField")]
    public class EnumFieldCollection : List<EnumField>, ICloneable
    {
        /// <inheritdoc/>
        public EnumFieldCollection()
        {
        }

        /// <inheritdoc/>
        public EnumFieldCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public EnumFieldCollection(IEnumerable<EnumField> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator EnumFieldCollection(EnumField[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator EnumField[](EnumFieldCollection values)
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
            return (EnumFieldCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new EnumFieldCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
