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
    /// Structure field
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class StructureField : IEncodeable, IJsonEncodeable
    {
        /// <inheritdoc/>
        public StructureField()
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
            Description = null;
            DataType = null;
            ValueRank = 0;
            m_arrayDimensions = [];
            MaxStringLength = 0;
            IsOptional = false;
        }

        /// <summary>
        /// Name
        /// </summary>
        [DataMember(Name = "Name", IsRequired = false, Order = 1)]
        public string Name { get; set; }

        /// <summary>
        /// Description
        /// </summary>
        [DataMember(Name = "Description", IsRequired = false, Order = 2)]
        public LocalizedText Description { get; set; }

        /// <summary>
        /// Data type
        /// </summary>
        [DataMember(Name = "DataType", IsRequired = false, Order = 3)]
        public NodeId DataType { get; set; }

        /// <summary>
        /// Value rank
        /// </summary>
        [DataMember(Name = "ValueRank", IsRequired = false, Order = 4)]
        public int ValueRank { get; set; }

        /// <summary>
        /// Array dimensions
        /// </summary>
        [DataMember(Name = "ArrayDimensions", IsRequired = false, Order = 5)]
        public UInt32Collection ArrayDimensions
        {
            get
            {
                return m_arrayDimensions;
            }

            set
            {
                m_arrayDimensions = value;

                if (value == null)
                {
                    m_arrayDimensions = [];
                }
            }
        }

        /// <summary>
        /// Max string length
        /// </summary>
        [DataMember(Name = "MaxStringLength", IsRequired = false, Order = 6)]
        public uint MaxStringLength { get; set; }

        /// <summary>
        /// Is optional
        /// </summary>
        [DataMember(Name = "IsOptional", IsRequired = false, Order = 7)]
        public bool IsOptional { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.StructureField;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.StructureField_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.StructureField_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.StructureField_Encoding_DefaultJson;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteString("Name", Name);
            encoder.WriteLocalizedText("Description", Description);
            encoder.WriteNodeId("DataType", DataType);
            encoder.WriteInt32("ValueRank", ValueRank);
            encoder.WriteUInt32Array("ArrayDimensions", ArrayDimensions);
            encoder.WriteUInt32("MaxStringLength", MaxStringLength);
            encoder.WriteBoolean("IsOptional", IsOptional);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Name = decoder.ReadString("Name");
            Description = decoder.ReadLocalizedText("Description");
            DataType = decoder.ReadNodeId("DataType");
            ValueRank = decoder.ReadInt32("ValueRank");
            ArrayDimensions = decoder.ReadUInt32Array("ArrayDimensions");
            MaxStringLength = decoder.ReadUInt32("MaxStringLength");
            IsOptional = decoder.ReadBoolean("IsOptional");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            var value = encodeable as StructureField;

            if (value == null)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Name, value.Name))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Description, value.Description))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(DataType, value.DataType))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(ValueRank, value.ValueRank))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(m_arrayDimensions, value.m_arrayDimensions))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(MaxStringLength, value.MaxStringLength))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(IsOptional, value.IsOptional))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (StructureField)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (StructureField)base.MemberwiseClone();

            clone.Name = CoreUtils.Clone(Name);
            clone.Description = CoreUtils.Clone(Description);
            clone.DataType = CoreUtils.Clone(DataType);
            clone.ValueRank = (int)CoreUtils.Clone(ValueRank);
            clone.m_arrayDimensions = CoreUtils.Clone(m_arrayDimensions);
            clone.MaxStringLength = (uint)CoreUtils.Clone(MaxStringLength);
            clone.IsOptional = (bool)CoreUtils.Clone(IsOptional);

            return clone;
        }

        private UInt32Collection m_arrayDimensions;
    }

    /// <summary>
    /// Structure field collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfStructureField",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "StructureField")]
    public class StructureFieldCollection : List<StructureField>, ICloneable
    {
        /// <inheritdoc/>
        public StructureFieldCollection()
        {
        }

        /// <inheritdoc/>
        public StructureFieldCollection(int capacity) : base(capacity)
        {
        }

        /// <inheritdoc/>
        public StructureFieldCollection(IEnumerable<StructureField> collection) : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator StructureFieldCollection(StructureField[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator StructureField[](StructureFieldCollection values)
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
            return (StructureFieldCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new StructureFieldCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
