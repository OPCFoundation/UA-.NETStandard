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
    /// Enum definition
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class EnumDefinition : DataTypeDefinition, IEquatable<EnumDefinition>
    {
        /// <summary>
        /// Fields
        /// </summary>
        [DataMember(Name = "Fields", IsRequired = false, Order = 1)]
        public ArrayOf<EnumField> Fields { get; set; }

        /// <summary>
        /// If TRUE the values are bit positions rather than values.
        /// </summary>
        public bool IsOptionSet { get; set; }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId
            => DataTypeIds.EnumDefinition;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId
            => ObjectIds.EnumDefinition_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId
            => ObjectIds.EnumDefinition_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteEncodeableArray("Fields", Fields);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Fields = decoder.ReadEncodeableArray<EnumField>("Fields");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not EnumDefinition value)
            {
                return false;
            }

            if (Fields != value.Fields)
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
            // the base class has no value based hash code.
            return Fields.GetHashCode();
        }

        /// <inheritdoc/>
        public bool Equals(EnumDefinition? other)
        {
            return IsEqual(other)!;
        }

        /// <inheritdoc/>
        public static bool operator ==(EnumDefinition? left, EnumDefinition? right)
        {
            if (left is null)
            {
                return right is null;
            }
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(EnumDefinition? left, EnumDefinition? right)
        {
            return !(left == right);
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

            clone.Fields = CoreUtils.Clone(Fields);

            return clone;
        }
    }
}
