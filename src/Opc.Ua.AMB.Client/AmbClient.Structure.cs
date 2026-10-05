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
            var locations = new List<AssetLocationNode>();
            var pending = new Queue<(NodeId Node, string Path, int Depth)>();
            foreach (ReferenceDescription root in await BrowseAsync(
                entryPoint,
                Ua.ReferenceTypeIds.Organizes,
                BrowseDirection.Forward,
                cancellationToken,
                NodeClass.Object).ConfigureAwait(false))
            {
                pending.Enqueue((ToNodeId(root.NodeId), root.BrowseName.Name ?? string.Empty, 1));
            }

            var seen = new HashSet<NodeId>();
            while (pending.Count > 0)
            {
                (NodeId node, string path, int depth) = pending.Dequeue();
                if (node.IsNull || !seen.Add(node))
                {
                    continue;
                }
                List<ReferenceDescription> contained = await BrowseAsync(
                    node,
                    contains,
                    BrowseDirection.Forward,
                    cancellationToken).ConfigureAwait(false);
                var assets = new ExpandedNodeId[contained.Count];
                for (int ii = 0; ii < assets.Length; ii++)
                {
                    assets[ii] = contained[ii].NodeId;
                }
                locations.Add(new AssetLocationNode(node, path, assets.ToArrayOf()));
                if (depth >= MaxDepth)
                {
                    continue;
                }
                foreach (ReferenceDescription child in await BrowseAsync(
                    node,
                    Ua.ReferenceTypeIds.HasComponent,
                    BrowseDirection.Forward,
                    cancellationToken,
                    NodeClass.Object).ConfigureAwait(false))
                {
                    pending.Enqueue((ToNodeId(child.NodeId), path + "/" + child.BrowseName.Name, depth + 1));
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
            foreach (AssetLocationKind kind in new[] { AssetLocationKind.Hierarchical, AssetLocationKind.Operational })
            {
                (_, NodeId contains) = LocationIdsOf(kind);
                foreach (ReferenceDescription location in await BrowseAsync(
                    asset,
                    contains,
                    BrowseDirection.Inverse,
                    cancellationToken).ConfigureAwait(false))
                {
                    locations.Add(new AssetLocationRecord(kind, ToNodeId(location.NodeId)));
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
            Variant[] values = await ReadChildValuesAsync(
                asset,
                [
                    [Amb(AmbBrowseNames.HierarchicalLocation)],
                    [Amb(AmbBrowseNames.OperationalLocation)],
                    [Amb(AmbBrowseNames.DigitalLocation)],
                    [new QualifiedName(Ua.BrowseNames.LocalTime)]
                ],
                cancellationToken).ConfigureAwait(false);
            List<ReferenceDescription> entries = await BrowseAsync(
                asset,
                Ua.ReferenceTypeIds.HasDictionaryEntry,
                BrowseDirection.Forward,
                cancellationToken).ConfigureAwait(false);
            TimeZoneDataType localTime;
            return new AssetContextRecord
            {
                HierarchicalLocation = StringOf(values[0]),
                OperationalLocation = StringOf(values[1]),
                DigitalLocation = StringOf(values[2]),
                LocalTime = values[3].TryGetStructure(Session.MessageContext, out localTime!) ? localTime : null,
                Classifications = TargetsOf(entries)
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
            NodeId folderId = await ResolveAsync(
                asset,
                cancellationToken,
                Amb(folder == AssetEntryFolder.Requirements
                    ? AmbBrowseNames.Requirements
                    : AmbBrowseNames.Capabilities)).ConfigureAwait(false);
            List<ReferenceDescription> variables = await BrowseAsync(
                folderId,
                Ua.ReferenceTypeIds.HierarchicalReferences,
                BrowseDirection.Forward,
                cancellationToken,
                NodeClass.Variable).ConfigureAwait(false);
            var nodes = new NodeId[variables.Count];
            for (int ii = 0; ii < nodes.Length; ii++)
            {
                nodes[ii] = ToNodeId(variables[ii].NodeId);
            }
            DataValue[] values = await ReadAsync(nodes, Attributes.Value, cancellationToken).ConfigureAwait(false);
            var entries = new AssetEntryRecord[nodes.Length];
            for (int ii = 0; ii < entries.Length; ii++)
            {
                List<ReferenceDescription> dictionary = await BrowseAsync(
                    nodes[ii],
                    Ua.ReferenceTypeIds.HasDictionaryEntry,
                    BrowseDirection.Forward,
                    cancellationToken).ConfigureAwait(false);
                entries[ii] = new AssetEntryRecord(
                    variables[ii].BrowseName,
                    StatusCode.IsGood(values[ii].StatusCode) ? values[ii].WrappedValue : Variant.Null,
                    TargetsOf(dictionary));
            }
            return entries.ToArrayOf();
        }

        /// <summary>
        /// Walks the hierarchical references below an asset and reports the
        /// other assets found (§14.1); an asset of another server is
        /// reported, but not followed.
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The sub-assets, nearer ones first.</returns>
        public async IAsyncEnumerable<ExpandedNodeId> EnumerateSubAssetsAsync(
            NodeId asset,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArrayOf<NodeId> known = await DiscoverAssetsAsync(cancellationToken).ConfigureAwait(false);
            var assets = new HashSet<NodeId>();
            foreach (NodeId node in known)
            {
                assets.Add(node);
            }

            var seen = new HashSet<NodeId> { asset };
            var pending = new Queue<(NodeId Node, int Depth)>();
            pending.Enqueue((asset, 0));
            while (pending.Count > 0)
            {
                (NodeId node, int depth) = pending.Dequeue();
                if (depth >= MaxDepth)
                {
                    continue;
                }
                foreach (ReferenceDescription child in await BrowseAsync(
                    node,
                    Ua.ReferenceTypeIds.HierarchicalReferences,
                    BrowseDirection.Forward,
                    cancellationToken,
                    NodeClass.Object).ConfigureAwait(false))
                {
                    if (IsRemote(child.NodeId))
                    {
                        yield return child.NodeId;
                        continue;
                    }
                    NodeId target = ToNodeId(child.NodeId);
                    if (target.IsNull || !seen.Add(target))
                    {
                        continue;
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
        /// Reads the non-hierarchical relations of an asset to other assets,
        /// in both directions (§14.2).
        /// </summary>
        /// <param name="asset">The asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The relations.</returns>
        public async ValueTask<ArrayOf<AssetRelation>> ReadRelationsAsync(
            NodeId asset,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<NodeId> known = await DiscoverAssetsAsync(cancellationToken).ConfigureAwait(false);
            var assets = new HashSet<NodeId>();
            foreach (NodeId node in known)
            {
                assets.Add(node);
            }

            var relations = new List<AssetRelation>();
            foreach (ReferenceDescription reference in await BrowseAsync(
                asset,
                Ua.ReferenceTypeIds.NonHierarchicalReferences,
                BrowseDirection.Both,
                cancellationToken).ConfigureAwait(false))
            {
                NodeId target = ToNodeId(reference.NodeId);
                if (IsRemote(reference.NodeId) || (target != asset && assets.Contains(target)))
                {
                    relations.Add(new AssetRelation(reference.ReferenceTypeId, !reference.IsForward, reference.NodeId));
                }
            }
            return relations.ToArrayOf();
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
