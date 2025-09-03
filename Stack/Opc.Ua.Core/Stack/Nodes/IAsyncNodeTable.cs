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
    /// Async node table methods
    /// </summary>
    public interface IAsyncNodeTable
    {
        /// <summary>
        /// The table of Namespace URIs used by the table.
        /// </summary>
        /// <value>The namespace URIs.</value>
        NamespaceTable NamespaceUris { get; }

        /// <summary>
        /// The table of Server URIs used by the table.
        /// </summary>
        /// <value>The server URIs.</value>
        StringTable ServerUris { get; }

        /// <summary>
        /// The type model that describes the nodes in the table.
        /// </summary>
        IAsyncTypeTable TypeTree { get; }

        /// <summary>
        /// Returns true if the node is in the table.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="ct">Token to use to cancel the operation.</param>
        /// <returns>True if the node is in the table.</returns>
        ValueTask<bool> ExistsAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Finds a node in the node set.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <param name="ct">Token to use to cancel the operation.</param>
        /// <returns>Returns null if the node does not exist.</returns>
        ValueTask<INode> FindAsync(
            ExpandedNodeId nodeId,
            CancellationToken ct = default);

        /// <summary>
        /// Follows the reference from the source and returns the first target with the
        /// specified browse name.
        /// </summary>
        /// <param name="sourceId">The source identifier.</param>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="browseName">Name of the browse.</param>
        /// <param name="ct">Token to use to cancel the operation.</param>
        /// <returns>
        /// Returns null if the source does not exist or if there is no matching target.
        /// </returns>
        ValueTask<INode> FindAsync(
            ExpandedNodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            QualifiedName browseName,
            CancellationToken ct = default);

        /// <summary>
        /// Follows the reference from the source and returns all target nodes.
        /// </summary>
        /// <param name="sourceId">The source identifier.</param>
        /// <param name="referenceTypeId">The reference type identifier.</param>
        /// <param name="isInverse">if set to <c>true</c> this is inverse reference.</param>
        /// <param name="includeSubtypes">if set to <c>true</c> subtypes are included.</param>
        /// <param name="ct">Token to use to cancel the operation.</param>
        /// <returns>
        /// Returns an empty list if the source does not exist or if there are no
        /// matching targets.
        /// </returns>
        ValueTask<IList<INode>> FindAsync(
            ExpandedNodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            CancellationToken ct = default);
    }

    internal sealed class NodeTableAdapter : INodeTable
    {
        public NodeTableAdapter(IAsyncNodeTable nodeTable)
        {
            m_table = nodeTable;
        }

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => m_table.NamespaceUris;

        /// <inheritdoc/>
        public StringTable ServerUris => m_table.ServerUris;

        /// <inheritdoc/>
        public ITypeTable TypeTree => new TypeTableAdapter(m_table.TypeTree);

        /// <inheritdoc/>
        public bool Exists(ExpandedNodeId nodeId)
        {
            return m_table.ExistsAsync(nodeId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public INode Find(ExpandedNodeId nodeId)
        {
            return m_table.FindAsync(nodeId)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public INode Find(
            ExpandedNodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes,
            QualifiedName browseName)
        {
            return m_table.FindAsync(
                sourceId,
                referenceTypeId,
                isInverse,
                includeSubtypes,
                browseName)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        /// <inheritdoc/>
        public IList<INode> Find(
            ExpandedNodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            bool includeSubtypes)
        {
            return m_table.FindAsync(
                sourceId,
                referenceTypeId,
                isInverse,
                includeSubtypes)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        private readonly IAsyncNodeTable m_table;
    }
}
