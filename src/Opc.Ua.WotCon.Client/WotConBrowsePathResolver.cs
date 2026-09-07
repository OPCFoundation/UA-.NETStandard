/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.WotCon.Client
{
    /// <summary>
    /// Shared <c>TranslateBrowsePaths</c> helper used by every WoT
    /// Connectivity client wrapper to resolve a single named child below a
    /// known starting node (server entry points, registry groups, group
    /// resources, ...). Centralizing this avoids re-implementing the same
    /// request/response shape and not-found handling in every wrapper.
    /// </summary>
    internal static class WotConBrowsePathResolver
    {
        /// <summary>
        /// Resolves the NodeId of the child named <paramref name="targetName"/>
        /// below <paramref name="startingNode"/>, reached via
        /// <paramref name="referenceType"/>.
        /// </summary>
        /// <param name="session">An open OPC UA session.</param>
        /// <param name="startingNode">The NodeId to start the relative path from.</param>
        /// <param name="referenceType">The reference type connecting
        /// <paramref name="startingNode"/> to the target child.</param>
        /// <param name="targetNamespaceIndex">Namespace index of the
        /// target child's BrowseName.</param>
        /// <param name="targetName">Name part of the target child's BrowseName.</param>
        /// <param name="notFoundStatus">Status code to report when the
        /// child cannot be resolved.</param>
        /// <param name="notFoundMessage">Message to report when the child
        /// cannot be resolved.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <exception cref="ServiceResultException">The child could not be resolved.</exception>
        public static async ValueTask<NodeId> ResolveChildAsync(
            ISession session,
            NodeId startingNode,
            NodeId referenceType,
            ushort targetNamespaceIndex,
            string targetName,
            StatusCode notFoundStatus,
            string notFoundMessage,
            CancellationToken ct)
        {
            BrowsePath path = new()
            {
                StartingNode = startingNode,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = referenceType,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(targetName, targetNamespaceIndex)
                        }
                    ]
                }
            };
            ArrayOf<BrowsePath> paths = new[] { path }.ToArrayOf();
            TranslateBrowsePathsToNodeIdsResponse response = await session
                .TranslateBrowsePathsToNodeIdsAsync(null, paths, ct)
                .ConfigureAwait(false);
            if (response.Results.Count == 0 ||
                response.Results[0].Targets.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode))
            {
                throw new ServiceResultException(notFoundStatus, notFoundMessage);
            }
            return ExpandedNodeId.ToNodeId(
                response.Results[0].Targets[0].TargetId, session.NamespaceUris);
        }
    }
}
