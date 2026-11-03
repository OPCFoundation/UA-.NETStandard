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

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.AMB.Client
{
    public sealed partial class AmbClient
    {
        /// <summary>
        /// The deepest level <see cref="BrowseLocationsAsync"/> and
        /// <see cref="EnumerateSubAssetsAsync"/> descend to.
        /// </summary>
        public const int MaxDepth = 16;

        /// <summary>
        /// Walks the location hierarchies below <c>HierarchicalLocations</c>
        /// or <c>OperationalLocations</c> (§13.3.3, §13.4.3).
        /// </summary>
        /// <param name="kind">
        /// <see cref="AssetLocationKind.Hierarchical"/> or
        /// <see cref="AssetLocationKind.Operational"/>.
        /// </param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Every level, parents before children, with the objects it contains.</returns>
        /// <exception cref="ArgumentException"><paramref name="kind"/> has no location objects.</exception>
        public async ValueTask<ArrayOf<AssetLocationNode>> BrowseLocationsAsync(
            AssetLocationKind kind,
            CancellationToken cancellationToken = default)
        {
            (NodeId entryPoint, NodeId contains) = LocationIdsOf(kind);
            BrowseBudget budget = CreateBrowseBudget();
            var locations = new List<AssetLocationNode>();
            var pending = new Queue<(NodeId Node, string Path, int Depth)>();
            var seen = new HashSet<NodeId>();
            foreach (ReferenceDescription root in await BrowseWithBudgetAsync(
                entryPoint,
                Ua.ReferenceTypeIds.Organizes,
                BrowseDirection.Forward,
                NodeClass.Object,
                budget,
                cancellationToken).ConfigureAwait(false))
            {
                NodeId rootId = ToNodeId(root.NodeId);
                if (!rootId.IsNull && seen.Add(rootId))
                {
                    pending.Enqueue((rootId, root.BrowseName.Name ?? string.Empty, 1));
                }
            }

            while (pending.Count > 0)
            {
                if (m_options is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                (NodeId node, string path, int depth) = pending.Dequeue();
                List<ReferenceDescription> contained = await BrowseWithBudgetAsync(
                    node,
                    contains,
                    BrowseDirection.Forward,
                    NodeClass.Unspecified,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                var assets = new ExpandedNodeId[contained.Count];
                for (int ii = 0; ii < assets.Length; ii++)
                {
                    assets[ii] = contained[ii].NodeId;
                }
                locations.Add(new AssetLocationNode(node, path, assets.ToArrayOf()));
                if (depth >= MaxDepth && m_options is null)
                {
                    continue;
                }
                foreach (ReferenceDescription child in await BrowseWithBudgetAsync(
                    node,
                    Ua.ReferenceTypeIds.HasComponent,
                    BrowseDirection.Forward,
                    NodeClass.Object,
                    budget,
                    cancellationToken).ConfigureAwait(false))
                {
                    NodeId childId = ToNodeId(child.NodeId);
                    if (childId.IsNull || !seen.Add(childId))
                    {
                        continue;
                    }
                    if (depth >= MaxDepth)
                    {
                        throw LimitExceeded("The AMB location depth limit was exceeded.");
                    }
                    pending.Enqueue((childId, path + "/" + child.BrowseName.Name, depth + 1));
                }
            }
            return locations.ToArrayOf();
        }

        /// <summary>
        /// Reads the locations that contain an asset, following the inverse
        /// <c>HierarchicalContains</c> and <c>OperationalContains</c>
        /// references (§13.1).
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The locations.</returns>
        public async ValueTask<ArrayOf<AssetLocationRecord>> ReadLocationsOfAssetAsync(
            NodeId asset,
            CancellationToken cancellationToken = default)
        {
            var locations = new List<AssetLocationRecord>();
            var seen = new HashSet<(AssetLocationKind Kind, NodeId Node)>();
            BrowseBudget budget = CreateBrowseBudget();
            foreach (AssetLocationKind kind in new[] { AssetLocationKind.Hierarchical, AssetLocationKind.Operational })
            {
                (_, NodeId contains) = LocationIdsOf(kind);
                foreach (ReferenceDescription location in await BrowseWithBudgetAsync(
                    asset,
                    contains,
                    BrowseDirection.Inverse,
                    NodeClass.Unspecified,
                    budget,
                    cancellationToken).ConfigureAwait(false))
                {
                    NodeId target = ToNodeId(location.NodeId);
                    if (m_options is null || (!target.IsNull && seen.Add((kind, target))))
                    {
                        locations.Add(new AssetLocationRecord(kind, target));
                    }
                }
            }
            return locations.ToArrayOf();
        }

        /// <summary>
        /// Reads where an asset is and how it is classified: the location
        /// Properties, <c>0:LocalTime</c> and the dictionary entries (§11,
        /// §13).
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The context; what the asset does not publish is null.</returns>
        public async ValueTask<AssetContextRecord> ReadContextAsync(
            NodeId asset,
            CancellationToken cancellationToken = default)
        {
            AssetReadValue[] fields = await ReadFieldsAsync(
                asset,
                [
                    [Amb(AmbBrowseNames.HierarchicalLocation)],
                    [Amb(AmbBrowseNames.OperationalLocation)],
                    [Amb(AmbBrowseNames.DigitalLocation)],
                    [new QualifiedName(Ua.BrowseNames.LocalTime)]
                ],
                cancellationToken).ConfigureAwait(false);
            Variant[] values = VariantsOf(fields);
            for (int ii = 0; ii < 3; ii++)
            {
                RequireValueType(fields[ii], values[ii].TryGetValue(out string? _));
            }
            bool hasLocalTime = values[3].TryGetValue<TimeZoneDataType>(
                out TimeZoneDataType? localTime, Session.MessageContext);
            RequireValueType(fields[3], hasLocalTime || IsNullStructure(values[3]));
            List<ReferenceDescription> entries = await BrowseAsync(
                asset,
                Ua.ReferenceTypeIds.HasDictionaryEntry,
                BrowseDirection.Forward,
                cancellationToken).ConfigureAwait(false);
            return new AssetContextRecord
            {
                HierarchicalLocation = StringOf(values[0]),
                OperationalLocation = StringOf(values[1]),
                DigitalLocation = StringOf(values[2]),
                LocalTime = hasLocalTime ? localTime : null,
                Classifications = TargetsOf(entries),
                Values = EvidenceOf(fields)
            };
        }

        /// <summary>
        /// Reads the entries of the <c>Requirements</c> or
        /// <c>Capabilities</c> folder of an asset (§10.6, §10.7).
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="folder">The folder.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The entries; empty when the asset has no such folder.</returns>
        public async ValueTask<ArrayOf<AssetEntryRecord>> ReadEntriesAsync(
            NodeId asset,
            AssetEntryFolder folder,
            CancellationToken cancellationToken = default)
        {
            BrowseBudget budget = CreateBrowseBudget();
            NodeId folderId = await ResolveAsync(
                asset,
                cancellationToken,
                Amb(folder == AssetEntryFolder.Requirements
                    ? AmbBrowseNames.Requirements
                    : AmbBrowseNames.Capabilities)).ConfigureAwait(false);
            List<ReferenceDescription> variables = await BrowseWithBudgetAsync(
                folderId,
                Ua.ReferenceTypeIds.HierarchicalReferences,
                BrowseDirection.Forward,
                NodeClass.Variable,
                budget,
                cancellationToken).ConfigureAwait(false);
            var nodes = new NodeId[variables.Count];
            for (int ii = 0; ii < nodes.Length; ii++)
            {
                nodes[ii] = ReadSourceOf(variables[ii]);
            }
            DataValue[] values = await ReadAsync(nodes, Attributes.Value, cancellationToken).ConfigureAwait(false);
            var entries = new AssetEntryRecord[nodes.Length];
            for (int ii = 0; ii < entries.Length; ii++)
            {
                List<ReferenceDescription> dictionary = await BrowseWithBudgetAsync(
                    nodes[ii],
                    Ua.ReferenceTypeIds.HasDictionaryEntry,
                    BrowseDirection.Forward,
                    NodeClass.Unspecified,
                    budget,
                    cancellationToken).ConfigureAwait(false);
                entries[ii] = new AssetEntryRecord(
                    variables[ii].BrowseName,
                    StatusCode.IsGood(values[ii].StatusCode) ? values[ii].WrappedValue : Variant.Null,
                    TargetsOf(dictionary))
                {
                    ReadValue = m_options is null ? null : new AssetReadValue(
                        nodes[ii], [variables[ii].BrowseName], Attributes.Value, values[ii])
                };
            }
            return entries.ToArrayOf();
        }

        /// <summary>
        /// Walks the hierarchical references below an asset and reports the
        /// other assets found (§14.1), including through grouping Objects.
        /// The browse applies an Object NodeClassMask, so a server that cannot classify a
        /// remote target omits it. Remote references the server returns are reported as
        /// published, without verification or traversal.
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// The sub-assets, nearer ones first. Remote references retain the originating session's identity.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// A local identity cannot be resolved, a browse fails, or a traversal limit is exceeded.
        /// </exception>
        public async IAsyncEnumerable<ExpandedNodeId> EnumerateSubAssetsAsync(
            NodeId asset,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (m_options is not null && asset.IsNull)
            {
                yield break;
            }
            BrowseBudget budget = CreateBrowseBudget();
            HashSet<NodeId> assets = await DiscoverAssetSetAsync(budget, cancellationToken).ConfigureAwait(false);
            var seen = new HashSet<NodeId> { asset };
            var remoteSeen = new HashSet<ExpandedNodeId>();
            var pending = new Queue<(NodeId Node, int Depth)>();
            pending.Enqueue((asset, 0));
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (NodeId node, int depth) = pending.Dequeue();
                if (depth >= MaxDepth && m_options is null)
                {
                    continue;
                }
                foreach (ReferenceDescription child in await BrowseWithBudgetAsync(
                    node, Ua.ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Forward,
                    NodeClass.Object, budget, cancellationToken).ConfigureAwait(false))
                {
                    if (IsRemote(child.NodeId))
                    {
                        if (m_options is null || remoteSeen.Add(child.NodeId))
                        {
                            if (depth >= MaxDepth)
                            {
                                throw LimitExceeded("The AMB sub-asset depth limit was exceeded.");
                            }
                            yield return child.NodeId;
                        }
                        continue;
                    }
                    NodeId target = ToNodeId(child.NodeId);
                    if (target.IsNull || !seen.Add(target))
                    {
                        continue;
                    }
                    if (depth >= MaxDepth)
                    {
                        throw LimitExceeded("The AMB sub-asset depth limit was exceeded.");
                    }
                    if (assets.Contains(target))
                    {
                        yield return target;
                    }
                    pending.Enqueue((target, depth + 1));
                }
            }
        }

        /// <summary>
        /// Enumerates verified sub-assets using stable server and namespace URIs,
        /// including servers not present in the originating ServerArray.
        /// </summary>
        /// <param name="asset">The local starting asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>URI-qualified asset identities, nearer ones first.</returns>
        public async IAsyncEnumerable<AssetNodeReference> EnumerateSubAssetReferencesAsync(
            NodeId asset,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach ((AmbClient client, NodeId node) in TraverseSubAssetsAsync(asset, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return SubAssetReferenceOf(client, node);
            }
        }

        /// <summary>
        /// Qualifies a verified asset without changing any session's authoritative tables.
        /// </summary>
        private static AssetNodeReference SubAssetReferenceOf(AmbClient client, NodeId node)
        {
            string? serverUri = client.Session.ServerUris.GetString(0);
            string? namespaceUri = client.Session.NamespaceUris.GetString(node.NamespaceIndex);
            if (string.IsNullOrEmpty(serverUri) || string.IsNullOrEmpty(namespaceUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "The asset's server or namespace URI is unknown.");
            }
            return new AssetNodeReference(serverUri, new ExpandedNodeId(node, namespaceUri));
        }

        /// <summary>
        /// Walks the asset graph with shared limits and per-server discovery evidence.
        /// </summary>
        private async IAsyncEnumerable<(AmbClient Client, NodeId Node)> TraverseSubAssetsAsync(
            NodeId asset,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (m_options is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (m_options is not null && asset.IsNull)
            {
                yield break;
            }
            BrowseBudget budget = CreateBrowseBudget();
            HashSet<NodeId> assets = await DiscoverAssetSetAsync(budget, cancellationToken).ConfigureAwait(false);

            var seen = new HashSet<NodeId> { asset };
            var clients = new Dictionary<string, AmbClient>(StringComparer.Ordinal);
            var knownByClient = new Dictionary<AmbClient, HashSet<NodeId>> { [this] = assets };
            var seenByClient = new Dictionary<AmbClient, HashSet<NodeId>> { [this] = seen };
            var pending = new Queue<(AmbClient Client, NodeId Node, int Depth)>();
            pending.Enqueue((this, asset, 0));
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (AmbClient client, NodeId node, int depth) = pending.Dequeue();
                if (depth >= MaxDepth && m_options is null)
                {
                    continue;
                }
                foreach (ReferenceDescription child in await client.BrowseWithBudgetAsync(
                    node,
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    BrowseDirection.Forward,
                    NodeClass.Unspecified,
                    budget,
                    cancellationToken).ConfigureAwait(false))
                {
                    if (child.NodeClass != NodeClass.Object &&
                        !(IsRemote(child.NodeId) && child.NodeClass == NodeClass.Unspecified))
                    {
                        continue;
                    }
                    (AmbClient targetClient, NodeId target) = await ResolveAssetTargetAsync(
                        client, child.NodeId, clients, cancellationToken).ConfigureAwait(false);
                    if (!seenByClient.TryGetValue(targetClient, out HashSet<NodeId>? targetSeen))
                    {
                        targetSeen = [];
                        seenByClient.Add(targetClient, targetSeen);
                    }
                    if (target.IsNull || !targetSeen.Add(target))
                    {
                        continue;
                    }
                    if (depth >= MaxDepth)
                    {
                        throw LimitExceeded("The AMB sub-asset depth limit was exceeded.");
                    }
                    if (!knownByClient.TryGetValue(targetClient, out HashSet<NodeId>? targetAssets))
                    {
                        targetAssets = await targetClient.DiscoverAssetSetAsync(
                            budget, cancellationToken).ConfigureAwait(false);
                        knownByClient.Add(targetClient, targetAssets);
                    }
                    if (targetAssets.Contains(target))
                    {
                        yield return (targetClient, target);
                    }
                    pending.Enqueue((targetClient, target, depth + 1));
                }
            }
        }

        /// <summary>
        /// Captures the server's alias-listed asset identities within the operation's budget.
        /// </summary>
        private async ValueTask<HashSet<NodeId>> DiscoverAssetSetAsync(
            BrowseBudget budget,
            CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> known = m_options is null
                ? await DiscoverAssetsAsync(cancellationToken).ConfigureAwait(false)
                : await DiscoverByBrowsingAsync(budget, cancellationToken).ConfigureAwait(false);
            var assets = new HashSet<NodeId>();
            foreach (NodeId node in known)
            {
                assets.Add(node);
            }
            return assets;
        }

        /// <summary>
        /// Resolves an asset candidate in its owning server's namespace table.
        /// </summary>
        private async ValueTask<(AmbClient Client, NodeId Node)> ResolveAssetTargetAsync(
            AmbClient source,
            ExpandedNodeId target,
            Dictionary<string, AmbClient> clients,
            CancellationToken cancellationToken)
        {
            if (!IsRemote(target))
            {
                return (source, source.ToNodeId(target));
            }
            AmbClient client = await ResolveSubAssetClientAsync(source, target, clients, cancellationToken)
                .ConfigureAwait(false);
            string? namespaceUri = target.NamespaceUri ??
                source.Session.NamespaceUris.GetString(target.NamespaceIndex);
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "The remote target namespace is unknown.");
            }
            return (client, client.ToNodeId(new ExpandedNodeId(target.InnerNodeId, namespaceUri), requireLocal: true));
        }

        /// <summary>
        /// Resolves one server identity to a borrowed client, caching it for this traversal only.
        /// </summary>
        private async ValueTask<AmbClient> ResolveSubAssetClientAsync(
            AmbClient source,
            ExpandedNodeId target,
            Dictionary<string, AmbClient> clients,
            CancellationToken cancellationToken)
        {
            IAmbSessionResolver resolver = m_options?.SessionResolver ??
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    $"A session resolver is required to verify the remote sub-asset candidate {target}.");
            string? serverUri = source.Session.ServerUris.GetString(target.ServerIndex);
            string? rootUri = Session.ServerUris.GetString(0);
            if (string.IsNullOrEmpty(serverUri) || string.IsNullOrEmpty(rootUri))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "The remote or originating server URI is unknown.");
            }
            if (serverUri == rootUri)
            {
                return this;
            }
            if (clients.TryGetValue(serverUri, out AmbClient? client))
            {
                return client;
            }
            ISession session = await resolver.ResolveAsync(serverUri, cancellationToken)
                .ConfigureAwait(false) ??
                throw new ServiceResultException(
                    StatusCodes.BadNotConnected, $"No application session is available for {serverUri}.");
            cancellationToken.ThrowIfCancellationRequested();
            if (session.ServerUris.GetString(0) != serverUri)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "The resolved session belongs to a different server.");
            }
            client = new AmbClient(session, Telemetry, m_options!);
            clients.Add(serverUri, client);
            return client;
        }

        /// <summary>
        /// Reads the non-hierarchical relations of an asset to other assets,
        /// in both directions (§14.2).
        /// Remote references are reported as published, without verification or traversal.
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The relations.</returns>
        public async ValueTask<ArrayOf<AssetRelation>> ReadRelationsAsync(
            NodeId asset,
            CancellationToken cancellationToken = default)
        {
            BrowseBudget budget = CreateBrowseBudget();
            HashSet<NodeId> assets = await DiscoverAssetSetAsync(budget, cancellationToken).ConfigureAwait(false);
            var relations = new List<AssetRelation>();
            foreach (ReferenceDescription reference in await BrowseWithBudgetAsync(
                asset, Ua.ReferenceTypeIds.NonHierarchicalReferences, BrowseDirection.Both,
                NodeClass.Unspecified, budget, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                NodeId target = IsRemote(reference.NodeId) ? NodeId.Null : ToNodeId(reference.NodeId);
                if (IsRemote(reference.NodeId) || (target != asset && assets.Contains(target)))
                {
                    relations.Add(new AssetRelation(reference.ReferenceTypeId, !reference.IsForward, reference.NodeId));
                }
            }
            return relations.ToArrayOf();
        }

        /// <summary>
        /// Reads non-hierarchical relations to verified assets using URI-qualified target identities.
        /// </summary>
        /// <param name="asset">The local asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Relations whose targets are listed in their server's AMB alias categories.</returns>
        public async ValueTask<ArrayOf<AssetRelationReference>> ReadRelationReferencesAsync(
            NodeId asset,
            CancellationToken cancellationToken = default)
        {
            var relations = new List<AssetRelationReference>();
            foreach (var relation in await ReadRelationTargetsAsync(asset, cancellationToken).ConfigureAwait(false))
            {
                relations.Add(new AssetRelationReference(relation.ReferenceTypeId, relation.IsInverse,
                    SubAssetReferenceOf(relation.Client, relation.Node)));
            }
            return relations.ToArrayOf();
        }

        /// <summary>
        /// Verifies relation targets with one discovery and browse budget.
        /// </summary>
        private async ValueTask<List<(NodeId ReferenceTypeId, bool IsInverse, AmbClient Client, NodeId Node)>>
            ReadRelationTargetsAsync(NodeId asset, CancellationToken cancellationToken)
        {
            BrowseBudget budget = CreateBrowseBudget();
            HashSet<NodeId> assets = await DiscoverAssetSetAsync(budget, cancellationToken).ConfigureAwait(false);

            var relations = new List<(NodeId ReferenceTypeId, bool IsInverse, AmbClient Client, NodeId Node)>();
            var clients = new Dictionary<string, AmbClient>(StringComparer.Ordinal);
            var knownByClient = new Dictionary<AmbClient, HashSet<NodeId>> { [this] = assets };
            var seen = new HashSet<(NodeId ReferenceTypeId, bool IsInverse, AmbClient Client, NodeId Node)>();
            foreach (ReferenceDescription reference in await BrowseWithBudgetAsync(
                asset,
                Ua.ReferenceTypeIds.NonHierarchicalReferences,
                BrowseDirection.Both,
                NodeClass.Unspecified,
                budget,
                cancellationToken).ConfigureAwait(false))
            {
                (AmbClient client, NodeId target) = await ResolveAssetTargetAsync(
                    this, reference.NodeId, clients, cancellationToken).ConfigureAwait(false);
                if (!knownByClient.TryGetValue(client, out HashSet<NodeId>? targetAssets))
                {
                    targetAssets = await client.DiscoverAssetSetAsync(
                        budget, cancellationToken).ConfigureAwait(false);
                    knownByClient.Add(client, targetAssets);
                }
                var relation = (reference.ReferenceTypeId, !reference.IsForward, client, target);
                if (!(client == this && target == asset) && targetAssets.Contains(target) && seen.Add(relation))
                {
                    relations.Add(relation);
                }
            }
            return relations;
        }

        private (NodeId EntryPoint, NodeId Contains) LocationIdsOf(AssetLocationKind kind)
        {
            return kind switch
            {
                AssetLocationKind.Hierarchical =>
                    (ToNodeId(ObjectIds.HierarchicalLocations), ToNodeId(ReferenceTypeIds.HierarchicalContains)),
                AssetLocationKind.Operational =>
                    (ToNodeId(ObjectIds.OperationalLocations), ToNodeId(ReferenceTypeIds.OperationalContains)),
                _ => throw new ArgumentException(
                    $"{kind} locations have no location objects (OPC 10000-110 §13.5).",
                    nameof(kind))
            };
        }

        private static bool IsRemote(ExpandedNodeId nodeId)
        {
            return nodeId.ServerIndex != 0;
        }

        private static ArrayOf<ExpandedNodeId> TargetsOf(List<ReferenceDescription> references)
        {
            var targets = new ExpandedNodeId[references.Count];
            for (int ii = 0; ii < targets.Length; ii++)
            {
                targets[ii] = references[ii].NodeId;
            }
            return targets.ToArrayOf();
        }
    }
}
