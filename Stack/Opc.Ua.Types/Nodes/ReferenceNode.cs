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
    /// Reference node
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class ReferenceNode : IEncodeable, IJsonEncodeable, IReference, IComparable
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

        /// <summary>
        /// Returns a string representation of the HierarchyBrowsePath.
        /// </summary>
        /// <returns>
        /// A <see cref="string"/> that represents the current <see cref="object"/>.
        /// </returns>
        public override string ToString()
        {
            if (IsInverse)
            {
                return CoreUtils.Format("<!{0}>{1}", ReferenceTypeId, TargetId);
            }

            return CoreUtils.Format("<{0}>{1}", ReferenceTypeId, TargetId);
        }

        /// <summary>
        /// Determines whether the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <param name="obj">The <see cref="T:System.Object"/> to compare with the current <see cref="T:System.Object"/>.</param>
        /// <returns>
        /// true if the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>; otherwise, false.
        /// </returns>
        /// <exception cref="T:System.NullReferenceException">
        /// The <paramref name="obj"/> parameter is null.
        /// </exception>
        public override bool Equals(object obj)
        {
            return CompareTo(obj) == 0;
        }

        /// <summary>
        /// Serves as a hash function for a particular type.
        /// </summary>
        /// <returns>
        /// A hash code for the current <see cref="T:System.Object"/>.
        /// </returns>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ReferenceTypeId);
            hash.Add(IsInverse);
            hash.Add(TargetId);
            return hash.ToHashCode();
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <param name="a">ReferenceNode A.</param>
        /// <param name="b">The ReferenceNode B.</param>
        /// <returns>The result of the operator.Returns true if the objects are equal.</returns>
        public static bool operator ==(ReferenceNode a, object b)
        {
            if (a is null)
            {
                return b is null;
            }

            return a.CompareTo(b) == 0;
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <param name="a">ReferenceNode A.</param>
        /// <param name="b">The ReferenceNode B.</param>
        /// <returns>The result of the operator.Returns true if the objects are not equal.</returns>
        public static bool operator !=(ReferenceNode a, object b)
        {
            if (a is null)
            {
                return b is not null;
            }

            return a.CompareTo(b) != 0;
        }

        /// <summary>
        /// Compares the current instance with another object of the same type and returns an
        /// integer that indicates whether the current instance precedes, follows, or occurs
        /// in the same position in the sort order as the other object.
        /// </summary>
        /// <param name="obj">An object to compare with this instance.</param>
        /// <returns>
        /// A 32-bit signed integer that indicates the relative order of the objects being
        /// compared. The return value has these meanings:
        /// Value
        /// Meaning
        /// Less than zero
        /// This instance is less than <paramref name="obj"/>.
        /// Zero
        /// This instance is equal to <paramref name="obj"/>.
        /// Greater than zero
        /// This instance is greater than <paramref name="obj"/>.
        /// </returns>
        /// <exception cref="T:System.ArgumentException">
        /// <paramref name="obj"/> is not the same type as this instance.
        /// </exception>
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

            if (ReferenceTypeId is null)
            {
                return reference.ReferenceTypeId is null ? 0 : -1;
            }

            int result = ReferenceTypeId.CompareTo(reference.ReferenceTypeId);

            if (result != 0)
            {
                return result;
            }

            if (reference.IsInverse != IsInverse)
            {
                return IsInverse ? +1 : -1;
            }

            if (TargetId is null)
            {
                return reference.TargetId is null ? 0 : -1;
            }

            return TargetId.CompareTo(reference.TargetId);
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
        public ReferenceNodeCollection(int capacity) : base(capacity)
        {
        }

        /// <inheritdoc/>
        public ReferenceNodeCollection(IEnumerable<ReferenceNode> collection) : base(collection)
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
