/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Role permission type
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class RolePermissionType :
        IEncodeable,
        IEquatable<RolePermissionType>
    {
        /// <summary>
        /// Role id
        /// </summary>
        [DataMember(Name = "RoleId", IsRequired = false, Order = 1)]
        public NodeId RoleId { get; set; }

        /// <summary>
        /// Permissions
        /// </summary>
        [DataMember(Name = "Permissions", IsRequired = false, Order = 2)]
        public uint Permissions { get; set; }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.RolePermissionType;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.RolePermissionType_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.RolePermissionType_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteNodeId("RoleId", RoleId);
            encoder.WriteUInt32("Permissions", Permissions);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            RoleId = decoder.ReadNodeId("RoleId");
            Permissions = decoder.ReadUInt32("Permissions");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not RolePermissionType value)
            {
                return false;
            }

            if (RoleId != value.RoleId)
            {
                return false;
            }

            if (Permissions != value.Permissions)
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
            return HashCode.Combine(RoleId, Permissions);
        }

        /// <inheritdoc/>
        public bool Equals(RolePermissionType? other)
        {
            return IsEqual(other)!;
        }

        /// <inheritdoc/>
        public static bool operator ==(RolePermissionType? left, RolePermissionType? right)
        {
            if (left is null)
            {
                return right is null;
            }
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(RolePermissionType? left, RolePermissionType? right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (RolePermissionType)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (RolePermissionType)base.MemberwiseClone();

            clone.RoleId = RoleId;
            clone.Permissions = CoreUtils.Clone(Permissions);

            return clone;
        }
    }
}
