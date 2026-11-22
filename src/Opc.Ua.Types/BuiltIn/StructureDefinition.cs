// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Structure definition
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class StructureDefinition :
        DataTypeDefinition,
        IEquatable<StructureDefinition>
    {
        /// <summary>
        /// Default encoding
        /// </summary>
        [DataMember(Name = "DefaultEncodingId", IsRequired = false, Order = 1)]
        public NodeId DefaultEncodingId { get; set; }

        /// <summary>
        /// Base data tyoe
        /// </summary>
        [DataMember(Name = "BaseDataType", IsRequired = false, Order = 2)]
        public NodeId BaseDataType { get; set; }

        /// <summary>
        /// Structure type
        /// </summary>
        [DataMember(Name = "StructureType", IsRequired = false, Order = 3)]
        public StructureType StructureType { get; set; }

        /// <summary>
        /// Fields
        /// </summary>
        [DataMember(Name = "Fields", IsRequired = false, Order = 4)]
        public ArrayOf<StructureField> Fields { get; set; }

        /// <summary>
        /// The first non-inherited field in the structure definition.
        /// </summary>
        public int FirstExplicitFieldIndex { get; set; }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId
            => DataTypeIds.StructureDefinition;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId
            => ObjectIds.StructureDefinition_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId
            => ObjectIds.StructureDefinition_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteNodeId("DefaultEncodingId", DefaultEncodingId);
            encoder.WriteNodeId("BaseDataType", BaseDataType);
            encoder.WriteEnumerated("StructureType", StructureType);
            encoder.WriteEncodeableArray("Fields", Fields);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            DefaultEncodingId = decoder.ReadNodeId("DefaultEncodingId");
            BaseDataType = decoder.ReadNodeId("BaseDataType");
            StructureType = decoder.ReadEnumerated<StructureType>("StructureType");
            Fields = decoder.ReadEncodeableArray<StructureField>("Fields");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not StructureDefinition value)
            {
                return false;
            }

            if (DefaultEncodingId != value.DefaultEncodingId)
            {
                return false;
            }

            if (BaseDataType != value.BaseDataType)
            {
                return false;
            }

            if (StructureType != value.StructureType)
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
            // hash exactly the members IsEqual compares; the base class has
            // no value based hash code.
            return HashCode.Combine(
                DefaultEncodingId,
                BaseDataType,
                StructureType,
                Fields);
        }

        /// <inheritdoc/>
        public bool Equals(StructureDefinition? other)
        {
            return IsEqual(other)!;
        }

        /// <inheritdoc/>
        public static bool operator ==(StructureDefinition? left, StructureDefinition? right)
        {
            if (left is null)
            {
                return right is null;
            }
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(StructureDefinition? left, StructureDefinition? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return (StructureDefinition)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (StructureDefinition)base.MemberwiseClone();

            clone.DefaultEncodingId = DefaultEncodingId;
            clone.BaseDataType = BaseDataType;
            clone.StructureType = CoreUtils.Clone(StructureType);
            clone.Fields = CoreUtils.Clone(Fields);

            return clone;
        }

        /// <summary>
        /// Set the default encoding id for the requested data encoding.
        /// </summary>
        /// <param name="context">The system context with the encodeable factory.</param>
        /// <param name="typeId">The type id of the Data Type.</param>
        /// <param name="dataEncoding">The data encoding to apply to the default encoding id.</param>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public void SetDefaultEncodingId(
            ISystemContext context,
            NodeId typeId,
            QualifiedName dataEncoding)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // note: custom types must be added to the encodeable factory by the node manager to be found
            var expandedTypeId = NodeId.ToExpandedNodeId(typeId, context.NamespaceUris);
            if (context.EncodeableFactory.TryGetEncodeableType(expandedTypeId, out IEncodeableType? type) &&
                type?.CreateInstance() is IEncodeable encodeable)
            {
                if (dataEncoding.IsNull || dataEncoding.Name == BrowseNames.DefaultBinary)
                {
                    DefaultEncodingId = ExpandedNodeId.ToNodeId(
                        encodeable.BinaryEncodingId,
                        context.NamespaceUris);
                }
                else if (dataEncoding.Name == BrowseNames.DefaultXml)
                {
                    DefaultEncodingId = ExpandedNodeId.ToNodeId(
                        encodeable.XmlEncodingId,
                        context.NamespaceUris);
                }
            }
        }
    }
}
