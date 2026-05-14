/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace UaLens.Connection;

/// <summary>
/// Exports the address space of a connected OPC UA server to a NodeSet2
/// XML file using the SDK's <see cref="CoreClientUtils.ExportNodesToNodeSet2(ISystemContext, IList{INode}, Stream, NodeSetExportOptions)"/>
/// helper.  Exports every node reachable from the supplied starting node
/// via <see cref="ReferenceTypeIds.HierarchicalReferences"/>; nodes in
/// namespace 0 (OPC UA base) are excluded by default since their schema
/// is part of the SDK and should not be re-exported.
/// </summary>
internal sealed class NodeSetExporter
{
    private const int kMaxSearchDepth = 32;

    private readonly ILogger m_log;
    private readonly ITelemetryContext m_telemetry;

    public NodeSetExporter(ITelemetryContext telemetry)
    {
        m_telemetry = telemetry;
        m_log = telemetry.CreateLogger("NodeSetExporter");
    }

    /// <summary>
    /// Browse the address space hierarchically from <paramref name="startingNode"/>
    /// and write a single NodeSet2 XML containing all reachable non-base
    /// nodes to <paramref name="filePath"/>.  Returns the count of exported
    /// nodes.  The session's <see cref="ISession.NodeCache"/> is used to
    /// deduplicate and cache the browse — repeat exports against the same
    /// session run noticeably faster.
    /// </summary>
    public async Task<int> ExportAsync(
        ISession session,
        string filePath,
        NodeId? startingNode = null,
        IReadOnlyCollection<string>? namespaceFilter = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Path required.", nameof(filePath));
        }

        Stopwatch sw = Stopwatch.StartNew();
        IList<INode> nodes = await BrowseAllAsync(
                session,
                startingNode ?? ObjectIds.RootFolder,
                ct)
            .ConfigureAwait(false);

        // Optional namespace-URI filter.  When supplied, only nodes whose
        // namespace is in the set are emitted; otherwise all non-base
        // namespaces are exported.
        if (namespaceFilter is { Count: > 0 })
        {
            var allowed = new HashSet<string>(namespaceFilter, StringComparer.OrdinalIgnoreCase);
            var filtered = new List<INode>(nodes.Count);
            foreach (INode n in nodes)
            {
                string? uri = session.NamespaceUris.GetString(n.NodeId.NamespaceIndex);
                if (uri is not null && allowed.Contains(uri))
                {
                    filtered.Add(n);
                }
            }
            nodes = filtered;
        }

        m_log.LogInformation(
            "Browsed {Count} non-base nodes in {ElapsedMs}ms; writing NodeSet2 to {Path}",
            nodes.Count,
            sw.ElapsedMilliseconds,
            filePath);

        // Run the (synchronous) export off the UI thread.
        await Task.Run(() =>
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            using var fs = new FileStream(filePath, FileMode.Create);
            var ctx = new SystemContext(m_telemetry)
            {
                NamespaceUris = session.NamespaceUris,
                ServerUris = session.ServerUris
            };
            CoreClientUtils.ExportNodesToNodeSet2(ctx, nodes, fs);
        }, ct).ConfigureAwait(false);

        sw.Stop();
        m_log.LogInformation(
            "Exported {Count} nodes to {Path} in {ElapsedMs}ms",
            nodes.Count,
            filePath,
            sw.ElapsedMilliseconds);
        return nodes.Count;
    }

    private async Task<IList<INode>> BrowseAllAsync(
        ISession session,
        NodeId startingNode,
        CancellationToken ct)
    {
        var nodeDictionary = new Dictionary<ExpandedNodeId, INode>();
        ArrayOf<NodeId> referenceTypes = [ReferenceTypeIds.HierarchicalReferences];

        // Seed the type tree so the NodeCache can resolve subtypes correctly.
        ArrayOf<ExpandedNodeId> seed = ReferenceTypeIds.Identifiers
            .Select(id => NodeId.ToExpandedNodeId(id, session.NamespaceUris))
            .ToArrayOf();
        await session.FetchTypeTreeAsync(seed, ct).ConfigureAwait(false);

        ArrayOf<ExpandedNodeId> nodesToBrowse = [startingNode];
        int depth = 0;
        while (nodesToBrowse.Count > 0 && depth < kMaxSearchDepth)
        {
            depth++;
            ArrayOf<INode> hits = await session.NodeCache
                .FindReferencesAsync(nodesToBrowse, referenceTypes, isInverse: false, includeSubtypes: true, ct)
                .ConfigureAwait(false);

            var next = new List<ExpandedNodeId>();
            foreach (INode node in hits)
            {
                if (nodeDictionary.ContainsKey(node.NodeId))
                {
                    continue;
                }
                // Skip namespace 0 (base UA types) — they're owned by the SDK schema.
                if (node.NodeId.NamespaceIndex == 0)
                {
                    continue;
                }
                nodeDictionary[node.NodeId] = node;
                next.Add(node.NodeId);
            }
            nodesToBrowse = next.ToArrayOf();
        }
        return nodeDictionary.Values.ToList();
    }
}
