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
    /// Reference node
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class ReferenceNode :
        IEncodeable,
        IJsonEncodeable,
        IReference,
        IEquatable<ReferenceNode>,
        IComparable,
        IComparable<ReferenceNode>
    {
        /// <summary>
        /// Initializes the reference.
        /// </summary>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="isInverse">if set to <c>true</c> [is inverse].</param>
        /// <param name="targetId">The target id.</param>
        public ReferenceNode(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId)
        {
            ReferenceTypeId = referenceTypeId;
            IsInverse = isInverse;
            TargetId = targetId;
        }

        /// <inheritdoc/>
        public ReferenceNode()
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
            ReferenceTypeId = null;
            IsInverse = true;
            TargetId = null;
        }

        /// <summary>
        /// Reference type
        /// </summary>
        [DataMember(Name = "ReferenceTypeId", IsRequired = false, Order = 1)]
        public NodeId ReferenceTypeId { get; set; }

        /// <summary>
        /// Is inverse
        /// </summary>
        [DataMember(Name = "IsInverse", IsRequired = false, Order = 2)]
        public bool IsInverse { get; set; }

        /// <summary>
        /// Target id
        /// </summary>
        [DataMember(Name = "TargetId", IsRequired = false, Order = 3)]
        public ExpandedNodeId TargetId { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.ReferenceNode;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.ReferenceNode_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.ReferenceNode_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.ReferenceNode_Encoding_DefaultJson;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteNodeId("ReferenceTypeId", ReferenceTypeId);
            encoder.WriteBoolean("IsInverse", IsInverse);
            encoder.WriteExpandedNodeId("TargetId", TargetId);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            ReferenceTypeId = decoder.ReadNodeId("ReferenceTypeId");
            IsInverse = decoder.ReadBoolean("IsInverse");
            TargetId = decoder.ReadExpandedNodeId("TargetId");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not ReferenceNode value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(ReferenceTypeId, value.ReferenceTypeId))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(IsInverse, value.IsInverse))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(TargetId, value.TargetId))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (ReferenceNode)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (ReferenceNode)base.MemberwiseClone();

            clone.ReferenceTypeId = CoreUtils.Clone(ReferenceTypeId);
            clone.IsInverse = (bool)CoreUtils.Clone(IsInverse);
            clone.TargetId = CoreUtils.Clone(TargetId);

            return clone;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (IsInverse)
            {
                return CoreUtils.Format("<!{0}>{1}", ReferenceTypeId, TargetId);
            }

            return CoreUtils.Format("<{0}>{1}", ReferenceTypeId, TargetId);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return CompareTo(obj) == 0;
        }

        /// <inheritdoc/>
        public bool Equals(ReferenceNode other)
        {
            return CompareTo(other) == 0;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ReferenceTypeId);
            hash.Add(IsInverse);
            hash.Add(TargetId);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(ReferenceNode a, object b)
        {
            return a is null ? b is null : a.CompareTo(b) == 0;
        }

        /// <inheritdoc/>
        public static bool operator !=(ReferenceNode a, object b)
        {
            return a is null ? b is not null : a.CompareTo(b) != 0;
        }

        /// <inheritdoc/>
        public int CompareTo(object obj)
        {
            if (obj is null)
            {
                return +1;
            }

            if (ReferenceEquals(obj, this))
            {
                return 0;
            }

            if (obj is not ReferenceNode reference)
            {
                return -1;
            }

            return CompareTo(reference);
        }

        /// <inheritdoc/>
        public int CompareTo(ReferenceNode obj)
        {
            if (ReferenceTypeId is null)
            {
                return obj?.ReferenceTypeId is null ? 0 : -1;
            }

            int result = ReferenceTypeId.CompareTo(obj.ReferenceTypeId);

            if (result != 0)
            {
                return result;
            }

            if (obj.IsInverse != IsInverse)
            {
                return IsInverse ? +1 : -1;
            }

            if (TargetId is null)
            {
                return obj.TargetId is null ? 0 : -1;
            }

            return TargetId.CompareTo(obj.TargetId);
        }

        /// <inheritdoc/>
        public static bool operator <(ReferenceNode left, ReferenceNode right)
        {
            return left is null ? right is not null : left.CompareTo(right) < 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(ReferenceNode left, ReferenceNode right)
        {
            return left is null || left.CompareTo(right) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator >(ReferenceNode left, ReferenceNode right)
        {
            return left is not null && left.CompareTo(right) > 0;
        }

        /// <inheritdoc/>
        public static bool operator >=(ReferenceNode left, ReferenceNode right)
        {
            return left is null ? right is null : left.CompareTo(right) >= 0;
        }
    }

    /// <summary>
    /// Reference node collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfReferenceNode",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ReferenceNode")]
    public class ReferenceNodeCollection : List<ReferenceNode>, ICloneable
    {
        /// <inheritdoc/>
        public ReferenceNodeCollection()
        {
        }

        /// <inheritdoc/>
        public ReferenceNodeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public ReferenceNodeCollection(IEnumerable<ReferenceNode> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator ReferenceNodeCollection(ReferenceNode[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator ReferenceNode[](ReferenceNodeCollection values)
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
            return (ReferenceNodeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new ReferenceNodeCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
