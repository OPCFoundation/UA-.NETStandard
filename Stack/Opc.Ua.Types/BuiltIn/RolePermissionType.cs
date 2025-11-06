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
    /// Role permission type
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class RolePermissionType : IEncodeable, IJsonEncodeable
    {
        /// <summary>
        /// Create role permission
        /// </summary>
        public RolePermissionType()
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
            RoleId = null;
            Permissions = 0;
        }

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
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.RolePermissionType_Encoding_DefaultJson;

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
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not RolePermissionType value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(RoleId, value.RoleId))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Permissions, value.Permissions))
            {
                return false;
            }

            return true;
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

            clone.RoleId = CoreUtils.Clone(RoleId);
            clone.Permissions = (uint)CoreUtils.Clone(Permissions);

            return clone;
        }
    }

    /// <summary>
    /// Role permission collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfRolePermissionType",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "RolePermissionType")]
    public class RolePermissionTypeCollection : List<RolePermissionType>, ICloneable
    {
        /// <inheritdoc/>
        public RolePermissionTypeCollection()
        {
        }

        /// <inheritdoc/>
        public RolePermissionTypeCollection(int capacity) : base(capacity)
        {
        }

        /// <inheritdoc/>
        public RolePermissionTypeCollection(IEnumerable<RolePermissionType> collection) : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator RolePermissionTypeCollection(RolePermissionType[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator RolePermissionType[](RolePermissionTypeCollection values)
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
            return (RolePermissionTypeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new RolePermissionTypeCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
