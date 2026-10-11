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

    public sealed partial class MachineVisionClient
    {
        /// <summary>
        /// Reads inventory, nameplate, maintenance, health, interfaces, and component-specific members.
        /// Shared nodes are visited once and cycles cannot make the traversal unbounded.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public async ValueTask<MachineVisionAssetSnapshot> ReadAssetAsync(
            NodeId root,
            int maximumDepth = 5,
            int maximumValues = 512,
            CancellationToken cancellationToken = default)
        {
            if (root.IsNull)
            {
                throw new ArgumentNullException(nameof(root));
            }
            if (maximumDepth < 0 || maximumDepth > 32)
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
                ArrayOf<ReferenceDescription> references = await BrowseAsync(
                    node, global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences, BrowseDirection.Forward,
                    (uint)(NodeClass.Object | NodeClass.Variable), cancellationToken).ConfigureAwait(false);
                var variables = new List<(NodeId Node, ArrayOf<QualifiedName> Path, NodeId Type)>();
                for (int ii = 0; ii < references.Count; ii++)
                {
                    ReferenceDescription reference = references[ii];
                    NodeId child = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
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
                    ArrayOf<QualifiedName> qualifiedPath = childPath.ToArrayOf();
                    if (reference.NodeClass == NodeClass.Variable)
                    {
                        if (variables.Count + values.Count == maximumValues)
                        {
                            complete = false;
                            break;
                        }
                        variables.Add((child, qualifiedPath,
                            ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris)));
                    }
                    pending.Enqueue((child, qualifiedPath, depth + 1));
                }
                if (variables.Count != 0)
                {
                    var nodes = new List<NodeId>(variables.Count);
                    foreach (var variable in variables)
                    {
                        nodes.Add(variable.Node);
                    }
                    ArrayOf<DataValue> readings = await m_operations.ReadValuesAsync(
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
    }
}
