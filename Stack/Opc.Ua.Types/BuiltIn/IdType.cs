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
        Opaque = 3
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
        public IdTypeCollection(int capacity)
            : base(capacity)
        {
        }

        /// <inheritdoc/>
        public IdTypeCollection(IEnumerable<IdType> collection)
            : base(collection)
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
