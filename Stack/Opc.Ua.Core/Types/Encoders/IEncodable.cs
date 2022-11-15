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

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Opc.Ua
{
    /// <summary>
    /// Defines methods used to encode and decode objects.
    /// </summary>
    public interface IEncodeable
    {
        /// <summary>
        /// Returns the NodeId for the encodable type.
        /// </summary>
        /// <value>The NodeId.</value>
        ExpandedNodeId TypeId { get; }

        /// <summary>
        /// Returns the NodeId for the default binary encoding for the type.
        /// </summary>
        /// <value>The NodeId for binary encoding.</value>
        ExpandedNodeId BinaryEncodingId { get; }

        /// <summary>
        /// Returns the NodeId for the default XML encoding for the type.
        /// </summary>
        /// <value>The NodeId for the  XML encoding id.</value>
        ExpandedNodeId XmlEncodingId { get; }

        /// <summary>
        /// Encodes the object in a stream.
        /// </summary>
        /// <param name="encoder">The encoder to be used for encoding the current value.</param>
        void Encode(IEncoder encoder);

        /// <summary>
        /// Decodes the object from a stream.
        /// </summary>
        /// <param name="decoder">The decoder to be used for decoding the current value.</param>
        void Decode(IDecoder decoder);

        /// <summary>
        /// Checks if the value is equal to the another encodeable object.
        /// </summary>
        /// <param name="encodeable">The encodeable.</param>
        /// <returns>
        /// 	<c>true</c> if the specified instance of the <see cref="IEncodeable"/> type is equal; otherwise <c>false</c>.
        /// </returns>
        bool IsEqual(IEncodeable encodeable);
    }

    #region IEncodeableCollection
    /// <summary>
    /// A collection of encodeable objects.
    /// </summary>
    [CollectionDataContract(Name = "ListOfEncodeable", Namespace = Namespaces.OpcUaXsd, ItemName = "Encodeable")]
    public class IEncodeableCollection : List<IEncodeable>
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        public IEncodeableCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <param name="collection">The collection whose elements are copied to the new list.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// 	<paramref name="collection"/> is null.
        /// </exception>
        public IEncodeableCollection(IEnumerable<IEncodeable> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <param name="capacity">The initial capacity of the collection.</param>
        public IEncodeableCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">The values to be converted to an instance of <see cref="IEncodeableCollection"/>.</param>
        /// <returns>Instance of the <see cref="IEncodeableCollection"/> containing <paramref name="values"/></returns>
        public static IEncodeableCollection ToIEncodeableCollection(IEncodeable[] values)
        {
            if (values != null)
            {
                return new IEncodeableCollection(values);
            }

            return new IEncodeableCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">The values to be converted to new instance of <see cref="IEncodeableCollection"/>.</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator IEncodeableCollection(IEncodeable[] values)
        {
            return ToIEncodeableCollection(values);
        }
    }
    #endregion

    /// <summary>
    /// Defines extensions to support the JSON encoding.
    /// </summary>
    public interface IJsonEncodeable
    {
        /// <summary>
        /// Returns the NodeId for the default JSON encoding for the type.
        /// </summary>
        /// <value>The NodeId for the JSON encoding.</value>
        ExpandedNodeId JsonEncodingId { get; }
    }
}
