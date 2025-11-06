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
    /// Enum definition
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public partial class EnumDefinition : DataTypeDefinition
    {
        /// <inheritdoc/>
        public EnumDefinition()
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
            m_fields = [];
        }

        /// <summary>
        /// Fields
        /// </summary>
        [DataMember(Name = "Fields", IsRequired = false, Order = 1)]
        public EnumFieldCollection Fields
        {
            get
            {
                return m_fields;
            }

            set
            {
                m_fields = value;

                if (value == null)
                {
                    m_fields = [];
                }
            }
        }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.EnumDefinition;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.EnumDefinition_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.EnumDefinition_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override ExpandedNodeId JsonEncodingId => ObjectIds.EnumDefinition_Encoding_DefaultJson;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteEncodeableArray("Fields", Fields.ToArray(), typeof(EnumField));

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Fields = (EnumFieldCollection)decoder.ReadEncodeableArray("Fields", typeof(EnumField));

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            var value = encodeable as EnumDefinition;

            if (value == null)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(m_fields, value.m_fields))
            {
                return false;
            }

            return base.IsEqual(encodeable);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return (EnumDefinition)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (EnumDefinition)base.MemberwiseClone();

            clone.m_fields = CoreUtils.Clone(m_fields);

            return clone;
        }

        private EnumFieldCollection m_fields;
    }

    /// <summary>
    /// Enum definition collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfEnumDefinition",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "EnumDefinition")]
    public class EnumDefinitionCollection : List<EnumDefinition>, ICloneable
    {
        /// <inheritdoc/>
        public EnumDefinitionCollection()
        {
        }

        /// <inheritdoc/>
        public EnumDefinitionCollection(int capacity) : base(capacity)
        {
        }

        /// <inheritdoc/>
        public EnumDefinitionCollection(IEnumerable<EnumDefinition> collection) : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator EnumDefinitionCollection(EnumDefinition[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator EnumDefinition[](EnumDefinitionCollection values)
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
            return (EnumDefinitionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new EnumDefinitionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
