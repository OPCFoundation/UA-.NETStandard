/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Caches the type tree of the server on the client side.
    /// </summary>
    public interface IAsyncTypeTable
    {
        /// <summary>
        /// Determines whether a node id is a known type id.
        /// </summary>
        /// <param name="typeId">The type extended identifier.</param>
        /// <param name="ct">Cancellation token to cancel operation
        /// with</param>
        /// <returns>
        /// <c>true</c> if the specified type id is known;
        /// otherwise, <c>false</c>.
        /// </returns>
        ValueTask<bool> IsKnownAsync(
            ExpandedNodeId typeId,
            CancellationToken ct = default);

        /// <summary>
        /// Determines whether a node id is a known type id.
        /// </summary>
        /// <param name="typeId">The type identifier.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>
        /// <c>true</c> if the specified type id is known; otherwise, <c>false</c>.
        /// </returns>
        ValueTask<bool> IsKnownAsync(
            NodeId typeId,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the immediate supertype for the type.
        /// </summary>
        /// <param name="typeId">The extended type identifier.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>A type identifier of the <paramref name="typeId "/></returns>
        ValueTask<NodeId> FindSuperTypeAsync(
            ExpandedNodeId typeId,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the immediate supertype for the type.
        /// </summary>
        /// <param name="typeId">The type identifier.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>The immediate supertype idnetyfier for
        /// <paramref name="typeId"/></returns>
        ValueTask<NodeId> FindSuperTypeAsync(
            NodeId typeId,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the immediate subtypes for the type.
        /// </summary>
        /// <param name="typeId">The extended type identifier.</param>
        /// <returns>List of type identifiers for
        /// <paramref name="typeId"/></returns>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        ValueTask<IList<NodeId>> FindSubTypesAsync(
            ExpandedNodeId typeId,
            CancellationToken ct = default);

        /// <summary>
        /// Determines whether a type is a subtype of another type.
        /// </summary>
        /// <param name="subTypeId">The subtype identifier.</param>
        /// <param name="superTypeId">The supertype identifier.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>
        /// <c>true</c> if <paramref name="superTypeId"/> is supertype
        /// of <paramref name="subTypeId"/>; otherwise, <c>false</c>.
        /// </returns>
        ValueTask<bool> IsTypeOfAsync(
            ExpandedNodeId subTypeId,
            ExpandedNodeId superTypeId,
            CancellationToken ct = default);

        /// <summary>
        /// Determines whether a type is a subtype of another type.
        /// </summary>
        /// <param name="subTypeId">The subtype identifier.</param>
        /// <param name="superTypeId">The supertype identifier.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>
        /// <c>true</c> if <paramref name="superTypeId"/> is
        /// supertype of <paramref name="subTypeId"/>; otherwise, <c>false</c>.
        /// </returns>
        ValueTask<bool> IsTypeOfAsync(
            NodeId subTypeId,
            NodeId superTypeId,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the qualified name for the reference type id.
        /// </summary>
        /// <param name="referenceTypeId">The reference type</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>A name qualified with a namespace for the reference
        /// <paramref name="referenceTypeId"/>. </returns>
        ValueTask<QualifiedName> FindReferenceTypeNameAsync(
            NodeId referenceTypeId,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the node identifier for the reference type with the
        /// specified browse name.
        /// </summary>
        /// <param name="browseName">Browse name of the reference.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>The identifier for the <paramref name="browseName"/></returns>
        ValueTask<NodeId> FindReferenceTypeAsync(
            QualifiedName browseName,
            CancellationToken ct = default);

        /// <summary>
        /// Checks if the identifier <paramref name="encodingId"/> represents
        /// a that provides encodings for the <paramref name="datatypeId "/>.
        /// </summary>
        /// <param name="encodingId">The id the encoding node .</param>
        /// <param name="datatypeId">The id of the DataType node.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>
        /// <c>true</c> if <paramref name="encodingId"/> is encoding of
        /// the <paramref name="datatypeId"/>; otherwise, <c>false</c>.
        /// </returns>
        ValueTask<bool> IsEncodingOfAsync(
            ExpandedNodeId encodingId,
            ExpandedNodeId datatypeId,
            CancellationToken ct = default);

        /// <summary>
        /// Determines if the value contained in an extension object
        /// <paramref name="value"/> matches the expected data type.
        /// </summary>
        /// <param name="expectedTypeId">The identifier of the expected type .</param>
        /// <param name="value">The value.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>
        /// <c>true</c> if the value contained in an extension object
        /// <paramref name="value"/> matches the expected data type;
        /// otherwise, <c>false</c>.
        /// </returns>
        ValueTask<bool> IsEncodingForAsync(
            NodeId expectedTypeId,
            ExtensionObject value,
            CancellationToken ct = default);

        /// <summary>
        /// Determines if the value is an encoding of the <paramref name="value"/>
        /// </summary>
        /// <param name="expectedTypeId">The expected type id.</param>
        /// <param name="value">The value.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>
        /// <c>true</c> the value is an encoding of the <paramref name="value"/>;
        /// otherwise, <c>false</c>.
        /// </returns>
        ValueTask<bool> IsEncodingForAsync(
            NodeId expectedTypeId,
            object value,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the data type for the specified encoding.
        /// </summary>
        /// <param name="encodingId">The encoding id.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        ValueTask<NodeId> FindDataTypeIdAsync(
            ExpandedNodeId encodingId,
            CancellationToken ct = default);

        /// <summary>
        /// Returns the data type for the specified encoding.
        /// </summary>
        /// <param name="encodingId">The encoding id.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>The data type for the <paramref name="encodingId"/></returns>
        ValueTask<NodeId> FindDataTypeIdAsync(
            NodeId encodingId,
            CancellationToken ct = default);
    }
}
