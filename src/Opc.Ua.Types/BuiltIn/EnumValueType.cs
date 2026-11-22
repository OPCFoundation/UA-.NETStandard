// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Enum value
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class EnumValueType : IEncodeable, IEquatable<EnumValueType>
    {
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
        public virtual bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not EnumValueType value)
            {
                return false;
            }

            if (Value != value.Value)
            {
                return false;
            }

            if (DisplayName != value.DisplayName)
            {
                return false;
            }

            if (Description != value.Description)
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
                Value,
                DisplayName,
                Description);
        }

        /// <inheritdoc/>
        public bool Equals(EnumValueType? other)
        {
            return IsEqual(other)!;
        }

        /// <inheritdoc/>
        public static bool operator ==(EnumValueType? left, EnumValueType? right)
        {
            if (left is null)
            {
                return right is null;
            }
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(EnumValueType? left, EnumValueType? right)
        {
            return !(left == right);
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

            clone.Value = CoreUtils.Clone(Value);
            clone.DisplayName = CoreUtils.Clone(DisplayName);
            clone.Description = CoreUtils.Clone(Description);

            return clone;
        }
    }
}
