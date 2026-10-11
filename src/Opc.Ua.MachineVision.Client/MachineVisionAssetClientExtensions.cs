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
using System.Threading;
using System.Threading.Tasks;
using Amcm = Opc.Ua.Vision.MachineVision.AMCM;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// One namespace-qualified asset value, retaining its source node for subsequent subscriptions.
    /// </summary>
    /// <param name="NodeId">The readable variable.</param>
    /// <param name="BrowsePath">Its relative path from the requested root.</param>
    /// <param name="TypeDefinition">The variable type, including vendor subtypes.</param>
    /// <param name="Value">The value, quality, and timestamps exactly as returned by the server.</param>
    public sealed record MachineVisionAssetValue(
        NodeId NodeId,
        ArrayOf<QualifiedName> BrowsePath,
        NodeId TypeDefinition,
        DataValue Value);

    /// <summary>
    /// Bounded Part 2 inventory/monitoring snapshot. IsComplete is false when a caller-selected bound was reached.
    /// </summary>
    /// <param name="Root">The root queried.</param>
    /// <param name="Values">The namespace-qualified values, including missing-data status codes.</param>
    /// <param name="IsComplete">Whether the subtree fit within both bounds.</param>
    public sealed record MachineVisionAssetSnapshot(
        NodeId Root, ArrayOf<MachineVisionAssetValue> Values, bool IsComplete);

    /// <summary>
    /// Part 2 asset operations for a Machine Vision session.
    /// </summary>
    public static class MachineVisionAssetClientExtensions
    {
        /// <summary>
        /// Discovers Part 2 system assets independently of the Part 1 namespace.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static IAsyncEnumerable<VisionNodeEntry> EnumerateAssetsAsync(
            this MachineVisionClient client,
            NodeId root = default,
            int maximumNodes = 10000,
            CancellationToken cancellationToken = default)
        {
            Register(client);
            if (maximumNodes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumNodes));
            }
            return client.EnumerateAsync(Amcm.ObjectTypeIds.VisionSystemAssetType,
                root, maximumNodes, cancellationToken);
        }

        /// <summary>
        /// Opens the generated Part 2 proxy for a system asset.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static Amcm.VisionSystemAssetTypeClient Asset(this MachineVisionClient client, NodeId asset)
        {
            Register(client);
            if (asset.IsNull)
            {
                throw new ArgumentNullException(nameof(asset));
            }
            return new Amcm.VisionSystemAssetTypeClient(client.Session, asset, client.Operations.Telemetry);
        }

        /// <summary>
        /// Reads inventory, nameplate, maintenance, health, interfaces, and component-specific members.
        /// Shared nodes are visited once and cycles cannot make the traversal unbounded.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static async ValueTask<MachineVisionAssetSnapshot> ReadAssetAsync(
            this MachineVisionClient client,
            NodeId root,
            int maximumDepth = 5,
            int maximumValues = 512,
            CancellationToken cancellationToken = default)
        {
            Register(client);
            if (root.IsNull)
            {
                throw new ArgumentNullException(nameof(root));
            }
            if (maximumDepth is < 0 or > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumDepth));
            }
            if (maximumValues <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumValues));
            }
            var pending = new Queue<(NodeId Node, ArrayOf<QualifiedName> Path, int Depth)>();
            var visited = new HashSet<NodeId> { root };
            var values = new List<MachineVisionAssetValue>();
            pending.Enqueue((root, [], 0));
            bool complete = true;
            int browsed = 0;
            while (pending.Count > 0)
            {
                (NodeId node, ArrayOf<QualifiedName> path, int depth) = pending.Dequeue();
                if (++browsed > maximumValues * 4L)
                {
                    complete = false;
                    break;
                }
                ArrayOf<ReferenceDescription> references = await client.BrowseAsync(
                    node, ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Forward,
                    (uint)(NodeClass.Object | NodeClass.Variable), cancellationToken).ConfigureAwait(false);
                var variables = new List<(NodeId Node, ArrayOf<QualifiedName> Path, NodeId Type)>();
                for (int ii = 0; ii < references.Count; ii++)
                {
                    ReferenceDescription reference = references[ii];
                    var child = ExpandedNodeId.ToNodeId(reference.NodeId, client.Session.NamespaceUris);
                    if (child.IsNull || !visited.Add(child))
                    {
                        continue;
                    }
                    if (depth >= maximumDepth)
                    {
                        complete = false;
                        continue;
                    }
                    var childPath = new List<QualifiedName>(path.ToArray() ?? []) { reference.BrowseName };
                    var qualifiedPath = childPath.ToArrayOf();
                    if (reference.NodeClass == NodeClass.Variable)
                    {
                        if (variables.Count + values.Count == maximumValues)
                        {
                            complete = false;
                            break;
                        }
                        variables.Add((child, qualifiedPath,
                            ExpandedNodeId.ToNodeId(reference.TypeDefinition, client.Session.NamespaceUris)));
                    }
                    pending.Enqueue((child, qualifiedPath, depth + 1));
                }
                if (variables.Count != 0)
                {
                    var nodes = new List<NodeId>(variables.Count);
                    foreach ((NodeId Node, ArrayOf<QualifiedName> Path, NodeId Type) variable in variables)
                    {
                        nodes.Add(variable.Node);
                    }
                    ArrayOf<DataValue> readings = await client.Operations.ReadValuesAsync(
                        nodes.ToArrayOf(), cancellationToken).ConfigureAwait(false);
                    for (int ii = 0; ii < variables.Count; ii++)
                    {
                        values.Add(new MachineVisionAssetValue(
                            variables[ii].Node, variables[ii].Path, variables[ii].Type, readings[ii]));
                    }
                }
                if (values.Count == maximumValues && pending.Count != 0)
                {
                    complete = false;
                    break;
                }
            }
            return new MachineVisionAssetSnapshot(root, values.ToArrayOf(), complete);
        }

        /// <summary>
        /// Registers the Part 2 value types for both session message contexts.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="client"/> is <c>null</c>.</exception>
        internal static void Register(this MachineVisionClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            RegisterFactory(client.Session.Factory);
            if (!ReferenceEquals(client.Session.Factory, client.Session.MessageContext.Factory))
            {
                RegisterFactory(client.Session.MessageContext.Factory);
            }
        }

        private static void RegisterFactory(IEncodeableFactory factory)
        {
            if (!factory.ContainsEncodeableType(Amcm.DataTypeIds.SEMI_E10SystemStateInfoDataType))
            {
                Amcm.OpcUaMachineVisionAMCMExtensions
                    .AddOpcUaMachineVisionAMCM(factory.Builder).Commit();
            }
        }
    }
}
