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
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Plugins.SubscriptionBench;

/// <summary>
/// Discovers a unique variable pool with bounded traversal and owned browse cursors.
/// </summary>
internal sealed class VariablePoolBrowser
{
    public VariablePoolBrowser(ITelemetryContext telemetry, int maxNodes = 2048, int maxDepth = 16)
    {
        m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        m_maxNodes = maxNodes;
        m_maxDepth = maxDepth;
    }

    public async Task<VariablePoolDiscovery> BrowseAsync(
        ISession session, NodeId root, CancellationToken cancellationToken)
    {
        var context = new CompanionContext(session, m_telemetry);
        var budget = new IndustrialBrowseBudget(context);
        var variables = new List<(NodeId NodeId, string DisplayName)>();
        var queue = new Queue<(NodeId Node, string Path, int Depth)>();
        var seen = new HashSet<NodeId> { root };
        queue.Enqueue((root, string.Empty, 0));
        string? incomplete = null;
        try
        {
            while (queue.TryDequeue(out (NodeId Node, string Path, int Depth) parent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
                    context, parent.Node, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
                    NodeClass.Object | NodeClass.Variable, budget, cancellationToken).ConfigureAwait(false))
                {
                    NodeId child = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                    if (child.IsNull || seen.Contains(child))
                    {
                        continue;
                    }
                    if (parent.Depth >= m_maxDepth)
                    {
                        incomplete = "The subtree depth limit was reached.";
                        continue;
                    }
                    if (seen.Count >= m_maxNodes)
                    {
                        return new VariablePoolDiscovery([.. variables], "The subtree node budget was exhausted.");
                    }
                    seen.Add(child);
                    string label = reference.DisplayName.Text ?? reference.BrowseName.Name ?? child.ToString();
                    string path = parent.Path + "/" + label;
                    if (reference.NodeClass == NodeClass.Variable)
                    {
                        variables.Add((child, path));
                    }
                    queue.Enqueue((child, path, parent.Depth + 1));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure)
        {
            incomplete = $"Browse failed: {failure.Message}";
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new VariablePoolDiscovery([.. variables], incomplete);
    }

    private readonly ITelemetryContext m_telemetry;
    private readonly int m_maxNodes;
    private readonly int m_maxDepth;
}

/// <summary>
/// A partial pool is usable but must never be presented as a complete subtree scan.
/// </summary>
internal sealed record VariablePoolDiscovery(
    ArrayOf<(NodeId NodeId, string DisplayName)> Variables,
    string? IncompleteReason);
