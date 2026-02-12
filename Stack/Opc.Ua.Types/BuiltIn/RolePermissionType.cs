/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Opc.Ua.Types;

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
            RoleId = default;
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

            clone.RoleId = RoleId;
            clone.Permissions = CoreUtils.Clone(Permissions);

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
        public RolePermissionTypeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public RolePermissionTypeCollection(IEnumerable<RolePermissionType> collection)
            : base(collection)
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
