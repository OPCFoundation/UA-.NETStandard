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
