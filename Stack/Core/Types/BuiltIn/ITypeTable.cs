/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

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
        bool IsKnown( ExpandedNodeId typeId );

        /// <summary>
        /// Determines whether a node id is a known type id.
        /// </summary>
        /// <param name="typeId">The type identifier.</param>
        /// <returns>
        /// 	<c>true</c> if the specified type id is known; otherwise, <c>false</c>.
        /// </returns>
        bool IsKnown( NodeId typeId );

        /// <summary>
        /// Returns the immediate supertype for the type.
        /// </summary>
        /// <param name="typeId">The extended type identifier.</param>
        /// <returns>A type identifier of the <paramref name="typeId "/></returns>
        NodeId FindSuperType( ExpandedNodeId typeId );

        /// <summary>
        /// Returns the immediate supertype for the type.
        /// </summary>
        /// <param name="typeId">The type identifier.</param>
        /// <returns>The immediate supertype idnetyfier for <paramref name="typeId"/></returns>
        NodeId FindSuperType( NodeId typeId );

        /// <summary>
        /// Returns the immediate subtypes for the type.
        /// </summary>
        /// <param name="typeId">The extended type identifier.</param>
        /// <returns>List of type identifiers for <paramref name="typeId"/></returns>
        IList<NodeId> FindSubTypes( ExpandedNodeId typeId );

        /// <summary>
        /// Determines whether a type is a subtype of another type.
        /// </summary>
        /// <param name="subTypeId">The subtype identifier.</param>
        /// <param name="superTypeId">The supertype identifier.</param>
        /// <returns>
        /// 	<c>true</c> if <paramref name="superTypeId"/> is supertype of <paramref name="subTypeId"/>; otherwise, <c>false</c>.
        /// </returns>
        bool IsTypeOf( ExpandedNodeId subTypeId, ExpandedNodeId superTypeId );

        /// <summary>
        /// Determines whether a type is a subtype of another type.
        /// </summary>
        /// <param name="subTypeId">The subtype identifier.</param>
        /// <param name="superTypeId">The supertype identyfier.</param>
        /// <returns>
        /// 	<c>true</c> if <paramref name="superTypeId"/> is supertype of <paramref name="subTypeId"/>; otherwise, <c>false</c>.
        /// </returns>
        bool IsTypeOf( NodeId subTypeId, NodeId superTypeId );

        /// <summary>
        /// Returns the qualified name for the reference type id.
        /// </summary>
        /// <param name="referenceTypeId">The reference type</param>
        /// <returns>A name qualified with a namespace for the reference <paramref name="referenceTypeId"/>. </returns>
        QualifiedName FindReferenceTypeName( NodeId referenceTypeId );

        /// <summary>
        /// Returns the node identifier for the reference type with the specified browse name.
        /// </summary>
        /// <param name="browseName">Browse name of the reference.</param>
        /// <returns>The identifier for the <paramref name="browseName"/></returns>
        NodeId FindReferenceType( QualifiedName browseName );

        /// <summary>
        /// Checks if the identifier <paramref name="encodingId"/> represents a that provides encodings 
        /// for the <paramref name="datatypeId "/>.
        /// </summary>
        /// <param name="encodingId">The id the encoding node .</param>
        /// <param name="datatypeId">The id of the DataType node.</param>
        /// <returns>
        /// 	<c>true</c> if <paramref name="encodingId"/> is encoding of the <paramref name="datatypeId"/>; otherwise, <c>false</c>.
        /// </returns>
        bool IsEncodingOf( ExpandedNodeId encodingId, ExpandedNodeId datatypeId );

        /// <summary>
        /// Determines if the value contained in an extension object <paramref name="value"/> matches the expected data type.
        /// </summary>
        /// <param name="expectedTypeId">The identifier of the expected type .</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// 	<c>true</c> if the value contained in an extension object <paramref name="value"/> matches the 
        /// 	expected data type; otherwise, <c>false</c>.
        /// </returns>
        bool IsEncodingFor( NodeId expectedTypeId, ExtensionObject value );

        /// <summary>
        /// Determines if the value is an encoding of the <paramref name="value"/>
        /// </summary>
        /// <param name="expectedTypeId">The expected type id.</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// 	<c>true</c> the value is an encoding of the <paramref name="value"/>; otherwise, <c>false</c>.
        /// </returns>
        bool IsEncodingFor( NodeId expectedTypeId, object value );

        /// <summary>
        /// Returns the data type for the specified encoding.
        /// </summary>
        /// <param name="encodingId">The encoding id.</param>
        /// <returns></returns>
        NodeId FindDataTypeId( ExpandedNodeId encodingId );

        /// <summary>
        /// Returns the data type for the specified encoding.
        /// </summary>
        /// <param name="encodingId">The encoding id.</param>
        /// <returns>The data type for the <paramref name="encodingId"/></returns>
        NodeId FindDataTypeId( NodeId encodingId );
    }
}
