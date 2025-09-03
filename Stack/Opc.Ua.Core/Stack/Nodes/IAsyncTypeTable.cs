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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
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

    /// <summary>
    /// Type table adapter for synchronous access to the table.
    /// We need to keep the sync interface for now as it is shared
    /// between the client and the server. However, we want to
    /// minimize use of it where we can on the client side.
    /// </summary>
    internal sealed class TypeTableAdapter : ITypeTable
    {
        public TypeTableAdapter(IAsyncTypeTable table)
        {
            m_table = table;
        }

        /// <inheritdoc/>
        public bool IsKnown(ExpandedNodeId typeId)
        {
            return m_table.IsKnownAsync(typeId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public bool IsKnown(NodeId typeId)
        {
            return m_table.IsKnownAsync(typeId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public NodeId FindSuperType(ExpandedNodeId typeId)
        {
            return m_table.FindSuperTypeAsync(typeId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public NodeId FindSuperType(NodeId typeId)
        {
            return m_table.FindSuperTypeAsync(typeId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public Task<NodeId> FindSuperTypeAsync(
            ExpandedNodeId typeId,
            CancellationToken ct = default)
        {
            return m_table.FindSuperTypeAsync(typeId, ct)
                .AsTask();
        }

        /// <inheritdoc/>
        public Task<NodeId> FindSuperTypeAsync(
            NodeId typeId,
            CancellationToken ct = default)
        {
            return m_table.FindSuperTypeAsync(typeId, ct)
                .AsTask();
        }

        /// <inheritdoc/>
        public IList<NodeId> FindSubTypes(ExpandedNodeId typeId)
        {
            return m_table.FindSubTypesAsync(typeId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public bool IsTypeOf(ExpandedNodeId subTypeId, ExpandedNodeId superTypeId)
        {
            return m_table.IsTypeOfAsync(subTypeId, superTypeId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public bool IsTypeOf(NodeId subTypeId, NodeId superTypeId)
        {
            return m_table.IsTypeOfAsync(subTypeId, superTypeId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public QualifiedName FindReferenceTypeName(NodeId referenceTypeId)
        {
            return m_table.FindReferenceTypeNameAsync(referenceTypeId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public NodeId FindReferenceType(QualifiedName browseName)
        {
            return m_table.FindReferenceTypeAsync(browseName)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public bool IsEncodingOf(ExpandedNodeId encodingId, ExpandedNodeId datatypeId)
        {
            return m_table.IsEncodingOfAsync(encodingId, datatypeId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public bool IsEncodingFor(NodeId expectedTypeId, ExtensionObject value)
        {
            return m_table.IsEncodingForAsync(expectedTypeId, value)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public bool IsEncodingFor(NodeId expectedTypeId, object value)
        {
            return m_table.IsEncodingForAsync(expectedTypeId, value)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public NodeId FindDataTypeId(ExpandedNodeId encodingId)
        {
            return m_table.FindDataTypeIdAsync(encodingId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public NodeId FindDataTypeId(NodeId encodingId)
        {
            return m_table.FindDataTypeIdAsync(encodingId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        private readonly IAsyncTypeTable m_table;
    }
}
