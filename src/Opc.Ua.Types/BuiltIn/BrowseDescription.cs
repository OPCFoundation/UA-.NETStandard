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
    /// Browse description
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class BrowseDescription : IEncodeable, IEquatable<BrowseDescription>
    {
        /// <inheritdoc/>
        public BrowseDescription()
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
            NodeId = default;
            BrowseDirection = BrowseDirection.Forward;
            ReferenceTypeId = default;
            IncludeSubtypes = true;
            NodeClassMask = 0;
            ResultMask = 0;
        }

        /// <summary>
        /// Node id
        /// </summary>
        [DataMember(Name = "NodeId", IsRequired = false, Order = 1)]
        public NodeId NodeId { get; set; }

        /// <summary>
        /// Browse direction
        /// </summary>
        [DataMember(Name = "BrowseDirection", IsRequired = false, Order = 2)]
        public BrowseDirection BrowseDirection { get; set; }

        /// <summary>
        /// Reference type id
        /// </summary>
        [DataMember(Name = "ReferenceTypeId", IsRequired = false, Order = 3)]
        public NodeId ReferenceTypeId { get; set; }

        /// <summary>
        /// Include sub types
        /// </summary>
        [DataMember(Name = "IncludeSubtypes", IsRequired = false, Order = 4)]
        public bool IncludeSubtypes { get; set; }

        /// <summary>
        /// Node class mask
        /// </summary>
        [DataMember(Name = "NodeClassMask", IsRequired = false, Order = 5)]
        public uint NodeClassMask { get; set; }

        /// <summary>
        /// Result mask
        /// </summary>
        [DataMember(Name = "ResultMask", IsRequired = false, Order = 6)]
        public uint ResultMask { get; set; }

        /// <summary>
        /// A handle assigned to the item during processing.
        /// </summary>
        [IgnoreDataMember]
        public object? Handle { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.BrowseDescription;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.BrowseDescription_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.BrowseDescription_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteNodeId("NodeId", NodeId);
            encoder.WriteEnumerated("BrowseDirection", BrowseDirection);
            encoder.WriteNodeId("ReferenceTypeId", ReferenceTypeId);
            encoder.WriteBoolean("IncludeSubtypes", IncludeSubtypes);
            encoder.WriteUInt32("NodeClassMask", NodeClassMask);
            encoder.WriteUInt32("ResultMask", ResultMask);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            NodeId = decoder.ReadNodeId("NodeId");
            BrowseDirection = decoder.ReadEnumerated<BrowseDirection>("BrowseDirection");
            ReferenceTypeId = decoder.ReadNodeId("ReferenceTypeId");
            IncludeSubtypes = decoder.ReadBoolean("IncludeSubtypes");
            NodeClassMask = decoder.ReadUInt32("NodeClassMask");
            ResultMask = decoder.ReadUInt32("ResultMask");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not BrowseDescription value)
            {
                return false;
            }

            if (NodeId != value.NodeId)
            {
                return false;
            }

            if (BrowseDirection != value.BrowseDirection)
            {
                return false;
            }

            if (ReferenceTypeId != value.ReferenceTypeId)
            {
                return false;
            }

            if (IncludeSubtypes != value.IncludeSubtypes)
            {
                return false;
            }

            if (NodeClassMask != value.NodeClassMask)
            {
                return false;
            }

            if (ResultMask != value.ResultMask)
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
                NodeId,
                BrowseDirection,
                ReferenceTypeId,
                IncludeSubtypes,
                NodeClassMask,
                ResultMask);
        }

        /// <inheritdoc/>
        public bool Equals(BrowseDescription? other)
        {
            return IsEqual(other)!;
        }

        /// <inheritdoc/>
        public static bool operator ==(BrowseDescription? left, BrowseDescription? right)
        {
            if (left is null)
            {
                return right is null;
            }
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(BrowseDescription? left, BrowseDescription? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (BrowseDescription)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (BrowseDescription)base.MemberwiseClone();

            clone.NodeId = NodeId;
            clone.BrowseDirection = CoreUtils.Clone(BrowseDirection);
            clone.ReferenceTypeId = ReferenceTypeId;
            clone.IncludeSubtypes = CoreUtils.Clone(IncludeSubtypes);
            clone.NodeClassMask = CoreUtils.Clone(NodeClassMask);
            clone.ResultMask = CoreUtils.Clone(ResultMask);

            return clone;
        }
    }
}
