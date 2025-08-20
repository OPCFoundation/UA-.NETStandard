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
}
