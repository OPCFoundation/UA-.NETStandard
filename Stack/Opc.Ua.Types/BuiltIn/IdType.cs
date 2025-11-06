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
    /// Node Id type
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public enum IdType
    {
        /// <summary>
        /// Numeric
        /// </summary>
        [EnumMember(Value = "Numeric_0")]
        Numeric = 0,

        /// <summary>
        /// String type
        /// </summary>
        [EnumMember(Value = "String_1")]
        String = 1,

        /// <summary>
        /// Guid
        /// </summary>
        [EnumMember(Value = "Guid_2")]
        Guid = 2,

        /// <summary>
        /// Opaque
        /// </summary>
        [EnumMember(Value = "Opaque_3")]
        Opaque = 3,
    }

    /// <summary>
    /// Id type collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfIdType",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "IdType")]
    public class IdTypeCollection : List<IdType>, ICloneable
    {
        /// <inheritdoc/>
        public IdTypeCollection()
        {
        }

        /// <inheritdoc/>
        public IdTypeCollection(int capacity) : base(capacity)
        {
        }

        /// <inheritdoc/>
        public IdTypeCollection(IEnumerable<IdType> collection) : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator IdTypeCollection(IdType[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator IdType[](IdTypeCollection values)
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
            return (IdTypeCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new IdTypeCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add((IdType)CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
