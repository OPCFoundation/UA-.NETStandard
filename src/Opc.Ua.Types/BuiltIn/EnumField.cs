// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Enum field
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class EnumField : EnumValueType, IEquatable<EnumField>
    {
        /// <summary>
        /// Name
        /// </summary>
        [DataMember(Name = "Name", IsRequired = false, Order = 1)] public string? Name { get; set; }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.EnumField;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.EnumField_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.EnumField_Encoding_DefaultXml;

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
        public override bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not EnumField value)
            {
                return false;
            }

            if (Name != value.Name)
            {
                return false;
            }

            return base.IsEqual(encodeable);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return IsEqual(obj as IEncodeable);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(base.GetHashCode(), Name);
        }

        /// <inheritdoc/>
        public bool Equals(EnumField? other)
        {
            return IsEqual(other)!;
        }

        /// <inheritdoc/>
        public static bool operator ==(EnumField? left, EnumField? right)
        {
            if (left is null)
            {
                return right is null;
            }
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(EnumField? left, EnumField? right)
        {
            return !(left == right);
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

            clone.Name = Name == null ? null : CoreUtils.Clone(Name);

            return clone;
        }
    }
}
