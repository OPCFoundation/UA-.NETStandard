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
    /// Data type definition
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public abstract class DataTypeDefinition : IEncodeable, IJsonEncodeable
    {
        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.DataTypeDefinition;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.DataTypeDefinition_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.DataTypeDefinition_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.DataTypeDefinition_Encoding_DefaultJson;

        /// <inheritdoc/>
        public abstract void Encode(IEncoder encoder);

        /// <inheritdoc/>
        public abstract void Decode(IDecoder decoder);

        /// <inheritdoc/>
        public abstract bool IsEqual(IEncodeable encodeable);

        /// <inheritdoc/>
        public abstract object Clone();
    }

    /// <summary>
    /// Data type definition collection
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfDataTypeDefinition",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "DataTypeDefinition")]
    public class DataTypeDefinitionCollection : List<DataTypeDefinition>, ICloneable
    {
        /// <inheritdoc/>
        public DataTypeDefinitionCollection()
        {
        }

        /// <inheritdoc/>
        public DataTypeDefinitionCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public DataTypeDefinitionCollection(IEnumerable<DataTypeDefinition> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public static implicit operator DataTypeDefinitionCollection(DataTypeDefinition[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <inheritdoc/>
        public static explicit operator DataTypeDefinition[](DataTypeDefinitionCollection values)
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
            return (DataTypeDefinitionCollection)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new DataTypeDefinitionCollection(Count);

            for (int ii = 0; ii < Count; ii++)
            {
                clone.Add(CoreUtils.Clone(this[ii]));
            }

            return clone;
        }
    }
}
