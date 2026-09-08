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

using System.Collections.Generic;
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
            List<NodeId> targets = await ResolveTargetsAsync(
                    session,
                    startingNode,
                    referenceType,
                    targetNamespaceIndex,
                    targetName,
                    ct)
                .ConfigureAwait(false);
            if (targets.Count == 0)
            {
                throw new ServiceResultException(notFoundStatus, notFoundMessage);
            }
            return targets[0];
        }

        /// <summary>
        /// Resolves the logical default Resource when a ResourceId collides with a
        /// non-default VersionId and therefore produces duplicate flat BrowseNames.
        /// When <paramref name="usesDistinctHierarchy"/> is <c>true</c> (0.6.0+),
        /// the Resource is a unique child of the Group, so no disambiguation is
        /// needed — the first (and only) candidate is returned directly.
        /// </summary>
        public static async ValueTask<NodeId> ResolveLogicalResourceAsync(
            ISession session,
            NodeId groupNode,
            ushort targetNamespaceIndex,
            string resourceId,
            bool usesDistinctHierarchy,
            StatusCode notFoundStatus,
            string notFoundMessage,
            CancellationToken ct)
        {
            List<NodeId> candidates = await BrowseNamedChildrenAsync(
                    session,
                    groupNode,
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    targetNamespaceIndex,
                    resourceId,
                    ct)
                .ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                throw new ServiceResultException(notFoundStatus, notFoundMessage);
            }
            if (candidates.Count == 1 || usesDistinctHierarchy)
            {
                return candidates[0];
            }
            return await DisambiguateByIsDefaultAsync(
                session, candidates, resourceId, notFoundStatus, notFoundMessage, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the logical default Resource. Overload without explicit hierarchy
        /// flag uses legacy disambiguation when multiple candidates exist.
        /// </summary>
        public static ValueTask<NodeId> ResolveLogicalResourceAsync(
            ISession session,
            NodeId groupNode,
            ushort targetNamespaceIndex,
            string resourceId,
            StatusCode notFoundStatus,
            string notFoundMessage,
            CancellationToken ct)
        {
            return ResolveLogicalResourceAsync(
                session, groupNode, targetNamespaceIndex, resourceId,
                usesDistinctHierarchy: false, notFoundStatus, notFoundMessage, ct);
        }

        private static async ValueTask<NodeId> DisambiguateByIsDefaultAsync(
            ISession session,
            List<NodeId> candidates,
            string resourceId,
            StatusCode notFoundStatus,
            string notFoundMessage,
            CancellationToken ct)
        {

            NodeId selected = NodeId.Null;
            foreach (NodeId candidate in candidates)
            {
                (NodeId resourceIdNode, NodeId isDefaultNode) =
                    await BrowseIdentityChildrenAsync(session, candidate, ct)
                    .ConfigureAwait(false);
                if (resourceIdNode.IsNull || isDefaultNode.IsNull)
                {
                    continue;
                }

                ArrayOf<ReadValueId> nodesToRead = new[]
                {
                    new ReadValueId
                    {
                        NodeId = resourceIdNode,
                        AttributeId = Attributes.Value
                    },
                    new ReadValueId
                    {
                        NodeId = isDefaultNode,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf();
                ReadResponse read = await session.ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Neither,
                        nodesToRead,
                        ct)
                    .ConfigureAwait(false);
                if (read.Results.Count < 2)
                {
                    continue;
                }
                DataValue resourceIdValue = read.Results[0];
                DataValue isDefaultValue = read.Results[1];
                if (StatusCode.IsBad(resourceIdValue.StatusCode) ||
                    StatusCode.IsBad(isDefaultValue.StatusCode) ||
                    !resourceIdValue.WrappedValue.TryGetValue(out string candidateResourceId) ||
                    !isDefaultValue.WrappedValue.TryGetValue(out bool isDefault) ||
                    !isDefault ||
                    !string.Equals(candidateResourceId, resourceId, System.StringComparison.Ordinal))
                {
                    continue;
                }
                if (!selected.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadBrowseNameDuplicated,
                        $"Resource '{resourceId}' has more than one default projection.");
                }
                selected = candidate;
            }
            if (selected.IsNull)
            {
                throw new ServiceResultException(notFoundStatus, notFoundMessage);
            }
            return selected;
        }

        private static async ValueTask<List<NodeId>> BrowseNamedChildrenAsync(
            ISession session,
            NodeId parent,
            NodeId referenceType,
            ushort targetNamespaceIndex,
            string targetName,
            CancellationToken ct)
        {
            var candidates = new List<NodeId>();
            List<ReferenceDescription> references = await BrowseReferencesAsync(
                session,
                parent,
                referenceType,
                ct).ConfigureAwait(false);
            var expected = new QualifiedName(targetName, targetNamespaceIndex);
            foreach (ReferenceDescription reference in references)
            {
                if (reference.BrowseName != expected)
                {
                    continue;
                }
                NodeId candidate = ExpandedNodeId.ToNodeId(
                    reference.NodeId,
                    session.NamespaceUris);
                if (!candidate.IsNull)
                {
                    candidates.Add(candidate);
                }
            }
            return candidates;
        }

        private static async ValueTask<(NodeId ResourceId, NodeId IsDefault)>
            BrowseIdentityChildrenAsync(
                ISession session,
                NodeId candidate,
                CancellationToken ct)
        {
                NodeId resourceIdNode = NodeId.Null;
                NodeId isDefaultNode = NodeId.Null;
                List<ReferenceDescription> references = await BrowseReferencesAsync(
                    session,
                    candidate,
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    ct).ConfigureAwait(false);
                foreach (ReferenceDescription reference in references)
                {
                string? name = reference.BrowseName.Name;
                if (!string.Equals(
                        name,
                        Opc.Ua.XRegistry.BrowseNames.ResourceId,
                        System.StringComparison.Ordinal) &&
                    !string.Equals(
                        name,
                        Opc.Ua.WotCon.BrowseNames.IsDefault,
                        System.StringComparison.Ordinal))
                {
                    continue;
                }
                NodeId child = ExpandedNodeId.ToNodeId(
                    reference.NodeId,
                    session.NamespaceUris);
                if (string.Equals(
                        name,
                        Opc.Ua.XRegistry.BrowseNames.ResourceId,
                        System.StringComparison.Ordinal))
                {
                    resourceIdNode = child;
                }
                else
                {
                    isDefaultNode = child;
                }
            }
            return (resourceIdNode, isDefaultNode);
        }

        private static async ValueTask<List<ReferenceDescription>> BrowseReferencesAsync(
            ISession session,
            NodeId parent,
            NodeId referenceType,
            CancellationToken ct)
        {
            BrowseResponse response = await session.BrowseAsync(
                    null,
                    null,
                    0,
                    new[]
                    {
                        new BrowseDescription
                        {
                            NodeId = parent,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = referenceType,
                            IncludeSubtypes = true,
                            NodeClassMask = 0,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    }.ToArrayOf(),
                    ct)
                .ConfigureAwait(false);
            var references = new List<ReferenceDescription>();
            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode))
            {
                return references;
            }

            BrowseResult result = response.Results[0];
            AddReferences(references, result.References);
            ByteString continuationPoint = result.ContinuationPoint;
            try
            {
                while (!continuationPoint.IsNull && continuationPoint.Length > 0)
                {
                    (_, continuationPoint, ArrayOf<ReferenceDescription> next) =
                        await session.BrowseNextAsync(
                                null,
                                releaseContinuationPoint: false,
                                continuationPoint,
                                ct)
                            .ConfigureAwait(false);
                    AddReferences(references, next);
                }
            }
            catch
            {
                if (!continuationPoint.IsNull && continuationPoint.Length > 0)
                {
                    try
                    {
                        _ = await session.BrowseNextAsync(
                                null,
                                releaseContinuationPoint: true,
                                continuationPoint,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
                throw;
            }
            return references;
        }

        private static void AddReferences(
            List<ReferenceDescription> destination,
            ArrayOf<ReferenceDescription> source)
        {
            for (int i = 0; i < source.Count; i++)
            {
                destination.Add(source[i]);
            }
        }

        private static async ValueTask<List<NodeId>> ResolveTargetsAsync(
            ISession session,
            NodeId startingNode,
            NodeId referenceType,
            ushort targetNamespaceIndex,
            string targetName,
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
            var targets = new List<NodeId>();
            if (response.Results.Count == 0 ||
                response.Results[0].Targets.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode))
            {
                return targets;
            }
            foreach (BrowsePathTarget target in response.Results[0].Targets)
            {
                if (target.RemainingPathIndex != uint.MaxValue)
                {
                    continue;
                }
                NodeId nodeId = ExpandedNodeId.ToNodeId(
                    target.TargetId,
                    session.NamespaceUris);
                if (!nodeId.IsNull)
                {
                    targets.Add(nodeId);
                }
            }
            return targets;
        }
    }
}
