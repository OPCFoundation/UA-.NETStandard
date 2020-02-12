/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the type tree for a server. 
    /// </summary>
    public interface ITypeTable
    {
        /// <summary>
        /// Determines whether a node id is a known type id.
        /// </summary>
        /// <param name="typeId">The type extended identifier.</param>
        /// <returns>
        /// 	<c>true</c> if the specified type id is known; otherwise, <c>false</c>.
        /// </returns>
        bool IsKnown(ExpandedNodeId typeId);

        /// <summary>
        /// Determines whether a node id is a known type id.
        /// </summary>
        /// <param name="typeId">The type identifier.</param>
        /// <returns>
        /// 	<c>true</c> if the specified type id is known; otherwise, <c>false</c>.
        /// </returns>
        bool IsKnown(NodeId typeId);

        /// <summary>
        /// Returns the immediate supertype for the type.
        /// </summary>
        /// <param name="typeId">The extended type identifier.</param>
        /// <returns>A type identifier of the <paramref name="typeId "/></returns>
        NodeId FindSuperType(ExpandedNodeId typeId);

        /// <summary>
        /// Returns the immediate supertype for the type.
        /// </summary>
        /// <param name="typeId">The type identifier.</param>
        /// <returns>The immediate supertype idnetyfier for <paramref name="typeId"/></returns>
        NodeId FindSuperType(NodeId typeId);

        /// <summary>
        /// Returns the immediate subtypes for the type.
        /// </summary>
        /// <param name="typeId">The extended type identifier.</param>
        /// <returns>List of type identifiers for <paramref name="typeId"/></returns>
        IList<NodeId> FindSubTypes(ExpandedNodeId typeId);

        /// <summary>
        /// Determines whether a type is a subtype of another type.
        /// </summary>
        /// <param name="subTypeId">The subtype identifier.</param>
        /// <param name="superTypeId">The supertype identifier.</param>
        /// <returns>
        /// 	<c>true</c> if <paramref name="superTypeId"/> is supertype of <paramref name="subTypeId"/>; otherwise, <c>false</c>.
        /// </returns>
        bool IsTypeOf(ExpandedNodeId subTypeId, ExpandedNodeId superTypeId);

        /// <summary>
        /// Determines whether a type is a subtype of another type.
        /// </summary>
        /// <param name="subTypeId">The subtype identifier.</param>
        /// <param name="superTypeId">The supertype identyfier.</param>
        /// <returns>
        /// 	<c>true</c> if <paramref name="superTypeId"/> is supertype of <paramref name="subTypeId"/>; otherwise, <c>false</c>.
        /// </returns>
        bool IsTypeOf(NodeId subTypeId, NodeId superTypeId);

        /// <summary>
        /// Returns the qualified name for the reference type id.
        /// </summary>
        /// <param name="referenceTypeId">The reference type</param>
        /// <returns>A name qualified with a namespace for the reference <paramref name="referenceTypeId"/>. </returns>
        QualifiedName FindReferenceTypeName(NodeId referenceTypeId);

        /// <summary>
        /// Returns the node identifier for the reference type with the specified browse name.
        /// </summary>
        /// <param name="browseName">Browse name of the reference.</param>
        /// <returns>The identifier for the <paramref name="browseName"/></returns>
        NodeId FindReferenceType(QualifiedName browseName);

        /// <summary>
        /// Checks if the identifier <paramref name="encodingId"/> represents a that provides encodings 
        /// for the <paramref name="datatypeId "/>.
        /// </summary>
        /// <param name="encodingId">The id the encoding node .</param>
        /// <param name="datatypeId">The id of the DataType node.</param>
        /// <returns>
        /// 	<c>true</c> if <paramref name="encodingId"/> is encoding of the <paramref name="datatypeId"/>; otherwise, <c>false</c>.
        /// </returns>
        bool IsEncodingOf(ExpandedNodeId encodingId, ExpandedNodeId datatypeId);

        /// <summary>
        /// Determines if the value contained in an extension object <paramref name="value"/> matches the expected data type.
        /// </summary>
        /// <param name="expectedTypeId">The identifier of the expected type .</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// 	<c>true</c> if the value contained in an extension object <paramref name="value"/> matches the 
        /// 	expected data type; otherwise, <c>false</c>.
        /// </returns>
        bool IsEncodingFor(NodeId expectedTypeId, ExtensionObject value);

        /// <summary>
        /// Determines if the value is an encoding of the <paramref name="value"/>
        /// </summary>
        /// <param name="expectedTypeId">The expected type id.</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// 	<c>true</c> the value is an encoding of the <paramref name="value"/>; otherwise, <c>false</c>.
        /// </returns>
        bool IsEncodingFor(NodeId expectedTypeId, object value);

        /// <summary>
        /// Returns the data type for the specified encoding.
        /// </summary>
        /// <param name="encodingId">The encoding id.</param>
        /// <returns></returns>
        NodeId FindDataTypeId(ExpandedNodeId encodingId);

        /// <summary>
        /// Returns the data type for the specified encoding.
        /// </summary>
        /// <param name="encodingId">The encoding id.</param>
        /// <returns>The data type for the <paramref name="encodingId"/></returns>
        NodeId FindDataTypeId(NodeId encodingId);
    }
}
