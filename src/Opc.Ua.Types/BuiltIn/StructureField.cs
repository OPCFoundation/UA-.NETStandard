// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Structure field
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class StructureField :
        IEncodeable,
        IEquatable<StructureField>
    {
        /// <summary>
        /// Name
        /// </summary>
        [DataMember(Name = "Name", IsRequired = false, Order = 1)] public string? Name { get; set; }

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
        public ArrayOf<uint> ArrayDimensions { get; set; }

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
        public virtual bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not StructureField value)
            {
                return false;
            }

            if (Name != value.Name)
            {
                return false;
            }

            if (Description != value.Description)
            {
                return false;
            }

            if (DataType != value.DataType)
            {
                return false;
            }

            if (ValueRank != value.ValueRank)
            {
                return false;
            }

            if (ArrayDimensions != value.ArrayDimensions)
            {
                return false;
            }

            if (MaxStringLength != value.MaxStringLength)
            {
                return false;
            }

            if (IsOptional != value.IsOptional)
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return IsEqual(obj as IEncodeable);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                Name,
                Description,
                DataType,
                ValueRank,
                ArrayDimensions,
                MaxStringLength,
                IsOptional);
        }

        /// <inheritdoc/>
        public bool Equals(StructureField? other)
        {
            return IsEqual(other)!;
        }

        /// <inheritdoc/>
        public static bool operator ==(StructureField? left, StructureField? right)
        {
            if (left is null)
            {
                return right is null;
            }
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(StructureField? left, StructureField? right)
        {
            return !(left == right);
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

            clone.Name = Name == null ? null : CoreUtils.Clone(Name);
            clone.Description = CoreUtils.Clone(Description);
            clone.DataType = DataType;
            clone.ValueRank = CoreUtils.Clone(ValueRank);
            clone.ArrayDimensions = CoreUtils.Clone(ArrayDimensions);
            clone.MaxStringLength = CoreUtils.Clone(MaxStringLength);
            clone.IsOptional = CoreUtils.Clone(IsOptional);

            return clone;
        }
    }
}
